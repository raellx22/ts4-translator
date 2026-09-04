namespace TS4Translator.Core.Glossary;

/// <param name="Term">The rule that was not honoured.</param>
/// <param name="Explanation">Ready-to-show Portuguese saying what should have happened.</param>
public sealed record GlossaryMiss(GlossaryTerm Term, string Explanation);

/// <summary>
/// Checks a finished translation against the glossary rules that apply to its source.
/// </summary>
/// <remarks>
/// This logic existed and worked, and lived in <c>TranslationBundleReader</c> where only the chat-window
/// import could reach it. The consequence was a hole with a strange shape: paste a mod into a chat, get
/// back a reply that ignored the glossary, and the app told you which terms were missed and marked those
/// strings for review — but run the same mod through the local model or a paid API, the two paths that
/// process thousands of strings with nobody watching, and nothing checked anything at all.
///
/// <para>Lifted here so both callers share one definition. A rule the app enforces on one engine and not
/// another is worse than no rule: it teaches the user to trust a status that means different things
/// depending on how the string was translated.</para>
///
/// <para><b>What it deliberately does not do</b> is inflect. If the glossary says
/// <c>Configurações</c> and the engine wrote <c>Configuração</c>, that is reported. Portuguese
/// agreement would need real morphology to judge, and guessing at it would either wave through the
/// wrong word or invent misses that are not there. A report here is a Warning — a row to glance at —
/// never a rejection.</para>
/// </remarks>
public static class GlossaryCompliance
{
    /// <summary>Every applicable rule that <paramref name="translation"/> did not honour.</summary>
    public static IReadOnlyList<GlossaryMiss> Check(string? original, string? translation, GlossaryIndex? glossary)
    {
        if (glossary is not { Count: > 0 } || string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translation))
        {
            return [];
        }

        List<GlossaryMiss>? missed = null;

        foreach (var term in glossary.MatchesIn(original))
        {
            var comparison = term.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var required = term.NeverTranslate ? term.Original : term.Translation;

            if (string.IsNullOrWhiteSpace(required) || translation.Contains(required, comparison))
            {
                continue;
            }

            (missed ??= []).Add(new GlossaryMiss(term, term.NeverTranslate
                ? $"\"{term.Original}\" devia ter sido mantido sem traduzir"
                : $"\"{term.Original}\" devia virar \"{term.Translation}\""));
        }

        return missed is null ? [] : missed;
    }

    /// <summary>The misses as one line, or null when there were none.</summary>
    public static string? Summarize(IReadOnlyList<GlossaryMiss> misses) =>
        misses.Count == 0 ? null : string.Join("; ", misses.Select(miss => miss.Explanation));
}
