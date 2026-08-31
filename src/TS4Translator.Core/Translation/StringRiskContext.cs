namespace TS4Translator.Core.Translation;

/// <summary>
/// What the app already knows about a specific string beyond its text, folded into its difficulty
/// score.
/// </summary>
/// <remarks>
/// Shape alone cannot see meaning: "Shallow" is one short word with no placeholders, scores near zero,
/// and Argos still rendered it "Rasgo" (a tear) instead of "Raso". These signals are the app's way of
/// noticing that class of problem anyway — not by understanding the text, but by remembering what
/// happened to it last time.
/// </remarks>
/// <param name="PreviouslyFlaggedForReview">
/// This string already came back from an engine with a validation warning, or a human marked it. The
/// single strongest signal available: something concrete went wrong here before.
/// </param>
/// <param name="HasGlossaryTerm">
/// A glossary term applies to this string, so there is terminology the translation must honour — worth
/// a stronger engine even when the sentence itself looks trivial.
/// </param>
public sealed record StringRiskContext(
    bool PreviouslyFlaggedForReview = false,
    bool HasGlossaryTerm = false);
