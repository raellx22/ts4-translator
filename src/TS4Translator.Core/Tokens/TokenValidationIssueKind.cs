namespace TS4Translator.Core.Tokens;

public enum TokenValidationIssueKind
{
    /// <summary>An expected placeholder does not appear anywhere in the translated text.</summary>
    MissingToken,

    /// <summary>An expected placeholder appears more than once in the translated text.</summary>
    DuplicatedToken,

    /// <summary>Text matching the protector's own placeholder format was found but does not correspond to any expected token.</summary>
    UnexpectedToken,

    /// <summary>Text resembling a placeholder was found but altered enough that it could not be matched to any expected token.</summary>
    ModifiedPlaceholder,

    /// <summary>All expected placeholders are present exactly once, but in a different relative order than the source.</summary>
    ReorderedToken,
}
