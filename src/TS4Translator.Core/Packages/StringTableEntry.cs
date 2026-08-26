namespace TS4Translator.Core.Packages;

/// <param name="KeyHash">The stable 32-bit FNV hash identifying this entry within its string table. Never changes across a translation.</param>
/// <param name="Value">The string stored for <paramref name="KeyHash"/>.</param>
public sealed record StringTableEntry(uint KeyHash, string Value);
