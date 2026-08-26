namespace TS4Translator.Persistence.Entities;

/// <summary>One imported `.package` file belonging to a <see cref="ProjectRecord"/>. A project can span multiple files (brief section 34).</summary>
internal sealed class ProjectSourceFileRecord
{
    public int Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string FilePath { get; set; }

    public ProjectRecord? Project { get; set; }
}
