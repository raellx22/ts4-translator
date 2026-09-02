using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// Owns the in-process llama.cpp engine: the loaded weights, the batched decode, and the answer to
/// "is this thing ready and on the GPU".
/// </summary>
/// <remarks>
/// A singleton because loading the weights costs over a second and holds GPU memory, and because the
/// executor underneath is single-threaded — one owner, one queue.
/// </remarks>
public interface ILocalModelRuntime : IAsyncDisposable
{
    /// <summary>What is installed and which backend would be used, without loading anything.</summary>
    LocalModelStatus GetStatus(string? modelPath = null);

    /// <summary>
    /// Translates a batch. Parallelism happens inside: the engine decodes several sequences per step,
    /// so callers must not invoke this concurrently.
    /// </summary>
    /// <param name="reasoning">
    /// Whether this model should think before answering. Ignored, harmlessly, by a model whose chat
    /// template knows nothing about thinking — the mode cannot add a capability the weights do not have.
    /// </param>
    Task<IReadOnlyList<TranslationResultItem>> TranslateAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        string? modelPath,
        ReasoningMode reasoning = ReasoningMode.Off,
        CancellationToken cancellationToken = default);
}
