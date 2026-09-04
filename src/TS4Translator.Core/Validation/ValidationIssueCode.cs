namespace TS4Translator.Core.Validation;

public enum ValidationIssueCode
{
    MissingToken,
    DuplicatedToken,
    UnexpectedToken,
    ModifiedPlaceholder,
    EmptyTranslation,
    IdenticalToSource,
    LengthDiscrepancy,
    UnbalancedMarkup,

    /// <summary>The translation provider's response did not include a result for this item's id.</summary>
    ProviderResultMissing,

    /// <summary>The translation provider's response included more than one result for this item's id.</summary>
    ProviderResultDuplicated,

    /// <summary>Reused from a similar-but-not-identical previous translation (fuzzy match, brief section 26); needs human review.</summary>
    FuzzyMatchReused,

    /// <summary>
    /// A glossary term applied to this string and the engine's answer did not honour it.
    /// </summary>
    /// <remarks>
    /// Until this existed, the only path that checked the glossary against an answer was the chat-window
    /// import. Every engine the app drives itself — the local GGUF model, the paid APIs, the free
    /// endpoints — could ignore the whole glossary on every string and the run would report success.
    /// </remarks>
    GlossaryNotRespected,

    /// <summary>
    /// The string is an internal identifier and was never sent to an engine; it is kept as-is.
    /// </summary>
    /// <remarks>
    /// Not a defect: an observation, so a run's report can say why a row has no translation without
    /// pretending an engine failed on it.
    /// </remarks>
    NotTranslatable,
}
