using TS4Translator.Core.Translation;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Turns a <see cref="SuspicionAssessment"/> into the sentence the editor shows on the row.
/// </summary>
/// <remarks>
/// Says what is missing, not which rule fired. "LostStructure, score 65" is a note to whoever wrote the
/// heuristic; the person triaging a mod needs to know that the line had a heading and the translation
/// does not — which is the difference between a row they can judge in a second and one they have to
/// re-derive from the two texts side by side. Same reasoning as
/// <see cref="TS4Translator.Core.Validation.ValidationIssueText"/> for validator codes.
/// </remarks>
public static class SuspicionReasonText
{
    /// <summary>The findings for one row, worst first, as one line.</summary>
    public static string Summarize(SuspicionAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        return assessment.Reasons.Count == 0
            ? string.Empty
            : string.Join(" · ", assessment.Reasons.Select(Describe));
    }

    public static string Describe(SuspicionReason reason) => reason switch
    {
        SuspicionReason.IdenticalToSource => "voltou em inglês",
        SuspicionReason.LostStructure => "perdeu a formatação do título (os tracinhos, ###)",
        SuspicionReason.LostNumbers => "perdeu números que estavam no original",
        SuspicionReason.LostSentences => "juntou ou comeu frases do original",
        SuspicionReason.SeverelyShortened => "muito mais curta que o original",
        SuspicionReason.LostWordsInLabel => "rótulo curto que perdeu palavras — pode ter cortado o termo do jogo",
        _ => reason.ToString(),
    };
}
