namespace TS4Translator.Core.Projects;

/// <param name="Total">Every string stored for the project.</param>
/// <param name="Pending">Not translated yet.</param>
/// <param name="NeedsReview">Translated but flagged by validation or by a failed refinement.</param>
/// <param name="Done">Translated, refined or manually edited — anything the user does not still owe work on.</param>
public sealed record ProjectProgress(int Total, int Pending, int NeedsReview, int Done);
