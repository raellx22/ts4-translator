using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// The damaged cases are real answers from Ministral-3-8B over 120 strings of
/// <c>[Kritical]DreamsOfSurrender</c>: 21 of them came back wearing markdown that would have shipped
/// into the game as literal asterisks. The "leave it alone" cases are the reason this is conditioned on
/// the source rather than applied blindly.
/// </summary>
public class AnswerArtifactsTests
{
    [Theory]
    [InlineData("Device Reset", "**Dispositivo Redefinido**", "Dispositivo Redefinido")]
    [InlineData("Service Brainwashed", "Serviço **Cerebro Lavado**", "Serviço Cerebro Lavado")]
    [InlineData("vfx buff debug", "**Efeitos visuais de buff de depuração**", "Efeitos visuais de buff de depuração")]
    [InlineData("Clean For Dom", "*Limpar para o Dom*", "Limpar para o Dom")]
    public void Strip_MarkdownTheModelInvented_IsRemoved(string source, string answer, string expected)
    {
        Assert.Equal(expected, AnswerArtifacts.Strip(answer, source));
    }

    [Fact]
    public void Strip_QuotesTheModelWrappedTheAnswerIn_AreRemoved()
    {
        Assert.Equal("Limpar o Ocupante", AnswerArtifacts.Strip("\"Limpar o Ocupante\"", "Clean The Occupant"));
    }

    /// <summary>A mod that really does decorate its own text keeps the decoration.</summary>
    [Theory]
    [InlineData("*** WARNING ***", "*** AVISO ***")]
    [InlineData("\"I could never say no.\"", "\"Eu nunca poderia dizer não.\"")]
    public void Strip_DecorationThatIsInTheSourceToo_IsKept(string source, string answer)
    {
        Assert.Equal(answer, AnswerArtifacts.Strip(answer, source));
    }

    /// <summary>
    /// The one that would be catastrophic: a protected marker is <c>__TS4_TOKEN_0001__</c>, and a naive
    /// markdown pass reads the doubled underscores as emphasis and eats the marker.
    /// </summary>
    [Fact]
    public void Strip_ProtectedMarkers_SurviveUntouched()
    {
        const string answer = "Marque para ativar.__TS4_TOKEN_0001__Desmarque para desativar.";

        Assert.Equal(answer, AnswerArtifacts.Strip(answer, "Check to enable.__TS4_TOKEN_0001__Uncheck to disable."));
    }

    [Fact]
    public void Strip_OrdinaryTranslation_IsReturnedUnchanged()
    {
        Assert.Equal("Falhando em uma Tarefa", AnswerArtifacts.Strip("Falhando em uma Tarefa", "Failing At A Task"));
    }

    [Fact]
    public void Strip_NothingUsable_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AnswerArtifacts.Strip(null, "qualquer coisa"));
        Assert.Equal(string.Empty, AnswerArtifacts.Strip("   ", "qualquer coisa"));
    }
}
