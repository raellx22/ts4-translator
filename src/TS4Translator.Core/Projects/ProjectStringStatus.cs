namespace TS4Translator.Core.Projects;

public enum ProjectStringStatus
{
    /// <summary>Extracted from the source mod, not yet translated.</summary>
    Pending,

    /// <summary>Translated with no critical validation issues.</summary>
    Translated,

    /// <summary>Translated but with at least one critical validation issue (missing/duplicated token, etc.).</summary>
    NeedsReview,

    /// <summary>A human edited the translation directly.</summary>
    ManuallyEdited,
}
