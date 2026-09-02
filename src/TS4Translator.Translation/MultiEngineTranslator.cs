using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Translation;

/// <summary>
/// Default <see cref="IMultiEngineTranslator"/>: buckets items by <see cref="StringDifficulty"/> and
/// runs every non-empty lane concurrently through <see cref="ITranslationOrchestrator"/>.
/// </summary>
public sealed class MultiEngineTranslator(ITranslationOrchestrator orchestrator) : IMultiEngineTranslator
{
    public RoutingPreview Preview(IReadOnlyList<RoutableItem> items, RoutingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(plan);

        var lanes = new List<RoutingPreviewLane>(plan.Lanes.Count);

        foreach (var (lane, laneItems) in Bucket(items, plan))
        {
            if (laneItems.Count == 0)
            {
                continue;
            }

            var characters = laneItems.Sum(item => (long)item.Item.Text.Length);

            // The engine's own recommended batch size is the honest basis for a request count; falling
            // back to one request per item would wildly overstate it for batching engines.
            var perRequest = Math.Max(1, lane.Provider.Capabilities.RecommendedBatchItems ?? 100);
            var requests = (int)Math.Ceiling(laneItems.Count / (double)perRequest);

            lanes.Add(new RoutingPreviewLane(lane.Name, laneItems.Count, characters, requests));
        }

        return new RoutingPreview(lanes);
    }

    public async Task TranslateAsync(
        IReadOnlyList<RoutableItem> items,
        RoutingPlan plan,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        int chunkSize,
        Func<IReadOnlyList<TranslationOutcome>, string, CancellationToken, Task> onChunkCompleted,
        bool reuseTranslationMemory = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(onChunkCompleted);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        if (items.Count == 0)
        {
            return;
        }

        // Serializes the persistence callback across lanes. Without it, two engines finishing at the
        // same moment would both write to the caller's DbContext and its observable collections
        // concurrently - a data race the caller has no way to defend against on its own.
        using var persistenceLock = new SemaphoreSlim(1, 1);

        async Task RunLaneAsync(EngineLane lane, IReadOnlyList<RoutableItem> laneItems)
        {
            foreach (var chunk in laneItems.Chunk(chunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var outcomes = await orchestrator.TranslateAsync(
                    [.. chunk.Select(routable => routable.Item)],
                    lane.Provider,
                    sourceLocale,
                    targetLocale,
                    glossaryTerms,
                    customInstructions,
                    reuseTranslationMemory,
                    cancellationToken).ConfigureAwait(false);

                await persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await onChunkCompleted(outcomes, lane.Name, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    persistenceLock.Release();
                }
            }
        }

        var running = Bucket(items, plan)
            .Where(bucket => bucket.Items.Count > 0)
            .Select(bucket => RunLaneAsync(bucket.Lane, bucket.Items))
            .ToList();

        // WhenAll rather than awaiting in sequence: running the offline engine and the paid one at the
        // same time is the entire reason this class exists.
        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private static List<(EngineLane Lane, List<RoutableItem> Items)> Bucket(IReadOnlyList<RoutableItem> items, RoutingPlan plan)
    {
        var buckets = plan.Lanes.ToDictionary(lane => lane, _ => new List<RoutableItem>());

        foreach (var item in items)
        {
            buckets[plan.Resolve(item.Score)].Add(item);
        }

        return [.. plan.Lanes.Select(lane => (lane, buckets[lane]))];
    }
}
