using System.Globalization;

namespace TS4Translator.Core.Projects;

/// <param name="Id">Stable identity, independent of the project's name (which the user can rename).</param>
/// <param name="Name">User-facing project name, typically the mod's name.</param>
/// <param name="SourcePackagePaths">Absolute paths to every <c>.package</c> file imported into this project.</param>
/// <param name="SourceLocale">The mod's original language.</param>
/// <param name="TargetLocale">The language being translated into.</param>
/// <param name="CreatedAtUtc">When the project was created.</param>
/// <param name="UpdatedAtUtc">When the project was last modified (import, translation, export, rename, ...).</param>
/// <param name="CustomInstructions">
/// Free-text style/tone instructions appended to the system prompt for every translation run in this
/// project (brief section 22 - a partial "Translation Profile": one set of instructions per project,
/// not a reusable named library of presets shared across projects). Null/empty means no customization.
/// </param>
public sealed record TranslationProject(
    Guid Id,
    string Name,
    IReadOnlyList<string> SourcePackagePaths,
    CultureInfo SourceLocale,
    CultureInfo TargetLocale,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? CustomInstructions = null);
