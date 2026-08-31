using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Projects;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfProjectService(IDbContextFactory<TranslatorDbContext> dbContextFactory) : IProjectService
{
    public async Task<TranslationProject> CreateAsync(
        string name,
        IReadOnlyList<string> sourcePackagePaths,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourcePackagePaths);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        var now = DateTimeOffset.UtcNow;
        var record = new ProjectRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            SourceLocale = sourceLocale.Name,
            TargetLocale = targetLocale.Name,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            SourceFiles = sourcePackagePaths.Select(path => new ProjectSourceFileRecord { FilePath = path }).ToList(),
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        dbContext.Projects.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToProject(record);
    }

    public async Task<TranslationProject?> FindBySourceFilesAsync(IReadOnlyList<string> sourcePackagePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePackagePaths);

        if (sourcePackagePaths.Count == 0)
        {
            return null;
        }

        var wanted = NormalizePaths(sourcePackagePaths);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.SourceFiles)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Set equality, not sequence equality: the file picker and drag-and-drop hand the same files
        // back in whatever order the shell produced them, and that must not create a second project.
        var match = records.FirstOrDefault(record =>
            NormalizePaths(record.SourceFiles.Select(file => file.FilePath).ToList()).SetEquals(wanted));

        return match is null ? null : ToProject(match);
    }

    public async Task<IReadOnlyList<TranslationProject>> ListRecentAsync(int maxCount = 20, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // EF Core's SQLite provider cannot translate ORDER BY on a DateTimeOffset column into SQL
        // ("SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses") -
        // confirmed by a failing test, not assumed. Ordering/Take happen client-side instead; fine at
        // the scale a "recent projects" list actually needs (tens, not millions, of projects).
        var records = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.SourceFiles)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Take(maxCount)
            .Select(ToProject)
            .ToList();
    }

    public async Task<TranslationProject?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.SourceFiles)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ToProject(record);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            dbContext.Projects.Remove(record);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateCustomInstructionsAsync(Guid id, string? customInstructions, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.CustomInstructions = string.IsNullOrWhiteSpace(customInstructions) ? null : customInstructions;
        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Windows paths, so case-insensitive; full-path'd so a relative and an absolute reference to the same package still match.</summary>
    private static HashSet<string> NormalizePaths(IReadOnlyCollection<string> paths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            try
            {
                normalized.Add(Path.GetFullPath(path));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A stored path the OS can no longer parse must not blow up the import; it simply
                // never matches, so the import falls through to creating a fresh project.
                normalized.Add(path);
            }
        }

        return normalized;
    }

    private static TranslationProject ToProject(ProjectRecord record) => new(
        record.Id,
        record.Name,
        record.SourceFiles.Select(f => f.FilePath).ToList(),
        CultureInfo.GetCultureInfo(record.SourceLocale),
        CultureInfo.GetCultureInfo(record.TargetLocale),
        record.CreatedAtUtc,
        record.UpdatedAtUtc,
        record.CustomInstructions);
}
