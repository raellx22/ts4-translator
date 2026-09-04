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

    public async Task<IReadOnlyList<FuzzyMatchResult?>> FindBestMatchesAsync(
        IReadOnlyList<string> originalTexts,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        double minSimilarity = 0.85,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalTexts);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        if (originalTexts.Count == 0)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var sourceName = sourceLocale.Name;
        var targetName = targetLocale.Name;

        // Exact hits by hash, straight off the unique (OriginalHash, SourceLocale, TargetLocale) index.
        //
        // This used to be `.Where(locale pair).ToListAsync()` — the entire table for that locale pair,
        // materialised, re-grouped and re-indexed in memory. The orchestrator calls this once per chunk
        // of a hundred strings and once per lane, so a 6.779-string mod meant 68 full loads of a
        // translation memory that holds ~170.000 rows on this user's machine, and a plain SELECT was
        // being used where an index already existed.
        var hashes = originalTexts.Select(ComputeHash).ToList();
        var exactByHash = new Dictionary<string, TranslationMemoryRecord>(StringComparer.Ordinal);
        foreach (var chunk in hashes.Distinct(StringComparer.Ordinal).Chunk(500))
        {
            var found = await dbContext.TranslationMemory
                .AsNoTracking()
                .Where(r => chunk.Contains(r.OriginalHash) && r.SourceLocale == sourceName && r.TargetLocale == targetName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var record in found)
            {
                exactByHash[record.OriginalHash] = record;
            }
        }

        // Only the strings with no exact hit can want a fuzzy one, and only some of those can possibly
        // have one. The Levenshtein distance cannot be below the length difference, so at a 97%
        // threshold a string shorter than 1/(1-0.97) characters admits a maximum distance of zero — no
        // candidate but an identical one, which the hash lookup already settled. On a mod, that is most
        // of the file: the short UI labels never reach the database a second time.
        var lengthsNeedingFuzzy = new List<int>();
        for (var index = 0; index < originalTexts.Count; index++)
        {
            if (exactByHash.ContainsKey(hashes[index]))
            {
                continue;
            }

            var length = originalTexts[index].Trim().Length;
            if ((int)Math.Floor((1.0 - minSimilarity) * length + 1e-9) > 0)
            {
                lengthsNeedingFuzzy.Add(length);
            }
        }

        var candidates = new List<TranslationMemoryRecord>();
        if (lengthsNeedingFuzzy.Count > 0)
        {
            // A candidate of length c can reach a string of length L only when
            // ceil(minSimilarity * L) <= c <= floor(L / minSimilarity). Widened by two characters at
            // each end because the comparison trims and the stored column does not.
            var minLength = (int)Math.Floor(minSimilarity * lengthsNeedingFuzzy.Min()) - 2;
            var maxLength = (int)Math.Ceiling(lengthsNeedingFuzzy.Max() / minSimilarity) + 2;

            candidates = await dbContext.TranslationMemory
                .AsNoTracking()
                .Where(r => r.SourceLocale == sourceName && r.TargetLocale == targetName
                    && r.OriginalText.Length >= minLength && r.OriginalText.Length <= maxLength)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var candidatesByLength = candidates
            .Select(record => (Record: record, Normalized: record.OriginalText.Trim()))
            .GroupBy(candidate => candidate.Normalized.Length)
            .ToDictionary(group => group.Key, group => group.ToList());
        var results = new List<FuzzyMatchResult?>(originalTexts.Count);

        for (var textIndex = 0; textIndex < originalTexts.Count; textIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalText = originalTexts[textIndex];
            var normalized = originalText.Trim();
            if (exactByHash.TryGetValue(hashes[textIndex], out var exact))
            {
                results.Add(new FuzzyMatchResult(ToEntry(exact), 1.0));
                continue;
            }

            FuzzyMatchResult? best = null;
            foreach (var (candidateLength, candidatesOfLength) in candidatesByLength)
            {
                // The Levenshtein distance can never be smaller than the length difference. At a
                // 97% auto-reuse threshold this removes almost every unrelated candidate before the
                // comparatively expensive dynamic-programming calculation.
                var maxLength = Math.Max(normalized.Length, candidateLength);
                var maximumDistance = (int)Math.Floor((1.0 - minSimilarity) * maxLength + 1e-9);
                if (maximumDistance == 0 || Math.Abs(normalized.Length - candidateLength) > maximumDistance)
                {
                    continue;
                }

                foreach (var candidate in candidatesOfLength)
                {
                    var similarity = StringSimilarity.RatioAtLeast(normalized, candidate.Normalized, minSimilarity);
                    if (similarity is not null && (best is null || similarity > best.Similarity))
                    {
                        best = new FuzzyMatchResult(ToEntry(candidate.Record), similarity.Value);
                    }
                }
            }

            results.Add(best);
        }

        return results;
    }

    public async Task<TranslationMemoryPage> SearchAsync(
        string? query,
        string? provider,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = dbContext.TranslationMemory.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            records = records.Where(record =>
                EF.Functions.Like(record.OriginalText, $"%{term}%") ||
                EF.Functions.Like(record.TranslatedText, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            records = records.Where(record => record.Provider == provider);
        }

        var total = await records.CountAsync(cancellationToken).ConfigureAwait(false);

        // Id descending rather than CreatedAtUtc: a whole batch shares a timestamp to the tick, and an
        // unstable order across pages is how a user scrolling a list sees the same row twice.
        var page = await records
            .OrderByDescending(record => record.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TranslationMemoryPage([.. page.Select(ToEntry)], total);
    }

    public async Task<IReadOnlyList<TranslationMemoryProviderCount>> CountByProviderAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var counts = await dbContext.TranslationMemory
            .AsNoTracking()
            .GroupBy(record => record.Provider)
            .Select(group => new { Provider = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. counts
            .OrderByDescending(count => count.Count)
            .Select(count => new TranslationMemoryProviderCount(count.Provider, count.Count))];
    }

    public async Task<bool> DeleteAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hash = ComputeHash(originalText);

        var removed = await dbContext.TranslationMemory
            .Where(record => record.OriginalHash == hash
                && record.SourceLocale == sourceLocale.Name
                && record.TargetLocale == targetLocale.Name)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    /// <summary>
    /// Deletes by hash in chunks, rather than one statement per text as the interface's default does.
    /// </summary>
    /// <remarks>
    /// The caller here is "forget this project": a real mod is several thousand strings, and the default
    /// implementation would open a transaction per string. The chunk size stays well under SQLite's
    /// 999-parameter limit for a single statement.
    /// </remarks>
    public async Task<int> DeleteManyAsync(
        IReadOnlyList<string> originalTexts,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalTexts);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        if (originalTexts.Count == 0)
        {
            return 0;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var source = sourceLocale.Name;
        var target = targetLocale.Name;
        var removed = 0;

        foreach (var chunk in originalTexts.Select(ComputeHash).Distinct(StringComparer.Ordinal).Chunk(500))
        {
            removed += await dbContext.TranslationMemory
                .Where(record => chunk.Contains(record.OriginalHash)
                    && record.SourceLocale == source
                    && record.TargetLocale == target)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return removed;
    }

    public async Task<int> DeleteByProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.TranslationMemory
            .Where(record => record.Provider == provider)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await SaveManyAsync([entry], cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveManyAsync(IReadOnlyList<TranslationMemoryEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var keys = entries
            .Select(entry => new
            {
                Entry = entry,
                Hash = ComputeHash(entry.OriginalText),
            })
            .ToList();
        var hashes = keys.Select(key => key.Hash).Distinct(StringComparer.Ordinal).ToList();
        var existingRecords = await dbContext.TranslationMemory
            .Where(record => hashes.Contains(record.OriginalHash))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingByKey = existingRecords.ToDictionary(
            record => (record.OriginalHash, record.SourceLocale, record.TargetLocale));

        foreach (var key in keys)
        {
            var entry = key.Entry;
            var compositeKey = (key.Hash, entry.SourceLocale.Name, entry.TargetLocale.Name);
            if (!existingByKey.TryGetValue(compositeKey, out var existing))
            {
                existing = new TranslationMemoryRecord
                {
                    OriginalHash = key.Hash,
                    OriginalText = entry.OriginalText,
                    SourceLocale = entry.SourceLocale.Name,
                    TargetLocale = entry.TargetLocale.Name,
                    TranslatedText = entry.TranslatedText,
                    Provider = entry.Provider,
                    ProfileName = entry.ProfileName,
                    CreatedAtUtc = entry.CreatedAtUtc,
                };
                dbContext.TranslationMemory.Add(existing);
                existingByKey[compositeKey] = existing;
            }
            else
            {
                existing.TranslatedText = entry.TranslatedText;
                existing.Provider = entry.Provider;
                existing.ProfileName = entry.ProfileName;
                existing.CreatedAtUtc = entry.CreatedAtUtc;
            }
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
