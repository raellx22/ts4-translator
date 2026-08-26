namespace TS4Translator.Providers.OpenAICompatible;

/// <param name="Endpoint">Base URL, e.g. <c>http://localhost:1234/v1</c> for a local LM Studio server.</param>
/// <param name="Model">Model id to send in the request body.</param>
/// <param name="ApiKey">Optional bearer token. Many local servers (LM Studio) don't require one.</param>
/// <param name="Temperature">Sampling temperature. Low/zero is recommended for deterministic translation.</param>
public sealed record OpenAICompatibleProviderOptions(
    string Endpoint,
    string Model,
    string? ApiKey = null,
    double Temperature = 0.0);
