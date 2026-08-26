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
}
