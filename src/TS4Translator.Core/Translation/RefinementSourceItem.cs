namespace TS4Translator.Core.Translation;

/// <param name="Id">Caller-assigned identity for this item, stable across the whole pipeline.</param>
/// <param name="Text">The untouched source text.</param>
/// <param name="CurrentTranslation">
/// The translation being improved — typically a fast, literal one from Argos. Providers that accept a
/// custom prompt are shown this so they can correct meaning and tone rather than start over; the ones
/// that do not simply retranslate <paramref name="Text"/> with a different engine, which is itself the
/// point when the user picks, say, Google Translate over Argos.
/// </param>
public sealed record RefinementSourceItem(string Id, string Text, string? CurrentTranslation);
