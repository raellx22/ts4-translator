namespace TS4Translator.Providers.Anthropic;

/// <param name="Endpoint">Base URL — "https://api.anthropic.com" for the public API.</param>
/// <param name="Model">Model id, e.g. "claude-sonnet-5".</param>
/// <param name="ApiKey">Anthropic API key, sent as an "x-api-key" header.</param>
/// <param name="Temperature">Sampling temperature. Low is recommended for deterministic translation.</param>
public sealed record AnthropicProviderOptions(string Endpoint, string Model, string? ApiKey, double Temperature = 0.0);
