using System.Globalization;
using TS4Translator.Core.Projects;

namespace TS4Translator.Core.Packages;

/// <summary>
/// Decides which STBL resources an export writes, with which entries, and into how many output
/// packages. Split out of the Editor's export command because this is exactly where a real export of
/// the WickedWhims tuning package failed: see <see cref="Build"/>.
/// </summary>
public static class StringTableExportPlan
{
    /// <summary>
    /// Builds the export plan for <paramref name="entries"/>, one output package per source mod.
    /// </summary>
    /// <param name="entries">Every string stored for the project.</param>
    /// <param name="sourceLocale">The project's source locale — only its tables hold source text.</param>
    /// <param name="localeOf">Resolves an STBL's locale, or <c>null</c> when the key encodes no locale The Sims 4 knows.</param>
    /// <param name="toTargetKey">Retargets a source STBL key to the project's target locale.</param>
    /// <remarks>
    /// Three things this must get right, all learned from real failures:
    /// <list type="bullet">
    /// <item>A published mod ships one STBL per locale it already supports (the real WickedWhims tuning
    /// package has 110, only 5 of them English). Only the source locale's tables are source text; the
    /// rest are that mod's own existing translations and must not be exported.</item>
    /// <item>Retargeting several source tables to one target locale legitimately collapses them onto the
    /// same key, so tables are merged, never fed to <c>ToDictionary</c> — which threw
    /// <see cref="ArgumentException"/> ("An item with the same key has already been added") and killed
    /// the export outright.</item>
    /// <item>A project can span many mods imported at once. Emitting a single merged package for all of
    /// them made a twelve-mod import look like only the first mod had been translated, so each source
    /// `.package` gets its own output. Rows stored before the app tracked the source file share one
    /// unnamed output, which is exactly the old behaviour.</item>
    /// </list>
    /// When the project holds no source-locale strings at all (nothing to lose by being permissive, and
    /// a mod that ships only, say, French text still deserves an export), every string is exported
    /// instead of none.
    /// </remarks>
    public static ExportPlan Build(
        IReadOnlyList<ProjectStringEntry> entries,
        CultureInfo sourceLocale,
        Func<StblKey, CultureInfo?> localeOf,
        Func<StblKey, StblKey> toTargetKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(localeOf);
        ArgumentNullException.ThrowIfNull(toTargetKey);

        var isSourceLocale = new Dictionary<StblKey, bool>();
        bool FromSourceLocale(StblKey key)
        {
            if (!isSourceLocale.TryGetValue(key, out var result))
            {
                var locale = localeOf(key);
                result = locale is not null && string.Equals(locale.Name, sourceLocale.Name, StringComparison.OrdinalIgnoreCase);
                isSourceLocale[key] = result;
            }

            return result;
        }

        var exportable = entries.Where(entry => FromSourceLocale(entry.SourceStbl)).ToList();
        var excludedCount = entries.Count - exportable.Count;
        if (exportable.Count == 0)
        {
            exportable = [.. entries];
            excludedCount = 0;
        }

        // Rows stored before the app tracked the source file have none; they all share one bucket,
        // keyed by an empty string because a dictionary key cannot be null.
        const string UnknownSourceFile = "";

        var merged = new Dictionary<string, Dictionary<StblKey, Dictionary<uint, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in exportable)
        {
            var sourceFileKey = entry.SourceFile ?? UnknownSourceFile;
            if (!merged.TryGetValue(sourceFileKey, out var tables))
            {
                tables = [];
                merged[sourceFileKey] = tables;
            }

            var targetKey = toTargetKey(entry.SourceStbl);
            if (!tables.TryGetValue(targetKey, out var byKeyHash))
            {
                byKeyHash = [];
                tables[targetKey] = byKeyHash;
            }

            // An untranslated string still has to be written, otherwise the exported table would be
            // missing entries the game expects; the original text is the only sane stand-in.
            byKeyHash[entry.KeyHash] = entry.TranslatedText ?? entry.OriginalText;
        }

        var outputs = merged
            .Select(mod => new ExportPackagePlan(
                mod.Key.Length == 0 ? null : mod.Key,
                mod.Value.ToDictionary(
                    table => table.Key,
                    IReadOnlyList<StringTableEntry> (table) => table.Value
                        .Select(item => new StringTableEntry(item.Key, item.Value))
                        .ToList())))
            .OrderBy(output => output.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExportPlan(outputs, excludedCount);
    }
}

/// <param name="SourceFile">The imported `.package` these tables came from, or null for rows stored before the app tracked it.</param>
/// <param name="Tables">The STBL resources to write into this output package, keyed by their target-locale key.</param>
public sealed record ExportPackagePlan(
    string? SourceFile,
    IReadOnlyDictionary<StblKey, IReadOnlyList<StringTableEntry>> Tables)
{
    public int StringCount => Tables.Sum(table => table.Value.Count);

    /// <summary>The mod's own file name, without extension, or null when the source file is unknown.</summary>
    public string? SourceModName => SourceFile is null ? null : Path.GetFileNameWithoutExtension(SourceFile);
}

/// <param name="Packages">One entry per output `.package` to write.</param>
/// <param name="ExcludedStringCount">How many stored strings were left out because they belong to a locale other than the project's source locale.</param>
public sealed record ExportPlan(
    IReadOnlyList<ExportPackagePlan> Packages,
    int ExcludedStringCount)
{
    public int TableCount => Packages.Sum(package => package.Tables.Count);

    public int StringCount => Packages.Sum(package => package.StringCount);
}
