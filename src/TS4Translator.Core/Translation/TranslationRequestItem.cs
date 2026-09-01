namespace TS4Translator.Core.Translation;

/// <param name="Id">Same identity as the originating <see cref="TranslationSourceItem"/>. Providers must echo it back unchanged.</param>
/// <param name="ProtectedText">Text with non-translatable tokens already replaced by opaque placeholders.</param>
/// <param name="Id">Identifies this item in the provider's answer. Never positional.</param>
/// <param name="ProtectedText">Source text with game tokens replaced by placeholders.</param>
/// <param name="CurrentDraft">
/// On a revision pass, the translation being improved — already in protected form, so the draft and the
/// source speak about the same placeholders.
/// </param>
/// <remarks>
/// The draft travels per item rather than only inside the prompt because not every engine takes a batch.
/// A provider that translates one string per request cannot act on "here are the drafts for all 21 ids";
/// it needs the draft for the string in front of it.
/// </remarks>
public sealed record TranslationRequestItem(string Id, string ProtectedText, string? CurrentDraft = null);
