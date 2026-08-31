using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TS4Translator.App.ViewModels;

namespace TS4Translator.App.Pages;

public sealed partial class MemoryPage : Page
{
    public MemoryPageViewModel ViewModel { get; }

    public MemoryPage()
    {
        ViewModel = App.Services.GetRequiredService<MemoryPageViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void TranslationTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MemoryEntryViewModel entry })
        {
            _ = ViewModel.SaveEntryAsync(entry);
        }
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MemoryEntryViewModel entry)
        {
            return;
        }

        await ViewModel.DeleteEntryAsync(entry);
    }

    /// <summary>
    /// Wiping every entry of one engine is not undoable, so it asks first — and says what it will and
    /// will not touch, because "limpar a memória" sounds like it might undo translations already applied
    /// to a project, and it does not.
    /// </summary>
    private async void ClearProvider_Click(object sender, RoutedEventArgs e)
    {
        var provider = ViewModel.SelectedProvider;
        if (ViewModel.ProviderOptions.Count == 0 || provider == ViewModel.ProviderOptions[0])
        {
            ViewModel.StatusText = "Escolha uma origem específica antes de limpar.";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Apagar tudo de \"{provider}\"?",
            Content = "Todas as entradas dessa origem saem da memória de tradução. "
                + "As traduções que já estão nos seus projetos não mudam - só deixam de ser reaproveitadas em mods futuros. "
                + "Não dá para desfazer.",
            PrimaryButtonText = "Apagar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteProviderAsync(provider);
        }
    }
}
