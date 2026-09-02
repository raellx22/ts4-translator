using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Memory;

/// <summary>
/// Decides what may be written into the shared translation memory, and under whose name.
/// </summary>
/// <remarks>
/// The memory is global: an entry written while translating one mod is handed to every future mod that
/// contains the same sentence. That makes a bad entry considerably more expensive than a missing one, so
/// this is deliberately a gate rather than a formality.
///
/// It exists because the two highest-quality sources in the app — a human editing a line, and a reply
/// from a chat model — were the only two that never reached the memory at all, while the automatic
/// pipeline wrote to it on every run. That was backwards. But those two paths also lack what the
/// pipeline has: a validator that refuses to persist anything which raised an issue.
/// Without a stand-in for that, the first thing that would be memorised is the English paragraph a chat
/// model declined to translate — poisoning every mod that shares that line.
/// </remarks>
public static class TranslationMemoryPolicy
{
    /// <summary>Recorded as the provider for a line the user typed or corrected themselves.</summary>
    public const string ManualProvider = "manual";

    /// <summary>Recorded as the provider for a line brought back from an external chat model.</summary>
    public const string ChatProvider = "chat";

    /// <summary>
    /// Recorded for a translation recovered from a community translation package.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="ManualProvider"/> even though a person wrote both. These came from
    /// somebody else's translation of somebody else's mod, so the user may disagree with a translator's
    /// choices wholesale — and the Memória screen can then clear all of them in one gesture, which it
    /// cannot do if they are mixed in with the lines the user typed themselves.
    /// </remarks>
    public const string CommunityProvider = "comunidade";

    /// <summary>
    /// Whether this pair is worth remembering for every future mod.
    /// </summary>
    /// <remarks>
    /// Three refusals, each for a failure that actually happened:
    /// <list type="bullet">
    /// <item>Empty translations — nothing to reuse.</item>
    /// <item>A translation identical to its source. In the thousand-string import that prompted this,
    /// one sentence came back untranslated and was stored as a finished translation; memorising it would
    /// have taught the app that the English is the Portuguese.</item>
    /// <item>Tuning identifiers (<see cref="StringKind.Code"/>) — <c>debug_savedActor2</c> mapped to
    /// itself is not knowledge, and a fifth of a mod's string table looks like that.</item>
    /// </list>
    /// </remarks>
    public static bool ShouldRemember(string? originalText, string? translatedText)
    {
        if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        if (string.Equals(originalText.Trim(), translatedText.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return StringTranslatability.IsWorthTranslating(originalText);
    }
}
