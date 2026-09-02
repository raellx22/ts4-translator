using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Packages;

namespace TS4Translator.Sims4;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSims4Services(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPackageService, Sims4PackageService>();
        services.AddSingleton<IStringTableService, SmartSimStringTableService>();
        services.AddSingleton<IAlignedTranslationHarvester, AlignedTranslationHarvester>();

        return services;
    }
}
