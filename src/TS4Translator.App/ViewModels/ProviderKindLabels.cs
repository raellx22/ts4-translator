using TS4Translator.Core.Providers;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// The single mapping between <see cref="ProviderKind"/> and the text the user sees, plus what each
/// engine actually needs configured. Shared by every screen that names an engine so the app can never
/// call the same thing by two different names.
/// </summary>
public static class ProviderKindLabels
{
    public const string OpenAICompatible = "LLM local / OpenAI-compatible (LM Studio, Bionic, NVIDIA NIM)";
    public const string OpenAI = "OpenAI (API paga)";
    public const string Anthropic = "Anthropic Claude (API paga)";
    public const string GoogleGemini = "Google Gemini (API paga)";
    public const string DeepL = "DeepL";
    public const string GoogleTranslate = "Google Tradutor (grátis, sem configuração)";
    public const string Argos = "Argos Translate (offline, sem configuração)";

    public static IReadOnlyList<string> All { get; } =
    [
        Argos,
        GoogleTranslate,
        OpenAICompatible,
        OpenAI,
        Anthropic,
        GoogleGemini,
        DeepL,
    ];

    /// <summary>Engines that work with no endpoint, model, or API key at all.</summary>
    public static bool NeedsNoConfiguration(ProviderKind kind) =>
        kind is ProviderKind.Argos or ProviderKind.GoogleTranslate;

    /// <summary>Engines with no model id concept.</summary>
    public static bool HasModel(ProviderKind kind) =>
        kind is not (ProviderKind.DeepL or ProviderKind.Argos or ProviderKind.GoogleTranslate);

    public static bool NeedsApiKey(ProviderKind kind) =>
        kind is ProviderKind.OpenAI or ProviderKind.Anthropic or ProviderKind.GoogleGemini or ProviderKind.DeepL;

    /// <summary>Short line explaining what this engine is for, shown under the picker while editing a profile.</summary>
    public static string Describe(ProviderKind kind) => kind switch
    {
        ProviderKind.Argos => "Roda 100% na sua máquina, sem internet e sem custo. Rápido e literal - ótimo para tradução em massa, fraco em frases longas e com gênero.",
        ProviderKind.GoogleTranslate => "Endpoint público e gratuito, sem nenhuma configuração. Melhor que o Argos em naturalidade, mas o Google limita requisições por IP - use para refino, não para milhares de strings.",
        ProviderKind.OpenAICompatible => "Qualquer servidor local ou remoto que fale o protocolo do OpenAI. É aqui que entra a sua LLM local (LM Studio, Bionic) com o modelo que você escolher.",
        ProviderKind.OpenAI => "API oficial da OpenAI. Paga, por uso.",
        ProviderKind.Anthropic => "API oficial da Anthropic (Claude). Paga, por uso.",
        ProviderKind.GoogleGemini => "API do Gemini. Tem camada gratuita com limite de requisições por minuto.",
        ProviderKind.DeepL => "Tradutor especializado da DeepL. Precisa de auth key (free ou Pro).",
        _ => string.Empty,
    };

    /// <summary>Endpoint and model this engine should start with when the user picks it for a new profile.</summary>
    public static (string Endpoint, string Model) Defaults(ProviderKind kind) => kind switch
    {
        ProviderKind.Argos => ("local://argos", "translate-en_pb-1_9"),
        ProviderKind.GoogleTranslate => ("https://translate.googleapis.com", string.Empty),
        ProviderKind.OpenAI => ("https://api.openai.com/v1", "gpt-4.1-mini"),
        ProviderKind.Anthropic => ("https://api.anthropic.com", "claude-sonnet-5"),
        ProviderKind.GoogleGemini => ("https://generativelanguage.googleapis.com", "gemini-2.5-flash"),
        ProviderKind.DeepL => ("https://api-free.deepl.com", string.Empty),
        _ => ("http://localhost:1234/v1", string.Empty),
    };

    public static string ToLabel(ProviderKind kind) => kind switch
    {
        ProviderKind.DeepL => DeepL,
        ProviderKind.GoogleGemini => GoogleGemini,
        ProviderKind.GoogleTranslate => GoogleTranslate,
        ProviderKind.Argos => Argos,
        ProviderKind.OpenAI => OpenAI,
        ProviderKind.Anthropic => Anthropic,
        _ => OpenAICompatible,
    };

    public static ProviderKind ToKind(string label) => label switch
    {
        DeepL => ProviderKind.DeepL,
        GoogleGemini => ProviderKind.GoogleGemini,
        GoogleTranslate => ProviderKind.GoogleTranslate,
        Argos => ProviderKind.Argos,
        OpenAI => ProviderKind.OpenAI,
        Anthropic => ProviderKind.Anthropic,
        _ => ProviderKind.OpenAICompatible,
    };
}
