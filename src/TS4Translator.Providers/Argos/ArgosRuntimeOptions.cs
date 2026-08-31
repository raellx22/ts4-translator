namespace TS4Translator.Providers.Argos;

public sealed record ArgosRuntimeOptions(string RootDirectory)
{
    public string PythonExecutable => Path.Combine(RootDirectory, "python", "python.exe");
    public string SidecarPath => Path.Combine(RootDirectory, "sidecar.py");
    public string PackagesDirectory => Path.Combine(RootDirectory, "data", "packages");
    public string DownloadsDirectory => Path.Combine(RootDirectory, "downloads");
    public string ReadyMarkerPath => Path.Combine(RootDirectory, ".runtime-ready-1.11.0");
    public string EndpointMarkerPath => Path.Combine(RootDirectory, ".sidecar-endpoint.json");
}
