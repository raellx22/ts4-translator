using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Projects;
using TS4Translator.Core.Providers;

namespace TS4Translator.Persistence;

public static class ServiceCollectionExtensions
{
    /// <param name="databasePath">Full path to the SQLite database file. Callers are responsible for
    /// ensuring the containing directory exists (see brief section 48: data lives under
    /// <c>%LOCALAPPDATA%\TS4Translator\Data</c>, never inside the install folder).</param>
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddDbContextFactory<TranslatorDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton<ITranslationMemory, SqliteTranslationMemory>();
        services.AddSingleton<IProjectService, EfProjectService>();
        services.AddSingleton<IProjectStringService, EfProjectStringService>();
        services.AddSingleton<IProviderConfigurationService, EfProviderConfigurationService>();

        return services;
    }
}
