namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing <see cref="TS4Translator.Core.Projects.IProjectStringService"/>. Never exposed outside this project.</summary>
internal sealed class ProjectStringRecord
{
    public int Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>
    /// Full path of the imported `.package` this string came from. Null for rows stored before the app
    /// tracked it; re-importing the project fills it in.
    /// </summary>
    public string? SourceFile { get; set; }

    public uint StblGroup { get; set; }

    public ulong StblFullInstance { get; set; }

    public uint KeyHash { get; set; }

    public required string OriginalText { get; set; }

    public string? TranslatedText { get; set; }

    /// <summary>Stored as text (enum name) rather than int so the schema stays readable if inspected directly.</summary>
    public required string Status { get; set; }

    public string? IssueSummary { get; set; }

    public ProjectRecord? Project { get; set; }
}
