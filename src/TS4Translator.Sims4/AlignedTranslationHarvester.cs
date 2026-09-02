using System.Globalization;
using TS4Translator.Core.Packages;

namespace TS4Translator.Sims4;

/// <summary>
/// Default <see cref="IAlignedTranslationHarvester"/>: indexes both sides by <c>KeyHash</c> and joins.
/// </summary>
public sealed class AlignedTranslationHarvester(
    IPackageService packageService,
    IStringTableService stringTableService) : IAlignedTranslationHarvester
{
    public async Task<TranslationHarvest> HarvestAsync(
        IReadOnlyList<string> sourceFolders,
        IReadOnlyList<string> translatedFolders,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFolders);
        ArgumentNullException.ThrowIfNull(translatedFolders);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        var scanned = 0;

        progress?.Report($"Lendo os textos em {sourceLocale.Name}...");
        var source = await IndexAsync(sourceFolders, sourceLocale, count => scanned += count, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report($"Lendo as traduções em {targetLocale.Name}...");
        var translated = await IndexAsync(translatedFolders, targetLocale, count => scanned += count, progress, cancellationToken).ConfigureAwait(false);

        var pairs = new List<AlignedTranslation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ambiguous = 0;

        foreach (var (hash, translations) in translated)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!source.TryGetValue(hash, out var originals))
            {
                continue;
            }

            // More than one distinct text behind a single hash means two mods collided on it. Which
            // original this translation belongs to is then a guess, and a wrong pair here would teach
            // the glossary and the memory something false about every future mod.
            if (originals.Count != 1 || translations.Count != 1)
            {
                ambiguous++;
                continue;
            }

            var original = originals.First();
            var translation = translations.First();

            // An untranslated entry is what a translation package looks like where the translator had
            // not got to it yet; storing it would teach the app that the English is the Portuguese.
            if (string.Equals(original, translation, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(original))
            {
                pairs.Add(new AlignedTranslation(original, translation));
            }
        }

        return new TranslationHarvest(scanned, source.Count, translated.Count, ambiguous, pairs);
    }

    /// <summary>
    /// Every string of <paramref name="locale"/> under <paramref name="folders"/>, keyed by hash.
    /// </summary>
    /// <remarks>
    /// The value is a set rather than a string precisely so a collision is visible to the caller instead
    /// of being silently overwritten by whichever package happened to be read last.
    /// </remarks>
    private async Task<Dictionary<uint, HashSet<string>>> IndexAsync(
        IReadOnlyList<string> folders,
        CultureInfo locale,
        Action<int> countScanned,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var index = new Dictionary<uint, HashSet<string>>();
        var files = folders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.package", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var done = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;

            // A library holds thousands of packages and most are meshes and textures with no STBL at
            // all; one unreadable file must never stop a harvest that is otherwise working.
            try
            {
                foreach (var key in await packageService.FindStringTablesAsync(file, cancellationToken).ConfigureAwait(false))
                {
                    CultureInfo tableLocale;
                    try
                    {
                        tableLocale = stringTableService.GetLocale(key);
                    }
                    catch (Exception exception) when (exception is NotSupportedException or FormatException or ArgumentException)
                    {
                        continue;
                    }

                    if (!string.Equals(tableLocale.Name, locale.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var entry in await packageService.ReadStringTableAsync(file, key, cancellationToken).ConfigureAwait(false))
                    {
                        if (string.IsNullOrWhiteSpace(entry.Value))
                        {
                            continue;
                        }

                        if (!index.TryGetValue(entry.KeyHash, out var texts))
                        {
                            index[entry.KeyHash] = texts = new HashSet<string>(StringComparer.Ordinal);
                        }

                        texts.Add(entry.Value.Trim());
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                continue;
            }

            if (done % 250 == 0)
            {
                progress?.Report($"{done:N0} de {files.Count:N0} pacotes lidos em {locale.Name}...");
            }
        }

        countScanned(files.Count);
        return index;
    }
}
