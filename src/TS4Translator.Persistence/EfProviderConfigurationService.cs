using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfProviderConfigurationService(IDbContextFactory<TranslatorDbContext> dbContextFactory, ISecretStore secretStore) : IProviderConfigurationService
{
    public async Task<ProviderConfiguration?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToConfiguration(record);
    }

    public async Task<ProviderConfiguration> SaveAsync(ProviderKind kind, string name, string endpoint, string? model, double temperature, string? apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderConfigurations.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            record = new ProviderConfigurationRecord { Kind = kind, Name = name, Endpoint = endpoint, Model = model };
            dbContext.ProviderConfigurations.Add(record);
        }

        record.Kind = kind;
        record.Name = name;
        record.Endpoint = endpoint;
        record.Model = model;
        record.Temperature = temperature;

        var secretName = ApiKeySecretName(kind);
        if (apiKey is not null)
        {
            if (apiKey.Length == 0)
            {
                secretStore.Delete(secretName);
            }
            else
            {
                secretStore.Save(secretName, apiKey);
            }
        }

        // Recomputed from the secret store every save, never just carried over from the record's
        // previous value - HasApiKey lives on the one shared configuration row, but the secret itself
        // is namespaced per Kind, so switching kinds (with apiKey left null, meaning "don't touch it
        // this call") must not keep reporting HasApiKey: true from a *different* kind's leftover flag.
        record.HasApiKey = secretStore.Load(secretName) is not null;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToConfiguration(record);
    }

    public string? GetApiKey()
    {
        // Synchronous by interface design (brief section 19 - callers just need "the current key",
        // not a round trip); reading the active row's Kind requires one, but this only reads a single
        // row from a local SQLite file, not a network call, so blocking here is acceptable.
        using var dbContext = dbContextFactory.CreateDbContext();
        var record = dbContext.ProviderConfigurations.AsNoTracking().FirstOrDefault();
        return record is null ? null : secretStore.Load(ApiKeySecretName(record.Kind));
    }

    // Namespaced by provider kind so switching kinds (and saving a new key for the new kind) never
    // reads or clobbers a key that belongs to a different kind - each kind keeps its own secret slot,
    // even though only one configuration row is active at a time.
    private static string ApiKeySecretName(ProviderKind kind) => $"provider:{kind}";

    private static ProviderConfiguration ToConfiguration(ProviderConfigurationRecord record) =>
        new(record.Id, record.Kind, record.Name, record.Endpoint, record.Model, record.Temperature, record.HasApiKey);
}
