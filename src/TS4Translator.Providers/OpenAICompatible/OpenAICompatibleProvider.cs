using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Resilience;

namespace TS4Translator.Providers.OpenAICompatible;

/// <summary>
/// <see cref="ITranslationProvider"/> for any endpoint that speaks the OpenAI chat-completions
/// contract: NVIDIA NIM, LM Studio, and any other OpenAI-compatible server (brief section 17). One
/// chat completion call per batch: the whole batch is sent as a JSON array in the user message, and
/// the model is instructed to answer with a JSON array of the same ids paired with translations.
/// </summary>
/// <remarks>
/// Never trusts the model's JSON to be complete or well-formed (brief section 46: "provider retorna
/// JSON truncado"). A malformed or truncated response is salvaged object-by-object — whatever parses
/// cleanly is kept, the rest is simply absent from the result list. The caller
/// (<see cref="Translation.TranslationOrchestrator"/>, in the Translation project) already treats a
/// missing id as a per-item failure rather than corrupting anything, so partial recovery here is safe.
/// A transient failure (503, 429, timeout, ...) is retried with backoff via <see cref="HttpRetry"/>
/// before giving up (brief section 11) - free-tier LLM endpoints return these under load routinely,
/// independent of the caller's own quota.
/// </remarks>
public sealed class OpenAICompatibleProvider(HttpClient httpClient, OpenAICompatibleProviderOptions options, int maxRetries = 3) : ITranslationProvider
{
    private static readonly JsonSerializerOptions RecoveryJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string Name { get; } = $"openai-compatible:{options.Model}";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true, SupportsStreaming: false, SupportsCustomPrompt: true,
        SupportsGlossary: true, SupportsLocalExecution: false, RequiresApiKey: false, MaxContextSize: null);

    public async Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return [];
        }

        var requestBody = new ChatCompletionRequest(
            options.Model,
            options.Temperature,
            [
                new ChatMessage("system", BuildSystemPrompt(sourceLocale, targetLocale, glossaryTerms, customInstructions)),
                new ChatMessage("user", JsonSerializer.Serialize(items.Select(i => new PromptRequestItem(i.Id, i.ProtectedText)))),
            ]);

        using var response = await HttpRetry.SendWithRetryAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Endpoint.TrimEnd('/')}/chat/completions")
                {
                    Content = JsonContent.Create(requestBody),
                };
                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }

                return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            },
            cancellationToken,
            maxRetries).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken).ConfigureAwait(false);
        var content = completion?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;

        // A structurally valid JSON object with "id": null or "translation": null (or the field
        // simply absent) deserializes without error but is not a usable result - drop it here rather
        // than passing a null translation downstream, same philosophy as SalvageCompleteObjects
        // dropping objects that don't parse at all (brief section 46: never trust the provider's
        // response to be complete or well-formed). The id will then legitimately fall into
        // TranslationOrchestrator's "provider returned no result for item" handling.
        return ParseWithRecovery(content)
            .Where(r => r.Id is not null && r.Translation is not null)
            .Select(r => new TranslationResultItem(r.Id!, r.Translation!))
            .ToList();
    }

    private static string BuildSystemPrompt(CultureInfo sourceLocale, CultureInfo targetLocale, IReadOnlyList<GlossaryTerm>? glossaryTerms, string? customInstructions)
    {
        var prompt = new StringBuilder($"""
            You are a translation engine translating video game UI text from {sourceLocale.EnglishName} to {targetLocale.EnglishName}.
            You will receive a JSON array of objects, each with "id" and "text".
            Respond with ONLY a JSON array of objects, each with "id" (copied exactly from the input) and "translation" (the translated text).
            Do not add commentary, explanations, or markdown code fences - respond with the raw JSON array only.
            Text of the form __TS4_TOKEN_0001__ is a placeholder and must be copied into your translation exactly as-is, unchanged, in a natural position for the target language's grammar.
            """);

        if (glossaryTerms is { Count: > 0 })
        {
            prompt.AppendLine();
            prompt.AppendLine("Glossary - apply these exactly, they override your own judgement:");
            foreach (var term in glossaryTerms)
            {
                prompt.AppendLine(term.NeverTranslate
                    ? $"- \"{term.Original}\" must NEVER be translated; keep it exactly as-is."
                    : $"- \"{term.Original}\" must be translated as \"{term.Translation}\".");
            }
        }

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            prompt.AppendLine();
            prompt.AppendLine("Additional style/tone instructions from the project's translator:");
            prompt.AppendLine(customInstructions);
        }

        return prompt.ToString();
    }

    private static List<PromptResponseItem> ParseWithRecovery(string content)
    {
        var jsonArrayText = ExtractJsonArrayText(content);

        try
        {
            return JsonSerializer.Deserialize<List<PromptResponseItem>>(jsonArrayText, RecoveryJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return SalvageCompleteObjects(jsonArrayText);
        }
    }

    private static string ExtractJsonArrayText(string content)
    {
        var start = content.IndexOf('[');
        if (start < 0)
        {
            return content;
        }

        var end = content.LastIndexOf(']');
        return end > start ? content[start..(end + 1)] : content[start..];
    }

    private static List<PromptResponseItem> SalvageCompleteObjects(string jsonArrayText)
    {
        var salvaged = new List<PromptResponseItem>();
        var depth = 0;
        var objectStart = -1;

        for (var i = 0; i < jsonArrayText.Length; i++)
        {
            var c = jsonArrayText[i];
            if (c == '{')
            {
                if (depth == 0)
                {
                    objectStart = i;
                }

                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && objectStart >= 0)
                {
                    var candidate = jsonArrayText[objectStart..(i + 1)];
                    try
                    {
                        var item = JsonSerializer.Deserialize<PromptResponseItem>(candidate, RecoveryJsonOptions);
                        if (item is not null)
                        {
                            salvaged.Add(item);
                        }
                    }
                    catch (JsonException)
                    {
                        // Not a valid item on its own - skip it rather than failing the whole batch.
                    }

                    objectStart = -1;
                }
            }
        }

        return salvaged;
    }

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatCompletionResponse([property: JsonPropertyName("choices")] List<ChatChoice> Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage Message);

    private sealed record PromptRequestItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text);

    /// <summary>
    /// Both fields are nullable even though a well-behaved provider always sends them: System.Text.Json
    /// does not enforce non-nullable reference-type properties at deserialization time, so a response
    /// with "id": null or "translation": null (or the field simply missing) silently produces null here
    /// rather than throwing - declaring these as non-nullable would just hide that reality, not prevent
    /// it (this was a real crash, not a hypothetical - see the filter in TranslateBatchAsync above).
    /// </summary>
    private sealed record PromptResponseItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("translation")] string? Translation);
}
