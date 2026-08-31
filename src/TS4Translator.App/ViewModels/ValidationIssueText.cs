using TS4Translator.Core.Validation;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Says, in the user's language, what is wrong with a translation and what to do about it.
/// </summary>
/// <remarks>
/// The rows used to show the validator's own message verbatim: "Text '__TS4_TOKEN_0001__' matches the
/// placeholder format but does not correspond to any protected token." That sentence is written for
/// whoever is debugging the pipeline. To the person looking at a review queue it names an internal
/// marker they have never heard of, does not say the model invented it, and does not hint that the
/// remedy is to revert rather than to retype twenty-nine lines by hand.
/// </remarks>
public static class ValidationIssueText
{
    /// <summary>Describes the worst issue found, or null when there is nothing to report.</summary>
    public static string? Describe(ValidationIssueCode code) => code switch
    {
        ValidationIssueCode.UnexpectedToken =>
            "A IA inventou um marcador que não existia no texto original - provavelmente no lugar de um número ou nome. Reverta esta linha para o original.",
        ValidationIssueCode.MissingToken =>
            "A tradução perdeu um marcador do jogo que o original tinha.",
        ValidationIssueCode.DuplicatedToken =>
            "A tradução repetiu um marcador do jogo que aparecia uma vez só.",
        ValidationIssueCode.ModifiedPlaceholder =>
            "A IA alterou um marcador do jogo e ele não pôde ser reconstituído.",
        ValidationIssueCode.EmptyTranslation =>
            "A tradução voltou vazia.",
        ValidationIssueCode.IdenticalToSource =>
            "Voltou igual ao original. Pode estar certo (nome próprio, marca) ou não ter sido traduzido.",
        ValidationIssueCode.LengthDiscrepancy =>
            "O tamanho da tradução destoa muito do original - pode ter sobrado ou faltado texto.",
        ValidationIssueCode.UnbalancedMarkup =>
            "As tags de formatação da tradução não fecham como no original.",
        ValidationIssueCode.ProviderResultMissing =>
            "O motor não devolveu resultado para esta linha.",
        ValidationIssueCode.ProviderResultDuplicated =>
            "O motor devolveu mais de um resultado para esta linha.",
        ValidationIssueCode.FuzzyMatchReused =>
            "Reaproveitada de uma tradução anterior parecida, mas não idêntica. Confira.",
        _ => null,
    };

    /// <summary>
    /// The summary stored on the row: the explanation in Portuguese, with the validator's own wording
    /// kept after it so the underlying detail is never simply thrown away.
    /// </summary>
    public static string? Summarize(IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (issues.Count == 0)
        {
            return null;
        }

        // Criticals first: a lost marker breaks the string in-game, an unusual length does not.
        var worst = issues
            .OrderByDescending(issue => issue.Severity)
            .First();

        var explained = Describe(worst.Code);
        var detail = issues.Count > 1 ? $" (+{issues.Count - 1} outro(s))" : string.Empty;

        return explained is null ? worst.Message + detail : explained + detail;
    }
}
