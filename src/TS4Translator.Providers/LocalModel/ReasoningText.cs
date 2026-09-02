using System.Text.RegularExpressions;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// Separates a reasoning model's thinking from the answer it finally gives.
/// </summary>
/// <remarks>
/// Applied to every answer from the local engine, whatever the profile's <c>ReasoningMode</c> is, because
/// the mode is a request and not a guarantee: a model built to reason can open a thinking block even when
/// the prompt asked it not to. Without this, that entire monologue is the translation — it goes through
/// validation, into translation memory, and into the game's string table, and the only thing that would
/// have caught it is a human reading the line.
/// </remarks>
public static partial class ReasoningText
{
    /// <summary>
    /// The tags the current crop of open reasoning models actually emit. Qwen3, DeepSeek-R1 and their
    /// distills use <c>think</c>; the other two show up in fine-tunes often enough to be worth naming.
    /// </summary>
    [GeneratedRegex(@"</\s*(?:think|thinking|reasoning)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosingTagPattern { get; }

    [GeneratedRegex(@"<\s*(?:think|thinking|reasoning)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpeningTagPattern { get; }

    /// <summary>
    /// The answer alone: everything after the model stopped thinking.
    /// </summary>
    /// <remarks>
    /// Split on the <b>last</b> closing tag rather than matching an open/close pair. A generation that ran
    /// into the token cap mid-thought has an opening tag and no closing one, and a pair-matching regex
    /// would find nothing and hand the whole monologue back as if it were the translation. Anchoring on
    /// the close instead means the only text ever returned is text the model wrote after it finished
    /// reasoning.
    ///
    /// Three cases, and they are all real:
    /// <list type="bullet">
    /// <item>Thought and then answered — return what follows the last close.</item>
    /// <item>Never thought — no tags at all, return the text untouched. This is the ordinary path for
    /// every non-reasoning model and must stay free of surprises.</item>
    /// <item>Thought and was cut off — an opening tag with nothing after it. There is no translation in
    /// there to salvage, so this returns empty, which the validator reports as an empty translation and
    /// the string goes to review. That is the honest outcome; the alternative is shipping reasoning.</item>
    /// </list>
    /// </remarks>
    public static string Strip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var closings = ClosingTagPattern.Matches(text);
        if (closings.Count > 0)
        {
            var last = closings[^1];
            return text[(last.Index + last.Length)..].Trim();
        }

        // No close. An opening tag means everything present is thinking, truncated before the answer.
        return OpeningTagPattern.IsMatch(text) ? string.Empty : text;
    }

    /// <summary>Whether <paramref name="text"/> shows any sign of a reasoning block, closed or not.</summary>
    public static bool ContainsReasoning(string? text) =>
        !string.IsNullOrEmpty(text) && (OpeningTagPattern.IsMatch(text) || ClosingTagPattern.IsMatch(text));
}
