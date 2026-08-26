using Windows.Storage.Pickers;

namespace TS4Translator.App.Services;

public sealed class WindowsAppSdkPathPickerService : IPathPickerService
{
    public async Task<IReadOnlyList<string>> PickPackageFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            CommitButtonText = "Adicionar",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".package");

        // WinUI 3 has no CoreWindow; the classic WinRT picker must be explicitly owned by our HWND
        // or it can wait forever without showing a visible dialog (proven pattern, see
        // ../SimsPackageBatcher/src/SimsPackageBatcher.App/Services/WindowsAppSdkPathPickerService.cs).
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(file => file.Path).ToArray();
    }

    public async Task<string?> PickSaveFileAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            CommitButtonText = "Exportar",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
        };
        picker.FileTypeChoices.Add("Sims 4 Package", [".package"]);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
