using System.Text.RegularExpressions;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// Small helpers for the <c>__TS4_TOKEN_nnnn__</c> markers, kept apart from the engine so they can be
/// tested without a GPU.
/// </summary>
public static partial class ProtectedMarkers
{
    [GeneratedRegex(@"__TS4_TOKEN_\d{4}__", RegexOptions.CultureInvariant)]
    private static partial Regex ExactPattern { get; }

    /// <summary>
    /// A marker followed by more underscores than it should have — but only when what follows is not
    /// another marker.
    /// </summary>
    /// <remarks>
    /// The lookahead is the whole difficulty. <c>"\n\n"</c> protects as two markers in a row, so
    /// <c>__TS4_TOKEN_0001____TS4_TOKEN_0002__</c> is correct and has four underscores in the middle,
    /// exactly like the artefact this is meant to trim. Without the lookahead, trimming eats the start
    /// of the second marker and turns a correct translation into a broken one.
    /// </remarks>
    [GeneratedRegex(@"(__TS4_TOKEN_\d{4})__(_+)(?!_*TS4_TOKEN_)", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingUnderscorePattern { get; }

    /// <summary>
    /// Trims the extra underscores a model sometimes adds when reproducing a marker.
    /// </summary>
    /// <remarks>
    /// Observed on the real corpus: told that a marker ends in two underscores, Hy-MT2 occasionally
    /// writes four, and the restore then leaves a stray <c>__</c> glued to the next word — "…reiniciar o
    /// jogo.\n\n__Salve e reinicie…". Both markers are present so nothing flags it, and the artefact
    /// ships into the game's string table.
    ///
    /// Narrow on purpose: only underscores immediately after a well-formed marker are trimmed, and only
    /// down to the two the marker is supposed to end with. A translation that legitimately begins with
    /// underscores is not something a game UI string does, but this would not touch one anyway unless it
    /// were glued to a marker.
    /// </remarks>
    public static string Normalize(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : TrailingUnderscorePattern.Replace(text, "$1__");

    /// <summary>Every well-formed marker in <paramref name="text"/>, in order.</summary>
    public static List<string> Extract(string? text) =>
        string.IsNullOrEmpty(text) ? [] : [.. ExactPattern.Matches(text).Select(match => match.Value)];

    /// <summary>
    /// Whether the markers of <paramref name="source"/> all came back, the same number of times, with
    /// none invented.
    /// </summary>
    public static bool Survived(string source, string? translation)
    {
        if (translation is null)
        {
            return false;
        }

        var expected = Extract(source);
        var actual = Extract(translation);

        // An invented marker is wrong even when the source had none - that is exactly what a weak model
        // does when it mistakes a trailing number for a variable (DEC-036).
        return expected.Count == actual.Count && expected.All(actual.Contains);
    }
}
