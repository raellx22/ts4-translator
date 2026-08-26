using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
}
