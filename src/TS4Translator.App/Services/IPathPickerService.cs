namespace TS4Translator.App.Services;

public interface IPathPickerService
{
    Task<IReadOnlyList<string>> PickPackageFilesAsync();
}
