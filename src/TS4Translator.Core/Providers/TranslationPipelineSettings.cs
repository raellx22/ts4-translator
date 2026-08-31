namespace TS4Translator.Core.Providers;

/// <summary>
/// Which profile does which job, plus the tiered routing setup. Stored once globally and, optionally,
/// once more per project that wants to override it.
/// </summary>
/// <param name="PrimaryProfileId">Engine for the bulk pass, and the lane that receives the easiest strings.</param>
/// <param name="RefinementProfileId">Engine for per-row refinement. Falls back to <paramref name="PrimaryProfileId"/> when null.</param>
/// <param name="ReviewProfileId">Engine for the batch sweep over strings flagged for review. Falls back to the refinement engine, then the primary one.</param>
/// <param name="TieredRoutingEnabled">
/// When on, the bulk pass splits its work by how hard each string looks and runs the lanes at the same
/// time, so the slow expensive engine only ever handles the minority of strings that need it.
/// </param>
/// <param name="MediumProfileId">Engine for the middle band. Null collapses that band into the primary lane, leaving two engines.</param>
/// <param name="MediumThreshold">Score at or above which a string leaves the primary lane.</param>
/// <param name="DifficultProfileId">Engine for the hardest band.</param>
/// <param name="DifficultThreshold">Score at or above which a string goes to the hardest lane.</param>
public sealed record TranslationPipelineSettings(
    int? PrimaryProfileId = null,
    int? RefinementProfileId = null,
    int? ReviewProfileId = null,
    bool TieredRoutingEnabled = false,
    int? MediumProfileId = null,
    // Literals rather than the constants below: a record's primary constructor cannot reference a
    // member of the type it is declaring.
    int MediumThreshold = 25,
    int? DifficultProfileId = null,
    int DifficultThreshold = 35)
{
    /// <summary>
    /// Chosen against the real WickedWhims corpus: short UI labels score well under it, while
    /// multi-sentence flavour text carrying placeholders scores above — the split where a literal
    /// engine starts losing meaning. At this value the hardest lane receives about a fifth of the mod.
    /// </summary>
    public const int DefaultDifficultThreshold = 35;

    /// <summary>Lower band boundary. Between this and <see cref="DefaultDifficultThreshold"/> sits text that is more than a label but not yet prose.</summary>
    public const int DefaultMediumThreshold = 25;

    /// <summary>The profile that should handle a given role, applying the documented fallbacks.</summary>
    public int? ResolveProfileId(TranslationRole role) => role switch
    {
        TranslationRole.Primary => PrimaryProfileId,
        TranslationRole.Refinement => RefinementProfileId ?? PrimaryProfileId,
        TranslationRole.Review => ReviewProfileId ?? RefinementProfileId ?? PrimaryProfileId,
        _ => PrimaryProfileId,
    };

    /// <summary>Whether routing would actually engage a second engine, rather than just being switched on.</summary>
    public bool IsTieredRoutingUsable =>
        TieredRoutingEnabled &&
        ((DifficultProfileId is not null && DifficultProfileId != PrimaryProfileId) ||
         (MediumProfileId is not null && MediumProfileId != PrimaryProfileId));

    /// <summary>Thresholds the user can set independently but which must stay ordered to mean anything.</summary>
    public TranslationPipelineSettings Normalized() => this with
    {
        MediumThreshold = Math.Clamp(MediumThreshold, 0, 100),
        DifficultThreshold = Math.Clamp(Math.Max(DifficultThreshold, MediumThreshold), 0, 100),
    };
}
