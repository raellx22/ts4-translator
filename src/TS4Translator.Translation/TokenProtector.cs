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
    [GeneratedRegex(@"(\{[^{}\r\n]*\}|</?[a-zA-Z][^<>\r\n]*/?>|\r\n|\n|\r)")]
    private static partial Regex ExtractionPattern();

    [GeneratedRegex(@"__TS4_TOKEN_\d{4}__")]
    private static partial Regex StrictPlaceholderPattern();

    [GeneratedRegex(@"__TS4_TOKEN_[^_]*?__")]
    private static partial Regex LoosePlaceholderPattern();

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

        foreach (Match looseMatch in LoosePlaceholderPattern().Matches(translatedText))
        {
            if (!StrictPlaceholderPattern().IsMatch(looseMatch.Value))
            {
                issues.Add(new TokenValidationIssue(TokenValidationIssueKind.ModifiedPlaceholder, null,
                    $"Text '{looseMatch.Value}' resembles a token placeholder but does not match the expected format and could not be reconciled."));
            }
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
