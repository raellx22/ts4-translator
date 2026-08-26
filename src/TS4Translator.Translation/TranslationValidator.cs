using System.Text.RegularExpressions;
using TS4Translator.Core.Tokens;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation;

/// <summary>
/// Default <see cref="ITranslationValidator"/>. Combines token-restoration issues with independent
/// heuristic checks (empty translation, untranslated content, length discrepancy, unbalanced markup).
/// Thresholds are deliberately generous — languages vary widely in expansion/contraction — so this
/// flags outliers for human review rather than trying to auto-reject them.
/// </summary>
public sealed partial class TranslationValidator(
    double minLengthRatio = 0.15,
    double maxLengthRatio = 6.0) : ITranslationValidator
{
    [GeneratedRegex(@"<(?<closing>/?)(?<name>[a-zA-Z][\w:-]*)[^<>]*?(?<selfclose>/?)>")]
    private static partial Regex TagPattern();

    public IReadOnlyList<ValidationIssue> Validate(string originalText, string translatedText, TokenRestoreResult? tokenRestoreResult = null)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(translatedText);

        var issues = new List<ValidationIssue>();

        if (tokenRestoreResult is not null)
        {
            issues.AddRange(tokenRestoreResult.Issues
                .Where(i => i.Kind != TokenValidationIssueKind.ReorderedToken)
                .Select(ToValidationIssue));
        }

        var originalTrimmed = originalText.Trim();
        var translatedTrimmed = translatedText.Trim();

        if (originalTrimmed.Length > 0 && translatedTrimmed.Length == 0)
        {
            issues.Add(new ValidationIssue(ValidationIssueCode.EmptyTranslation, ValidationSeverity.Critical,
                "Translation is empty but the source text is not."));
            return issues;
        }

        if (originalTrimmed.Length > 0
            && string.Equals(originalTrimmed, translatedTrimmed, StringComparison.Ordinal)
            && originalTrimmed.Any(char.IsLetter))
        {
            issues.Add(new ValidationIssue(ValidationIssueCode.IdenticalToSource, ValidationSeverity.Warning,
                "Translation is identical to the source text; it may not have been translated."));
        }

        if (originalTrimmed.Length >= 3 && translatedTrimmed.Length > 0)
        {
            var ratio = (double)translatedTrimmed.Length / originalTrimmed.Length;
            if (ratio < minLengthRatio || ratio > maxLengthRatio)
            {
                issues.Add(new ValidationIssue(ValidationIssueCode.LengthDiscrepancy, ValidationSeverity.Warning,
                    $"Translated length ({translatedTrimmed.Length}) is disproportionate to source length ({originalTrimmed.Length})."));
            }
        }

        if (!HasBalancedMarkup(translatedTrimmed))
        {
            issues.Add(new ValidationIssue(ValidationIssueCode.UnbalancedMarkup, ValidationSeverity.Error,
                "Translated text contains unbalanced or mismatched markup tags."));
        }

        return issues;
    }

    private static ValidationIssue ToValidationIssue(TokenValidationIssue issue)
    {
        var code = issue.Kind switch
        {
            TokenValidationIssueKind.MissingToken => ValidationIssueCode.MissingToken,
            TokenValidationIssueKind.DuplicatedToken => ValidationIssueCode.DuplicatedToken,
            TokenValidationIssueKind.UnexpectedToken => ValidationIssueCode.UnexpectedToken,
            TokenValidationIssueKind.ModifiedPlaceholder => ValidationIssueCode.ModifiedPlaceholder,
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue.Kind, "Unmapped token validation issue kind."),
        };

        return new ValidationIssue(code, ValidationSeverity.Critical, issue.Detail);
    }

    private static bool HasBalancedMarkup(string text)
    {
        var stack = new Stack<string>();

        foreach (Match match in TagPattern().Matches(text))
        {
            var isClosing = match.Groups["closing"].Value == "/";
            var isSelfClosing = match.Groups["selfclose"].Value == "/";
            var name = match.Groups["name"].Value;

            if (isSelfClosing)
            {
                continue;
            }

            if (isClosing)
            {
                if (stack.Count == 0 || !string.Equals(stack.Peek(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                stack.Pop();
            }
            else
            {
                stack.Push(name);
            }
        }

        return stack.Count == 0;
    }
}
