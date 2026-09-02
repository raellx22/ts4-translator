namespace TS4Translator.Core.Providers;

/// <summary>
/// One saved, named translation engine setup — "Gemini Flash (pago)", "LM Studio Qwen3.5", "Argos
/// offline". Several profiles of the same <see cref="ProviderKind"/> can coexist (two Gemini keys, a
/// local server and a remote one), which is what makes it possible to assign different engines to
/// different jobs without reconfiguring anything.
/// </summary>
/// <param name="Id">Persistence identity. Also namespaces this profile's API key in the secret store.</param>
/// <param name="Name">User-chosen label shown everywhere a profile is picked.</param>
/// <param name="Kind">Which engine implementation this profile drives.</param>
/// <param name="Endpoint">Base URL of the engine's API. Zero-configuration kinds supply their own.</param>
/// <param name="Model">Model id, when the engine has that concept (DeepL and Google Translate do not).</param>
/// <param name="Temperature">Sampling temperature for generative engines.</param>
/// <param name="HasApiKey">Whether a key is stored for this profile — the key itself never travels in this record.</param>
/// <param name="Reasoning">
/// Whether this engine should think before answering. Only <see cref="ProviderKind.LocalModel"/> acts on
/// it today; for every other kind it is stored and ignored.
/// </param>
public sealed record ProviderProfile(
    int Id,
    string Name,
    ProviderKind Kind,
    string Endpoint,
    string? Model,
    double Temperature,
    bool HasApiKey,
    ReasoningMode Reasoning = ReasoningMode.Off);
