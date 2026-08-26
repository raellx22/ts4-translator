namespace TS4Translator.Core.Tokens;

/// <param name="Kind">What kind of token discrepancy was detected.</param>
/// <param name="Placeholder">The placeholder involved, when known.</param>
/// <param name="Detail">Human-readable detail for logs and the review UI.</param>
public sealed record TokenValidationIssue(TokenValidationIssueKind Kind, string? Placeholder, string Detail)
{
    /// <summary>
    /// True for issues that mean the restored text cannot be trusted as-is (missing/duplicated/unexpected/modified
    /// tokens). Reordering alone is not fatal — word order legitimately differs across languages.
    /// </summary>
    public bool IsCritical => Kind != TokenValidationIssueKind.ReorderedToken;
}
