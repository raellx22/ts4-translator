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

    public async Task<string?> PickSaveTextFileAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            CommitButtonText = "Salvar",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
        };
        picker.FileTypeChoices.Add("Texto", [".txt"]);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickOpenTextFileAsync()
    {
        var picker = new FileOpenPicker
        {
            CommitButtonText = "Importar",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickModelFileAsync()
    {
        var picker = new FileOpenPicker
        {
            CommitButtonText = "Usar este modelo",
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".gguf");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<IReadOnlyList<string>> PickOpenTextFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            CommitButtonText = "Importar",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var files = await picker.PickMultipleFilesAsync();

        // Ordered by name so parte01, parte02, parte03 are applied in the order they were produced -
        // the picker returns them in click order, which is whatever the user happened to do.
        return [.. files.Select(file => file.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            CommitButtonText = "Exportar aqui",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        // FolderPicker throws E_INVALIDARG when its filter list is empty - a long-standing WinRT quirk
        // that has nothing to do with folders actually being filtered.
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
