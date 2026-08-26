using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfProjectStringService(IDbContextFactory<TranslatorDbContext> dbContextFactory) : IProjectStringService
{
    public async Task<ImportDiffResult> ReplaceStringsAsync(Guid projectId, StblKey sourceStbl, IReadOnlyList<(uint KeyHash, string Text)> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await dbContext.ProjectStrings
            .Where(r => r.ProjectId == projectId && r.StblGroup == sourceStbl.Group && r.StblFullInstance == sourceStbl.FullInstance)
            .ToDictionaryAsync(r => r.KeyHash, cancellationToken)
            .ConfigureAwait(false);

        var incomingHashes = new HashSet<uint>();
        var newCount = 0;
        var changedCount = 0;
        var unchangedCount = 0;

        foreach (var (keyHash, text) in entries)
        {
            incomingHashes.Add(keyHash);

            if (existing.TryGetValue(keyHash, out var record))
            {
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
                    StblGroup = sourceStbl.Group,
                    StblFullInstance = sourceStbl.FullInstance,
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
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProjectStrings.FirstOrDefaultAsync(r => r.Id == stringEntryId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        if (record.TranslatedText is not null && !string.Equals(record.TranslatedText, translatedText, StringComparison.Ordinal))
        {
            dbContext.ProjectStringRevisions.Add(new ProjectStringRevisionRecord
            {
                StringEntryId = stringEntryId,
                PreviousText = record.TranslatedText,
                ChangedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        record.TranslatedText = translatedText;
        record.Status = status.ToString();
        record.IssueSummary = issueSummary;

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
        new StblKey(record.StblGroup, record.StblFullInstance),
        record.KeyHash,
        record.OriginalText,
        record.TranslatedText,
        Enum.Parse<ProjectStringStatus>(record.Status),
        record.IssueSummary);
}
