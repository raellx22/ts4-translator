using System.Text.RegularExpressions;
using TS4Translator.Core.Tokens;

namespace TS4Translator.Translation;

/// <summary>
/// Default <see cref="ITokenProtector"/>. Protects three kinds of non-translatable content found in
/// Sims 4 STBL strings: substitution tokens (<c>{0.SimFirstName}</c>, <c>{M0.He}</c>, <c>{F0.She}</c>, ...),
/// markup/rich-text tags (<c>&lt;tag&gt;</c>, <c>&lt;/tag&gt;</c>, <c>&lt;tag/&gt;</c>), and line breaks.
/// </summary>
public sealed partial class TokenProtector : ITokenProtector
{
    [GeneratedRegex(@"(\{[^{}\r\n]*\}|</?[a-zA-Z][^<>\r\n]*/?>|https?://[^\s<>]+|@[a-zA-Z0-9_]+|\\r\\n|\\n|\\r|\r\n|\n|\r)")]
    private static partial Regex ExtractionPattern();

    [GeneratedRegex(@"__TS4_TOKEN_\d{4}__")]
    private static partial Regex StrictPlaceholderPattern();

    /// <summary>A well-formed placeholder carrying extra underscores that are not another placeholder.</summary>
    [GeneratedRegex(@"(__TS4_TOKEN_\d{4})__(_+)(?!_*TS4_TOKEN_)")]
    private static partial Regex GluedUnderscorePattern();

    /// <summary>
    /// Anything that starts like a placeholder, however it ends.
    /// </summary>
    /// <remarks>
    /// The previous pattern (<c>__TS4_TOKEN_[^_]*?__</c>) required a closing <c>__</c>, so a model that
    /// emitted the bare stem <c>__TS4_TOKEN_</c> as its whole answer matched nothing and the translation
    /// was accepted as good. That happened on a real run: "Ghost Unemotional" and "Penis Growing Even
    /// Larger!" were both stored as <c>__TS4_TOKEN_</c>, status "Traduzido", with the project reporting
    /// zero strings to review — the worst possible failure, because nothing points at it and the only way
    /// to find it is reading a whole mod line by line.
    ///
    /// Case-insensitive because a model that mangles the marker also mangles its casing.
    /// </remarks>
    [GeneratedRegex(@"__TS4_TOKEN[A-Za-z0-9_]*", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderLikePattern();

    public ProtectedString Protect(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new List<ProtectedToken>();
        var sequence = 0;

        var protectedText = ExtractionPattern().Replace(source, match =>
        {
            sequence++;
            var placeholder = FormatPlaceholder(sequence);
            tokens.Add(new ProtectedToken(placeholder, match.Value, match.Index));
            return placeholder;
        });

        return new ProtectedString(source, protectedText, tokens);
    }

    public TokenRestoreResult Restore(string translatedText, ProtectedString protectedString)
    {
        ArgumentNullException.ThrowIfNull(translatedText);
        ArgumentNullException.ThrowIfNull(protectedString);

        var issues = new List<TokenValidationIssue>();
        var expectedPlaceholders = new HashSet<string>(
            protectedString.Tokens.Select(t => t.Placeholder), StringComparer.Ordinal);

        var foundCounts = StrictPlaceholderPattern().Matches(translatedText)
            .Select(m => m.Value)
            .GroupBy(v => v, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var token in protectedString.Tokens)
        {
            var count = foundCounts.GetValueOrDefault(token.Placeholder, 0);
            if (count == 0)
            {
                issues.Add(new TokenValidationIssue(TokenValidationIssueKind.MissingToken, token.Placeholder,
                    $"Placeholder '{token.Placeholder}' for '{token.OriginalValue}' was not found in the translated text."));
            }
            else if (count > 1)
            {
                issues.Add(new TokenValidationIssue(TokenValidationIssueKind.DuplicatedToken, token.Placeholder,
                    $"Placeholder '{token.Placeholder}' for '{token.OriginalValue}' appears {count} times in the translated text."));
            }
        }

        foreach (var found in foundCounts.Keys)
        {
            if (!expectedPlaceholders.Contains(found))
            {
                issues.Add(new TokenValidationIssue(TokenValidationIssueKind.UnexpectedToken, found,
                    $"Text '{found}' matches the placeholder format but does not correspond to any protected token."));
            }
        }

        // Take the well-formed placeholders out first, then see what placeholder-shaped debris is left.
        // Scanning without removing them cannot work: "\n\n" protects as two adjacent placeholders, and
        // any pattern greedy enough to catch a mangled one swallows that legitimate pair as a single
        // malformed blob. Removing first makes adjacency a non-issue.
        var residue = StrictPlaceholderPattern().Replace(translatedText, " ");
        foreach (Match leftover in PlaceholderLikePattern().Matches(residue))
        {
            issues.Add(new TokenValidationIssue(TokenValidationIssueKind.ModifiedPlaceholder, null,
                $"Text '{leftover.Value}' resembles a token placeholder but does not match the expected format and could not be reconciled."));
        }

        // A placeholder wearing extra underscores restores into the game string with the junk still
        // attached ("...jogo.\n\n__Salve"). The lookahead is what keeps the legitimate adjacent pair,
        // whose four underscores in the middle look identical to the defect.
        foreach (Match glued in GluedUnderscorePattern().Matches(translatedText))
        {
            issues.Add(new TokenValidationIssue(TokenValidationIssueKind.ModifiedPlaceholder, null,
                $"Placeholder '{glued.Groups[1].Value}__' is followed by stray underscores that would end up in the game text."));
        }

        var restored = translatedText;
        foreach (var token in protectedString.Tokens)
        {
            if (foundCounts.GetValueOrDefault(token.Placeholder, 0) > 0)
            {
                restored = restored.Replace(token.Placeholder, token.OriginalValue, StringComparison.Ordinal);
            }
        }

        var hasCountIssue = issues.Any(i =>
            i.Kind is TokenValidationIssueKind.MissingToken or TokenValidationIssueKind.DuplicatedToken);

        if (!hasCountIssue && protectedString.Tokens.Count > 1)
        {
            var appearanceOrder = protectedString.Tokens
                .Select(t => (t.Placeholder, Index: translatedText.IndexOf(t.Placeholder, StringComparison.Ordinal)))
                .OrderBy(t => t.Index)
                .Select(t => t.Placeholder)
                .ToList();
            var originalOrder = protectedString.Tokens.Select(t => t.Placeholder).ToList();

            if (!appearanceOrder.SequenceEqual(originalOrder, StringComparer.Ordinal))
            {
                issues.Add(new TokenValidationIssue(TokenValidationIssueKind.ReorderedToken, null,
                    "Protected tokens appear in a different order than in the source text."));
            }
        }

        var isValid = !issues.Any(i => i.IsCritical);
        return new TokenRestoreResult(restored, isValid, issues);
    }

    private static string FormatPlaceholder(int sequence) => $"__TS4_TOKEN_{sequence:D4}__";
}
