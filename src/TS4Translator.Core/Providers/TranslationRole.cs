namespace TS4Translator.Core.Providers;

/// <summary>
/// The three distinct jobs a translation engine can be assigned to. Keeping them separate is the point
/// of the whole profile system: a cheap offline engine can do the bulk pass while an expensive one is
/// reserved for the handful of strings that actually need judgement.
/// </summary>
public enum TranslationRole
{
    /// <summary>The bulk pass — "Traduzir pendentes". Usually the cheapest engine that is good enough.</summary>
    Primary,

    /// <summary>On-demand, one string at a time: the user reads a bad translation and clicks refine on that row.</summary>
    Refinement,

    /// <summary>The batch sweep over everything flagged for review. Separate from <see cref="Refinement"/> so a slow, expensive engine can be used for a handful of manual clicks without also being used for a sweep of hundreds — or the other way around.</summary>
    Review,
}
