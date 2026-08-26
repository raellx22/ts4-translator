using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Security;

namespace TS4Translator.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers Windows-only infrastructure (DPAPI secret storage). This app is Windows-only (WinUI 3 desktop).</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISecretStore, DpapiSecretStore>();

        return services;
    }
}
