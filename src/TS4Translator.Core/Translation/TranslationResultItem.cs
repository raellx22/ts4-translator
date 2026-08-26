namespace TS4Translator.Core.Translation;

/// <param name="Id">Must match the <see cref="TranslationRequestItem.Id"/> it answers. Callers reconcile by this id, never by list position.</param>
/// <param name="TranslatedProtectedText">The translated text, with placeholders still in place (not yet restored).</param>
public sealed record TranslationResultItem(string Id, string TranslatedProtectedText);
