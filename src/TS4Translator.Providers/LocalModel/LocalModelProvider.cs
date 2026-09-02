using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// The app's own offline translation engine: a GGUF model running inside this process.
/// </summary>
/// <remarks>
/// The difference from <see cref="Argos.ArgosTranslateProvider"/> is not speed, it is capability.
/// Argos declares <c>SupportsCustomPrompt: false</c> and <c>SupportsGlossary: false</c> because it has
/// no prompt input at all — it cannot be told about terminology, tone, or the fact that it is
/// translating game UI, and no amount of configuration changes that. This one is instruction-following,
/// so everything the app knows about a string reaches the engine that translates it.
/// </remarks>
public sealed class LocalModelProvider(
    ILocalModelRuntime runtime,
    string? modelPath,
    ReasoningMode reasoning = ReasoningMode.Off) : ITranslationProvider
{
    public string Name => "local-gguf";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true,
        SupportsStreaming: false,
        SupportsCustomPrompt: true,
        SupportsGlossary: true,
        SupportsLocalExecution: true,
        RequiresApiKey: false,
        MaxContextSize: 8192,

        // One, deliberately. The provider is internally parallel - it decodes eight sequences per step -
        // and the executor underneath is single-threaded, so a second concurrent call would serialise
        // behind a lock at best.
        RecommendedConcurrency: 1,

        // Bigger than the engine's internal batch on purpose: a call carries enough work to keep all
        // eight sequences fed, while staying small enough that a cancelled run loses little.
        RecommendedBatchItems: 64,
        RecommendedBatchCharacters: 8_000);

    public Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
        => runtime.TranslateAsync(items, sourceLocale, targetLocale, glossaryTerms, customInstructions, modelPath, reasoning, cancellationToken);
}
