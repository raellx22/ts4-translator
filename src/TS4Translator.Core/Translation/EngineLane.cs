namespace TS4Translator.Core.Translation;

/// <summary>One engine and the difficulty band it handles.</summary>
/// <param name="Name">Display name of the profile driving this lane, for progress messages.</param>
/// <param name="Provider">The engine.</param>
/// <param name="MinimumScore">Inclusive lower bound of this lane's band. The lane with the highest bound that a score clears wins.</param>
public sealed record EngineLane(string Name, ITranslationProvider Provider, int MinimumScore);

/// <summary>
/// How a translation job is split across engines. One lane means a single engine does everything; two
/// or three mean the cheap engine takes the bulk while stronger ones handle progressively harder text.
/// </summary>
public sealed record RoutingPlan
{
    public RoutingPlan(IReadOnlyList<EngineLane> lanes)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        if (lanes.Count == 0)
        {
            throw new ArgumentException("A routing plan needs at least one lane.", nameof(lanes));
        }

        // Sorted once, descending, so resolving a score is a short scan from the hardest band down.
        Lanes = [.. lanes.OrderByDescending(lane => lane.MinimumScore)];
    }

    public IReadOnlyList<EngineLane> Lanes { get; }

    /// <summary>The lane that should handle <paramref name="score"/>.</summary>
    public EngineLane Resolve(int score) =>
        Lanes.FirstOrDefault(lane => score >= lane.MinimumScore) ?? Lanes[^1];
}

/// <param name="Name">The lane's engine name.</param>
/// <param name="ItemCount">How many strings land here.</param>
/// <param name="CharacterCount">Total source characters, the closest thing to a billing unit that is knowable without a pricing table.</param>
/// <param name="EstimatedRequests">Roughly how many calls this lane will make, from the engine's own recommended batch size.</param>
public sealed record RoutingPreviewLane(string Name, int ItemCount, long CharacterCount, int EstimatedRequests);

/// <param name="Lanes">One entry per engine that would be used, hardest band first.</param>
public sealed record RoutingPreview(IReadOnlyList<RoutingPreviewLane> Lanes)
{
    public int TotalItems => Lanes.Sum(lane => lane.ItemCount);
}
