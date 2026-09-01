namespace TS4Translator.Core.Providers;

/// <summary>Which concrete <see cref="Translation.ITranslationProvider"/> a <see cref="ProviderProfile"/> targets.</summary>
public enum ProviderKind
{
    /// <summary>Any endpoint speaking the OpenAI chat-completions contract — LM Studio, Bionic, NVIDIA NIM, a local llama.cpp server.</summary>
    OpenAICompatible,
    DeepL,
    /// <summary>Google's Gemini LLM API. Paid, needs an API key — not the same thing as <see cref="GoogleTranslate"/>.</summary>
    GoogleGemini,
    /// <summary>Offline neural translation through the app-managed Argos Translate runtime.</summary>
    Argos,
    /// <summary>Google Translate's free public endpoint. No API key, no endpoint, no model to configure.</summary>
    GoogleTranslate,
    /// <summary>OpenAI's own hosted API. Same wire contract as <see cref="OpenAICompatible"/>, kept separate so the UI can preset its endpoint and make the paid/local distinction obvious.</summary>
    OpenAI,
    /// <summary>Anthropic's Messages API.</summary>
    Anthropic,

    /// <summary>
    /// A GGUF model running inside this app's own process through llama.cpp — offline, no server to
    /// install or configure, and unlike <see cref="Argos"/> it honours the glossary and the project's
    /// instructions.
    /// </summary>
    LocalModel,
}
