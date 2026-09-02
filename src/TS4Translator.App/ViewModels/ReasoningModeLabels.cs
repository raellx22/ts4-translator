using TS4Translator.Core.Providers;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// The mapping between <see cref="ReasoningMode"/> and the text the user picks from, in the same spirit
/// as <see cref="ProviderKindLabels"/>: one place, so the app never calls the same setting two things.
/// </summary>
public static class ReasoningModeLabels
{
    public const string Off = "Desligado - responde direto";
    public const string On = "Ligado - pensa antes de responder";
    public const string ModelDefault = "Como o modelo vier de fábrica";

    public static IReadOnlyList<string> All { get; } = [Off, On, ModelDefault];

    /// <summary>What each choice costs and buys, shown under the picker.</summary>
    public static string Describe(ReasoningMode mode) => mode switch
    {
        ReasoningMode.Off =>
            "O modelo responde só a tradução. É o certo para rótulos e frases curtas, e é o mais rápido - "
            + "vários textos são traduzidos ao mesmo tempo.",
        ReasoningMode.On =>
            "O modelo raciocina antes de responder e o app usa só o que vem depois do raciocínio. Vale a pena "
            + "justamente nas frases curtas com pegadinha - um termo do jogo, um buff, um trocadilho - que um "
            + "modelo pequeno costuma simplificar até perder o sentido. Em troca, gasta bem mais tempo por texto "
            + "e traduz menos textos em paralelo.",
        _ =>
            "Nem liga nem desliga: o app não mexe no template do modelo e aceita o que ele fizer, só com "
            + "orçamento de tempo suficiente para terminar. Use se o modelo for feito para raciocinar e piorar "
            + "quando mandam parar.",
    };

    public static string ToLabel(ReasoningMode mode) => mode switch
    {
        ReasoningMode.On => On,
        ReasoningMode.ModelDefault => ModelDefault,
        _ => Off,
    };

    public static ReasoningMode ToMode(string? label) => label switch
    {
        On => ReasoningMode.On,
        ModelDefault => ReasoningMode.ModelDefault,
        _ => ReasoningMode.Off,
    };
}
