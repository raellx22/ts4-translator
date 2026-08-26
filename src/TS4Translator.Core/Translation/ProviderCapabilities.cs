namespace TS4Translator.Core.Translation;

/// <param name="SupportsBatch">Can translate multiple items in a single call.</param>
/// <param name="SupportsStreaming">Can stream partial results back as they complete.</param>
/// <param name="SupportsCustomPrompt">Accepts a caller-supplied instruction/style prompt.</param>
/// <param name="SupportsGlossary">Accepts glossary terms to enforce.</param>
/// <param name="SupportsLocalExecution">Runs entirely on the user's machine (no network call).</param>
/// <param name="RequiresApiKey">Needs a credential configured before it can be used.</param>
/// <param name="MaxContextSize">Approximate maximum input size the provider accepts, in characters, when known.</param>
public sealed record ProviderCapabilities(
    bool SupportsBatch,
    bool SupportsStreaming,
    bool SupportsCustomPrompt,
    bool SupportsGlossary,
    bool SupportsLocalExecution,
    bool RequiresApiKey,
    int? MaxContextSize);
