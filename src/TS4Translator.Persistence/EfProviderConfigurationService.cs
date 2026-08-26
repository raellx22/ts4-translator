using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfProviderConfigurationService(IDbContextFactory<TranslatorDbContext> dbContextFactory, ISecretStore secretStore) : IProviderConfigurationService
{
    private const string ApiKeySecretName = "provider:openai-compatible";

    public async Task<ProviderConfiguration?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToConfiguration(record);
    }

    public async Task<ProviderConfiguration> SaveAsync(string name, string endpoint, string model, double temperature, string? apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderConfigurations.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            record = new ProviderConfigurationRecord { Name = name, Endpoint = endpoint, Model = model };
            dbContext.ProviderConfigurations.Add(record);
        }

        record.Name = name;
        record.Endpoint = endpoint;
        record.Model = model;
        record.Temperature = temperature;

        if (apiKey is not null)
        {
            if (apiKey.Length == 0)
            {
                secretStore.Delete(ApiKeySecretName);
                record.HasApiKey = false;
            }
            else
            {
                secretStore.Save(ApiKeySecretName, apiKey);
                record.HasApiKey = true;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToConfiguration(record);
    }

    public string? GetApiKey() => secretStore.Load(ApiKeySecretName);

    private static ProviderConfiguration ToConfiguration(ProviderConfigurationRecord record) =>
        new(record.Id, record.Name, record.Endpoint, record.Model, record.Temperature, record.HasApiKey);
}
