using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Translation.Tests;

public enum FakeProviderMisbehavior
{
    None,
    DropFirstResult,
    DuplicateFirstResult,
    StripTokensFromFirstResult,
    NullTranslationForFirstResult,
}

/// <summary>Configurable <see cref="ITranslationProvider"/> test double for orchestrator tests.</summary>
public sealed class FakeTranslationProvider(
    Func<string, string>? translate = null,
    FakeProviderMisbehavior misbehavior = FakeProviderMisbehavior.None,
    int recommendedConcurrency = 1,
    TimeSpan? delay = null,
    int? recommendedBatchItems = null,
    int? recommendedBatchCharacters = null,
    bool supportsCustomPrompt = false) : ITranslationProvider
{
    private readonly Func<string, string> _translate = translate ?? (text => $"[fake] {text}");
    private int _activeCalls;
    private int _maxConcurrentCalls;

    public string Name => "fake";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true, SupportsStreaming: false, SupportsCustomPrompt: supportsCustomPrompt,
        SupportsGlossary: false, SupportsLocalExecution: true, RequiresApiKey: false, MaxContextSize: null,
        RecommendedConcurrency: recommendedConcurrency,
        RecommendedBatchItems: recommendedBatchItems,
        RecommendedBatchCharacters: recommendedBatchCharacters);

    public int MaxConcurrentCalls => _maxConcurrentCalls;

    public List<int> ObservedBatchSizes { get; } = [];

    public List<IReadOnlyList<GlossaryTerm>?> ObservedGlossaryTerms { get; } = [];

    public List<string?> ObservedCustomInstructions { get; } = [];

    /// <summary>Every request item the provider was handed, so a test can assert on per-item fields.</summary>
    public List<TranslationRequestItem> ObservedRequestItems { get; } = [];

    public async Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items, CultureInfo sourceLocale, CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null, string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var activeCalls = Interlocked.Increment(ref _activeCalls);
        int observedMaximum;
        do
        {
            observedMaximum = _maxConcurrentCalls;
        }
        while (activeCalls > observedMaximum && Interlocked.CompareExchange(ref _maxConcurrentCalls, activeCalls, observedMaximum) != observedMaximum);

        try
        {
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            lock (ObservedBatchSizes)
            {
                ObservedBatchSizes.Add(items.Count);
                ObservedGlossaryTerms.Add(glossaryTerms);
                ObservedCustomInstructions.Add(customInstructions);
                ObservedRequestItems.AddRange(items);
            }

            var results = items
                .Select(item => new TranslationResultItem(item.Id, _translate(item.ProtectedText)))
                .ToList();

            if (misbehavior == FakeProviderMisbehavior.DropFirstResult && results.Count > 0)
            {
                results.RemoveAt(0);
            }
            else if (misbehavior == FakeProviderMisbehavior.DuplicateFirstResult && results.Count > 0)
            {
                results.Add(results[0]);
            }
            else if (misbehavior == FakeProviderMisbehavior.StripTokensFromFirstResult && results.Count > 0)
            {
                results[0] = results[0] with { TranslatedProtectedText = "text with token removed" };
            }
            else if (misbehavior == FakeProviderMisbehavior.NullTranslationForFirstResult && results.Count > 0)
            {
                // Simulates a real bug found in production: a provider whose JSON response has
                // "translation": null for one id. TranslationResultItem.TranslatedProtectedText is declared
                // non-nullable, but nothing stops a misbehaving provider from constructing one with null
                // anyway (the compiler only warns; it does not throw) - the orchestrator must defend
                // against this itself rather than trust every ITranslationProvider implementation to be
                // well-behaved.
                results[0] = results[0] with { TranslatedProtectedText = null! };
            }

            return results;
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }
}
