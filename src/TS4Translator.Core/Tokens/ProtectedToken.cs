namespace TS4Translator.Core.Tokens;

/// <summary>
/// One placeholder, markup tag, or line break extracted from a source string before translation.
/// </summary>
/// <param name="Placeholder">The opaque marker substituted into the protected text, e.g. <c>__TS4_TOKEN_0001__</c>.</param>
/// <param name="OriginalValue">The exact original substring this placeholder stands in for.</param>
/// <param name="OriginalIndex">Character offset of <paramref name="OriginalValue"/> in the original source text.</param>
public sealed record ProtectedToken(string Placeholder, string OriginalValue, int OriginalIndex);
