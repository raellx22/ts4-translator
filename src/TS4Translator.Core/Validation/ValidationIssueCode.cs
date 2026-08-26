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
}
