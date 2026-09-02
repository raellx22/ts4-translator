namespace TS4Translator.Providers.LocalModel;

/// <summary>Which llama.cpp build the app found and will actually use.</summary>
public enum LocalModelBackend
{
    /// <summary>No usable build found — the engine cannot run at all.</summary>
    None,

    /// <summary>CPU only. Works everywhere; on the measured corpus it is several times slower.</summary>
    Cpu,

    /// <summary>CUDA 12. What the benchmark numbers were measured on.</summary>
    Cuda,
}

/// <param name="Backend">The build that will be used, already decided rather than guessed at load time.</param>
/// <param name="ModelPath">The GGUF that will be loaded, or null when none is configured.</param>
/// <param name="ModelSizeBytes">Size of that file, for a status line that means something.</param>
/// <param name="Message">Ready-to-show explanation, including what is missing when it is not ready.</param>
/// <param name="Model">What the GGUF's own header says about itself, or null when it could not be read.</param>
public sealed record LocalModelStatus(
    LocalModelBackend Backend,
    string? ModelPath,
    long ModelSizeBytes,
    string Message,
    GgufModelInfo? Model = null)
{
    public bool IsReady => Backend != LocalModelBackend.None && ModelPath is not null;

    /// <summary>
    /// Whether this particular file is a reasoning model, so the UI can stop the user setting a switch
    /// that would do nothing.
    /// </summary>
    public bool SupportsReasoning => Model?.SupportsReasoning == true;

    /// <summary>
    /// True when the engine runs but without the GPU. Worth saying out loud: a silent fall back to CPU
    /// is exactly how a "local model is too slow" conclusion gets reached for the wrong reason.
    /// </summary>
    public bool IsCpuOnly => Backend == LocalModelBackend.Cpu;
}
