using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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

    private void TranslationTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StringRowViewModel row })
        {
            _ = ViewModel.SaveManualEditAsync(row);
        }
    }

    // x:Bind generates an instance call (this.Method(...)) for an unqualified function reference in
    // XAML regardless of whether the method could be static - CA1822 disagrees, but making these
    // static breaks the generated MarkupCompilePass2 code (CS0176).
#pragma warning disable CA1822
    public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public bool Not(bool value) => !value;
#pragma warning restore CA1822
}
