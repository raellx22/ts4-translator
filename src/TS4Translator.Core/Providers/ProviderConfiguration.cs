namespace TS4Translator.Core.Providers;

/// <param name="Name">Display name for the UI.</param>
/// <param name="Endpoint">Base URL of the OpenAI-compatible endpoint (e.g. LM Studio or NVIDIA NIM).</param>
/// <param name="Model">Model id to send in requests.</param>
/// <param name="Temperature">Sampling temperature.</param>
/// <param name="HasApiKey">Whether an API key is stored for this configuration — the key itself is never exposed here.</param>
public sealed record ProviderConfiguration(int Id, string Name, string Endpoint, string Model, double Temperature, bool HasApiKey);
