using Microsoft.EntityFrameworkCore;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class EfProviderProfileService(IDbContextFactory<TranslatorDbContext> dbContextFactory, ISecretStore secretStore) : IProviderProfileService
{
    public async Task<IReadOnlyList<ProviderProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await dbContext.ProviderProfiles
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.OrderBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToProfile).ToList();
    }

    public async Task<ProviderProfile?> GetAsync(int profileId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == profileId, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ToProfile(record);
    }

    public async Task<ProviderProfile> SaveAsync(
        int? profileId,
        string name,
        ProviderKind kind,
        string endpoint,
        string? model,
        double temperature,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // The name is how a profile is picked in every role dropdown, so two profiles sharing one
        // makes the choice ambiguous and silently resolves to whichever came first.
        // Compared in memory rather than in SQL: SQLite's lower() only folds ASCII by default, so it
        // would catch "Gemini" vs "gemini" but miss accented pairs. The profile list is a handful of
        // rows, so loading the names costs nothing.
        var existingNames = await dbContext.ProviderProfiles
            .AsNoTracking()
            .Where(r => profileId == null || r.Id != profileId)
            .Select(r => r.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingNames.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Já existe um perfil chamado \"{name}\". Escolha outro nome.");
        }

        ProviderProfileRecord? record = null;
        if (profileId is int id)
        {
            record = await dbContext.ProviderProfiles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        }

        if (record is null)
        {
            record = new ProviderProfileRecord { Name = name, Kind = kind, Endpoint = endpoint, Model = model };
            dbContext.ProviderProfiles.Add(record);
        }

        record.Name = name;
        record.Kind = kind;
        record.Endpoint = endpoint;
        record.Model = model;
        record.Temperature = temperature;

        // Saved before touching the secret store: a brand-new profile has no id until this point, and
        // the id is what namespaces its key.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var secretName = SecretName(record.Id);
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

        // Always recomputed from the store, never carried over from the record's previous value - the
        // flag and the actual secret must not be able to disagree (DEC-019).
        record.HasApiKey = ResolveApiKey(record.Id, record.Kind) is not null;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToProfile(record);
    }

    public async Task DeleteAsync(int profileId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderProfiles.FirstOrDefaultAsync(r => r.Id == profileId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        dbContext.ProviderProfiles.Remove(record);

        // A role still pointing at a deleted profile would resolve to nothing at translation time and
        // fail with a confusing "no provider configured"; clear it here instead. Every pipeline row is
        // swept, not just the first - the global one and each project override can all reference it.
        var pipelines = await dbContext.TranslationPipelines.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pipeline in pipelines)
        {
            if (pipeline.PrimaryProfileId == profileId) { pipeline.PrimaryProfileId = null; }
            if (pipeline.RefinementProfileId == profileId) { pipeline.RefinementProfileId = null; }
            if (pipeline.ReviewProfileId == profileId) { pipeline.ReviewProfileId = null; }
            if (pipeline.MediumProfileId == profileId) { pipeline.MediumProfileId = null; }
            if (pipeline.DifficultProfileId == profileId) { pipeline.DifficultProfileId = null; }

            // Routing left switched on with no second engine would silently do nothing while claiming
            // to be active.
            if (pipeline.MediumProfileId is null && pipeline.DifficultProfileId is null)
            {
                pipeline.TieredRoutingEnabled = false;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        secretStore.Delete(SecretName(profileId));
    }

    public async Task<string?> GetApiKeyAsync(int profileId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ProviderProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == profileId, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ResolveApiKey(record.Id, record.Kind);
    }

    public async Task<TranslationPipelineSettings> GetPipelineSettingsAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // A project's own pipeline wins; otherwise it follows the global one. Falling back rather than
        // copying means changing the global setup still reaches every project that never overrode it.
        var record = projectId is Guid id
            ? await dbContext.TranslationPipelines.AsNoTracking().FirstOrDefaultAsync(r => r.ProjectId == id, cancellationToken).ConfigureAwait(false)
            : null;

        record ??= await dbContext.TranslationPipelines
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == null, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? new TranslationPipelineSettings() : ToSettings(record);
    }

    public async Task SavePipelineSettingsAsync(TranslationPipelineSettings settings, Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Normalized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await dbContext.TranslationPipelines
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            record = new TranslationPipelineRecord { ProjectId = projectId };
            dbContext.TranslationPipelines.Add(record);
        }

        record.PrimaryProfileId = normalized.PrimaryProfileId;
        record.RefinementProfileId = normalized.RefinementProfileId;
        record.ReviewProfileId = normalized.ReviewProfileId;
        record.TieredRoutingEnabled = normalized.TieredRoutingEnabled;
        record.MediumProfileId = normalized.MediumProfileId;
        record.MediumThreshold = normalized.MediumThreshold;
        record.DifficultProfileId = normalized.DifficultProfileId;
        record.DifficultThreshold = normalized.DifficultThreshold;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasPipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.TranslationPipelines
            .AsNoTracking()
            .AnyAsync(r => r.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemovePipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await dbContext.TranslationPipelines
            .Where(r => r.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static TranslationPipelineSettings ToSettings(TranslationPipelineRecord record) => new(
        record.PrimaryProfileId,
        record.RefinementProfileId,
        record.ReviewProfileId,
        record.TieredRoutingEnabled,
        record.MediumProfileId,
        record.MediumThreshold,
        record.DifficultProfileId,
        record.DifficultThreshold);

    /// <summary>
    /// This profile's key, falling back to the per-kind slot the app used before profiles existed.
    /// </summary>
    /// <remarks>
    /// The migration that introduced profiles turned each old configuration row into a profile, but it
    /// could not move the DPAPI blobs (they are encrypted per user and only readable at runtime). The
    /// fallback means an upgrading user keeps the Gemini key they already entered instead of silently
    /// finding an empty box; the key is re-namespaced under the profile id the first time they save it.
    /// </remarks>
    private string? ResolveApiKey(int profileId, ProviderKind kind) =>
        secretStore.Load(SecretName(profileId)) ?? secretStore.Load($"provider:{kind}");

    private static string SecretName(int profileId) => $"provider-profile:{profileId}";

    private static ProviderProfile ToProfile(ProviderProfileRecord record) =>
        new(record.Id, record.Name, record.Kind, record.Endpoint, record.Model, record.Temperature, record.HasApiKey);
}
