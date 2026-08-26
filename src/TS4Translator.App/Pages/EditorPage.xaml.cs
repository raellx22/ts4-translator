using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TS4Translator.App.ViewModels;

namespace TS4Translator.App.Pages;

public sealed partial class EditorPage : Page
{
    public EditorPageViewModel ViewModel { get; }

    public EditorPage()
    {
        ViewModel = App.Services.GetRequiredService<EditorPageViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Guid projectId)
        {
            _ = ViewModel.LoadCommand.ExecuteAsync(projectId);
        }
    }
}
