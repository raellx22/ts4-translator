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
/// <param name="IsUncoveredLabel">
/// A bare label whose wording the app knows nothing about: no glossary term applies and translation
/// memory has never seen it.
/// </param>
/// <remarks>
/// <paramref name="IsUncoveredLabel"/> is the signal that turned out to matter most, and it was measured
/// rather than reasoned about. Two local engines translated 227 strings from eight unseen mods; the
/// small one returned 26 of them untranslated, in English, and <b>all 26 were one-to-three word labels
/// with no sentence around them</b> — "Twink", "Mellow", "Blue Eyes", "Cheese Toastie". Their mean
/// <see cref="StringDifficulty"/> score was 14,1 against 24,0 for the strings it handled fine, so at the
/// default threshold of 35 <b>none</b> of them would have been routed to the better engine. The routing
/// signal was not merely weak on this failure: it pointed the wrong way.
///
/// Length cannot fix that on its own, because short labels are most of a mod — 55 thousand of
/// WickedWhims' 149 thousand — and promoting all of them would send the whole job to the expensive
/// engine. What separates "Blue Eyes", which any engine gets right, from "Twink", which stops it dead,
/// is not shape but whether the app has ever been told what the words mean. Coverage is exactly that,
/// and the app already knows it.
/// </remarks>
public sealed record StringRiskContext(
    bool PreviouslyFlaggedForReview = false,
    bool HasGlossaryTerm = false,
    bool IsUncoveredLabel = false);
