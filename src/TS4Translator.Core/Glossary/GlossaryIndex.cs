namespace TS4Translator.Core.Glossary;

/// <summary>
/// A glossary prepared for repeated lookups: which terms occur in a string, and whether a string
/// <i>is</i> a term.
/// </summary>
/// <remarks>
/// Built because the glossary stopped being small. At 386 terms a linear <see cref="GlossaryTerm.AppearsIn"/>
/// sweep per string was free; at 5.143 it is not. Measured against the real list with the real matching
/// code, one sweep of a string that matches nothing — the common case, and the one that cannot
/// short-circuit — costs <b>0,074 ms</b>. The editor's routing pass ran two of those per string, which is
/// 22 seconds of dead time on a 149.000-string project before the first request leaves the app.
///
/// <para><b>How the index works.</b> Every term is filed under the first <i>word</i> of its
/// <see cref="GlossaryTerm.Original"/> — the first run of letters, digits or underscores, lowercased.
/// A string is scanned once, and only the buckets for the words it contains are consulted. This is
/// exact, not approximate: <see cref="GlossaryTerm.AppearsIn"/> matches on word boundaries where a
/// boundary is "not a letter, digit or underscore", so a term whose first word is not a whole word of
/// the text cannot possibly match it. Every candidate a bucket yields is still confirmed with
/// <c>AppearsIn</c>, so the answer is identical to the linear sweep's — only faster.</para>
///
/// <para>Terms with no word character at all (a bare rule of dashes) cannot be filed and are checked
/// against every string. There are none in the shipped list and there is no reason to forbid one.</para>
/// </remarks>
public sealed class GlossaryIndex
{
    private readonly Dictionary<string, List<GlossaryTerm>> _byFirstWord;
    private readonly List<GlossaryTerm> _unindexed;
    private readonly Dictionary<string, GlossaryTerm> _wholeStringOrdinal;
    private readonly Dictionary<string, GlossaryTerm> _wholeStringIgnoreCase;

