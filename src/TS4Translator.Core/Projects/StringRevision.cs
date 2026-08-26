namespace TS4Translator.Core.Projects;

/// <summary>
/// A previous translation value for a <see cref="ProjectStringEntry"/>, kept so a user can recover
/// it after an automatic translation, a review pass, or a manual edit overwrites it (brief section 31).
/// </summary>
public sealed record StringRevision(int Id, int StringEntryId, string PreviousText, DateTimeOffset ChangedAtUtc);
