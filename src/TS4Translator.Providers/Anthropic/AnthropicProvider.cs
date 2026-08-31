using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Prompting;
using TS4Translator.Providers.Resilience;

namespace TS4Translator.Providers.Anthropic;

/// <summary>
/// <see cref="ITranslationProvider"/> for Anthropic's Messages API. Same "send a JSON array, ask for a
/// JSON array back" contract as the other chat providers here, with Anthropic's own envelope: the
/// system prompt is a top-level field rather than a message, auth is <c>x-api-key</c> rather than a
/// bearer token, and the API version header is mandatory.
/// </summary>
public sealed class AnthropicProvider(HttpClient httpClient, AnthropicProviderOptions options, int maxRetries = 3) : ITranslationProvider
{
    private const string ApiVersion = "2023-06-01";

    /// <summary>Anthropic requires an explicit output cap. Sized off the input, since a translation is roughly as long as its source.</summary>
    private const int OutputTokensPerCharacter = 1;
    private const int MinimumOutputTokens = 1024;
    private const int MaximumOutputTokens = 16_384;

    public string Name { get; } = $"anthropic:{options.Model}";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true, SupportsStreaming: false, SupportsCustomPrompt: true,
        SupportsGlossary: true, SupportsLocalExecution: false, RequiresApiKey: true, MaxContextSize: null,
        RecommendedConcurrency: 4,
        RecommendedBatchItems: 80,
        RecommendedBatchCharacters: 6400);

    public async Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        if (items.Count == 0)
        {
            return [];
        }

        var requestBody = new MessagesRequest(
            options.Model,
            CalculateMaxOutputTokens(items),
            options.Temperature,
            TranslationPrompt.BuildSystemPrompt(sourceLocale, targetLocale, glossaryTerms, customInstructions),
            [new AnthropicMessage("user", TranslationPrompt.SerializeRequestItems(items))]);

        using var response = await HttpRetry.SendWithRetryAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Endpoint.TrimEnd('/')}/v1/messages")
                {
                    Content = JsonContent.Create(requestBody),
                };

                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
                }

                request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
                return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            },
            cancellationToken,
            maxRetries).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var completion = await response.Content.ReadFromJsonAsync<MessagesResponse>(cancellationToken).ConfigureAwait(false);
        var content = completion?.Content?.FirstOrDefault(block => block.Type == "text")?.Text ?? string.Empty;

        // A structurally valid object with a null id or translation is not a usable result; drop it and
        // let TranslationOrchestrator report that id as missing rather than passing null downstream.
        return TranslationPrompt.ParseWithRecovery(content)
            .Where(item => item.Id is not null && item.Translation is not null)
            .Select(item => new TranslationResultItem(item.Id!, item.Translation!))
            .ToList();
    }

    private static int CalculateMaxOutputTokens(IReadOnlyList<TranslationRequestItem> items)
    {
        var characters = items.Sum(item => item.ProtectedText.Length);
        return Math.Clamp(characters * OutputTokensPerCharacter, MinimumOutputTokens, MaximumOutputTokens);
    }

    private sealed record MessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MessagesResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}
