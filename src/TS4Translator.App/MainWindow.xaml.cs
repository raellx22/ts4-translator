using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TS4Translator.App.Pages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TS4Translator.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Explicit, because navigation runs off ItemInvoked and that only fires on a real click.
        // SelectionChanged used to fire once during load (Home carries IsSelected="True") and that was
        // what put the first page on screen; without this the app opens to an empty content area.
        NavFrame.Navigate(typeof(HomePage));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    /// <summary>
    /// Navigation is driven by ItemInvoked rather than SelectionChanged.
    /// </summary>
    /// <remarks>
    /// Opening a project navigates the frame to the Editor without changing which sidebar item is
    /// selected, so "Home" stays highlighted. SelectionChanged then never fires when the user clicks it
    /// - the selection did not change - and the click did nothing at all, leaving the only way out of a
    /// project the title bar's back arrow. ItemInvoked fires either way.
    /// </remarks>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            NavigateTo(typeof(SettingsPage));
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        NavigateTo(item.Tag switch
        {
            "home" => typeof(HomePage),
            "glossary" => typeof(GlossaryPage),
            "memory" => typeof(MemoryPage),
            "about" => typeof(AboutPage),
            _ => throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}"),
        });
    }

    /// <summary>
    /// Navigates unless that page is already showing - re-navigating to the page the user is already on
    /// would throw away its state (a typed search, a scroll position) for no reason.
    /// </summary>
    private void NavigateTo(Type pageType)
    {
        if (NavFrame.CurrentSourcePageType != pageType)
        {
            NavFrame.Navigate(pageType);
        }
    }
}
