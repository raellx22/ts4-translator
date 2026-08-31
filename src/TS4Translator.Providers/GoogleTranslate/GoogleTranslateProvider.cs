using System.Globalization;
using System.Text.Json;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Resilience;

namespace TS4Translator.Providers.GoogleTranslate;

/// <summary>
/// Google Translate through its public `translate_a/single` endpoint — the same one the web widget
/// uses. **No API key, no endpoint, no model**: unlike <c>GoogleGeminiProvider</c> (a paid LLM API
/// that happens to come from the same company), this needs zero configuration, which is why the
/// Settings page treats it like Argos and shows no credential fields at all.
/// </summary>
/// <remarks>
/// This endpoint is public but undocumented and unsupported: it throttles aggressively per IP
/// (HTTP 429), and Google can change or withdraw it without notice. That is an acceptable trade for a
/// free, zero-setup quality step above Argos, but it is the reason concurrency is capped low and every
/// request goes through <see cref="HttpRetry"/>. It is not a substitute for the paid Cloud Translation
/// API in anything that needs a uptime guarantee.
///
/// The response is a deeply nested JSON array rather than an object — roughly
/// <c>[[["translated","original",null,null,10],...],null,"en",...]</c> — with the sentence chunks the
/// service split the input into. Every chunk's first element is concatenated back together to rebuild
/// the full translation.
/// </remarks>
public sealed class GoogleTranslateProvider(HttpClient httpClient, GoogleTranslateProviderOptions? options = null, int maxRetries = 3) : ITranslationProvider
{
    private readonly GoogleTranslateProviderOptions options = options ?? new GoogleTranslateProviderOptions();

    private const string UserAgent = "TS4Translator/1.0";

    public string Name => "google-translate-free";

    public ProviderCapabilities Capabilities { get; } = new(
        // One HTTP request per string: the endpoint takes a single "q" and gives no way to correlate
        // several inputs to several outputs, so batching would be guesswork by position.
        SupportsBatch: false,
        SupportsStreaming: false,
        SupportsCustomPrompt: false,
        SupportsGlossary: false,
        SupportsLocalExecution: false,
        RequiresApiKey: false,
        MaxContextSize: 5000,
        RecommendedConcurrency: 1,
        RecommendedBatchItems: 64,
        RecommendedBatchCharacters: 20_000);

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

        var source = ToLanguageCode(sourceLocale);
        var target = ToLanguageCode(targetLocale);

        var results = new List<TranslationResultItem>(items.Count);
        var delay = this.options.EffectiveDelay;
        HttpRequestException? lastFailure = null;

        using var gate = new SemaphoreSlim(Math.Max(1, this.options.MaxConcurrency));
        var tasks = items.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Paced rather than fired all at once - see GoogleTranslateProviderOptions. The first
                // item goes immediately so a single-string refinement stays instant.
                if (index > 0 && delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                var translated = await TranslateOneAsync(item.ProtectedText, source, target, cancellationToken).ConfigureAwait(false);
                return translated is null ? null : new TranslationResultItem(item.Id, translated);
            }
            catch (HttpRequestException exception)
            {
                // One throttled string must not destroy a whole run's worth of work. The item is
                // dropped (the orchestrator then reports it as "no result", and it stays pending for a
                // later attempt) while the rest of the batch keeps whatever succeeded. The exception is
                // remembered so a batch where *everything* failed can still say why - silently
                // returning an empty list would look like the service simply had nothing to say.
                lastFailure = exception;
                return null;
            }
            finally
            {
                gate.Release();
            }
        });

        // A null result means this endpoint gave nothing usable for that id. Dropping it here lets
        // TranslationOrchestrator report it as a per-item failure instead of inventing a translation.
        results.AddRange((await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<TranslationResultItem>());

        if (results.Count == 0 && lastFailure is not null)
        {
            throw new HttpRequestException(
                $"O Google Tradutor recusou todas as {items.Count} requisições ({lastFailure.StatusCode}). " +
                "O endpoint gratuito limita requisições por IP; espere alguns minutos e tente de novo, " +
                "ou use outro provider para volumes grandes.",
                lastFailure,
                lastFailure.StatusCode);
        }

        return results;
    }

    private async Task<string?> TranslateOneAsync(string text, string source, string target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var url = $"{this.options.Endpoint.TrimEnd('/')}/translate_a/single?client=gtx&sl={source}&tl={target}&dt=t&q={Uri.EscapeDataString(text)}";

        using var response = await HttpRetry.SendWithRetryAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Not optional and not cosmetic: with no User-Agent at all this endpoint answers 429
                // with an HTML "Sorry..." page on the very first request, forever - verified against
                // the live service. Any non-empty value is accepted, so it identifies this app
                // honestly rather than impersonating a browser.
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            },
            cancellationToken,
            maxRetries).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ExtractTranslation(payload);
    }

    /// <summary>
    /// Rebuilds the translated text out of the endpoint's nested-array response. Returns null when the
    /// shape is anything other than what is expected — never a partial or guessed string.
    /// </summary>
    public static string? ExtractTranslation(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var sentences = document.RootElement[0];
            if (sentences.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var sentence in sentences.EnumerateArray())
            {
                if (sentence.ValueKind != JsonValueKind.Array || sentence.GetArrayLength() == 0)
                {
                    continue;
                }

                var chunk = sentence[0];
                if (chunk.ValueKind == JsonValueKind.String)
                {
                    builder.Append(chunk.GetString());
                }
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Google Translate wants "pt-BR" for Brazilian Portuguese but plain two-letter codes elsewhere;
    /// sending "en-US" where "en" is expected is accepted, but the two-letter form is what the widget
    /// itself sends, so stay with what is known to work.
    /// </summary>
    private static string ToLanguageCode(CultureInfo locale) =>
        locale.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? "pt-BR"
            : locale.TwoLetterISOLanguageName;
}
