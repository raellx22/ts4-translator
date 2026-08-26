using System.Globalization;

namespace TS4Translator.Core.Translation;

/// <summary>
/// Runs the full per-item pipeline described in brief section 10/11: skip items already in
/// translation memory, protect tokens, call the provider, reconcile results by id, restore tokens,
/// validate, and record new results back to translation memory. Never trusts the provider's response
/// order or count.
/// </summary>
public interface ITranslationOrchestrator
{
    Task<IReadOnlyList<TranslationOutcome>> TranslateAsync(
        IReadOnlyList<TranslationSourceItem> items,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        CancellationToken cancellationToken = default);
}
