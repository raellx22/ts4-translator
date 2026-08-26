namespace TS4Translator.Core.Packages;

/// <summary>
/// Read/write access to STBL resources inside a Sims 4 <c>.package</c> (DBPF) file. Writing never
/// touches the source package — callers always supply a distinct output path, per the project's rule
/// that the original mod is never modified (docs/ARCHITECTURE.md, brief section 7).
/// </summary>
public interface IPackageService
{
    /// <summary>Enumerates every STBL resource key found in the package at <paramref name="packagePath"/>.</summary>
    Task<IReadOnlyList<StblKey>> FindStringTablesAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>Reads every entry of the STBL resource identified by <paramref name="key"/>.</summary>
    Task<IReadOnlyList<StringTableEntry>> ReadStringTableAsync(string packagePath, StblKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a brand-new package at <paramref name="outputPackagePath"/> containing a single STBL
    /// resource built from <paramref name="entries"/>. Fails if a file already exists at that path.
    /// </summary>
    Task WriteStringTableAsync(string outputPackagePath, StblKey key, IReadOnlyList<StringTableEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a brand-new package at <paramref name="outputPackagePath"/> containing one STBL resource
    /// per entry in <paramref name="tables"/> — used when a project's strings span more than one
    /// source STBL and the export should still be a single output package. Fails if a file already
    /// exists at that path.
    /// </summary>
    Task WriteStringTablesAsync(string outputPackagePath, IReadOnlyDictionary<StblKey, IReadOnlyList<StringTableEntry>> tables, CancellationToken cancellationToken = default);
}
