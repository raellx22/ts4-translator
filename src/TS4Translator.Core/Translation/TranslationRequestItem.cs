namespace TS4Translator.Core.Translation;

/// <param name="Id">Same identity as the originating <see cref="TranslationSourceItem"/>. Providers must echo it back unchanged.</param>
/// <param name="ProtectedText">Text with non-translatable tokens already replaced by opaque placeholders.</param>
public sealed record TranslationRequestItem(string Id, string ProtectedText);
