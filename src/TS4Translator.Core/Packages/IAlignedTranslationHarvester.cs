using System.Globalization;

namespace TS4Translator.Core.Packages;

/// <param name="OriginalText">The source-language string, from a mod's own STBL.</param>
/// <param name="TranslatedText">What a translator rendered it as, from a translation package.</param>
/// <param name="SourcePackage">
/// The translation package this came from, without extension — the name a translator published under.
/// </param>
/// <remarks>
/// <paramref name="SourcePackage"/> is carried per pair rather than summarised once, because it has two
/// jobs that a header list cannot do: it credits the translator on the row itself, wherever that row
/// later shows up, and it makes one translator's contribution removable without disturbing the rest.
/// </remarks>
public sealed record AlignedTranslation(string OriginalText, string TranslatedText, string SourcePackage);

/// <param name="PackagesScanned">How many `.package` files were opened across both sides.</param>
/// <param name="SourceStrings">Distinct source-language strings indexed.</param>
/// <param name="TranslatedStrings">Distinct translated strings indexed.</param>
/// <param name="AmbiguousKeys">
/// Keys dropped because the same hash carried different text in different mods — a collision that makes
/// the pairing a guess, so it is refused.
/// </param>
/// <param name="Pairs">Every usable pair, deduplicated.</param>
public sealed record TranslationHarvest(
    int PackagesScanned,
    int SourceStrings,
    int TranslatedStrings,
    int AmbiguousKeys,
    IReadOnlyList<AlignedTranslation> Pairs);

/// <summary>
/// Recovers finished translations that already exist on disk, by pairing a mod's own strings with a
/// translation package written for it.
/// </summary>
/// <remarks>
/// The Sims 4 stores every string under a <c>KeyHash</c> that is stable across locales: a translation
/// package is the same table with the same hashes and different text. So a community translation and the
/// mod it was written for align exactly, with no similarity matching, no model, and no guessing — the
/// hash either matches or it does not.
///
/// <para>That turns a folder of downloaded translations into two things the app has been short of.
/// Measured on this user's library: 251 translation packages against 15.111 installed mods produced
/// <b>19.255 aligned pairs</b> — 7.185 short labels, which are glossary material, and 12.070 sentences,
/// which are translation memory. For comparison the hand-built default glossary holds 386 terms. The
/// pairs arrive with placeholders and gendered variants intact, because they were written by people
/// translating that exact string: <c>Streetwalker</c> came back as
/// <c>{M0.Garoto}{F0.Garota} de Programa</c>, and <c>John</c> as <c>Cliente</c> rather than a name.</para>
///
/// <para>Collisions are refused rather than resolved. A hash that carries two different source strings
/// across two mods cannot be paired without picking one, so both are dropped — 1.884 of them in that
/// run. Silence is the right answer where the data is ambiguous; this feeds a glossary and a memory that
/// steer every future translation.</para>
/// </remarks>
public interface IAlignedTranslationHarvester
{
    /// <summary>
    /// Pairs source strings found under <paramref name="sourceFolders"/> with translations found under
    /// <paramref name="translatedFolders"/>.
    /// </summary>
    /// <remarks>
    /// The two sets may be the same folder: a mod that ships several locales inside one package pairs
    /// with itself, which is how the 83 self-translated mods in the library were found.
    /// </remarks>
    Task<TranslationHarvest> HarvestAsync(
        IReadOnlyList<string> sourceFolders,
        IReadOnlyList<string> translatedFolders,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
