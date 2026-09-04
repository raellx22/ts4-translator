using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Tests;

/// <summary>
/// The default term list is data, and a bad row in it is worse than a missing one: it instructs every
/// engine to make the same mistake on every mod. These are the invariants that keep it safe to ship.
/// </summary>
public class DefaultTermGlossaryTests
{
    [Fact]
    public void EveryTerm_HasBothSides()
    {
        foreach (var term in DefaultTermGlossary.Terms)
        {
            Assert.False(string.IsNullOrWhiteSpace(term.Original), "termo sem original");
            Assert.False(string.IsNullOrWhiteSpace(term.Translation), $"\"{term.Original}\" sem tradução");
        }
    }

    [Fact]
    public void NoTermIsListedTwice()
    {
        var duplicates = DefaultTermGlossary.Terms
            .GroupBy(term => term.Original, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// A one or two character term matches everywhere and means nothing; a whole sentence matches
    /// nothing. Both are rules that cannot pay for the prompt space they take.
    /// </summary>
    [Fact]
    public void EveryTerm_IsALabelSizedString()
    {
        foreach (var term in DefaultTermGlossary.Terms)
        {
            Assert.InRange(term.Original.Length, 3, 40);
            Assert.InRange(term.Translation.Length, 1, 60);
        }
    }

    /// <summary>
    /// No rule may say "leave this in English" by accident — that is what the never-translate list is
    /// for, and a term mapping to itself here would waste a prompt line on every batch it appears in.
    /// </summary>
    [Fact]
    public void NoTerm_TranslatesToItself()
    {
        var identity = DefaultTermGlossary.Terms
            .Where(term => string.Equals(term.Original.Trim(), term.Translation.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(term => !string.Equals(term.Original, "Bondage", StringComparison.OrdinalIgnoreCase))
            .Select(term => term.Original)
            .ToList();

        Assert.Empty(identity);
    }

    /// <summary>
    /// A term that classifies as <see cref="StringKind.Code"/> is a tuning identifier, and the app never
    /// sends those to a translator at all — a rule about one is dead weight at best.
    /// </summary>
    [Fact]
    public void NoTerm_IsATuningIdentifier()
    {
        var code = DefaultTermGlossary.Terms
            .Where(term => StringTranslatability.Classify(term.Original) == StringKind.Code)
            .Select(term => term.Original)
            .ToList();

        Assert.Empty(code);
    }

    /// <summary>
    /// Two rules that fire on the same text must not disagree about it.
    /// </summary>
    /// <remarks>
    /// When a short term appears as a whole word inside a longer one, both rules reach the engine for
    /// any string containing the longer phrase — <c>Settings → Configurações</c> arrives next to
    /// <c>Sex Settings → Configurações de Sexo</c>. That is fine, and useful, as long as they agree.
    /// A pair that disagrees is worse than either rule alone: the prompt then contains two instructions
    /// about the same words and the engine picks one at random, which is how a glossary starts producing
    /// inconsistency instead of removing it.
    ///
    /// Nesting itself is not the defect and is not checked here — <c>Interaction</c> deliberately does
    /// <b>not</b> fire inside <c>Interactions</c>, because the plural carries its own rule. That is the
    /// word-boundary matcher working, and <see cref="GlossaryTermMatchingTests"/> owns it.
    /// </remarks>
    [Fact]
    public void OverlappingTerms_DoNotContradictEachOther()
    {
        var conflicts = new List<string>();

        foreach (var shorter in DefaultTermGlossary.Terms)
        {
            var rule = new GlossaryTerm(0, shorter.Original, shorter.Translation, false, false, null, null);

            foreach (var longer in DefaultTermGlossary.Terms)
            {
                if (longer.Original.Length <= shorter.Original.Length || !rule.AppearsIn(longer.Original))
                {
                    continue;
                }

                // The head word is enough: Portuguese inflects ("Configuração"/"Configurações"), and
                // demanding the whole rendering would fail on agreement rather than on meaning.
                var head = HeadWord(shorter.Translation);
                if (!longer.Translation.Contains(head, StringComparison.CurrentCultureIgnoreCase))
                {
                    conflicts.Add($"\"{shorter.Original}\"->\"{shorter.Translation}\" contradiz \"{longer.Original}\"->\"{longer.Translation}\"");
                }
            }
        }

        Assert.True(conflicts.Count == 0, string.Join(" · ", conflicts.Take(15)));
    }

    /// <summary>
    /// A withdrawal must not name the translation the list currently ships.
    /// </summary>
    /// <remarks>
    /// The seeder removes withdrawn terms and then adds what is missing, so an entry matching the
    /// current TSV row would delete and re-add the same term on every version bump — churn that looks
    /// like it is doing something and never converges. The entry has to name the <i>old</i> text.
    /// </remarks>
    [Fact]
    public void WithdrawnTerms_NameTheTranslationTheyShippedWith_NotTheCurrentOne()
    {
        var current = DefaultTermGlossary.Terms
            .ToDictionary(term => term.Original, term => term.Translation, StringComparer.Ordinal);

        var pointless = DefaultTermGlossary.Withdrawn
            .Where(withdrawn => current.TryGetValue(withdrawn.Original, out var shipped)
                && string.Equals(shipped, withdrawn.Translation, StringComparison.Ordinal))
            .Select(withdrawn => withdrawn.Original)
            .ToList();

        Assert.True(pointless.Count == 0, string.Join(" · ", pointless));
    }

    /// <summary>Two entries for the same term would make the outcome depend on iteration order.</summary>
    [Fact]
    public void WithdrawnTerms_AreNotListedTwice()
    {
        var duplicates = DefaultTermGlossary.Withdrawn
            .GroupBy(term => term.Original, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// A mod's own section heading must come back with its rule intact, exactly as it went in.
    /// </summary>
    /// <remarks>
    /// The harvested list shipped <c>----------Misc----------</c> → <c>---------- Diversos ----------</c>:
    /// the same words, with the separator run silently re-spaced. That is the precise damage
    /// <see cref="TranslationSuspicion.LostStructure"/> was written to catch in an engine's output, and
    /// the app was seeding it as a rule — instructing every engine to introduce it deliberately, on a
    /// string shape where <c>LostStructure</c> is the highest-precision signal on the whole corpus.
    ///
    /// Only the decoration is compared, never the words between it.
    /// </remarks>
    [Fact]
    public void DecoratedTerms_KeepTheirDecorationExactly()
    {
        var mangled = DefaultTermGlossary.Terms
            .Where(term => Decoration(term.Original) is { Leading.Length: > 0 } or { Trailing.Length: > 0 })
            .Where(term => Decoration(term.Original) != Decoration(term.Translation))
            .Select(term => $"\"{term.Original}\" -> \"{term.Translation}\"")
            .ToList();

        Assert.True(mangled.Count == 0, string.Join(" · ", mangled.Take(15)));
    }

    /// <summary>The runs of separator characters at each end, which a translation must reproduce.</summary>
    private static (string Leading, string Trailing) Decoration(string text)
    {
        const string separators = "-=_~*#";

        var start = 0;
        while (start < text.Length && separators.Contains(text[start], StringComparison.Ordinal))
        {
            start++;
        }

        var end = text.Length;
        while (end > start && separators.Contains(text[end - 1], StringComparison.Ordinal))
        {
            end--;
        }

        return (text[..start], text[end..]);
    }

    private static string HeadWord(string translation)
    {
        var first = translation.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries) is [var word, ..] ? word : translation;

        // Trim a trailing plural so "Configurações" still matches "Configuração de ..." and vice versa.
        return first.Length > 4 && first.EndsWith('s') ? first[..^1] : first;
    }
}
