using System.Globalization;
using System.Text;

namespace TS4Translator.Core.Interchange;

/// <param name="Severity">Worst severity in the group — every member shares it.</param>
/// <param name="Kind">What all these problems have in common.</param>
/// <param name="Title">A heading that already contains the count, e.g. "128 textos voltaram iguais ao original".</param>
/// <param name="Messages">Every individual message, in the order they were produced.</param>
public sealed record BundleProblemGroup(
    BundleProblemSeverity Severity,
    BundleProblemKind Kind,
    string Title,
    IReadOnlyList<string> Messages);

/// <summary>
/// Turns an import result into something a person can actually read: grouped on screen, and complete on
/// disk.
/// </summary>
/// <remarks>
/// The app used to show three problems and the text "(+184 aviso(s))" in a one-line status bar, with no
/// way to see the rest. A user who had just imported a thousand strings could tell that something was
/// off and had no way at all to find out what — which is a worse failure than the parsing problem they
/// were trying to diagnose. So: on screen the problems are collapsed by kind, because a hundred
/// identifiers are one fact and not a hundred; and the full list always goes to a file, because the
/// interesting line is usually the one that would have been cut off.
/// </remarks>
public static class BundleImportReport
{
    /// <summary>Collapses problems into one entry per kind, worst first, keeping every message.</summary>
    public static IReadOnlyList<BundleProblemGroup> Group(IReadOnlyList<BundleProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        return [.. problems
            .GroupBy(problem => (problem.Severity, problem.Kind))
            .OrderByDescending(group => group.Key.Severity)
            .ThenByDescending(group => group.Count())
            .Select(group => new BundleProblemGroup(
                group.Key.Severity,
                group.Key.Kind,
                Title(group.Key.Kind, group.Key.Severity, group.Count()),
                [.. group.Select(problem => problem.Message)]))];
    }

    /// <summary>The full, unabridged report — the thing the status bar could never be.</summary>
    public static string Write(IReadOnlyList<string> sourceFiles, BundleImportResult result)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.AppendLine("RELATORIO DE IMPORTACAO - TS4Translator");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Data: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Arquivo(s): {string.Join(", ", sourceFiles)}");
        builder.AppendLine();
        builder.AppendLine(result.Summary);
        builder.AppendLine();

        var byStrategy = result.Translations.CountBy(translation => translation.Strategy).ToList();
        if (byStrategy.Count > 0)
        {
            builder.AppendLine("Como cada tradução foi identificada:");
            foreach (var (strategy, count) in byStrategy.OrderByDescending(pair => pair.Value))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {count} {Describe(strategy)}");
            }

            builder.AppendLine();
        }

        if (result.Problems.Count == 0)
        {
            builder.AppendLine("Nenhum problema encontrado.");
            return builder.ToString();
        }

        foreach (var group in Group(result.Problems))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"== [{Label(group.Severity)}] {group.Title}");
            foreach (var message in group.Messages)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"   {message}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Title(BundleProblemKind kind, BundleProblemSeverity severity, int count) => kind switch
    {
        BundleProblemKind.EmptyReply => "O arquivo está vazio",
        BundleProblemKind.NoRecords => "Nenhum bloco de tradução foi reconhecido no arquivo",
        BundleProblemKind.UnknownId => $"{count} id(s) da resposta não existem no projeto",
        BundleProblemKind.DuplicateId => $"{count} id(s) apareceram repetidos na resposta",
        BundleProblemKind.CountMismatch => "A resposta veio sem ids e com quantidade diferente da enviada",
        BundleProblemKind.MissingStrings => "Textos enviados que não voltaram na resposta",
        BundleProblemKind.NoTranslation => $"{count} texto(s) voltaram com a linha PT: em branco",
        BundleProblemKind.SameAsOriginal => severity == BundleProblemSeverity.Warning
            ? $"{count} frase(s) voltaram iguais ao original - provável falha de tradução"
            : $"{count} texto(s) voltaram iguais ao original, o que é esperado para eles",
        BundleProblemKind.PlaceholdersLost => $"{count} tradução(ões) perderam marcadores do jogo",
        BundleProblemKind.GlossaryMissed => $"{count} tradução(ões) não respeitaram o glossário",
        BundleProblemKind.DraftKept => $"{count} texto(s) voltaram idênticos ao rascunho enviado - a IA não refinou essas linhas",
        BundleProblemKind.MatchedWithoutId => "Traduções identificadas pelo texto original, sem id",
        BundleProblemKind.MatchedByPosition => "Traduções associadas pela ordem, sem id",
        _ => $"{count} ocorrência(s)",
    };

    private static string Label(BundleProblemSeverity severity) => severity switch
    {
        BundleProblemSeverity.Error => "ERRO",
        BundleProblemSeverity.Warning => "AVISO",
        _ => "NOTA",
    };

    private static string Describe(BundleMatchStrategy strategy) => strategy switch
    {
        BundleMatchStrategy.ById => "pelo id (confiável)",
        BundleMatchStrategy.ByOriginalText => "pelo texto original, porque o id sumiu",
        _ => "pela ordem, porque não havia id nem texto original",
    };
}
