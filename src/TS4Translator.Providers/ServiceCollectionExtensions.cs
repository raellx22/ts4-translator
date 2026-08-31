using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Argos;

namespace TS4Translator.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProviderServices(this IServiceCollection services, ArgosRuntimeOptions argosOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(argosOptions);

        services.AddSingleton<HttpClient>();
        services.AddSingleton(argosOptions);
        services.AddSingleton<IArgosRuntimeManager, ArgosRuntimeManager>();
        services.AddSingleton<ITranslationProviderFactory, ProfileProviderFactory>();
        services.AddSingleton<IProviderDirectory, HttpProviderDirectory>();

        return services;
    }
}
