using CommunityToolkit.Mvvm.ComponentModel;
using TS4Translator.Core.Interchange;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// One collapsed line of an import report: "128 textos voltaram iguais ao original", expandable into
/// every individual message.
/// </summary>
/// <remarks>
/// Grouping is the whole point. A reply that leaves a hundred tuning identifiers untranslated produces a
/// hundred messages that all say the same thing, and printing them one after another is how the single
/// message that mattered — one sentence the model refused to translate — became invisible.
/// </remarks>
public sealed partial class ImportProblemGroupViewModel(BundleProblemGroup group) : ObservableObject
{
    public string Title { get; } = group.Title;

    /// <summary>Prefix that survives being read aloud or copied into a bug report, unlike colour alone.</summary>
    public string SeverityLabel { get; } = group.Severity switch
    {
        BundleProblemSeverity.Error => "ERRO",
        BundleProblemSeverity.Warning => "AVISO",
        _ => "NOTA",
    };

    /// <summary>The detail, capped: the file always has all of it, the panel only has to make it findable.</summary>
    public string Details { get; } = group.Messages.Count <= MaximumShown
        ? string.Join(Environment.NewLine, group.Messages)
        : string.Join(Environment.NewLine, group.Messages.Take(MaximumShown))
            + Environment.NewLine
            + $"… e mais {group.Messages.Count - MaximumShown}. Use \"Salvar relatório\" para ver todos.";

    /// <summary>A real problem opens itself; a note about identifiers waits to be asked for.</summary>
    public bool IsExpanded { get; } = group.Severity >= BundleProblemSeverity.Warning;

    private const int MaximumShown = 30;
}
