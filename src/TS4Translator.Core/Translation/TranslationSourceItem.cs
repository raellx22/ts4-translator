namespace TS4Translator.Core.Translation;

/// <param name="Id">Caller-assigned identity for this item (e.g. an STBL key hash), stable across the whole pipeline.</param>
/// <param name="Text">The text to translate, exactly as extracted from the source (not yet token-protected).</param>
public sealed record TranslationSourceItem(string Id, string Text);
