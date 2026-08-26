using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Tokens;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTranslationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITokenProtector, TokenProtector>();
        services.AddSingleton<ITranslationValidator, TranslationValidator>();

        return services;
    }
}
