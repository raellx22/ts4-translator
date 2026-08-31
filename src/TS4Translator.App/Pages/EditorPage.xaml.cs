using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TS4Translator.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

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

    private void CopyOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            CopyToClipboard(row.Original);
        }
    }

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { Translation: { } translation })
        {
            CopyToClipboard(translation);
        }
    }

    private void RefineRow_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _ = ViewModel.RefineRowAsync(row);
        }
    }

    /// <summary>
    /// Pre-fills a glossary term from the row and lets the user cut it down to the actual term before
    /// saving. Pre-filled because the user is looking at the word right now; editable because a glossary
    /// entry holding a whole sentence would match nothing and silently accomplish nothing.
    /// </summary>
    private async void AddToGlossary_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }

        var original = new TextBox { Header = "Termo original", Text = Trim(row.Original), TextWrapping = TextWrapping.Wrap };
        var translation = new TextBox { Header = "Deve virar", Text = Trim(row.Translation ?? string.Empty), TextWrapping = TextWrapping.Wrap };
        var neverTranslate = new CheckBox { Content = "Nunca traduzir (nome próprio, marca)" };
        var global = new CheckBox { Content = "Vale para todos os projetos", IsChecked = true };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Adicionar ao glossário",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Guarde só o termo, não a frase inteira. Ele passa a valer para todos os motores e vai junto no arquivo que você leva para o chat.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                    },
                    original,
                    translation,
                    neverTranslate,
                    global,
                },
            },
            PrimaryButtonText = "Adicionar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AddGlossaryTermAsync(
                original.Text, translation.Text, neverTranslate.IsChecked == true, global.IsChecked == true);
        }
    }

    /// <summary>A sentence pre-filled in full would just be deleted; a short label is usually the term itself.</summary>
    private static string Trim(string text)
    {
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 40 ? single : string.Empty;
    }

    // Tag rather than DataContext or ElementName binding: a button inside a ListView ItemTemplate
    // cannot reach the page's ViewModel through ElementName reliably in WinUI 3 (DEC-013), and a
    // MenuFlyoutItem is not in the visual tree, so it has no DataContext to inherit either.
    private static StringRowViewModel? RowOf(object sender) =>
        (sender as FrameworkElement)?.Tag as StringRowViewModel;

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);

        // Without Flush the clipboard contents die with the app, which is exactly the case here: the
        // user is copying the original precisely so they can paste it into a browser afterwards.
        Clipboard.Flush();
    }

    // x:Bind generates an instance call (this.Method(...)) for an unqualified function reference in
    // XAML regardless of whether the method could be static - CA1822 disagrees, but making these
    // static breaks the generated MarkupCompilePass2 code (CS0176).
#pragma warning disable CA1822
    public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public bool Not(bool value) => !value;
#pragma warning restore CA1822
}
