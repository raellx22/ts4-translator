using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.Argos;

public sealed class ArgosTranslateProvider(IArgosRuntimeManager runtimeManager) : ITranslationProvider
{
    public string Name => "argos-en-pb-1.9";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: true,
        SupportsStreaming: false,
        SupportsCustomPrompt: false,
        SupportsGlossary: false,
        SupportsLocalExecution: true,
        RequiresApiKey: false,
        MaxContextSize: null,
        RecommendedConcurrency: 1,
        RecommendedBatchItems: 256,
        RecommendedBatchCharacters: 64_000);

    public Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        var sourceCode = sourceLocale.TwoLetterISOLanguageName;
        var targetCode = targetLocale.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? "pb"
            : targetLocale.TwoLetterISOLanguageName;

        if (sourceCode != "en" || targetCode != "pb")
        {
            throw new NotSupportedException("O modelo Argos instalado nesta versão suporta somente inglês para português do Brasil.");
        }

        return runtimeManager.TranslateAsync(items, sourceCode, targetCode, cancellationToken);
    }
}
