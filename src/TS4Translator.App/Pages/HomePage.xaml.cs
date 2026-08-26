using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using TS4Translator.App.ViewModels;
using TS4Translator.Core.Projects;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TS4Translator.App.Pages;

public sealed partial class HomePage : Page
{
    public HomePageViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomePageViewModel>();
        InitializeComponent();
    }

    private void RecentProjects_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TranslationProject project)
        {
            Frame.Navigate(typeof(EditorPage), project.Id);
        }
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var packageFiles = items
                .OfType<StorageFile>()
                .Where(f => f.FileType.Equals(".package", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Path)
                .ToList();

            if (packageFiles.Count > 0)
            {
                await ViewModel.ImportFilesAsync(packageFiles);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
