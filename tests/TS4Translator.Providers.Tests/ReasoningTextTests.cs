using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// Guards the one thing that must never happen with a reasoning model: its thinking reaching the game's
/// string table as if it were the translation.
/// </summary>
public class ReasoningTextTests
{
    [Fact]
    public void Strip_ThoughtThenAnswered_KeepsOnlyTheAnswer()
    {
        var raw = "<think>\nThe source says \"Buff\", which is a game term and must survive.\n</think>\n\nBuff de Confiança";

        Assert.Equal("Buff de Confiança", ReasoningText.Strip(raw));
    }

    /// <summary>The ordinary path for every model that does not reason - it has to stay untouched.</summary>
    [Fact]
    public void Strip_NoReasoningAtAll_ReturnsTheTextUnchanged()
    {
        const string raw = "Beijar apaixonadamente";

        Assert.Equal(raw, ReasoningText.Strip(raw));
    }

    /// <summary>
    /// The generation ran into the token cap mid-thought. There is no translation in there, and returning
    /// the monologue would ship it: empty is the honest answer, and the validator flags it for review.
    /// </summary>
    [Fact]
    public void Strip_ThinkingNeverClosed_ReturnsEmptyRatherThanTheMonologue()
    {
        const string raw = "<think>\nHmm, this one is tricky because the word could mean";

        Assert.Equal(string.Empty, ReasoningText.Strip(raw));
    }

    /// <summary>
    /// Anchored on the last close, not on a matched pair: a model that reopens its thinking must not be
    /// able to leave the first block in the answer.
    /// </summary>
    [Fact]
    public void Strip_SeveralThinkingBlocks_KeepsWhatFollowsTheLastOne()
    {
        const string raw = "<think>primeiro</think>rascunho<think>segundo</think>Resposta final";

        Assert.Equal("Resposta final", ReasoningText.Strip(raw));
    }

    [Theory]
    [InlineData("<thinking>x</thinking>Pronto", "Pronto")]
    [InlineData("<reasoning>x</reasoning>  Pronto  ", "Pronto")]
    [InlineData("< think >x</ think >Pronto", "Pronto")]
    public void Strip_HandlesTheTagSpellingsSeenInTheWild(string raw, string expected)
    {
        Assert.Equal(expected, ReasoningText.Strip(raw));
    }

    /// <summary>A marker-bearing translation has to come out of the block intact - that is the whole job.</summary>
    [Fact]
    public void Strip_AnswerCarryingProtectedMarkers_PreservesThemExactly()
    {
        const string raw = "<think>two sentences, keep the break</think>Marque para ativar.__TS4_TOKEN_0001__Desmarque para desativar.";

        Assert.Equal("Marque para ativar.__TS4_TOKEN_0001__Desmarque para desativar.", ReasoningText.Strip(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Strip_NothingToStrip_ReturnsEmpty(string? raw)
    {
        Assert.Equal(string.Empty, ReasoningText.Strip(raw));
    }

    [Fact]
    public void ContainsReasoning_SeesAnUnclosedBlock()
    {
        Assert.True(ReasoningText.ContainsReasoning("<think>ainda pensando"));
        Assert.False(ReasoningText.ContainsReasoning("Uma tradução comum."));
    }
}
