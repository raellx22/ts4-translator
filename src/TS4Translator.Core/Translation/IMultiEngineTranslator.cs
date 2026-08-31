using System.Globalization;
using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Translation;

/// <summary>
/// Runs one translation job across up to three engines at the same time, routing each string to a lane
/// by how hard it looks (<see cref="StringDifficulty"/>).
/// </summary>
/// <remarks>
/// The point is throughput *and* cost: the offline engine chews through the thousands of short UI
/// labels while a paid model handles only the minority of lines that need judgement, and the lanes run
/// concurrently rather than one after another. All lanes report into the same callback, so the UI shows
/// a single moving bar rather than several competing ones.
/// </remarks>
public interface IMultiEngineTranslator
{
    /// <summary>
    /// What the split would look like, without translating anything — how many strings and characters
    /// each engine would receive, and roughly how many requests that is. Lets the UI show the cost of a
    /// run before the user commits to it.
    /// </summary>
    RoutingPreview Preview(IReadOnlyList<RoutableItem> items, RoutingPlan plan);

    /// <summary>
    /// Translates every item through the lane <paramref name="plan"/> selects for it.
    /// </summary>
    /// <param name="onChunkCompleted">
    /// Invoked as each lane finishes a chunk, with that chunk's outcomes and the name of the engine
    /// that produced them. Persistence stays the caller's job — that is what lets a cancelled run keep
    /// everything already done. Implementations must never invoke it from two lanes concurrently.
    /// </param>
    Task TranslateAsync(
        IReadOnlyList<RoutableItem> items,
        RoutingPlan plan,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        int chunkSize,
        Func<IReadOnlyList<TranslationOutcome>, string, CancellationToken, Task> onChunkCompleted,
        CancellationToken cancellationToken = default);
}

/// <param name="Item">The string to translate.</param>
/// <param name="Risk">What the app already knows about this string beyond its text.</param>
public sealed record RoutableItem(TranslationSourceItem Item, StringRiskContext Risk)
{
    public RoutableItem(TranslationSourceItem item) : this(item, new StringRiskContext())
    {
    }

    public int Score => StringDifficulty.Score(Item.Text, Risk);
}
