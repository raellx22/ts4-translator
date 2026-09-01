namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// Where the app keeps the pieces of its own local translation engine.
/// </summary>
/// <param name="RootDirectory">App-managed folder holding the llama.cpp binaries and the model files.</param>
/// <remarks>
/// The native binaries live in an app-managed folder rather than beside the executable, for the same
/// reason the Argos runtime does: the GPU build is roughly 1.3 GB and the model another 1.8 GB, which
/// has no business inside an MSIX. Putting them here means one mechanism — check, fetch, report — for
/// both, and it keeps the installed app small for the people who never turn the local engine on.
/// </remarks>
public sealed record LocalModelOptions(string RootDirectory)
{
    /// <summary>Native llama.cpp built against CUDA 12. Needs the CUDA runtime beside it.</summary>
    public string CudaDirectory => Path.Combine(RootDirectory, "llama", "cuda12");

    /// <summary>Native llama.cpp for CPU. Always usable, considerably slower.</summary>
    public string CpuDirectory => Path.Combine(RootDirectory, "llama", "cpu");

    public string ModelsDirectory => Path.Combine(RootDirectory, "models");

    /// <summary>
    /// Every file that has to be present for a backend folder to be usable. The CUDA one is listed in
    /// full because a partial copy is the failure that cost the most time to diagnose: LLamaSharp finds
    /// llama.dll, fails to resolve ggml-cuda.dll's imports, and silently runs on the CPU.
    /// </summary>
    public static IReadOnlyList<string> CudaFiles { get; } =
    [
        "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu.dll", "ggml-cuda.dll",
        "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll",
    ];

    public static IReadOnlyList<string> CpuFiles { get; } =
    [
        "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu.dll",
    ];
}
