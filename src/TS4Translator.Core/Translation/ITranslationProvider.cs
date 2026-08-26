using System.Globalization;
using TS4Translator.Core.Glossary;

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

    /// <param name="glossaryTerms">
    /// Only the terms relevant to this specific batch (brief section 23 - never send the whole
    /// glossary). A provider whose <see cref="ProviderCapabilities.SupportsGlossary"/> is false is
    /// free to ignore this.
    /// </param>
    Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        CancellationToken cancellationToken = default);
}
