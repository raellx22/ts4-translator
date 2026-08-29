namespace TS4Translator.Providers.DeepL;

/// <param name="Endpoint">Base URL - "https://api-free.deepl.com" for a free-tier key, "https://api.deepl.com" for Pro.</param>
/// <param name="ApiKey">DeepL auth key, sent as an "Authorization: DeepL-Auth-Key {key}" header.</param>
public sealed record DeepLProviderOptions(string Endpoint, string? ApiKey);
