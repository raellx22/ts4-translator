using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Glossary;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfGlossaryService(IDbContextFactory<TranslatorDbContext> dbContextFactory) : IGlossaryService
{
    public async Task<GlossaryTerm> AddTermAsync(string original, string translation, bool neverTranslate, bool caseSensitive, string? notes, Guid? projectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(original);
        ArgumentNullException.ThrowIfNull(translation);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = new GlossaryTermRecord
        {
            Original = original,
            Translation = translation,
            NeverTranslate = neverTranslate,
            CaseSensitive = caseSensitive,
            Notes = notes,
            ProjectId = projectId,
        };

        dbContext.GlossaryTerms.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToTerm(record);
    }

    public async Task<IReadOnlyList<GlossaryTerm>> GetTermsAsync(Guid? projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await dbContext.GlossaryTerms
            .AsNoTracking()
            .Where(r => r.ProjectId == null || r.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(ToTerm).ToList();
    }

    public async Task<GlossaryTerm?> UpdateTermAsync(int termId, string original, string translation, bool neverTranslate, bool caseSensitive, string? notes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(original);
        ArgumentNullException.ThrowIfNull(translation);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.GlossaryTerms.FirstOrDefaultAsync(r => r.Id == termId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        record.Original = original;
        record.Translation = translation;
        record.NeverTranslate = neverTranslate;
        record.CaseSensitive = caseSensitive;
        record.Notes = notes;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToTerm(record);
    }

    public async Task DeleteTermAsync(int termId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.GlossaryTerms.FirstOrDefaultAsync(r => r.Id == termId, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            dbContext.GlossaryTerms.Remove(record);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static GlossaryTerm ToTerm(GlossaryTermRecord record) =>
        new(record.Id, record.Original, record.Translation, record.NeverTranslate, record.CaseSensitive, record.Notes, record.ProjectId);
}
