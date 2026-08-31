// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TS4Translator.App.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TS4Translator.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsPageViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void TestProfile_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _ = ViewModel.TestProfileAsync(row);
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _ = ViewModel.EditProfileAsync(row);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _ = ViewModel.DeleteProfileAsync(row);
        }
    }

    // Tag, not ElementName binding: a button inside an ItemTemplate cannot reach the page's ViewModel
    // through ElementName reliably in WinUI 3 (DEC-013).
    private static ProviderProfileRowViewModel? RowOf(object sender) =>
        (sender as FrameworkElement)?.Tag as ProviderProfileRowViewModel;

    // x:Bind generates an instance call for an unqualified function reference in XAML - CA1822 would
    // suggest static, but that breaks the generated MarkupCompilePass2 code (see EditorPage.xaml.cs).
#pragma warning disable CA1822
    public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
#pragma warning restore CA1822
}
