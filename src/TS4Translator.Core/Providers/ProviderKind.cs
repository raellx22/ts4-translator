namespace TS4Translator.Core.Providers;

/// <summary>Which concrete <see cref="Translation.ITranslationProvider"/> a <see cref="ProviderConfiguration"/> targets.</summary>
public enum ProviderKind
{
    /// <summary>Any endpoint speaking the OpenAI chat-completions contract (LM Studio, NVIDIA NIM, ...).</summary>
    OpenAICompatible,
    DeepL,
    GoogleGemini,
}
