using TS4Translator.Core.Packages;

namespace TS4Translator.Core.Projects;

/// <summary>
/// One STBL resource as it was found during an import, together with the `.package` it came from.
/// </summary>
/// <remarks>
/// The source file is part of the identity, not decoration. A project can import a dozen mods at once,
/// and two unrelated mods are free to ship an STBL with the same group and instance id — without the
/// file in the key, importing the second one would silently replace the first one's strings, and the
/// export would have no way to write one `.package` per mod.
/// </remarks>
/// <param name="SourceFile">Full path of the `.package` this table was read from. Null only for rows stored before the app tracked it.</param>
/// <param name="Stbl">The STBL resource key within that package.</param>
public sealed record ImportedTable(string? SourceFile, StblKey Stbl);
