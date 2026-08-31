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

    // The list holds ProjectCardViewModel, not TranslationProject. When the card view model was
    // introduced this cast was left matching the old type, so it silently failed for every row and
    // clicking a project did nothing at all - no error, no navigation.
    private void RecentProjects_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProjectCardViewModel card)
        {
            Frame.Navigate(typeof(EditorPage), card.Id);
        }
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        // Tag, not DataContext: a button inside an ItemTemplate cannot reach the page's ViewModel
        // through ElementName reliably in WinUI 3 (DEC-013).
        if ((sender as FrameworkElement)?.Tag is not ProjectCardViewModel card)
        {
            return;
        }

        // Deleting a project throws away every translation in it - on the user's real WickedWhims
        // project that is a hundred and forty nine thousand rows and hours of engine time. It is not
        // undoable, so it does not happen on one stray click.
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Apagar este projeto?",
            Content = $"\"{card.Name}\" e todas as suas {card.Progress.Total:N0} string(s) traduzidas serão apagadas. Os arquivos .package originais não são tocados, mas o trabalho de tradução não tem como ser recuperado.",
            PrimaryButtonText = "Apagar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteProjectAsync(card);
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
