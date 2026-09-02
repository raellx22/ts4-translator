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

    private void RethinkRow_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _ = ViewModel.RethinkRowAsync(row);
        }
    }

    /// <summary>
    /// Confirms the batch rethink, because reasoning is slow enough that the count matters: a hundred
    /// strings on a local model with thinking on is minutes, not seconds, and the user should see the
    /// number before starting rather than discover it from a progress bar.
    /// </summary>
    private async void RethinkSuspects_Click(object sender, RoutedEventArgs e)
    {
        var suspects = ViewModel.SuspectCount;
        if (suspects == 0)
        {
            ViewModel.StatusText = "Nenhuma tradução suspeita neste projeto. Nada a repensar.";
            return;
        }

        // Desmarcado por padrão, e o texto diz por quê com número medido: no que esta fila realmente
        // contém - conteúdo que o motor comeu -, o raciocínio não consertou nada a mais e custou vinte
        // vezes o relógio. Ele paga na outra falha, que é o botão de uma linha só.
        var useReasoning = new CheckBox
        {
            Content = "Usar o raciocínio do modelo (bem mais lento)",
            IsChecked = false,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Repensar {suspects:N0} tradução(ões) suspeita(s)?",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Elas são traduzidas de novo por \"{ViewModel.SelectedRefineProfileName}\", a partir do texto original — "
                            + "a tradução atual é descartada, não mostrada ao modelo. Mostrar o rascunho ruim faz o modelo repeti-lo, "
                            + "que é por que isto não é um refino. O texto descartado fica no histórico de cada linha.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    useReasoning,
                    new TextBlock
                    {
                        Text = "Medido com o Qwen3-8B nas linhas deste projeto: sem raciocínio consertou todas as testadas a "
                            + "0,33 s por texto; com raciocínio consertou as mesmas a 6,5 s por texto. Para esta fila o raciocínio "
                            + "custa vinte vezes mais e não achou nada a mais — ele ajuda no rótulo curto de sentido ambíguo, "
                            + "que é o botão \"Repensar\" de cada linha. Dá para cancelar no meio.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                    },
                },
            },
            PrimaryButtonText = "Repensar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RethinkSuspectsAsync(useReasoning.IsChecked == true);
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

    /// <summary>
    /// Confirms before discarding translations in bulk. Nothing is lost - each discarded text is kept as
    /// a revision - but the count is stated up front, because "reverter" on a list the user cannot see
    /// the end of should never be a surprise.
    /// </summary>
    private async void RevertSelected_Click(object sender, RoutedEventArgs e)
    {
        var affected = ViewModel.SelectedWithTranslationCount;
        if (affected == 0)
        {
            ViewModel.StatusText = "Nenhuma das linhas marcadas tem tradução para descartar.";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Reverter {affected} linha(s) para o original?",
            Content = "A tradução dessas linhas é descartada e elas voltam a ficar pendentes. "
                + "Na exportação do package, uma linha sem tradução sai com o texto original - em inglês, mas sem quebrar nada no jogo. "
                + "O texto descartado continua no histórico de cada linha.",
            PrimaryButtonText = "Reverter",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RevertSelectedToOriginalAsync();
        }
    }

    private async void AcceptSelected_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AcceptSelectedAsync();
    }

    /// <summary>
    /// Confirms a full restart, saying in plain words what nothing in the app said before: that the old
    /// translations are also being forgotten by the memory, and that this is what stops them coming back.
    /// </summary>
    private async void RestartTranslation_Click(object sender, RoutedEventArgs e)
    {
        var (total, manual) = ViewModel.CountRestartImpact();
        if (total == 0)
        {
            ViewModel.StatusText = "Não há nenhuma tradução para refazer - o projeto já está todo pendente.";
            return;
        }

        var keepManual = new CheckBox
        {
            Content = $"Manter as {manual:N0} linha(s) que eu editei ou refinei à mão",
            IsChecked = true,
            IsEnabled = manual > 0,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Refazer a tradução das {total:N0} linha(s)?",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "As traduções atuais são descartadas (ficam no histórico de cada linha), o app esquece o que elas "
                            + "ensinaram à memória de tradução, e tudo é traduzido de novo pelo motor configurado agora.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "Esquecer a memória é a parte que importa: sem isso, a rodada seguinte devolveria as mesmas "
                            + "traduções sem chamar motor nenhum. Frases que outros mods compartilhem com este vão ser "
                            + "traduzidas de novo quando aparecerem.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                    },
                    keepManual,
                },
            },
            PrimaryButtonText = "Refazer do zero",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RestartTranslationAsync(keepManual.IsChecked == true);
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
