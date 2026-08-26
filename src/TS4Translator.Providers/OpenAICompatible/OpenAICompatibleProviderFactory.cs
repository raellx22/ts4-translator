using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.OpenAICompatible;

public sealed class OpenAICompatibleProviderFactory(IProviderConfigurationService configurationService, HttpClient httpClient) : ITranslationProviderFactory
{
    public async Task<ITranslationProvider?> CreateActiveProviderAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (configuration is null)
        {
            return null;
        }

        var apiKey = configuration.HasApiKey ? configurationService.GetApiKey() : null;
        var options = new OpenAICompatibleProviderOptions(configuration.Endpoint, configuration.Model, apiKey, configuration.Temperature);
        return new OpenAICompatibleProvider(httpClient, options);
    }
}
