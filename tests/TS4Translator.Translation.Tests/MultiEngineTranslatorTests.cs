using System.Globalization;
using TS4Translator.Core.Translation;

namespace TS4Translator.Translation.Tests;

public sealed class MultiEngineTranslatorTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private const string ShortLabel = "Shallow";
    /// <summary>One plain sentence, long enough to leave the easy band but with no placeholders or markup to reach the hard one.</summary>
    private const string MediumText = "The water in this particular swimming pool is surprisingly shallow and rather cold for the season, so be careful today.";
    private const string HardText = "Sharing a shower, and all the shower thoughts that come with it, gave {0.SimFirstName} some great ideas! They're ready to get creative, whether it's back in bed or a different outlet.";

    private static MultiEngineTranslator CreateTranslator() => new(
        new TranslationOrchestrator(new TokenProtector(), new TranslationValidator(), new BatchBuilder(), new FakeTranslationMemory()));

    private static List<RoutableItem> Items(params string[] texts) =>
        [.. texts.Select((text, index) => new RoutableItem(new TranslationSourceItem((index + 1).ToString(CultureInfo.InvariantCulture), text)))];

    private static Func<IReadOnlyList<TranslationOutcome>, string, CancellationToken, Task> Collect(List<(TranslationOutcome Outcome, string Lane)> into) =>
        (outcomes, laneName, _) =>
        {
            lock (into)
            {
                into.AddRange(outcomes.Select(outcome => (outcome, laneName)));
            }

            return Task.CompletedTask;
        };

    private static RoutingPlan ThreeLanes(ITranslationProvider easy, ITranslationProvider medium, ITranslationProvider hard) =>
        new([new EngineLane("fácil", easy, 0), new EngineLane("média", medium, 25), new EngineLane("difícil", hard, 35)]);

    [Fact]
    public void Resolve_PicksTheHighestBandTheScoreClears()
    {
        var plan = new RoutingPlan([new EngineLane("a", new FakeTranslationProvider(), 0), new EngineLane("b", new FakeTranslationProvider(), 25), new EngineLane("c", new FakeTranslationProvider(), 35)]);

        Assert.Equal("a", plan.Resolve(0).Name);
        Assert.Equal("a", plan.Resolve(24).Name);
        Assert.Equal("b", plan.Resolve(25).Name);
        Assert.Equal("b", plan.Resolve(34).Name);
        Assert.Equal("c", plan.Resolve(35).Name);
        Assert.Equal("c", plan.Resolve(100).Name);
    }

    [Fact]
    public void RoutingPlan_RequiresAtLeastOneLane()
        => Assert.Throws<ArgumentException>(() => new RoutingPlan([]));

    /// <summary>Three engines at once is what the user asked for beyond the original two.</summary>
    [Fact]
    public async Task TranslateAsync_ThreeLanes_EachStringReachesItsOwnEngine()
    {
        var easy = new FakeTranslationProvider(text => $"[f] {text}");
        var medium = new FakeTranslationProvider(text => $"[m] {text}");
        var hard = new FakeTranslationProvider(text => $"[d] {text}");
        var collected = new List<(TranslationOutcome Outcome, string Lane)>();

        await CreateTranslator().TranslateAsync(
            Items(ShortLabel, MediumText, HardText),
            ThreeLanes(easy, medium, hard),
            English, PortugueseBrazil, null, null, chunkSize: 10, Collect(collected));

        var laneById = collected.ToDictionary(entry => entry.Outcome.Id, entry => entry.Lane);
        Assert.Equal("fácil", laneById["1"]);
        Assert.Equal("média", laneById["2"]);
        Assert.Equal("difícil", laneById["3"]);
    }

    /// <summary>
    /// The whole point of the risk signals: a string whose text looks trivial but which already failed
    /// review gets promoted to a stronger engine.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_PreviouslyFlaggedShortLabel_IsPromotedOutOfTheCheapLane()
    {
        var easy = new FakeTranslationProvider(text => $"[f] {text}");
        var hard = new FakeTranslationProvider(text => $"[d] {text}");
        var collected = new List<(TranslationOutcome Outcome, string Lane)>();

        List<RoutableItem> items =
        [
            new(new TranslationSourceItem("1", ShortLabel)),
            new(new TranslationSourceItem("2", ShortLabel), new StringRiskContext(PreviouslyFlaggedForReview: true)),
        ];

        await CreateTranslator().TranslateAsync(
            items,
            new RoutingPlan([new EngineLane("fácil", easy, 0), new EngineLane("difícil", hard, 35)]),
            English, PortugueseBrazil, null, null, chunkSize: 10, Collect(collected));

        var laneById = collected.ToDictionary(entry => entry.Outcome.Id, entry => entry.Lane);
        Assert.Equal("fácil", laneById["1"]);
        Assert.Equal("difícil", laneById["2"]);
    }

    [Fact]
    public void Preview_ReportsItemsCharactersAndRequestsPerLane()
    {
        var easy = new FakeTranslationProvider(recommendedBatchItems: 2);
        var hard = new FakeTranslationProvider(recommendedBatchItems: 80);

        var preview = CreateTranslator().Preview(
            Items(ShortLabel, ShortLabel, ShortLabel, HardText),
            new RoutingPlan([new EngineLane("fácil", easy, 0), new EngineLane("difícil", hard, 35)]));

        var easyLane = preview.Lanes.Single(lane => lane.Name == "fácil");
        Assert.Equal(3, easyLane.ItemCount);
        Assert.Equal(ShortLabel.Length * 3, easyLane.CharacterCount);

        // Three items at two per request is two requests, not three or one.
        Assert.Equal(2, easyLane.EstimatedRequests);
        Assert.Equal(4, preview.TotalItems);
    }

    /// <summary>
    /// The preview is the number the user presses the button on, so it must count what will actually be
    /// sent. A tuning identifier and a string the glossary answers whole never reach an engine.
    /// </summary>
    [Fact]
    public void Preview_StringsAnsweredWithoutAnEngine_AreNotQuoted()
    {
        var provider = new FakeTranslationProvider(recommendedBatchItems: 100);
        var plan = new RoutingPlan([new EngineLane("único", provider, 0)]);
        var glossary = new[] { new Core.Glossary.GlossaryTerm(1, "Body Selector", "Seletor de Corpo", false, false, null, null) };

        var items = Items("Open the settings", "debug_savedActor2", "Body Selector");

        var withoutGlossary = CreateTranslator().Preview(items, plan);
        var withGlossary = CreateTranslator().Preview(items, plan, glossary);

        // The identifier is dropped either way; the glossary label only once the glossary is known.
        Assert.Equal(2, withoutGlossary.TotalItems);
        Assert.Equal(1, withGlossary.TotalItems);
    }

    [Fact]
    public void Preview_LaneThatWouldReceiveNothing_IsOmitted()
    {
        var preview = CreateTranslator().Preview(
            Items(ShortLabel),
            new RoutingPlan([new EngineLane("fácil", new FakeTranslationProvider(), 0), new EngineLane("difícil", new FakeTranslationProvider(), 35)]));

        Assert.Equal("fácil", Assert.Single(preview.Lanes).Name);
    }

    [Fact]
    public async Task TranslateAsync_SingleLane_SendsEverythingThere()
    {
        var only = new FakeTranslationProvider(text => $"[u] {text}");
        var collected = new List<(TranslationOutcome Outcome, string Lane)>();

        await CreateTranslator().TranslateAsync(
            Items(ShortLabel, HardText),
            new RoutingPlan([new EngineLane("único", only, 0)]),
            English, PortugueseBrazil, null, null, chunkSize: 10, Collect(collected));

        Assert.Equal(2, collected.Count);
        Assert.All(collected, entry => Assert.Equal("único", entry.Lane));
    }

    [Fact]
    public async Task TranslateAsync_PersistenceCallbackIsNeverEnteredConcurrently()
    {
        var concurrent = 0;
        var maxConcurrent = 0;

        var texts = Enumerable.Range(0, 60)
            .Select(index => (index % 3) switch { 0 => ShortLabel, 1 => MediumText, _ => HardText })
            .ToArray();

        await CreateTranslator().TranslateAsync(
            Items(texts),
            ThreeLanes(new FakeTranslationProvider(), new FakeTranslationProvider(), new FakeTranslationProvider()),
            English, PortugueseBrazil, null, null, chunkSize: 2,
            async (_, _, _) =>
            {
                var now = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                await Task.Delay(5, CancellationToken.None);
                Interlocked.Decrement(ref concurrent);
            });

        Assert.Equal(1, maxConcurrent);
    }

    [Fact]
    public async Task TranslateAsync_NoItems_DoesNothing()
    {
        var provider = new FakeTranslationProvider();
        var called = false;

        await CreateTranslator().TranslateAsync(
            [], new RoutingPlan([new EngineLane("único", provider, 0)]), English, PortugueseBrazil, null, null, 10,
            (_, _, _) => { called = true; return Task.CompletedTask; });

        Assert.False(called);
        Assert.Empty(provider.ObservedBatchSizes);
    }

    [Fact]
    public async Task TranslateAsync_Cancelled_SurfacesTheCancellation()
    {
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await CreateTranslator().TranslateAsync(
                Items(Enumerable.Repeat(ShortLabel, 20).ToArray()),
                new RoutingPlan([new EngineLane("único", new FakeTranslationProvider(), 0)]),
                English, PortugueseBrazil, null, null, chunkSize: 1,
                async (_, _, ct) =>
                {
                    await cts.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                },
                cancellationToken: cts.Token);
        });
    }
}
