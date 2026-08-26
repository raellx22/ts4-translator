namespace TS4Translator.Infrastructure;

/// <summary>
/// Resolves the app's data directories under <c>%LOCALAPPDATA%\TS4Translator</c>, creating them on
/// first access. Nothing is ever written inside the installation folder (brief section 48).
/// </summary>
public static class AppDataPaths
{
    private const string AppFolderName = "TS4Translator";

    public static string Root => EnsureDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

    public static string Data => EnsureDirectory(Path.Combine(Root, "Data"));

    public static string Logs => EnsureDirectory(Path.Combine(Root, "Logs"));

    public static string Cache => EnsureDirectory(Path.Combine(Root, "Cache"));

    public static string Models => EnsureDirectory(Path.Combine(Root, "Models"));

    public static string DatabaseFilePath => Path.Combine(Data, "ts4translator.db");

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
