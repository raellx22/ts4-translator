using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.DeepL;
using TS4Translator.Providers.GoogleGemini;
using TS4Translator.Providers.OpenAICompatible;

namespace TS4Translator.Providers;

/// <summary>
/// Builds whichever concrete <see cref="ITranslationProvider"/> the user's saved
/// <see cref="ProviderConfiguration.Kind"/> selects (brief section 4/12/18) - the single place that
/// knows about every provider implementation this project has; nothing else needs to.
/// </summary>
public sealed class ConfiguredProviderFactory(IProviderConfigurationService configurationService, HttpClient httpClient) : ITranslationProviderFactory
{
    public async Task<ITranslationProvider?> CreateActiveProviderAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (configuration is null)
        {
            return null;
        }

        var apiKey = configuration.HasApiKey ? configurationService.GetApiKey() : null;

        return configuration.Kind switch
        {
            ProviderKind.OpenAICompatible => new OpenAICompatibleProvider(
                httpClient, new OpenAICompatibleProviderOptions(configuration.Endpoint, configuration.Model ?? string.Empty, apiKey, configuration.Temperature)),
            ProviderKind.DeepL => new DeepLProvider(
                httpClient, new DeepLProviderOptions(configuration.Endpoint, apiKey)),
            ProviderKind.GoogleGemini => new GoogleGeminiProvider(
                httpClient, new GoogleGeminiProviderOptions(configuration.Endpoint, configuration.Model ?? "gemini-2.5-flash", apiKey)),
            _ => throw new NotSupportedException($"Unknown provider kind: {configuration.Kind}."),
        };
    }
}
