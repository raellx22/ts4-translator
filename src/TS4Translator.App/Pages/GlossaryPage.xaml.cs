using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TS4Translator.App.ViewModels;

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
        if (sender is FrameworkElement { Tag: GlossaryTermViewModel term })
        {
            _ = ViewModel.DeleteTermCommand.ExecuteAsync(term);
        }
    }

    private void TermField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GlossaryTermViewModel term })
        {
            _ = ViewModel.SaveTermAsync(term);
        }
    }

    // A checkbox has no "lost focus" moment worth waiting for - the click is the decision.
    private void TermToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GlossaryTermViewModel term })
        {
            _ = ViewModel.SaveTermAsync(term);
        }
    }
}
