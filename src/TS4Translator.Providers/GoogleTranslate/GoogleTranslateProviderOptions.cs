namespace TS4Translator.Providers.GoogleTranslate;

/// <param name="Endpoint">Base URL of the public translate endpoint. Overridable only so tests can point it at a fake server.</param>
/// <param name="MaxConcurrency">
/// How many strings to translate at a time. One, deliberately: measured against the live endpoint,
/// six parallel requests got four HTTP 429s, and even sequential requests start being refused once a
/// burst has been sent from the same IP. Parallelism here buys throttling, not speed.
/// </param>
/// <param name="DelayBetweenRequests">
/// Pause inserted between consecutive requests, for the same reason. Small enough to stay usable for
/// the refinement flow (a handful of strings), and the only thing that keeps a longer run from being
/// throttled off entirely.
/// </param>
public sealed record GoogleTranslateProviderOptions(
    string Endpoint = "https://translate.googleapis.com",
    int MaxConcurrency = 1,
    TimeSpan? DelayBetweenRequests = null)
{
    public TimeSpan EffectiveDelay => DelayBetweenRequests ?? TimeSpan.FromMilliseconds(250);
}
