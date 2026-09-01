using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Argos;
using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProviderServices(
        this IServiceCollection services,
        ArgosRuntimeOptions argosOptions,
        LocalModelOptions localModelOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(argosOptions);
        ArgumentNullException.ThrowIfNull(localModelOptions);

        services.AddSingleton<HttpClient>();
        services.AddSingleton(argosOptions);
        services.AddSingleton<IArgosRuntimeManager, ArgosRuntimeManager>();

        // Singleton: loading the weights costs over a second and holds GPU memory, and the engine
        // underneath serialises anyway.
        services.AddSingleton(localModelOptions);
        services.AddSingleton<ILocalModelRuntime, LocalModelRuntime>();
        services.AddSingleton<ITranslationProviderFactory, ProfileProviderFactory>();
        services.AddSingleton<IProviderDirectory, HttpProviderDirectory>();

        return services;
    }
}
