namespace TS4Translator.Core.Packages;

/// <summary>
/// App-level identity of a Sims 4 STBL (string table) resource within a package: its group and full
/// 64-bit instance id. The instance id's top byte encodes locale — see <see cref="IStringTableService"/>.
/// Deliberately independent of any DBPF library type, so <c>TS4Translator.Core</c> stays free of
/// third-party dependencies (see docs/ARCHITECTURE.md).
/// </summary>
public readonly record struct StblKey(uint Group, ulong FullInstance);
