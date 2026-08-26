namespace TS4Translator.App.Services;

public interface IPathPickerService
{
    Task<IReadOnlyList<string>> PickPackageFilesAsync();

    /// <returns>The chosen path, or null if the user cancelled.</returns>
    Task<string?> PickSaveFileAsync(string suggestedFileName);
}
