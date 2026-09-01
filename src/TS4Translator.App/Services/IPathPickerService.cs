namespace TS4Translator.App.Services;

public interface IPathPickerService
{
    Task<IReadOnlyList<string>> PickPackageFilesAsync();

    /// <returns>The chosen path, or null if the user cancelled.</returns>
    Task<string?> PickSaveFileAsync(string suggestedFileName);

    /// <summary>
    /// Picks an output directory. Used by the export when a project spans several mods and therefore
    /// produces one `.package` per mod instead of a single file.
    /// </summary>
    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    Task<string?> PickFolderAsync();

    /// <summary>Picks where to write the plain-text bundle the user will paste into a chat model.</summary>
    Task<string?> PickSaveTextFileAsync(string suggestedFileName);

    /// <summary>Picks a .gguf model file for the app-managed local engine.</summary>
    Task<string?> PickModelFileAsync();

    /// <summary>Picks the text file holding a chat model's reply.</summary>
    Task<string?> PickOpenTextFileAsync();

    /// <summary>
    /// Picks one or more reply files at once. A mod exported in parts comes back as several files, and
    /// making the user import them one dialog at a time is how a part gets forgotten.
    /// </summary>
    Task<IReadOnlyList<string>> PickOpenTextFilesAsync();
}
