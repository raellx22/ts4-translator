namespace TS4Translator.Core.Projects;

/// <summary>
/// Summarizes what changed when (re-)importing a mod's strings into a project — the basis of the
/// "killer feature" described in brief sections 25/63: only translate what's new or changed, reuse
/// everything else automatically.
/// </summary>
/// <param name="NewCount">Strings whose hash did not exist before.</param>
/// <param name="ChangedCount">Strings whose hash already existed but whose original text differs — their translation is kept but flagged for review.</param>
/// <param name="UnchangedCount">Strings whose hash and text are identical to what was already stored — untouched, translation (if any) stays valid.</param>
/// <param name="RemovedCount">Strings that existed before for this STBL but are no longer present in the source.</param>
public sealed record ImportDiffResult(int NewCount, int ChangedCount, int UnchangedCount, int RemovedCount)
{
    public int TotalAfterImport => NewCount + ChangedCount + UnchangedCount;
}
