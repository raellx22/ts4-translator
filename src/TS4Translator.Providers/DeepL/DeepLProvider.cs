using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Resilience;

namespace TS4Translator.Providers.DeepL;

/// <summary>
/// <see cref="ITranslationProvider"/> for the DeepL API (brief section 18) - a dedicated translation
/// endpoint, not a chat/completions model, so there is no system prompt to plug a glossary or custom
/// style instructions into (<see cref="Capabilities"/> reports both as unsupported, honestly, rather
/// than pretending to honor them). DeepL's own glossary feature (a separate resource uploaded via its
/// own API) and its <c>tag_handling=xml</c> option for better token protection are real DeepL features
/// this provider does not use yet - a reasonable follow-up once basic translation is proven working.
/// A transient failure (503, 429, timeout, ...) is retried with backoff via <see cref="HttpRetry"/>
/// before giving up (brief section 11).
/// </summary>
public sealed class DeepLProvider(HttpClient httpClient, DeepLProviderOptions options, int maxRetries = 3) : ITranslationProvider
{
    public string Name => "deepl";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true, SupportsStreaming: false, SupportsCustomPrompt: false,
        SupportsGlossary: false, SupportsLocalExecution: false, RequiresApiKey: true, MaxContextSize: null);

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

        var requestBody = new DeepLRequest(
            items.Select(i => i.ProtectedText).ToList(),
            ToDeepLSourceLang(sourceLocale),
            ToDeepLTargetLang(targetLocale));

        using var response = await HttpRetry.SendWithRetryAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Endpoint.TrimEnd('/')}/v2/translate")
                {
                    Content = JsonContent.Create(requestBody),
                };
                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", options.ApiKey);
                }

                return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            },
            cancellationToken,
            maxRetries).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken).ConfigureAwait(false);
        var translations = body?.Translations ?? [];

        // DeepL correlates results with requests by array POSITION - it has no concept of the "id"
        // TranslationRequestItem carries, and always returns exactly one translation per input text in
        // the same order (documented DeepL behavior). Reconciliation by position happens here, at the
        // provider boundary, precisely so nothing downstream (TranslationOrchestrator) ever has to
        // trust position - it only ever sees id-tagged results. If DeepL ever returns fewer entries
        // than requested, the missing tail items simply have no result here, and the orchestrator's
        // existing "provider returned no result for item" handling covers it without any special case.
        var count = Math.Min(items.Count, translations.Count);
        return Enumerable.Range(0, count)
            .Select(i => new TranslationResultItem(items[i].Id, translations[i].Text))
            .ToList();
    }

    /// <summary>
    /// DeepL wants just the base language for source (it auto-detects dialect), but requires the
    /// region variant as the target for languages where it distinguishes them. This covers DeepL's
    /// most common documented special cases (English and Portuguese); an unusual target pair may need
    /// a specific code DeepL expects that isn't covered by this simple mapping.
    /// </summary>
    public static string ToDeepLTargetLang(CultureInfo locale)
    {
        var twoLetter = locale.TwoLetterISOLanguageName.ToUpperInvariant();
        return twoLetter switch
        {
            "EN" => locale.Name.EndsWith("GB", StringComparison.OrdinalIgnoreCase) ? "EN-GB" : "EN-US",
            "PT" => locale.Name.EndsWith("PT", StringComparison.OrdinalIgnoreCase) ? "PT-PT" : "PT-BR",
            _ => twoLetter,
        };
    }

    public static string ToDeepLSourceLang(CultureInfo locale) => locale.TwoLetterISOLanguageName.ToUpperInvariant();

    private sealed record DeepLRequest(
        [property: JsonPropertyName("text")] List<string> Text,
        [property: JsonPropertyName("source_lang")] string SourceLang,
        [property: JsonPropertyName("target_lang")] string TargetLang);

    private sealed record DeepLResponse([property: JsonPropertyName("translations")] List<DeepLTranslation>? Translations);

    private sealed record DeepLTranslation([property: JsonPropertyName("text")] string Text);
}
