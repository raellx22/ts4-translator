namespace TS4Translator.Core.Tokens;

/// <param name="RestoredText">The translated text with recognized placeholders substituted back to their original values.</param>
/// <param name="IsValid">False when at least one critical issue (see <see cref="TokenValidationIssue.IsCritical"/>) was found.</param>
/// <param name="Issues">Every discrepancy found while reconciling placeholders, critical or not.</param>
public sealed record TokenRestoreResult(string RestoredText, bool IsValid, IReadOnlyList<TokenValidationIssue> Issues);
