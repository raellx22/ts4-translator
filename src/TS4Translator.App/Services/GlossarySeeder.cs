using System.Globalization;
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

    /// <summary>
    /// Seeds whatever this build adds that the install has not seen. Safe to call on every launch.
    /// </summary>
    /// <remarks>
    /// The marker now records a <b>version</b> rather than just existing. A plain "have I ever seeded"
    /// flag meant an install that ran once could never receive a later default — and the round that
    /// added <see cref="DefaultTermGlossary"/> put 390 measured terms in, which every existing install
    /// would have missed. Recording the version applied lets a new block reach them while a term the
    /// user deleted from an older block stays deleted, which is the promise below.
    /// </remarks>
    /// <returns>How many terms were added.</returns>
    public async Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var seeded = await ReadSeededVersionAsync(cancellationToken).ConfigureAwait(false);
        if (seeded >= DefaultTermGlossary.Version)
        {
            return 0;
        }

        // Withdrawals run first, and the order is what makes one list do two jobs: a bad default that is
        // still in the TSV is taken out here and put straight back by AddMissingAsync in its corrected
        // form, while one that was dropped from the TSV is taken out and stays out. Adding first would
        // make the corrected term look like a term already present, and nothing would change.
        await RemoveWithdrawnAsync(cancellationToken).ConfigureAwait(false);

        // Only what this version introduced: names came in with version 1, so an install that already
        // has them (and may have pruned some) is left alone and receives the term block only.
        var added = await AddMissingAsync(includeNames: seeded < 1, includeTerms: true, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(MarkerPath, DefaultTermGlossary.Version.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        return added;
    }

    /// <summary>
    /// Takes back a default that shipped wrong — but only where it still says what it shipped saying.
    /// </summary>
    /// <remarks>
    /// Fixing the embedded list only ever helped the next install; the term stayed in the database of
    /// every install that had already seeded, with nothing able to remove it but the user noticing —
    /// seeding adds what is missing and never revisits what is present. This closes that, and matching on
    /// the stored translation is what keeps it honest: a term the user has already corrected by hand no
    /// longer matches and is left exactly as they made it.
    ///
    /// Called before <see cref="AddMissingAsync"/>, which is what turns the same list into a correction
    /// as well as a retraction — see <see cref="DefaultTermGlossary.Withdrawn"/>.
    /// </remarks>
    private async Task RemoveWithdrawnAsync(CancellationToken cancellationToken)
    {
        if (DefaultTermGlossary.Withdrawn.Count == 0)
        {
            return;
        }

        var existing = await glossaryService.GetTermsAsync(projectId: null, cancellationToken).ConfigureAwait(false);

        foreach (var withdrawn in DefaultTermGlossary.Withdrawn)
        {
            var stale = existing.FirstOrDefault(term =>
                string.Equals(term.Original, withdrawn.Original, StringComparison.Ordinal)
                && string.Equals(term.Translation, withdrawn.Translation, StringComparison.Ordinal));

            if (stale is not null)
            {
                await glossaryService.DeleteTermAsync(stale.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Puts back any default the user removed. Explicit, because it undoes their earlier decision.
    /// </summary>
    public Task<int> RestoreAsync(CancellationToken cancellationToken = default) =>
        AddMissingAsync(includeNames: true, includeTerms: true, cancellationToken);

    /// <summary>
    /// The version already applied. A marker written before versioning existed holds a timestamp, and
    /// that install has the names but not the terms — which is exactly version 1.
    /// </summary>
    private static async Task<int> ReadSeededVersionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(MarkerPath))
        {
            return 0;
        }

        var content = (await File.ReadAllTextAsync(MarkerPath, cancellationToken).ConfigureAwait(false)).Trim();
        return int.TryParse(content, CultureInfo.InvariantCulture, out var version) ? version : 1;
    }

    private async Task<int> AddMissingAsync(bool includeNames, bool includeTerms, CancellationToken cancellationToken)
    {
        var existing = await glossaryService.GetTermsAsync(projectId: null, cancellationToken).ConfigureAwait(false);
        var known = existing.Select(term => term.Original).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;

        if (includeNames)
        {
            foreach (var term in DefaultGlossary.Terms.Where(term => !known.Contains(term.Original)))
            {
                // Case-sensitive: these are names. "CAS" must not fire on "cas" in prose, and "Sims"
                // must not fire on a lowercase sentence.
                await glossaryService.AddTermAsync(
                    term.Original,
                    term.Original,
                    neverTranslate: true,
                    caseSensitive: true,
                    notes: term.Notes,
                    projectId: null,
                    cancellationToken).ConfigureAwait(false);
                known.Add(term.Original);
                added++;
            }
        }

        if (includeTerms)
        {
            foreach (var term in DefaultTermGlossary.Terms.Where(term => !known.Contains(term.Original)))
            {
                // Case-insensitive, unlike the names: a mod writes the same label as "Body Selector",
                // "body selector" and "BODY SELECTOR", and the rule is about the word either way. Safe
                // now that matching is on word boundaries rather than substrings.
                // No note, deliberately. The same sentence on all 5.143 terms is not information about
                // any of them, and it is not free: TranslationBundleWriter prints a term's note under
                // its line, so one boilerplate sentence became hundreds of repeated lines in every file
                // pasted into a chat window. Where these came from belongs in DEC-048, not in each row.
                await glossaryService.AddTermAsync(
                    term.Original,
                    term.Translation,
                    neverTranslate: false,
                    caseSensitive: false,
                    notes: null,
                    projectId: null,
                    cancellationToken).ConfigureAwait(false);
                known.Add(term.Original);
                added++;
            }
        }

        return added;
    }
}
