namespace TS4Translator.Core.Glossary;

/// <param name="Original">The term exactly as it appears in mod text.</param>
/// <param name="Notes">Why it is here — shown in the glossary so the user can judge whether to keep it.</param>
public sealed record DefaultGlossaryTerm(string Original, string Notes);

/// <summary>
/// Proper nouns the app protects out of the box, so a fresh install already translates to a standard.
/// </summary>
/// <remarks>
/// Every entry is "never translate", and every entry is a <b>name</b>: a mod, a creator, a tool, or a
/// word The Sims itself keeps in Portuguese. That restriction is the whole design, and it came from the
/// data pushing back.
///
/// <para>Mining the user's 169.390 translated strings for high retention — how often an English word
/// survives verbatim into the translation — produced a ranked list that mixed two very different things:
/// genuine names, and ordinary words the engine simply fails to translate (<c>Lap</c> at 63%,
/// <c>Leggings</c> at 60%, <c>Shampoo</c> at 99%). Retention cannot tell them apart. Seeding the second
/// group would freeze a translation bug as a rule, so only names made it in.</para>
///
/// <para>The counts below are corruptions measured in that corpus: occurrences where the name was
/// translated into something else and nothing flagged it. <c>WickedWhims</c> alone was mangled 423 times
/// across 1.219 uses.</para>
///
/// <para>Deliberately absent: the bare word <c>Sim</c>. Terms match as substrings, so it would fire on
/// "Similar", "Simple" and "Simulation", and it is also an ordinary Portuguese word. <c>Sims</c> carries
/// the same protection without the collateral.</para>
/// </remarks>
public static class DefaultGlossary
{
    /// <summary>
    /// Names measured in real mods, ordered by how much damage leaving them unprotected caused.
    /// </summary>
    public static IReadOnlyList<DefaultGlossaryTerm> Terms { get; } =
    [
        // ---- medidos no corpus real, com corrupção observada ----
        new("WickedWhims", "Nome do mod. Foi traduzido 423 vezes em 1.219 usos, em 4 mods."),
        new("Wicked Whims", "Grafia alternativa do mesmo mod, corrompida em 6 de 8 usos."),
        new("Sims", "Nome do jogo e do seu substantivo. 261 corrupções em 19.229 usos - uma delas virou \"os símbolos pré-fabricados\"."),
        new("CAS", "Create-A-Sim, sigla padrão do jogo. 73 corrupções em 1.036 usos, em 11 mods."),
        new("Kritical", "Criador de mods. 22 corrupções em 91 usos."),
        new("Simoleon", "Moeda do jogo; cobre também \"Simoleons\"."),
        new("Sexpertise", "Perícia do WickedWhims, nome próprio da mecânica."),
        new("WooHoo", "Termo do jogo, mantido em português pela própria EA."),
        new("SimHub", "Ferramenta do ecossistema. 8.624 usos."),
        new("TURBODRIVER", "Criador do WickedWhims."),
        new("Khlas", "Criador de mods."),
        new("Plumbob", "O cristal acima do Sim; nome próprio do jogo."),
        new("Nisa", "Criadora de mods (Nisa's Wicked Perversions)."),
        new("Basemental", "Criador de mods."),
        new("Maxis", "Estúdio do jogo."),

        // ---- convenção do ecossistema, não medidos neste corpus ----
        new("Simlish", "O idioma fictício do jogo. Nome próprio."),
        new("MC Command Center", "Mod onipresente no Sims 4."),
        new("LittleMsSam", "Criadora de mods."),
        new("Lumpinou", "Criadora de mods."),
        new("Nraas", "Suíte de mods clássica."),
    ];
}
