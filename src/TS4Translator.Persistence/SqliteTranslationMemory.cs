using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Memory;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class SqliteTranslationMemory(IDbContextFactory<TranslatorDbContext> dbContextFactory) : ITranslationMemory
{
    public async Task<TranslationMemoryEntry?> FindExactAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hash = ComputeHash(originalText);

        var record = await dbContext.TranslationMemory
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.OriginalHash == hash && r.SourceLocale == sourceLocale.Name && r.TargetLocale == targetLocale.Name,
                cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ToEntry(record);
    }

    public async Task<FuzzyMatchResult?> FindSimilarAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, double minSimilarity = 0.85, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // No SQL-side string-similarity function is available on SQLite, so candidates for this locale
        // pair are pulled into memory and scored one by one. Fine at translation-memory scale (tens of
        // thousands of entries at most for a single Sims 4 modder's library, not millions) - revisit
        // with a smarter index (n-gram, trigram table) if this ever shows up as a real bottleneck.
        var candidates = await dbContext.TranslationMemory
            .AsNoTracking()
            .Where(r => r.SourceLocale == sourceLocale.Name && r.TargetLocale == targetLocale.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var normalized = originalText.Trim();
        FuzzyMatchResult? best = null;

        foreach (var candidate in candidates)
        {
            var similarity = StringSimilarity.Ratio(normalized, candidate.OriginalText.Trim());
            if (similarity >= minSimilarity && (best is null || similarity > best.Similarity))
            {
                best = new FuzzyMatchResult(ToEntry(candidate), similarity);
            }
        }

        return best;
    }

    public async Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hash = ComputeHash(entry.OriginalText);

        var existing = await dbContext.TranslationMemory
            .FirstOrDefaultAsync(
                r => r.OriginalHash == hash && r.SourceLocale == entry.SourceLocale.Name && r.TargetLocale == entry.TargetLocale.Name,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            dbContext.TranslationMemory.Add(new TranslationMemoryRecord
            {
                OriginalHash = hash,
                OriginalText = entry.OriginalText,
                SourceLocale = entry.SourceLocale.Name,
                TargetLocale = entry.TargetLocale.Name,
                TranslatedText = entry.TranslatedText,
                Provider = entry.Provider,
                ProfileName = entry.ProfileName,
                CreatedAtUtc = entry.CreatedAtUtc,
            });
        }
        else
        {
            existing.TranslatedText = entry.TranslatedText;
            existing.Provider = entry.Provider;
            existing.ProfileName = entry.ProfileName;
            existing.CreatedAtUtc = entry.CreatedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TranslationMemoryEntry ToEntry(TranslationMemoryRecord record) => new(
        record.OriginalText,
        CultureInfo.GetCultureInfo(record.SourceLocale),
        CultureInfo.GetCultureInfo(record.TargetLocale),
        record.TranslatedText,
        record.Provider,
        record.ProfileName,
        record.CreatedAtUtc);

    private static string ComputeHash(string text)
    {
        var normalized = text.Trim();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }
}
