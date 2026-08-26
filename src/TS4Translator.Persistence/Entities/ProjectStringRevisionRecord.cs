namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing the history exposed by <see cref="TS4Translator.Core.Projects.IProjectStringService.GetHistoryAsync"/>.</summary>
internal sealed class ProjectStringRevisionRecord
{
    public int Id { get; set; }

    public int StringEntryId { get; set; }

    public required string PreviousText { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }

    public ProjectStringRecord? StringEntry { get; set; }
}
