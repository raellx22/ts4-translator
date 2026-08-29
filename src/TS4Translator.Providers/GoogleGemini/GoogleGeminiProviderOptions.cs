namespace TS4Translator.Providers.GoogleGemini;

/// <param name="Endpoint">Base URL - "https://generativelanguage.googleapis.com" for the public Gemini API.</param>
/// <param name="Model">
/// Model id, e.g. "gemini-2.5-flash". Google retires/renames Gemini model ids fairly often (e.g.
/// "gemini-2.0-flash" was retired 2026-06-01) - a hardcoded default here or in
/// <see cref="ConfiguredProviderFactory"/> can go stale. If "Testar conexão" in Settings returns 404,
/// the configured model id is the first thing to check against Google's current model list
/// (<c>GET /v1beta/models</c>, or the Google AI Studio UI), not a bug in this provider's request shape.
/// </param>
/// <param name="ApiKey">Google AI Studio API key, sent as an "x-goog-api-key" header.</param>
public sealed record GoogleGeminiProviderOptions(string Endpoint, string Model, string? ApiKey);
