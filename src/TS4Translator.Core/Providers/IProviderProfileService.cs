namespace TS4Translator.Core.Providers;

/// <summary>
/// Stores the user's named engine profiles and the pipeline settings that say which profile does which
/// job. API keys never live here in plaintext; they go through <see cref="Security.ISecretStore"/>,
/// namespaced per profile so two profiles of the same kind can hold different keys.
/// </summary>
public interface IProviderProfileService
{
    Task<IReadOnlyList<ProviderProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProviderProfile?> GetAsync(int profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a profile, or updates the one whose id is <paramref name="profileId"/>.
    /// </summary>
    /// <param name="apiKey">Null leaves a previously stored key untouched; an empty string clears it.</param>
    Task<ProviderProfile> SaveAsync(
        int? profileId,
        string name,
        ProviderKind kind,
        string endpoint,
        string? model,
        double temperature,
        string? apiKey,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a profile and its stored key, and clears it from any pipeline role it held.</summary>
    Task DeleteAsync(int profileId, CancellationToken cancellationToken = default);

    Task<string?> GetApiKeyAsync(int profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The pipeline for <paramref name="projectId"/>, or the global one when that project has no override
    /// (or when <paramref name="projectId"/> is null).
    /// </summary>
    Task<TranslationPipelineSettings> GetPipelineSettingsAsync(Guid? projectId = null, CancellationToken cancellationToken = default);

    /// <summary>Saves the global pipeline, or a per-project override when <paramref name="projectId"/> is given.</summary>
    Task SavePipelineSettingsAsync(TranslationPipelineSettings settings, Guid? projectId = null, CancellationToken cancellationToken = default);

    /// <summary>Whether this project has its own pipeline rather than inheriting the global one.</summary>
    Task<bool> HasPipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Drops a project's override so it goes back to following the global pipeline.</summary>
    Task RemovePipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default);
}
