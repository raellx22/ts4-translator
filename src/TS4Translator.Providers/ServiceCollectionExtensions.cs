using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProviderServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<HttpClient>();
        services.AddSingleton<ITranslationProviderFactory, ConfiguredProviderFactory>();

        return services;
    }
}
