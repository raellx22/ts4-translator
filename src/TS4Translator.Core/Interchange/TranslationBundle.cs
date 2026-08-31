using System.Globalization;
using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Interchange;

/// <param name="Id">The project string's persistence id — the anchor that survives the round trip.</param>
/// <param name="Original">Source text, exactly as extracted.</param>
/// <param name="CurrentTranslation">Existing translation, offered to the chat model as a draft. Null when there is none.</param>
public sealed record BundleItem(int Id, string Original, string? CurrentTranslation);

/// <param name="ProjectName">Shown in the header so a user with several bundles open can tell them apart.</param>
/// <param name="SourceLocale">Language the originals are in.</param>
/// <param name="TargetLocale">Language the model must produce.</param>
/// <param name="Items">The strings the user chose to send.</param>
/// <param name="PartNumber">Which file of a split export this is, 1-based.</param>
/// <param name="PartCount">How many files the export was split into. 1 means the whole selection is in this file.</param>
/// <param name="Glossary">
/// Terminology the reply has to honour. Carried by the bundle because a chat model is a translation
/// engine like any other, and it was the only one the app never told about the glossary — the strongest
/// engine in the workflow, translating with no terminology at all.
/// </param>
/// <param name="CustomInstructions">The project's tone/style instructions, for the same reason.</param>
public sealed record TranslationBundle(
    string ProjectName,
    CultureInfo SourceLocale,
    CultureInfo TargetLocale,
    IReadOnlyList<BundleItem> Items,
    int PartNumber = 1,
    int PartCount = 1,
    IReadOnlyList<GlossaryTerm>? Glossary = null,
    string? CustomInstructions = null)
{
    /// <summary>True when this bundle is one file of several and the model must not expect the whole mod.</summary>
    public bool IsSplit => PartCount > 1;

    /// <summary>
    /// Only the terms that actually occur in this file's strings — the same rule the provider path
    /// follows. A chat window has a context budget, and a hundred irrelevant terms spend it while
    /// burying the handful that matter.
    /// </summary>
    public IReadOnlyList<GlossaryTerm> RelevantGlossary => Glossary is not { Count: > 0 }
        ? []
        : [.. Glossary.Where(term => Items.Any(item => term.AppearsIn(item.Original)))];
}
