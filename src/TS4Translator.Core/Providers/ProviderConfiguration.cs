namespace TS4Translator.Core.Providers;

/// <param name="Kind">Which concrete provider this configuration targets.</param>
/// <param name="Name">Display name for the UI.</param>
/// <param name="Endpoint">Base URL of the provider's API.</param>
/// <param name="Model">Model id to send in requests, when the provider has one (DeepL does not).</param>
/// <param name="Temperature">Sampling temperature.</param>
/// <param name="HasApiKey">Whether an API key is stored for this configuration — the key itself is never exposed here.</param>
public sealed record ProviderConfiguration(int Id, ProviderKind Kind, string Name, string Endpoint, string? Model, double Temperature, bool HasApiKey);
