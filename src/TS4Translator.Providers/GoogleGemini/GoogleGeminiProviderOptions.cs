namespace TS4Translator.Providers.GoogleGemini;

/// <param name="Endpoint">Base URL - "https://generativelanguage.googleapis.com" for the public Gemini API.</param>
/// <param name="Model">Model id, e.g. "gemini-2.0-flash".</param>
/// <param name="ApiKey">Google AI Studio API key, sent as an "x-goog-api-key" header.</param>
public sealed record GoogleGeminiProviderOptions(string Endpoint, string Model, string? ApiKey);