    private GlossaryIndex(IReadOnlyList<GlossaryTerm> terms)
    {
        Terms = terms;
        _byFirstWord = new Dictionary<string, List<GlossaryTerm>>(StringComparer.Ordinal);
        _unindexed = [];
        _wholeStringOrdinal = new Dictionary<string, GlossaryTerm>(StringComparer.Ordinal);
        _wholeStringIgnoreCase = new Dictionary<string, GlossaryTerm>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term.Original))
            {
                continue;
            }

            if (FirstWord(term.Original) is { Length: > 0 } word)
            {
                if (!_byFirstWord.TryGetValue(word, out var bucket))
                {
                    bucket = [];
                    _byFirstWord[word] = bucket;
                }

                bucket.Add(term);
            }
            else
            {
                _unindexed.Add(term);
            }

            var trimmed = term.Original.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // TryAdd, not indexer assignment: a project term and a global one may share an Original,
            // and whichever was listed first stays authoritative rather than being silently replaced.
            // GetTermsAsync lists project terms after global ones, so this is a deliberate ordering
            // dependency and GlossaryIndexTests pins it.
            if (term.CaseSensitive)
            {
                _wholeStringOrdinal.TryAdd(trimmed, term);
            }
            else
            {
                _wholeStringIgnoreCase.TryAdd(trimmed, term);
            }
        }

        // Longest first inside a bucket, so the most specific rule is the one a capped selection keeps:
        // given both "Sex" and "Sex Autonomy Settings", the one naming the whole label wins.
        foreach (var bucket in _byFirstWord.Values)
        {
            bucket.Sort(static (left, right) => right.Original.Length.CompareTo(left.Original.Length));
        }
    }

    /// <summary>An index over no terms. Every lookup returns nothing.</summary>
    public static GlossaryIndex Empty { get; } = new([]);

    /// <summary>The terms this index was built from, in the order they were given.</summary>
    public IReadOnlyList<GlossaryTerm> Terms { get; }

    public int Count => Terms.Count;

    public static GlossaryIndex For(IReadOnlyList<GlossaryTerm>? terms) =>
        terms is { Count: > 0 } ? new GlossaryIndex(terms) : Empty;

    /// <summary>
    /// The term whose <see cref="GlossaryTerm.Original"/> <i>is</i> <paramref name="text"/>, ignoring
    /// surrounding whitespace — or null.
    /// </summary>
    /// <remarks>
    /// This is the case where no engine needs to be involved at all. Most of the shipped glossary was
    /// harvested from real mod labels ("1 Test Left", "---------- Buffs ----------"), so a mod using the
    /// same label again is asking a question the app already holds the approved answer to. Sending it to
    /// a model instead is a request whose best possible outcome is the string already in hand, and whose
    /// likely outcome from a small model is worse.
    ///
    /// Case-sensitive terms are consulted first: those are the names ("CAS", "Sims"), and a name must
    /// not be matched by a lowercase word that merely spells it.
    /// </remarks>
    public GlossaryTerm? WholeStringMatch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Count == 0)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (_wholeStringOrdinal.TryGetValue(trimmed, out var exact))
        {
            return exact;
        }

        return _wholeStringIgnoreCase.TryGetValue(trimmed, out var insensitive) ? insensitive : null;
    }

    /// <summary>Every term that occurs in <paramref name="text"/>, longest first.</summary>
    public IReadOnlyList<GlossaryTerm> MatchesIn(string? text)
    {
        if (string.IsNullOrEmpty(text) || Count == 0)
        {
            return [];
        }

        List<GlossaryTerm>? matches = null;
        HashSet<GlossaryTerm>? seen = null;

        foreach (var candidate in Candidates(text))
        {
            if (!candidate.AppearsIn(text))
            {
                continue;
            }

            // A word repeated in the text re-visits its bucket, so the same term can be yielded twice.
            seen ??= [];
            if (seen.Add(candidate))
            {
                (matches ??= []).Add(candidate);
            }
        }

        if (matches is null)
        {
            return [];
        }

        matches.Sort(static (left, right) => right.Original.Length.CompareTo(left.Original.Length));
        return matches;
    }

    /// <summary>Whether any term occurs in <paramref name="text"/>. Stops at the first hit.</summary>
    public bool HasMatchIn(string? text)
    {
        if (string.IsNullOrEmpty(text) || Count == 0)
        {
            return false;
        }

        foreach (var candidate in Candidates(text))
        {
            if (candidate.AppearsIn(text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The terms worth spending prompt on for a whole batch, at most <paramref name="maximum"/> of them.
    /// </summary>
    /// <remarks>
    /// Filled a round at a time across the batch's strings rather than by taking the globally longest
    /// matches. The old rule sorted every match in the batch by length and cut at the ceiling, which
    /// meant a short term that was the <i>only</i> rule for one string was dropped because eighty other
    /// strings happened to carry longer ones — the string whose terminology the glossary knew was the
    /// one that lost it. Round-robin gives every string its most specific term before any string gets
    /// its second.
    /// </remarks>
    public IReadOnlyList<GlossaryTerm> SelectForBatch(IReadOnlyList<string> texts, int maximum)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        if (Count == 0 || texts.Count == 0 || maximum == 0)
        {
            return [];
        }

        var perText = new List<IReadOnlyList<GlossaryTerm>>(texts.Count);
        var deepest = 0;
        foreach (var text in texts)
        {
            var matches = MatchesIn(text);
            if (matches.Count > 0)
            {
                perText.Add(matches);
                deepest = Math.Max(deepest, matches.Count);
            }
        }

        if (perText.Count == 0)
        {
            return [];
        }

        var selected = new List<GlossaryTerm>(Math.Min(maximum, perText.Count));
        var seen = new HashSet<GlossaryTerm>();

        for (var round = 0; round < deepest && selected.Count < maximum; round++)
        {
            foreach (var matches in perText)
            {
                if (selected.Count >= maximum)
                {
                    break;
                }

                if (round < matches.Count && seen.Add(matches[round]))
                {
                    selected.Add(matches[round]);
                }
            }
        }

        return selected;
    }

    private IEnumerable<GlossaryTerm> Candidates(string text)
    {
        foreach (var term in _unindexed)
        {
            yield return term;
        }

        // One pass over the string, yielding each word's bucket as the word ends. Building a set of the
        // text's words first would be tidier and measurably slower on the short strings that make up
        // most of a mod.
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var isWord = index < text.Length && IsWordCharacter(text[index]);

            if (isWord)
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start < 0)
            {
                continue;
            }

            if (_byFirstWord.TryGetValue(Lowercase(text, start, index - start), out var bucket))
            {
                foreach (var term in bucket)
                {
                    yield return term;
                }
            }

            start = -1;
        }
    }

    /// <summary>The first run of word characters in <paramref name="value"/>, lowercased.</summary>
    private static string? FirstWord(string value)
    {
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (IsWordCharacter(value[index]))
            {
                if (start < 0)
                {
                    start = index;
                }
            }
            else if (start >= 0)
            {
                return Lowercase(value, start, index - start);
            }
        }

        return start >= 0 ? Lowercase(value, start, value.Length - start) : null;
    }

    private static string Lowercase(string value, int start, int length) =>
        string.Create(length, (value, start), static (span, state) =>
        {
            var (text, offset) = state;
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = char.ToLowerInvariant(text[offset + index]);
            }
        });

    /// <summary>Must stay identical to <see cref="GlossaryTerm"/>'s own definition, or the index would lie.</summary>
    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
