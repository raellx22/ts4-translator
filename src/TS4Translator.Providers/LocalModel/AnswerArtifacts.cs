using System.Text.RegularExpressions;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// Removes the chat habits a local model brings to a translation that nobody asked it to decorate.
/// </summary>
/// <remarks>
/// Measured, not anticipated. Running seven local models over 120 real strings of
/// <c>[Kritical]DreamsOfSurrender</c>, Ministral-3-8B returned <b>21 of 120</b> wrapped in markdown
/// emphasis — <c>**Dispositivo Redefinido**</c>, <c>Serviço **Cerebro Lavado**</c> — while every other
/// model returned none. Nothing caught it: the markers survive, the length is right, the meaning is
/// right, so it validates clean and the asterisks ship into the game's string table, where they are
/// literal asterisks on screen.
///
/// Every rule here is conditioned on the source <b>not</b> having the same decoration. A mod that really
/// does put asterisks or quotes around a line keeps them; only decoration the model invented is removed.
/// That condition is what makes this safe to run on every answer rather than a guess about intent.
/// </remarks>
public static partial class AnswerArtifacts
{
    /// <summary>Markdown emphasis around a run of text: <c>**bold**</c>, <c>__bold__</c>, <c>*italic*</c>.</summary>
    [GeneratedRegex(@"(\*\*|__|\*)(?<inner>[^*_]+?)\1", RegexOptions.CultureInvariant)]
    private static partial Regex EmphasisPattern { get; }

    /// <summary>The whole answer inside one pair of quotes.</summary>
    [GeneratedRegex(@"^\s*(?<quote>[""'“”])(?<inner>.+)\k<quote>\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex WrappingQuotePattern { get; }

    /// <summary>Strips decoration the model added and the source never had.</summary>
    public static string Strip(string? answer, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var cleaned = answer.Trim();

        // Underscores are load-bearing in this app: a protected marker is __TS4_TOKEN_0001__, and
        // treating that as markdown emphasis would eat it. Only run the emphasis pass when the source
        // has no asterisk of its own and the answer holds no marker.
        if (!sourceText.Contains('*', StringComparison.Ordinal)
            && !cleaned.Contains("__TS4_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = EmphasisPattern.Replace(cleaned, "${inner}").Trim();
        }

        // "Reply with only the translation" gets answered with the translation in quotes often enough to
        // be worth undoing - but a line of dialogue that really is quoted in the source stays quoted.
        if (WrappingQuotePattern.Match(cleaned) is { Success: true } match
            && !WrappingQuotePattern.IsMatch(sourceText.Trim()))
        {
            var inner = match.Groups["inner"].Value.Trim();

            // Only when the quotes are genuinely a wrapper: if the inside carries its own quote, the
            // answer is a sentence about quoted text and unwrapping would unbalance it.
            if (inner.Length > 0 && !inner.Contains('"', StringComparison.Ordinal))
            {
                cleaned = inner;
            }
        }

        return cleaned;
    }
}
