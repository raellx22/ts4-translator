using System.Globalization;

namespace TS4Translator.Core.Translation;

/// <summary>
/// A pluggable translation mechanism (Argos, NVIDIA NIM, an OpenAI-compatible endpoint, ...).
/// Implementations never see raw source text with tokens intact — callers always run it through
/// <see cref="TS4Translator.Core.Tokens.ITokenProtector"/> first — and are never trusted to preserve
/// item order or count: every result is matched back to its request by <see cref="TranslationRequestItem.Id"/>.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>Display name shown in the UI and stored alongside translation memory entries.</summary>
    string Name { get; }

    ProviderCapabilities Capabilities { get; }

    Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        CancellationToken cancellationToken = default);
}
