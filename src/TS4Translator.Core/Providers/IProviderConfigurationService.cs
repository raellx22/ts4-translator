namespace TS4Translator.Core.Providers;

/// <summary>
/// Stores the user's chosen translation provider configuration. Only one active configuration is
/// supported today, of whichever <see cref="ProviderKind"/> the user picked (multiple simultaneous
/// named profiles are future work, brief section 12/37) — <see cref="SaveAsync"/> upserts it. The API
/// key, when present, is never stored here in plaintext; it goes through <see cref="Security.ISecretStore"/>.
/// </summary>
public interface IProviderConfigurationService
{
    Task<ProviderConfiguration?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <param name="model">Ignored for provider kinds that have no model concept (e.g. DeepL).</param>
    /// <param name="apiKey">Pass null to leave a previously stored key untouched; pass an empty string to clear it.</param>
    Task<ProviderConfiguration> SaveAsync(ProviderKind kind, string name, string endpoint, string? model, double temperature, string? apiKey, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the stored API key for the active configuration, or null if none is stored.</summary>
    string? GetApiKey();
}
