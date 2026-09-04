using System.Text.RegularExpressions;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Tokens;

namespace TS4Translator.Translation;

/// <summary>
/// Default <see cref="ITokenProtector"/>. Protects three kinds of non-translatable content found in
/// Sims 4 STBL strings: substitution tokens (<c>{0.SimFirstName}</c>, <c>{M0.He}</c>, <c>{F0.She}</c>, ...),
/// markup/rich-text tags (<c>&lt;tag&gt;</c>, <c>&lt;/tag&gt;</c>, <c>&lt;tag/&gt;</c>), and line breaks —
/// plus, when the caller asks for it, the glossary terms that must not be left to the engine's judgement.
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

    public ProtectedString Protect(string source) => Protect(source, enforcement: null);

    public ProtectedString Protect(string source, GlossaryEnforcement? enforcement)
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

        if (enforcement is not null)
        {
            protectedText = EnforceGlossary(protectedText, enforcement, tokens, ref sequence);
        }

        return new ProtectedString(source, protectedText, OrderByAppearance(tokens, protectedText));
    }

    /// <summary>
    /// Takes the enforced glossary terms out of <paramref name="protectedText"/>, recording a token that
    /// restores each one to the text the glossary requires.
    /// </summary>
    /// <remarks>
    /// Longest match first and never overlapping, so <c>Sex Autonomy Settings</c> claims its span before
    /// <c>Sex</c> can take a bite out of it. Regions already occupied by a placeholder are reserved up
    /// front: a term can only match on word boundaries and a placeholder is all word characters, so this
    /// is belt and braces rather than a live risk — but the cost of being wrong is a corrupted marker,
    /// which is the failure this whole file exists to prevent.
    ///
    /// <para>If the substitutions would consume every letter in the string, the whole pass is abandoned
    /// and the text goes to the engine unchanged. Handing a model a string that is nothing but
    /// placeholders is asking it to echo markers, which is precisely the prompt shape that made a 7B
    /// answer with the bare stem <c>__TS4_TOKEN_</c>. A string that <i>is</i> a glossary term should
    /// never reach here at all — <c>TranslationOrchestrator</c> answers it from the glossary without an
    /// engine — so this only guards the odd case where a term covers a string with nothing else in it.</para>
    /// </remarks>
    private static string EnforceGlossary(
        string protectedText,
        GlossaryEnforcement enforcement,
        List<ProtectedToken> tokens,
        ref int sequence)
    {
        var terms = enforcement.TermsIn(protectedText);
        if (terms.Count == 0)
        {
            return protectedText;
        }

        var claimed = new bool[protectedText.Length];
        foreach (Match placeholder in StrictPlaceholderPattern().Matches(protectedText))
        {
            for (var index = placeholder.Index; index < placeholder.Index + placeholder.Length; index++)
            {
                claimed[index] = true;
            }
        }

        var replacements = new List<(int Start, int Length, GlossaryTerm Term, string Matched)>();

        foreach (var term in terms)
        {
            var comparison = term.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var search = 0;
            while (search <= protectedText.Length - term.Original.Length)
            {
                var start = protectedText.IndexOf(term.Original, search, comparison);
                if (start < 0)
                {
                    break;
                }

                var end = start + term.Original.Length;
                search = start + 1;

                if (!IsWholeWord(protectedText, start, end) || IsClaimed(claimed, start, end))
                {
                    continue;
                }

                for (var index = start; index < end; index++)
                {
                    claimed[index] = true;
                }

                replacements.Add((start, term.Original.Length, term, protectedText[start..end]));
            }
        }

        if (replacements.Count == 0)
        {
            return protectedText;
        }

        if (!WouldLeaveTranslatableText(protectedText, replacements))
        {
            return protectedText;
        }

        replacements.Sort((left, right) => left.Start.CompareTo(right.Start));

        var builder = new System.Text.StringBuilder(protectedText.Length);
        var cursor = 0;

        foreach (var (start, length, term, matched) in replacements)
        {
            builder.Append(protectedText, cursor, start - cursor);

            sequence++;
            var placeholder = FormatPlaceholder(sequence);

            // "Never translate" restores exactly what was in the text, casing included, so a name the mod
            // wrote its own way keeps it. A required translation restores the glossary's Portuguese.
            var restored = term.NeverTranslate ? matched : term.Translation;
            tokens.Add(new ProtectedToken(placeholder, matched, start, restored));

            builder.Append(placeholder);
            cursor = start + length;
        }

        builder.Append(protectedText, cursor, protectedText.Length - cursor);
        return builder.ToString();
    }

    /// <summary>Whether any letter would survive the substitutions — i.e. whether there is still work for an engine.</summary>
    private static bool WouldLeaveTranslatableText(
        string protectedText,
        List<(int Start, int Length, GlossaryTerm Term, string Matched)> replacements)
    {
        var covered = new bool[protectedText.Length];
        foreach (var (start, length, _, _) in replacements)
        {
            for (var index = start; index < start + length; index++)
            {
                covered[index] = true;
            }
        }

        foreach (Match placeholder in StrictPlaceholderPattern().Matches(protectedText))
        {
            for (var index = placeholder.Index; index < placeholder.Index + placeholder.Length; index++)
            {
                covered[index] = true;
            }
        }

        for (var index = 0; index < protectedText.Length; index++)
        {
            if (!covered[index] && char.IsLetter(protectedText[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClaimed(bool[] claimed, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (claimed[index])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The same boundary rule <see cref="GlossaryTerm.AppearsIn"/> uses, applied to one occurrence.</summary>
    private static bool IsWholeWord(string text, int start, int end)
    {
        var beforeIsWord = start > 0 && IsWordCharacter(text[start - 1]);
        var afterIsWord = end < text.Length && IsWordCharacter(text[end]);
        return !beforeIsWord && !afterIsWord;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    /// <summary>
    /// The tokens in the order their placeholders actually appear in the protected text.
    /// </summary>
    /// <remarks>
    /// <see cref="Restore"/> reports a reordering by comparing this list's order against the order the
    /// placeholders come back in. Glossary tokens are appended after the syntax ones, so without this
    /// they would sit at the end of the list while standing in the middle of the string, and every
    /// string carrying one would be reported as reordered.
    /// </remarks>
    private static List<ProtectedToken> OrderByAppearance(List<ProtectedToken> tokens, string protectedText)
    {
        tokens.Sort((left, right) =>
            protectedText.IndexOf(left.Placeholder, StringComparison.Ordinal)
                .CompareTo(protectedText.IndexOf(right.Placeholder, StringComparison.Ordinal)));

        return tokens;
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
                    token.IsGlossaryTerm
                        ? $"O termo de glossário '{token.OriginalValue}' foi retirado do texto para ser preservado e não voltou na tradução."
                        : $"Placeholder '{token.Placeholder}' for '{token.OriginalValue}' was not found in the translated text."));
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
                restored = restored.Replace(token.Placeholder, token.ValueToRestore, StringComparison.Ordinal);
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
