using TS4Translator.Core.Glossary;
using TS4Translator.Infrastructure;

namespace TS4Translator.App.Services;

/// <summary>
/// Installs <see cref="DefaultGlossary"/> into the global glossary the first time the app runs.
/// </summary>
/// <remarks>
/// Seeded once and never again, tracked by a marker file rather than by "is the glossary empty". The
/// difference matters: a user who deletes a default term is making a decision, and re-adding it on the
/// next launch would override them silently. Removing is the intended way to disagree with a default,
/// so removal has to stick.
/// </remarks>
public sealed class GlossarySeeder(IGlossaryService glossaryService)
{
    private static string MarkerPath => Path.Combine(AppDataPaths.Data, "glossary-seeded");

    /// <summary>Seeds the defaults once. Safe to call on every launch.</summary>
    /// <returns>How many terms were added.</returns>
    public async Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(MarkerPath))
        {
            return 0;
        }

        var added = await AddMissingAsync(cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.Now.ToString("O"), cancellationToken).ConfigureAwait(false);
        return added;
    }

    /// <summary>
    /// Puts back any default the user removed. Explicit, because it undoes their earlier decision.
    /// </summary>
    public Task<int> RestoreAsync(CancellationToken cancellationToken = default) => AddMissingAsync(cancellationToken);

    private async Task<int> AddMissingAsync(CancellationToken cancellationToken)
    {
        var existing = await glossaryService.GetTermsAsync(projectId: null, cancellationToken).ConfigureAwait(false);
        var known = existing.Select(term => term.Original).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var term in DefaultGlossary.Terms.Where(term => !known.Contains(term.Original)))
        {
            // Case-sensitive: these are names, and matching is by substring. "CAS" must not fire on
            // "cases", and "Sims" must not fire on lowercase prose.
            await glossaryService.AddTermAsync(
                term.Original,
                term.Original,
                neverTranslate: true,
                caseSensitive: true,
                notes: term.Notes,
                projectId: null,
                cancellationToken).ConfigureAwait(false);
            added++;
        }

        return added;
    }
}
