using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Resilience;

namespace TS4Translator.Providers.GoogleGemini;

/// <summary>
/// <see cref="ITranslationProvider"/> for the Google Gemini API's <c>generateContent</c> endpoint
/// (brief section 18). Same prompt-driven "send a JSON array, ask for a JSON array back" strategy as
/// <see cref="OpenAICompatible.OpenAICompatibleProvider"/> - Gemini is a generative model like any
/// OpenAI-compatible one, just with a different request/response envelope and auth header, so it can
/// support glossary/custom instructions the same way. The prompt-building and truncated-JSON recovery
/// logic below intentionally duplicates <c>OpenAICompatibleProvider</c>'s rather than sharing it: this
/// is only the second provider that needs it, and extracting a shared helper is a reasonable follow-up
/// if a third one ever needs the same logic, not before. A transient failure (503, 429, timeout, ...)
/// is retried with backoff via <see cref="HttpRetry"/> before giving up (brief section 11) - the
/// free-tier Gemini API returns 503 under Google-side load routinely, independent of the caller's own
/// quota (confirmed by a real failure a user hit mid-translation).
/// </summary>
public sealed class GoogleGeminiProvider(HttpClient httpClient, GoogleGeminiProviderOptions options, int maxRetries = 3) : ITranslationProvider
{
    private static readonly JsonSerializerOptions RecoveryJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string Name { get; } = $"google-gemini:{options.Model}";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true, SupportsStreaming: false, SupportsCustomPrompt: true,
        SupportsGlossary: true, SupportsLocalExecution: false, RequiresApiKey: true, MaxContextSize: null);

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

        var requestBody = new GenerateContentRequest(
            new GeminiContent([new GeminiPart(BuildSystemPrompt(sourceLocale, targetLocale, glossaryTerms, customInstructions))]),
            [new GeminiContent([new GeminiPart(JsonSerializer.Serialize(items.Select(i => new PromptRequestItem(i.Id, i.ProtectedText))))])]);

        var url = $"{options.Endpoint.TrimEnd('/')}/v1beta/models/{options.Model}:generateContent";
        using var response = await HttpRetry.SendWithRetryAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(requestBody),
                };
                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    request.Headers.Add("x-goog-api-key", options.ApiKey);
                }

                return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            },
            cancellationToken,
            maxRetries).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var completion = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(cancellationToken).ConfigureAwait(false);
        var content = completion?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;

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

    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("systemInstruction")] GeminiContent SystemInstruction,
        [property: JsonPropertyName("contents")] List<GeminiContent> Contents);

    private sealed record GeminiContent([property: JsonPropertyName("parts")] List<GeminiPart> Parts);

    private sealed record GeminiPart([property: JsonPropertyName("text")] string Text);

    private sealed record GenerateContentResponse([property: JsonPropertyName("candidates")] List<GeminiCandidate>? Candidates);

    private sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiContent? Content);

    private sealed record PromptRequestItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text);

    private sealed record PromptResponseItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("translation")] string? Translation);
}
