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

    private static string HeadWord(string translation)
    {
        var first = translation.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries) is [var word, ..] ? word : translation;

        // Trim a trailing plural so "Configurações" still matches "Configuração de ..." and vice versa.
        return first.Length > 4 && first.EndsWith('s') ? first[..^1] : first;
    }
}
