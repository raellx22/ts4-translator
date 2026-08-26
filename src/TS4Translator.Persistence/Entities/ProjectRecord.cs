namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing <see cref="TS4Translator.Core.Projects.IProjectService"/>. Never exposed outside this project.</summary>
internal sealed class ProjectRecord
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>BCP-47 name of the source <see cref="System.Globalization.CultureInfo"/> (e.g. "en-US").</summary>
    public required string SourceLocale { get; set; }

    /// <summary>BCP-47 name of the target <see cref="System.Globalization.CultureInfo"/> (e.g. "pt-BR").</summary>
    public required string TargetLocale { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<ProjectSourceFileRecord> SourceFiles { get; set; } = [];
}
