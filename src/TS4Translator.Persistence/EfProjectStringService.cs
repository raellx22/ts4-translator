using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfProjectStringService(IDbContextFactory<TranslatorDbContext> dbContextFactory) : IProjectStringService
{
    public async Task<ImportDiffResult> ReplaceStringsAsync(Guid projectId, ImportedTable table, IReadOnlyList<(uint KeyHash, string Text)> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(entries);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sourceFile = table.SourceFile;
        var group = table.Stbl.Group;
        var fullInstance = table.Stbl.FullInstance;

        // Rows stored before the app tracked which `.package` a string came from carry SourceFile NULL.
        // They are matched here alongside the exact-file rows and ADOPTED below.
        //
        // Ignoring them was a real data-loss bug: the re-import found no match, inserted fresh Pending
        // rows, and RemoveStringsOutsideAsync then deleted the legacy ones because their table key
        // (null file) was not in the imported set - taking every translation with them. It destroyed a
        // 1,351-string project on the very re-import the release notes told the user to perform.
        var candidates = await dbContext.ProjectStrings
            .Where(r => r.ProjectId == projectId
                        && r.StblGroup == group
                        && r.StblFullInstance == fullInstance
                        && (r.SourceFile == sourceFile || r.SourceFile == null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = new Dictionary<uint, ProjectStringRecord>();
        var supersededLegacyRows = new List<ProjectStringRecord>();

        foreach (var candidate in candidates)
        {
            if (!existing.TryGetValue(candidate.KeyHash, out var incumbent))
            {
                existing[candidate.KeyHash] = candidate;
                continue;
            }

            // A row already tied to this exact file always wins over a legacy one; keeping both would
            // violate the unique index the moment the legacy row is adopted.
            if (incumbent.SourceFile is null && candidate.SourceFile is not null)
            {
                existing[candidate.KeyHash] = candidate;
                supersededLegacyRows.Add(incumbent);
            }
            else
            {
                supersededLegacyRows.Add(candidate);
            }
        }

        dbContext.ProjectStrings.RemoveRange(supersededLegacyRows);

        var incomingHashes = new HashSet<uint>();
        var newCount = 0;
        var changedCount = 0;
        var unchangedCount = 0;

        foreach (var (keyHash, text) in entries)
        {
            incomingHashes.Add(keyHash);

            if (existing.TryGetValue(keyHash, out var record))
            {
                // Adoption: a legacy row now belongs to the file that just claimed it, and keeps its
                // translation, status and history exactly as they were.
                record.SourceFile ??= sourceFile;

                if (!string.Equals(record.OriginalText, text, StringComparison.Ordinal))
                {
                    record.OriginalText = text;
                    // The source text changed under an existing translation - never present a
                    // translation of the old text as if it still matched (brief section 25).
                    if (record.TranslatedText is not null)
                    {
                        record.Status = nameof(ProjectStringStatus.NeedsReview);
                        record.IssueSummary = "O texto original mudou desde a última tradução.";
                    }

                    changedCount++;
                }
                else
                {
                    unchangedCount++;
                }
            }
            else
            {
                dbContext.ProjectStrings.Add(new ProjectStringRecord
                {
                    ProjectId = projectId,
                    SourceFile = sourceFile,
                    StblGroup = group,
                    StblFullInstance = fullInstance,
                    KeyHash = keyHash,
                    OriginalText = text,
                    Status = nameof(ProjectStringStatus.Pending),
                });
                newCount++;
            }
        }

        var removedHashes = existing.Keys.Except(incomingHashes).ToList();
        foreach (var removedHash in removedHashes)
        {
            dbContext.ProjectStrings.Remove(existing[removedHash]);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ImportDiffResult(newCount, changedCount, unchangedCount, removedHashes.Count);
    }

    public async Task<int> RemoveStringsOutsideAsync(Guid projectId, IReadOnlyCollection<ImportedTable> importedTables, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedTables);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var storedTables = await dbContext.ProjectStrings
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => new { r.SourceFile, r.StblGroup, r.StblFullInstance })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var kept = importedTables.ToHashSet();
        var removed = 0;

        foreach (var stored in storedTables)
        {
            if (kept.Contains(new ImportedTable(stored.SourceFile, new StblKey(stored.StblGroup, stored.StblFullInstance))))
            {
                continue;
            }

            var sourceFile = stored.SourceFile;
            var group = stored.StblGroup;
            var fullInstance = stored.StblFullInstance;

            // ExecuteDeleteAsync rather than load-then-Remove: one stale table can hold thousands of
            // rows (a real project held 142,359 across 105 of them), and the revision rows cascade at
            // the database level - see TranslatorDbContext's OnDelete(Cascade).
            removed += await dbContext.ProjectStrings
                .Where(r => r.ProjectId == projectId && r.SourceFile == sourceFile && r.StblGroup == group && r.StblFullInstance == fullInstance)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return removed;
    }

    public async Task<IReadOnlyList<ProjectStringEntry>> GetStringsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await dbContext.ProjectStrings
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(ToEntry).ToList();
    }

    public async Task<ProjectProgress> GetProgressAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Grouped in SQL: one COUNT per status rather than pulling rows across the wire.
        var counts = await dbContext.ProjectStrings
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .GroupBy(r => r.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int CountOf(ProjectStringStatus status) =>
            counts.FirstOrDefault(entry => entry.Status == status.ToString())?.Count ?? 0;

        var pending = CountOf(ProjectStringStatus.Pending);
        var needsReview = CountOf(ProjectStringStatus.NeedsReview);
        var done = CountOf(ProjectStringStatus.Translated) + CountOf(ProjectStringStatus.Refined) + CountOf(ProjectStringStatus.ManuallyEdited);

        return new ProjectProgress(counts.Sum(entry => entry.Count), pending, needsReview, done);
    }

    public async Task<IReadOnlyList<ProjectStringEntry>> GetPendingStringsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await dbContext.ProjectStrings
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Status == nameof(ProjectStringStatus.Pending))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(ToEntry).ToList();
    }

    public async Task UpdateTranslationAsync(int stringEntryId, string translatedText, ProjectStringStatus status, string? issueSummary, CancellationToken cancellationToken = default)
        => await UpdateTranslationsAsync(
            [new ProjectStringTranslationUpdate(stringEntryId, translatedText, status, issueSummary)],
            cancellationToken).ConfigureAwait(false);

    public async Task UpdateTranslationsAsync(IReadOnlyList<ProjectStringTranslationUpdate> updates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return;
        }

        // Last update wins if a caller accidentally repeats an id. This matches applying the same
        // updates sequentially while avoiding duplicate tracked records and revisions.
        var updatesById = updates
            .GroupBy(update => update.StringEntryId)
            .ToDictionary(group => group.Key, group => group.Last());

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var ids = updatesById.Keys.ToList();
        var records = await dbContext.ProjectStrings
            .Where(record => ids.Contains(record.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changedAtUtc = DateTimeOffset.UtcNow;
        foreach (var record in records)
        {
            var update = updatesById[record.Id];
            if (record.TranslatedText is not null && !string.Equals(record.TranslatedText, update.TranslatedText, StringComparison.Ordinal))
            {
                dbContext.ProjectStringRevisions.Add(new ProjectStringRevisionRecord
                {
                    StringEntryId = record.Id,
                    PreviousText = record.TranslatedText,
                    ChangedAtUtc = changedAtUtc,
                });
            }

            record.TranslatedText = update.TranslatedText;
            record.Status = update.Status.ToString();
            record.IssueSummary = update.IssueSummary;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StringRevision>> GetHistoryAsync(int stringEntryId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await dbContext.ProjectStringRevisions
            .AsNoTracking()
            .Where(r => r.StringEntryId == stringEntryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records
            .OrderByDescending(r => r.ChangedAtUtc)
            .Select(r => new StringRevision(r.Id, r.StringEntryId, r.PreviousText, r.ChangedAtUtc))
            .ToList();
    }

    private static ProjectStringEntry ToEntry(ProjectStringRecord record) => new(
        record.Id,
        record.ProjectId,
        record.SourceFile,
        new StblKey(record.StblGroup, record.StblFullInstance),
        record.KeyHash,
        record.OriginalText,
        record.TranslatedText,
        Enum.Parse<ProjectStringStatus>(record.Status),
        record.IssueSummary);
}
