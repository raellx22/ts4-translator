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

        // A memória de tradução não é do projeto: ela é global, guardada por texto original, e continua
        // ali depois que o projeto some. Sem dizer isso aqui, apagar o projeto e reimportar o mesmo mod
        // devolvia exatamente as traduções que o usuário tinha jogado fora, sem chamar motor nenhum -
        // e não havia nada na tela que explicasse por quê.
        var forget = new CheckBox
        {
            Content = "Esquecer também estas traduções na memória",
            IsChecked = false,
        };

        // Deleting a project throws away every translation in it - on the user's real WickedWhims
        // project that is a hundred and forty nine thousand rows and hours of engine time. It is not
        // undoable, so it does not happen on one stray click.
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Apagar este projeto?",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"\"{card.Name}\" e todas as suas {card.Progress.Total:N0} string(s) traduzidas serão apagadas. "
                            + "Os arquivos .package originais não são tocados, mas o trabalho de tradução não tem como ser recuperado.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    forget,
                    new TextBlock
                    {
                        Text = "A memória de tradução é global e não vai junto: sem marcar isto, reimportar o mesmo mod e mandar "
                            + "traduzir devolve estas mesmas traduções sem passar por nenhum motor. Marque se elas ficaram ruins e "
                            + "você quer refazer com outro modelo; deixe desmarcado para não perder linhas que outros mods também usam.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                    },
                },
            },
            PrimaryButtonText = "Apagar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteProjectAsync(card, forget.IsChecked == true);
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
