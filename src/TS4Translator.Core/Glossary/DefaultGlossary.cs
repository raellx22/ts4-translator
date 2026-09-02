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

/// <param name="Original">The English term exactly as it appears in mod text.</param>
/// <param name="Translation">The Portuguese it must become.</param>
public sealed record DefaultTermTranslation(string Original, string Translation);

/// <summary>
/// Terms the app requires a specific translation for, rather than merely protecting from translation.
/// </summary>
/// <remarks>
/// A different kind of rule from <see cref="DefaultGlossary"/>, and it exists because the measurement
/// said so. Seven local models translated the same 120 lines against the user's own approved
/// translations; a glossary roughly <b>doubled</b> the exact-match rate for every one of them, from
/// 5-19% to 17-31%. Nothing else tried — a bigger model, a better quantisation, the model's own
/// reasoning — moved the number that much, and this one costs nothing per string.
///
/// <para><b>Where the terms come from.</b> Overwhelmingly from translations that already existed. The
/// Sims 4 keeps every string under a <c>KeyHash</c> that is identical across locales, so a community
/// translation package and the mod it was written for align exactly — see
/// <see cref="Packages.IAlignedTranslationHarvester"/>. Pairing 251 translation packages against 15.111
/// installed mods produced 18.635 aligned pairs, of which 5.536 are short labels. The rest of the list
/// is the 387 terms mined earlier from the strings this project's user corrected by hand.</para>
///
/// <para><b>What was thrown away, which is the part that makes it trustworthy.</b> Four filters, each
/// answering a way the list could have been worse than no list at all:</para>
/// <list type="number">
/// <item><b>Self-consistency (358 dropped).</b> Every candidate was checked against every other pair in
/// the corpus containing that term. <c>Black → Preto</c> agreed with only 13% of the 111 lines that use
/// "Black"; <c>Leave → Sair</c> with 27% of 45; <c>Have Sex → Fazer sexo</c> with 42% of 73. A
/// context-dependent word frozen into a rule is a mistake repeated on every mod.</item>
/// <item><b>The user's own standard wins (89 dropped, 211 overridden).</b> No term may contradict a
/// translation this user approved by hand.</item>
/// <item><b>European Portuguese (2 dropped).</b> The Sims 4 has one locale byte for Portuguese, so a
/// translation made in Portugal arrives labelled pt-BR. <c>Definições</c> and <c>Recordações</c> in a
/// pt-BR glossary would be a silent regression.</item>
/// <item><b>Rules that fight each other (114 dropped).</b> Where a short term fires inside a longer one
/// and they disagree, one has to go — and it is the community's, never the user's.</item>
/// </list>
///
/// <para>Held as an embedded TSV rather than five thousand C# literals: it is data, it should read and
/// diff as data, and <c>DefaultTermGlossaryTests</c> re-checks every invariant above at build time.</para>
///
/// <para>Every entry is a suggestion the user owns: removing one in the Glossário screen sticks, and the
/// seeder never puts it back.</para>
/// </remarks>
public static class DefaultTermGlossary
{
    /// <summary>
    /// Bumped when terms are added. The seeder records the version it applied, so an existing install
    /// receives later additions without a term it deliberately deleted coming back.
    /// </summary>
    public const int Version = 3;

    private const string ResourceName = "TS4Translator.Core.Glossary.default-terms.tsv";

    private static readonly Lazy<IReadOnlyList<DefaultTermTranslation>> LazyTerms = new(Load);

    public static IReadOnlyList<DefaultTermTranslation> Terms => LazyTerms.Value;

    private static List<DefaultTermTranslation> Load()
    {
        using var stream = typeof(DefaultTermGlossary).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Recurso embutido não encontrado: {ResourceName}.");

        using var reader = new StreamReader(stream);
        var terms = new List<DefaultTermTranslation>(5200);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('\t', StringComparison.Ordinal);
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            terms.Add(new DefaultTermTranslation(line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        return terms;
    }
}
