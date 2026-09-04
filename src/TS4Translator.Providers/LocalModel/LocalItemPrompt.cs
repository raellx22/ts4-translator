using System.Text;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// What the local model is shown for one string: the source, the terminology that applies to <i>it</i>,
/// and the draft when this is a revision.
/// </summary>
/// <remarks>
/// Kept apart from the engine so it can be tested without a GPU, like <see cref="ProtectedMarkers"/> and
/// <see cref="AnswerArtifacts"/>.
///
/// <para><b>Why the glossary lives here and not in the system prompt.</b> The orchestrator selects the
/// terms occurring anywhere in a batch — 44 on average against the shipped list, up to a ceiling of 80 —
/// and those all went into the system prompt. But this engine translates one string at a time, so every
/// string was asked to render three words while reading dozens of terminology rules about other strings.
/// That is the exact prompt pressure that made translategemma-4b answer three of 120 strings with a
/// translation of the instructions (DEC-045).</para>
///
/// <para>Moving the block into the user turn costs nothing that mattered: the cached prefix is the
/// system prompt, and this text is appended per item after the fork, so the prefill is still shared by
/// the whole batch. Each string now sees the zero to three rules that are actually about it.</para>
/// </remarks>
public static class LocalItemPrompt
{
    /// <summary>
    /// How many terms one string may carry.
    /// </summary>
    /// <remarks>
    /// Not a budget, a limit that essentially never binds: matched against the real 5.143-term list, a
    /// mod label carries zero to two. It exists so one pathological string — a section heading naming
    /// eight game concepts — cannot reintroduce, for itself, the bloat this removed for everyone else.
    /// </remarks>
    public const int MaximumTermsPerItem = 4;

    /// <summary>The line that introduces the rules, and the word the system prompt refers to.</summary>
    public const string TermsHeading = "TERMS:";

    /// <summary>The glossary rules that apply to one item, most specific first.</summary>
    public static IReadOnlyList<GlossaryTerm> TermsFor(TranslationRequestItem item, GlossaryIndex? glossary)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (glossary is not { Count: > 0 })
        {
            return [];
        }

        var matches = glossary.MatchesIn(item.ProtectedText);
        return matches.Count <= MaximumTermsPerItem ? matches : [.. matches.Take(MaximumTermsPerItem)];
    }

    /// <summary>
    /// The text a translation the app is <b>supposed</b> to see in the answer, so
    /// <see cref="PromptEcho"/> does not mistake correct terminology for recitation.
    /// </summary>
    public static IReadOnlyList<string> RequiredTextsFor(TranslationRequestItem item, GlossaryIndex? glossary) =>
        [.. TermsFor(item, glossary).Select(term => term.NeverTranslate ? term.Original : term.Translation)];

    /// <summary>Builds the user turn for one item.</summary>
    public static string Build(TranslationRequestItem item, GlossaryIndex? glossary)
    {
        ArgumentNullException.ThrowIfNull(item);

        var builder = new StringBuilder(item.ProtectedText);
        var terms = TermsFor(item, glossary);

        if (terms.Count > 0)
        {
            builder.AppendLine().AppendLine().Append(TermsHeading);
            foreach (var term in terms)
            {
                builder.AppendLine().Append(term.NeverTranslate
                    ? $"- keep \"{term.Original}\" exactly as-is"
                    : $"- \"{term.Original}\" -> \"{term.Translation}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(item.CurrentDraft))
        {
            builder.AppendLine().AppendLine()
                .AppendLine("Existing translation to improve (fix it, or repeat it if it is already right):")
                .Append(item.CurrentDraft);
        }

        return builder.ToString();
    }
}
