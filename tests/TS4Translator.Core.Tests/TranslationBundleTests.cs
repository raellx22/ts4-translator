using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Interchange;

namespace TS4Translator.Core.Tests;

public sealed class TranslationBundleTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static TranslationBundle Bundle(params BundleItem[] items) =>
        new("Projeto", English, PortugueseBrazil, items);

    private static BundleItem Item(int id, string original, string? draft = null) => new(id, original, draft);

    // ---------- escaping ----------

    [Theory]
    [InlineData("simples", "simples")]
    [InlineData("com\nquebra", @"com\nquebra")]
    [InlineData("barra \\ invertida", @"barra \\ invertida")]
    [InlineData("tab\there", @"tab\there")]
    public void Escape_ProducesASingleLine(string text, string expected)
    {
        var escaped = BundleText.Escape(text);

        Assert.Equal(expected, escaped);
        Assert.DoesNotContain('\n', escaped);
    }

    [Theory]
    [InlineData("Shallow")]
    [InlineData("Duas\nlinhas\ne mais")]
    [InlineData("Barra \\ e \\n literal")]
    [InlineData("{0.SimFirstName} likes it.\r\nReally.")]
    public void EscapeThenUnescape_RoundTrips(string text)
        => Assert.Equal(text, BundleText.Unescape(BundleText.Escape(text)));

    /// <summary>A stray backslash from a model must not eat the character after it.</summary>
    [Fact]
    public void Unescape_UnknownEscape_KeepsBothCharacters()
        => Assert.Equal("\\q", BundleText.Unescape("\\q"));

    // ---------- writing ----------

    [Fact]
    public void Write_ProducesOneBlockPerItemWithIdOriginalAndEmptyTranslation()
    {
        var text = TranslationBundleWriter.Write(Bundle(Item(1042, "Shallow")));

        Assert.Contains(TranslationBundleWriter.FormatHeader, text, StringComparison.Ordinal);
        Assert.Contains("#1042", text, StringComparison.Ordinal);
        Assert.Contains("EN: Shallow", text, StringComparison.Ordinal);
        Assert.Contains(TranslationBundleWriter.BeginMarker, text, StringComparison.Ordinal);
        Assert.Contains(TranslationBundleWriter.EndMarker, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_MultiLineOriginal_StaysOnOneLine()
    {
        var text = TranslationBundleWriter.Write(Bundle(Item(1, "Primeira\nSegunda")));

        Assert.Contains(@"EN: Primeira\nSegunda", text, StringComparison.Ordinal);
    }

    /// <summary>An existing translation is offered as a draft on its own line, never pre-filled into PT:.</summary>
    [Fact]
    public void Write_ItemWithDraft_PutsItOnTheDraftLineAndLeavesTranslationEmpty()
    {
        var lines = TranslationBundleWriter.Write(Bundle(Item(7, "Shallow", "Rasgo")))
            .ReplaceLineEndings("\n")
            .Split('\n');

        Assert.Contains("ATUAL: Rasgo", lines, StringComparer.Ordinal);

        // PT: is left bare: the parser then never has to decide whether a PT: line is the model's answer
        // or the draft echoed back at it.
        Assert.Contains("PT:", lines, StringComparer.Ordinal);
    }

    // ---------- the happy path ----------

    [Fact]
    public void Read_WellFormedReply_MatchesByIdWithNoProblems()
    {
        var bundle = Bundle(Item(1, "Shallow"), Item(2, "Average Hips"));
        const string reply = """
            === INICIO ===

            #1
            EN: Shallow
            PT: Raso

            #2
            EN: Average Hips
            PT: Quadril médio

            === FIM ===
            """;

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Equal(2, result.Translations.Count);
        Assert.All(result.Translations, t => Assert.Equal(BundleMatchStrategy.ById, t.Strategy));
        Assert.Equal("Raso", result.Translations.First(t => t.Id == 1).Translation);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Read_EscapedNewline_ComesBackAsARealNewline()
    {
        var bundle = Bundle(Item(1, "A\nB"));
        const string reply = "#1\nEN: A\\nB\nPT: C\\nD";

        Assert.Equal("C\nD", Assert.Single(TranslationBundleReader.Read(reply, bundle).Translations).Translation);
    }

    // ---------- what chat models actually do ----------

    [Fact]
    public void Read_ReplyWrappedInProseAndACodeFence_IsStillParsed()
    {
        var bundle = Bundle(Item(1, "Shallow"));
        const string reply = """
            Claro! Aqui está o arquivo traduzido:

            ```
            === INICIO ===
            #1
            EN: Shallow
            PT: Raso
            === FIM ===
            ```

            Espero ter ajudado!
            """;

        var only = Assert.Single(TranslationBundleReader.Read(reply, bundle).Translations);
        Assert.Equal("Raso", only.Translation);
    }

    [Fact]
    public void Read_MarkdownDecoratedLabels_AreStillRecognized()
    {
        var bundle = Bundle(Item(1, "Shallow"));
        const string reply = """
            - **#1**
            - **EN:** Shallow
            - **PT:** Raso
            """;

        // The bold id line does not parse as an id, so this falls back to the original text - which is
        // exactly the graceful degradation the format is meant to have.
        var result = TranslationBundleReader.Read(reply, bundle);
        var only = Assert.Single(result.Translations);
        Assert.Equal("Raso", only.Translation);
    }

    /// <summary>The failure the user asked to be defended against: the reply comes back without the ids.</summary>
    [Fact]
    public void Read_ReplyWithoutIds_RecoversFromTheOriginalText()
    {
        var bundle = Bundle(Item(10, "Shallow"), Item(20, "Average Hips"));
        const string reply = """
            EN: Average Hips
            PT: Quadril médio

            EN: Shallow
            PT: Raso
            """;

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Equal(2, result.Translations.Count);
        Assert.All(result.Translations, t => Assert.Equal(BundleMatchStrategy.ByOriginalText, t.Strategy));
        Assert.Equal("Raso", result.Translations.First(t => t.Id == 10).Translation);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Read_NeitherIdsNorOriginals_ButCountsMatch_FallsBackToOrderWithAWarning()
    {
        var bundle = Bundle(Item(10, "Shallow"), Item(20, "Average Hips"));
        const string reply = """
            PT: Raso
            PT: Quadril médio
            """;

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Equal(2, result.Translations.Count);
        Assert.All(result.Translations, t => Assert.Equal(BundleMatchStrategy.ByPosition, t.Strategy));
        Assert.Equal("Raso", result.Translations.First(t => t.Id == 10).Translation);
        Assert.Contains(result.Problems, p => p.Severity == BundleProblemSeverity.Warning && p.Message.Contains("ordem", StringComparison.Ordinal));
    }

    /// <summary>Without ids and with a different count, guessing would corrupt the project - refuse instead.</summary>
    [Fact]
    public void Read_NeitherIdsNorOriginals_AndCountsDiffer_RefusesRatherThanGuessing()
    {
        var bundle = Bundle(Item(10, "Shallow"), Item(20, "Average Hips"), Item(30, "Foot Model"));
        const string reply = """
            PT: Raso
            PT: Quadril médio
            """;

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Empty(result.Translations);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Read_TruncatedReply_AppliesWhatArrivedAndSaysWhatIsMissing()
    {
        var bundle = Bundle(Item(1, "A"), Item(2, "B"), Item(3, "C"));
        const string reply = "#1\nEN: A\nPT: Aa\n\n#2\nEN: B\nPT: Bb";

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Equal(2, result.Translations.Count);
        Assert.Contains(result.Problems, p => p.Message.Contains("não voltaram", StringComparison.Ordinal));
    }

    // ---------- guarding the project ----------

    [Fact]
    public void Read_UnknownId_IsIgnoredWithAWarning()
    {
        var bundle = Bundle(Item(1, "Shallow"));
        const string reply = "#999\nEN: Outra\nPT: Qualquer";

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Empty(result.Translations);
        Assert.Contains(result.Problems, p => p.Message.Contains("#999", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RepeatedId_KeepsTheFirstAndWarns()
    {
        var bundle = Bundle(Item(1, "Shallow"));
        const string reply = "#1\nEN: Shallow\nPT: Raso\n\n#1\nEN: Shallow\nPT: Outro";

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Equal("Raso", Assert.Single(result.Translations).Translation);
        Assert.Contains(result.Problems, p => p.Message.Contains("mais de uma vez", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_DroppedPlaceholder_IsFlaggedButStillImported()
    {
        var bundle = Bundle(Item(1, "{0.SimFirstName} likes it."));
        const string reply = "#1\nEN: {0.SimFirstName} likes it.\nPT: Ele gosta disso.";

        var result = TranslationBundleReader.Read(reply, bundle);

        var only = Assert.Single(result.Translations);
        Assert.False(only.PlaceholdersMatch);
        Assert.Equal(1, result.PlaceholderMismatchCount);

        // Naming what went missing, rather than saying "marcadores" and leaving the user to hunt.
        Assert.Contains(result.Problems, p => p.Message.Contains("{0.SimFirstName}", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_PlaceholderMovedToANaturalPosition_IsAccepted()
    {
        var bundle = Bundle(Item(1, "{0.SimFirstName} is happy."));
        const string reply = "#1\nEN: {0.SimFirstName} is happy.\nPT: Está feliz, {0.SimFirstName}.";

        Assert.True(Assert.Single(TranslationBundleReader.Read(reply, bundle).Translations).PlaceholdersMatch);
    }

    [Fact]
    public void Read_EmptyTranslationLine_IsSkippedNotStoredAsBlank()
    {
        var bundle = Bundle(Item(1, "Shallow"));
        const string reply = "#1\nEN: Shallow\nPT:";

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Empty(result.Translations);
        Assert.Contains(result.Problems, p => p.Severity == BundleProblemSeverity.Info);
    }

    /// <summary>A whole sentence coming back untouched is the real failure this check exists for.</summary>
    [Fact]
    public void Read_SentenceIdenticalToOriginal_IsImportedButWarned()
    {
        var bundle = Bundle(Item(1, "Flogging for fun and profit."));
        const string reply = "#1\nEN: Flogging for fun and profit.\nPT: Flogging for fun and profit.";

        var result = TranslationBundleReader.Read(reply, bundle);

        Assert.Single(result.Translations);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(BundleProblemSeverity.Warning, problem.Severity);
        Assert.Equal(BundleProblemKind.SameAsOriginal, problem.Kind);
        Assert.Equal(1, problem.StringId);
    }

    /// <summary>
    /// A tuning identifier coming back unchanged is the correct answer, and calling it a warning is how
    /// one real miss ended up buried under 133 false alarms in a thousand-string import.
    /// </summary>
    [Theory]
    [InlineData("debug_savedActor2")]
    [InlineData("publicUseCd")]
    [InlineData("var2")]
    public void Read_IdentifierIdenticalToOriginal_IsOnlyAnObservation(string identifier)
    {
        var bundle = Bundle(Item(1, identifier));

        var result = TranslationBundleReader.Read($"#1\nEN: {identifier}\nPT: {identifier}", bundle);

        Assert.Single(result.Translations);
        Assert.Equal(0, result.WarningCount);
        Assert.Equal(BundleProblemSeverity.Info, Assert.Single(result.Problems).Severity);
    }

    /// <summary>A short label may legitimately survive translation unchanged; it is worth a note, not an alarm.</summary>
    [Fact]
    public void Read_ShortTermIdenticalToOriginal_IsOnlyAnObservation()
    {
        var result = TranslationBundleReader.Read("#1\nEN: Kritical\nPT: Kritical", Bundle(Item(1, "Kritical")));

        Assert.Equal(0, result.WarningCount);
        Assert.Equal(BundleProblemSeverity.Info, Assert.Single(result.Problems).Severity);
    }

    /// <summary>
    /// Sending a draft is a request to improve it. A reply that hands every draft back untouched looks
    /// like a success and is not one, and nothing else in the app would notice.
    /// </summary>
    [Fact]
    public void Read_TranslationIdenticalToTheDraftSent_IsReportedAsNotRefined()
    {
        var bundle = Bundle(Item(1, "Shallow", "Rasgo"));

        var result = TranslationBundleReader.Read("#1\nEN: Shallow\nPT: Rasgo", bundle);

        Assert.Equal("Rasgo", Assert.Single(result.Translations).Translation);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(BundleProblemKind.DraftKept, problem.Kind);
        Assert.Equal(BundleProblemSeverity.Info, problem.Severity);
    }

    [Fact]
    public void Read_TranslationThatImprovedTheDraft_IsNotReported()
    {
        var bundle = Bundle(Item(1, "Shallow", "Rasgo"));

        Assert.Empty(TranslationBundleReader.Read("#1\nEN: Shallow\nPT: Raso", bundle).Problems);
    }

    [Fact]
    public void Read_RepeatedOriginalInTheBundle_IsNotMatchedByTextAlone()
    {
        // Two strings share the same source text, so the text cannot identify either one.
        var bundle = Bundle(Item(1, "Open"), Item(2, "Open"));
        const string reply = "EN: Open\nPT: Abrir";

        var result = TranslationBundleReader.Read(reply, bundle);

        // Falls through to positional, which refuses because one translation cannot cover two strings.
        Assert.Empty(result.Translations);
        Assert.True(result.HasErrors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_EmptyReply_IsAnError(string? reply)
        => Assert.True(TranslationBundleReader.Read(reply, Bundle(Item(1, "A"))).HasErrors);

    [Fact]
    public void Read_ReplyWithNoRecognizableBlocks_IsAnError()
    {
        var result = TranslationBundleReader.Read("Desculpe, não consegui traduzir esse conteúdo.", Bundle(Item(1, "A")));

        Assert.True(result.HasErrors);
        Assert.Empty(result.Translations);
    }

    // ---------- full round trip ----------

    /// <summary>Write a bundle, fill every PT: line as a model would, read it back, get everything.</summary>
    [Fact]
    public void WriteThenFillThenRead_RoundTripsEveryString()
    {
        var bundle = Bundle(
            Item(1, "Shallow"),
            Item(2, "{0.SimFirstName} likes a clean-shaven face.", "rascunho ruim"),
            Item(3, "Linha um\nLinha dois"),
            Item(4, "<font color='#AB0C10'>{0.String}</font>"));

        var exported = TranslationBundleWriter.Write(bundle);

        var filled = string.Join('\n', exported.ReplaceLineEndings("\n").Split('\n').Select(line =>
            line == "PT:" ? "PT: traduzido " + line.Length : line));

        var result = TranslationBundleReader.Read(filled, bundle);

        Assert.Equal(4, result.Translations.Count);
        Assert.All(result.Translations, t => Assert.Equal(BundleMatchStrategy.ById, t.Strategy));
        Assert.DoesNotContain(result.Problems, p => p.Severity == BundleProblemSeverity.Error);
    }

    // ---------- splitting a mod across several files ----------

    /// <summary>
    /// The guarantee the whole "exportar o mod inteiro" flow rests on: every string lands in exactly one
    /// part, in order. Anything less and the user is back to checking by hand whether a line was missed.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 3)]
    [InlineData(10, 10)]
    [InlineData(10, 4)]
    [InlineData(1000, 300)]
    public void Split_CoversEveryItemExactlyOnceAndInOrder(int itemCount, int perPart)
    {
        var items = Enumerable.Range(1, itemCount).Select(id => Item(id, $"texto {id}")).ToList();

        var parts = BundleSplitter.Split("Projeto", English, PortugueseBrazil, items, perPart);

        Assert.All(parts, part => Assert.True(part.Items.Count <= perPart));
        Assert.Equal(items, parts.SelectMany(part => part.Items).ToList());
        Assert.Equal(parts.Count, parts[^1].PartCount);
        Assert.Equal([.. Enumerable.Range(1, parts.Count)], parts.Select(part => part.PartNumber).ToList());
    }

    [Fact]
    public void Split_WithNoLimit_KeepsEverythingInOneFile()
    {
        var items = Enumerable.Range(1, 5000).Select(id => Item(id, $"texto {id}")).ToList();

        var only = Assert.Single(BundleSplitter.Split("Projeto", English, PortugueseBrazil, items, 0));

        Assert.Equal(5000, only.Items.Count);
        Assert.False(only.IsSplit);
    }

    [Fact]
    public void Split_NoItems_ProducesNoFiles()
        => Assert.Empty(BundleSplitter.Split("Projeto", English, PortugueseBrazil, [], 100));

    [Fact]
    public void NameFor_SinglePart_KeepsTheNameTheUserChose()
    {
        var only = Assert.Single(BundleSplitter.Split("Projeto", English, PortugueseBrazil, [Item(1, "a")], 0));

        Assert.Equal(@"C:\saida\mod.txt", BundleSplitter.NameFor(@"C:\saida\mod.txt", only));
    }

    /// <summary>Zero-padded so Explorer sorts the parts in the order they must be pasted.</summary>
    [Fact]
    public void NameFor_SplitExport_NumbersThePartsSoTheySort()
    {
        var items = Enumerable.Range(1, 100).Select(id => Item(id, $"t{id}")).ToList();
        var parts = BundleSplitter.Split("Projeto", English, PortugueseBrazil, items, 10);

        Assert.Equal(@"C:\saida\mod_parte01de10.txt", BundleSplitter.NameFor(@"C:\saida\mod.txt", parts[0]));
        Assert.Equal(@"C:\saida\mod_parte10de10.txt", BundleSplitter.NameFor(@"C:\saida\mod.txt", parts[9]));
    }

    /// <summary>A model handed part 3 of 7 must not try to supply the strings it cannot see.</summary>
    [Fact]
    public void Write_OnePartOfSeveral_SaysSoInTheHeaderAndTheInstructions()
    {
        var items = Enumerable.Range(1, 30).Select(id => Item(id, $"texto {id}")).ToList();
        var parts = BundleSplitter.Split("Projeto", English, PortugueseBrazil, items, 10);

        var text = TranslationBundleWriter.Write(parts[2]);

        Assert.Contains("parte: 3 de 3", text, StringComparison.Ordinal);
        Assert.Contains("PARTE 3 DE 3", text, StringComparison.Ordinal);
        Assert.Contains("Nao procure, nao complete e nao invente", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_SingleFile_DoesNotMentionParts()
    {
        var text = TranslationBundleWriter.Write(Bundle(Item(1, "Shallow")));

        Assert.DoesNotContain("parte:", text, StringComparison.Ordinal);
    }

    // ---------- reporting ----------

    /// <summary>
    /// A hundred instances of one fact are one line on screen. This is the fix for a status bar that said
    /// "(+184 aviso(s))" and offered no way to see them.
    /// </summary>
    [Fact]
    public void Group_CollapsesProblemsByKindWorstFirst()
    {
        var bundle = Bundle(
            Item(1, "debug_one"),
            Item(2, "debug_two"),
            Item(3, "A whole sentence, written out."),
            Item(4, "{0.SimFirstName} is happy."));

        const string reply = """
            #1
            EN: debug_one
            PT: debug_one

            #2
            EN: debug_two
            PT: debug_two

            #3
            EN: A whole sentence, written out.
            PT: A whole sentence, written out.

            #4
            EN: {0.SimFirstName} is happy.
            PT: Está feliz.
            """;

        var groups = BundleImportReport.Group(TranslationBundleReader.Read(reply, bundle).Problems);

        Assert.Equal(3, groups.Count);
        Assert.Equal(BundleProblemSeverity.Warning, groups[0].Severity);
        Assert.Equal(BundleProblemSeverity.Warning, groups[1].Severity);

        var identifiers = Assert.Single(groups, group => group.Severity == BundleProblemSeverity.Info);
        Assert.Equal(2, identifiers.Messages.Count);
        Assert.Contains("2 texto(s)", identifiers.Title, StringComparison.Ordinal);
    }

    /// <summary>The saved report is the one place nothing is elided.</summary>
    [Fact]
    public void Write_Report_ContainsEveryMessage()
    {
        var items = Enumerable.Range(1, 40).Select(id => Item(id, $"debug_value{id}")).ToList();
        var reply = string.Join("\n\n", items.Select(item => $"#{item.Id}\nEN: {item.Original}\nPT: {item.Original}"));

        var result = TranslationBundleReader.Read(reply, Bundle([.. items]));
        var report = BundleImportReport.Write(["resposta.txt"], result);

        Assert.All(items, item => Assert.Contains($"#{item.Id} ", report, StringComparison.Ordinal));
        Assert.Contains("resposta.txt", report, StringComparison.Ordinal);
    }

    // ---------- glossary ----------

    private static GlossaryTerm Term(string original, string translation, bool neverTranslate = false, bool caseSensitive = false) =>
        new(1, original, translation, neverTranslate, caseSensitive, Notes: null, ProjectId: null);

    private static TranslationBundle WithGlossary(IReadOnlyList<GlossaryTerm> glossary, params BundleItem[] items) =>
        new("Projeto", English, PortugueseBrazil, items, Glossary: glossary);

    /// <summary>
    /// A chat window is a translation engine like any other, and it was the only one the app never told
    /// about the glossary.
    /// </summary>
    [Fact]
    public void Write_BundleWithGlossary_PutsTheTermsInTheFile()
    {
        var bundle = WithGlossary(
            [Term("Sim", "Sim"), Term("WickedWhims", "WickedWhims", neverTranslate: true)],
            Item(1, "The Sim is happy."),
            Item(2, "WickedWhims settings"));

        var text = TranslationBundleWriter.Write(bundle);

        Assert.Contains("GLOSSARIO OBRIGATORIO", text, StringComparison.Ordinal);
        Assert.Contains("\"Sim\" -> \"Sim\"", text, StringComparison.Ordinal);
        Assert.Contains("\"WickedWhims\" -> NAO TRADUZA", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule the provider path follows: a chat window has a context budget, and irrelevant terms
    /// spend it while burying the ones that matter.
    /// </summary>
    [Fact]
    public void Write_OnlyIncludesGlossaryTermsThatOccurInThisFile()
    {
        var bundle = WithGlossary([Term("Sim", "Sim"), Term("Vampire", "Vampiro")], Item(1, "The Sim is happy."));

        var text = TranslationBundleWriter.Write(bundle);

        Assert.Contains("\"Sim\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Vampire", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The API path has had a ceiling since DEC-048 and this one had none, which was backwards: a part
    /// holds up to a thousand strings against a 5.143-term glossary, so the block could outweigh the
    /// text being translated in the one place where the budget is a person's chat window.
    /// </summary>
    [Fact]
    public void RelevantGlossary_MoreMatchesThanTheCeiling_StopsAtTheCeiling()
    {
        var glossary = Enumerable.Range(0, TranslationBundle.MaximumGlossaryTerms + 50)
            .Select(index => new GlossaryTerm(index, $"Term{index:D4}", $"Termo{index:D4}", false, false, null, null))
            .ToList();
        var items = glossary.Select((term, index) => Item(index + 1, $"Use {term.Original} here")).ToArray();

        var bundle = new TranslationBundle("Projeto", English, PortugueseBrazil, items, Glossary: glossary);

        Assert.Equal(TranslationBundle.MaximumGlossaryTerms, bundle.RelevantGlossary.Count);
    }

    /// <summary>
    /// The seeder used to write one identical sentence as the note of all 5.143 default terms, and the
    /// writer printed a note under every glossary line — the same words repeated hundreds of times,
    /// telling the model nothing and spending the budget that matters here.
    /// </summary>
    [Fact]
    public void Write_NoteSharedByManyTerms_IsNotRepeatedUnderEveryLine()
    {
        const string Boilerplate = "Padrão medido: dobrou o acerto exato dos motores locais.";

        var bundle = new TranslationBundle("Projeto", English, PortugueseBrazil,
            [Item(1, "The Sim uses the Plug")],
            Glossary:
            [
                new GlossaryTerm(1, "Sim", "Sim", false, false, Boilerplate, null),
                new GlossaryTerm(2, "Plug", "Plugue", false, false, Boilerplate, null),
            ]);

        var text = TranslationBundleWriter.Write(bundle);

        Assert.DoesNotContain(Boilerplate, text, StringComparison.Ordinal);
    }

    /// <summary>A note that says something about its own term still earns its line.</summary>
    [Fact]
    public void Write_NoteUniqueToOneTerm_IsKept()
    {
        var bundle = new TranslationBundle("Projeto", English, PortugueseBrazil,
            [Item(1, "The Sim is happy.")],
            Glossary: [new GlossaryTerm(1, "Sim", "Sim", false, false, "Nome do jogo e do seu substantivo.", null)]);

        Assert.Contains("Nome do jogo e do seu substantivo.", TranslationBundleWriter.Write(bundle), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ProjectInstructions_ReachTheChatToo()
    {
        var bundle = new TranslationBundle("Projeto", English, PortugueseBrazil, [Item(1, "Shallow")],
            CustomInstructions: "Use tratamento informal.");

        Assert.Contains("Use tratamento informal.", TranslationBundleWriter.Write(bundle), StringComparison.Ordinal);
    }

    [Fact]
    public void Split_CarriesTheGlossaryIntoEveryPart()
    {
        var items = Enumerable.Range(1, 25).Select(id => Item(id, $"The Sim number {id} is happy.")).ToList();

        var parts = BundleSplitter.Split("Projeto", English, PortugueseBrazil, items, 10, [Term("Sim", "Sim")], "instrucoes");

        // Each part is pasted into its own chat window, so a part carrying neither is translated with neither.
        Assert.All(parts, part => Assert.Single(part.RelevantGlossary));
        Assert.All(parts, part => Assert.Equal("instrucoes", part.CustomInstructions));
    }

    [Fact]
    public void Read_TranslationThatIgnoredAGlossaryTerm_IsFlaggedForReview()
    {
        var bundle = WithGlossary([Term("Vampire", "Vampiro")], Item(1, "The Vampire is hungry."));

        var result = TranslationBundleReader.Read("#1\nEN: The Vampire is hungry.\nPT: O morcegão está com fome.", bundle);

        var only = Assert.Single(result.Translations);
        Assert.False(only.GlossaryRespected);
        Assert.False(only.IsTrustworthy);
        Assert.Equal(1, result.GlossaryMissCount);
        Assert.Contains(result.Problems, p => p.Kind == BundleProblemKind.GlossaryMissed && p.StringId == 1);
    }

    /// <summary>Presence, not position — a term lands wherever the target language puts it.</summary>
    [Fact]
    public void Read_GlossaryTermHonouredAnywhereInTheLine_IsAccepted()
    {
        var bundle = WithGlossary([Term("Vampire", "Vampiro")], Item(1, "The Vampire is hungry."));

        var result = TranslationBundleReader.Read("#1\nEN: The Vampire is hungry.\nPT: Está com fome, o Vampiro.", bundle);

        Assert.True(Assert.Single(result.Translations).GlossaryRespected);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Read_NeverTranslateTermThatWasTranslated_IsFlagged()
    {
        var bundle = WithGlossary(
            [Term("WickedWhims", "WickedWhims", neverTranslate: true)],
            Item(1, "Open WickedWhims settings."));

        var result = TranslationBundleReader.Read("#1\nEN: Open WickedWhims settings.\nPT: Abrir configurações de Caprichos Perversos.", bundle);

        Assert.False(Assert.Single(result.Translations).GlossaryRespected);
    }

    [Fact]
    public void Read_TermThatDoesNotOccurInTheSource_IsNotRequiredInTheTranslation()
    {
        var bundle = WithGlossary([Term("Vampire", "Vampiro")], Item(1, "The Sim is hungry."));

        Assert.True(Assert.Single(TranslationBundleReader.Read("#1\nEN: The Sim is hungry.\nPT: O Sim está com fome.", bundle).Translations).GlossaryRespected);
    }

    [Fact]
    public void Read_NoGlossary_LeavesEverythingRespected()
    {
        var result = TranslationBundleReader.Read("#1\nEN: Shallow\nPT: Raso", Bundle(Item(1, "Shallow")));

        Assert.True(Assert.Single(result.Translations).GlossaryRespected);
    }

    [Fact]
    public void Summary_CleanImport_DoesNotSoundLikeAProblem()
    {
        var result = TranslationBundleReader.Read("#1\nEN: Shallow\nPT: Raso", Bundle(Item(1, "Shallow")));

        Assert.Equal("1 tradução(ões) aplicada(s) · nenhum aviso", result.Summary);
    }
}
