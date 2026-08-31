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

    /// <summary>
    /// A second, stronger engine improved an earlier translation and the result passed validation.
    /// Kept distinct from <see cref="Translated"/> so the user can see at a glance what has already
    /// been through a revision pass and what has not.
    /// </summary>
    Refined,
}
