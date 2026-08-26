using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TS4Translator.App.ViewModels;
using TS4Translator.Core.Glossary;

namespace TS4Translator.App.Pages;

public sealed partial class GlossaryPage : Page
{
    public GlossaryPageViewModel ViewModel { get; }

    public GlossaryPage()
    {
        ViewModel = App.Services.GetRequiredService<GlossaryPageViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void RemoveTermButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GlossaryTerm term })
        {
            _ = ViewModel.DeleteTermCommand.ExecuteAsync(term);
        }
    }
}
