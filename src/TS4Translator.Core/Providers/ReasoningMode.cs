namespace TS4Translator.Core.Providers;

/// <summary>
/// Whether an engine should think before answering — for the models that can.
/// </summary>
/// <remarks>
/// A reasoning model writes its working out first, inside <c>&lt;think&gt;…&lt;/think&gt;</c>, and only then
/// the answer. That is a real trade for this app rather than an obvious win, which is why it is a
/// setting and not a default:
///
/// <list type="bullet">
/// <item>It is the plausible fix for the failure that hurts most here — a short line with a catch, where
/// a small model flattens "Buff" or a game term into whatever the sentence looked like it meant.
/// Thinking is exactly the budget needed to notice that the sentence has a catch.</item>
/// <item>It also costs several times the tokens per string, on a mod with thousands of them, and on
/// plain UI labels it buys nothing at all.</item>
/// </list>
///
/// The mode does not create the capability: a model without a thinking chat template ignores all three
/// values and behaves the same way. What the app always does, whatever is set here, is take the thinking
/// out of the answer before it reaches the string table — without that, a reasoning model's whole
/// monologue was being stored as the translation.
/// </remarks>
public enum ReasoningMode
{
    /// <summary>
    /// Suppress it. The engine is asked for the translation and nothing else, which is what short UI
    /// text wants and what every non-reasoning model does anyway.
    /// </summary>
    Off,

    /// <summary>
    /// Let the model think, and read the answer from after its reasoning. Costs a much larger token
    /// budget per string, so the engine also runs fewer strings at a time.
    /// </summary>
    On,

    /// <summary>
    /// Leave the model's own chat template alone and take whatever it does by default. The honest choice
    /// for a model that is built to reason and behaves badly when told not to.
    /// </summary>
    ModelDefault,
}
