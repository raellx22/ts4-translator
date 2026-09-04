namespace TS4Translator.Core.Glossary;

/// <summary>
/// Which glossary rules are applied by <i>substitution</i> rather than by asking the engine to obey.
/// </summary>
/// <remarks>
/// The glossary used to reach engines exactly one way: as lines of English in a prompt. That is the
/// format that depends most on the model's willingness to comply, which is the thing a 4B model has
/// least of — and for Argos, Google Translate and DeepL it depended on something they do not have at
/// all, since they take no prompt. Those three declare
/// <c>ProviderCapabilities.SupportsGlossary: false</c>, were handed the terms anyway, and dropped them
/// on the floor. On the cheapest lane of the routing plan, the 5.143-term glossary had no effect
/// whatsoever.
///
/// <para>Substitution removes the question instead of asking it. The term is taken out of the source
/// before the engine sees it and written back afterwards, so there is no rendering for the engine to get
/// wrong, and it works identically for an engine with no prompt input.</para>
///
/// <para><b>Two different rules, deliberately.</b></para>
/// <list type="bullet">
/// <item><b>"Never translate" is always enforced.</b> A name has exactly one correct rendering — itself —
/// so there is no grammar to get wrong by freezing it, and the measured damage is large:
/// <c>WickedWhims</c> alone was mangled 423 times in 1.219 uses.</item>
/// <item><b>A required translation is enforced only for engines that cannot be told about it.</b>
/// Freezing "Body Selector" into "Seletor de Corpo" mid-sentence hands the engine a noun phrase it
/// cannot inflect or agree with, and in Portuguese that costs an article or a gender now and then. Where
/// a prompt exists, the prompt is the better tool and the post-hoc check
/// (<c>ValidationIssueCode.GlossaryNotRespected</c>) catches what it misses. Where no prompt exists, the
/// choice is between a slightly stiff phrase and no terminology at all, and the stiff phrase wins.</item>
/// </list>
/// </remarks>
public sealed class GlossaryEnforcement
{
    private readonly GlossaryIndex _glossary;
    private readonly bool _enforceRequiredTranslations;

    private GlossaryEnforcement(GlossaryIndex glossary, bool enforceRequiredTranslations)
    {
        _glossary = glossary;
        _enforceRequiredTranslations = enforceRequiredTranslations;
    }

    /// <param name="glossary">The terms in play for this run.</param>
    /// <param name="providerHonoursPrompt">
    /// Whether the engine can be told about terminology at all — <c>ProviderCapabilities.SupportsGlossary</c>.
    /// </param>
    /// <returns>Null when there is nothing to enforce, so callers can skip the work entirely.</returns>
    public static GlossaryEnforcement? For(GlossaryIndex? glossary, bool providerHonoursPrompt)
    {
        if (glossary is not { Count: > 0 })
        {
            return null;
        }

        return new GlossaryEnforcement(glossary, enforceRequiredTranslations: !providerHonoursPrompt);
    }

    /// <summary>
    /// The terms to substitute out of <paramref name="text"/>, longest first so an overlapping shorter
    /// rule cannot claim part of a longer one.
    /// </summary>
    public IReadOnlyList<GlossaryTerm> TermsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var matches = _glossary.MatchesIn(text);
        if (matches.Count == 0)
        {
            return [];
        }

        var enforced = new List<GlossaryTerm>(matches.Count);
        foreach (var term in matches)
        {
            if (term.NeverTranslate)
            {
                enforced.Add(term);
            }
            else if (_enforceRequiredTranslations && !string.IsNullOrWhiteSpace(term.Translation))
            {
                enforced.Add(term);
            }
        }

        return enforced;
    }
}
