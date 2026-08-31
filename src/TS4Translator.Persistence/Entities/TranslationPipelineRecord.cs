namespace TS4Translator.Persistence.Entities;

/// <summary>
/// Which profile does which job. One row with a null <see cref="ProjectId"/> is the global pipeline;
/// a row carrying a project id is that project's override.
/// </summary>
internal sealed class TranslationPipelineRecord
{
    public int Id { get; set; }

    /// <summary>Null for the global pipeline; otherwise the project this override belongs to.</summary>
    public Guid? ProjectId { get; set; }

    public int? PrimaryProfileId { get; set; }

    public int? RefinementProfileId { get; set; }

    public int? ReviewProfileId { get; set; }

    public bool TieredRoutingEnabled { get; set; }

    public int? MediumProfileId { get; set; }

    public int MediumThreshold { get; set; }

    public int? DifficultProfileId { get; set; }

    public int DifficultThreshold { get; set; }

    public ProjectRecord? Project { get; set; }
}
