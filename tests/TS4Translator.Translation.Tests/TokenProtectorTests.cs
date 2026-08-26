using TS4Translator.Core.Tokens;
using TS4Translator.Translation;

namespace TS4Translator.Translation.Tests;

public class TokenProtectorTests
{
    private readonly TokenProtector _sut = new();

    [Theory]
    [InlineData("Ask {0.SimFirstName} about their relationship.")]
    [InlineData("Tell {1.SimLastName} the news.")]
    [InlineData("{M0.He} left the party early.")]
    [InlineData("{F0.She} left the party early.")]
    public void Protect_ReplacesSingleBraceToken_WithOnePlaceholder(string source)
    {
        var result = _sut.Protect(source);

        Assert.Single(result.Tokens);
        Assert.DoesNotContain('{', result.ProtectedText);
        Assert.DoesNotContain('}', result.ProtectedText);
        Assert.Contains("__TS4_TOKEN_0001__", result.ProtectedText);
    }

    [Fact]
    public void Protect_MultipleTokens_AreNumberedInSourceOrder()
    {
        var source = "{0.SimFirstName} asks {1.SimFirstName} about {M0.He} car.";

        var result = _sut.Protect(source);

        Assert.Equal(3, result.Tokens.Count);
        Assert.Equal("{0.SimFirstName}", result.Tokens[0].OriginalValue);
        Assert.Equal("{1.SimFirstName}", result.Tokens[1].OriginalValue);
        Assert.Equal("{M0.He}", result.Tokens[2].OriginalValue);
        Assert.Equal("__TS4_TOKEN_0001__ asks __TS4_TOKEN_0002__ about __TS4_TOKEN_0003__ car.", result.ProtectedText);
    }

    [Fact]
    public void Protect_RepeatedIdenticalTokens_GetDistinctPlaceholders()
    {
        var source = "{0.SimFirstName} loves {0.SimFirstName}.";

        var result = _sut.Protect(source);

        Assert.Equal(2, result.Tokens.Count);
        Assert.NotEqual(result.Tokens[0].Placeholder, result.Tokens[1].Placeholder);
        Assert.All(result.Tokens, t => Assert.Equal("{0.SimFirstName}", t.OriginalValue));
    }

    [Fact]
    public void Protect_AdjacentTokens_AreBothExtracted()
    {
        var source = "{0.SimFirstName}{1.SimLastName}";

        var result = _sut.Protect(source);

        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("__TS4_TOKEN_0001____TS4_TOKEN_0002__", result.ProtectedText);
    }

    [Fact]
    public void Protect_NewlineCharacters_AreExtractedAsTokens()
    {
        var source = "Line one\nLine two\r\nLine three";

        var result = _sut.Protect(source);

        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("\n", result.Tokens[0].OriginalValue);
        Assert.Equal("\r\n", result.Tokens[1].OriginalValue);
    }

    [Theory]
    [InlineData("Click <b>here</b> to continue.")]
    [InlineData("Insert a <camera/> shot here.")]
    [InlineData("<a href=\"sims4://help\">Learn more</a>")]
    public void Protect_MarkupTags_AreExtracted(string source)
    {
        var result = _sut.Protect(source);

        Assert.NotEmpty(result.Tokens);
        Assert.DoesNotContain('<', result.ProtectedText);
        Assert.DoesNotContain('>', result.ProtectedText);
    }

    [Fact]
    public void Protect_UnicodeAndEmoji_PassThroughUnaffectedOutsideTokens()
    {
        var source = "Café com {0.SimFirstName} 😀 é ótimo!";

        var result = _sut.Protect(source);

        Assert.Single(result.Tokens);
        Assert.Contains("Café com", result.ProtectedText);
        Assert.Contains("😀 é ótimo!", result.ProtectedText);
    }

    [Fact]
    public void Protect_EmptyString_ReturnsNoTokens()
    {
        var result = _sut.Protect(string.Empty);

        Assert.Empty(result.Tokens);
        Assert.Equal(string.Empty, result.ProtectedText);
    }

    [Fact]
    public void Protect_LargeText_HandlesManyTokensWithoutError()
    {
        var source = string.Concat(Enumerable.Repeat("{0.SimFirstName} said hello. ", 5000));

        var result = _sut.Protect(source);

        Assert.Equal(5000, result.Tokens.Count);
    }

    [Fact]
    public void Protect_PlainTextWithNoTokens_IsUnchanged()
    {
        var result = _sut.Protect("Just a plain sentence with no placeholders.");

        Assert.Empty(result.Tokens);
        Assert.Equal("Just a plain sentence with no placeholders.", result.ProtectedText);
    }

    [Fact]
    public void Restore_RoundTripsCorrectTranslation()
    {
        var protectedString = _sut.Protect("Ask {0.SimFirstName} about their {1.SimLastName} relationship.");
        var translated = protectedString.ProtectedText.Replace("Ask", "Pedir a").Replace("about their", "sobre o relacionamento com");

        var result = _sut.Restore(translated, protectedString);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Contains("{0.SimFirstName}", result.RestoredText);
        Assert.Contains("{1.SimLastName}", result.RestoredText);
    }

    [Fact]
    public void Restore_ReorderedTokens_IsValidButFlagged()
    {
        var protectedString = _sut.Protect("{0.SimFirstName} and {1.SimLastName}");
        var translated = "__TS4_TOKEN_0002__ e __TS4_TOKEN_0001__";

        var result = _sut.Restore(translated, protectedString);

        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, i => i.Kind == TokenValidationIssueKind.ReorderedToken);
    }

    [Fact]
    public void Restore_MissingToken_IsInvalidWithCriticalIssue()
    {
        var protectedString = _sut.Protect("Ask {0.SimFirstName} to stay.");
        var translated = "Pedir para ficar.";

        var result = _sut.Restore(translated, protectedString);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TokenValidationIssueKind.MissingToken, issue.Kind);
        Assert.True(issue.IsCritical);
    }

    [Fact]
    public void Restore_EmptyTranslatedString_ReportsMissingTokenForEachExpectedToken()
    {
        var protectedString = _sut.Protect("{0.SimFirstName} and {1.SimLastName}");

        var result = _sut.Restore(string.Empty, protectedString);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Issues.Count(i => i.Kind == TokenValidationIssueKind.MissingToken));
    }

    [Fact]
    public void Restore_DuplicatedToken_IsInvalidAndSubstitutesBothOccurrences()
    {
        var protectedString = _sut.Protect("Ask {0.SimFirstName} to stay.");
        var translated = "__TS4_TOKEN_0001__ __TS4_TOKEN_0001__ to stay.";

        var result = _sut.Restore(translated, protectedString);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Kind == TokenValidationIssueKind.DuplicatedToken);
        Assert.Equal("{0.SimFirstName} {0.SimFirstName} to stay.", result.RestoredText);
    }

    [Fact]
    public void Restore_UnexpectedToken_IsFlaggedButDoesNotThrow()
    {
        var protectedString = _sut.Protect("Ask {0.SimFirstName} to stay.");
        var translated = "__TS4_TOKEN_0001__ and __TS4_TOKEN_0002__ to stay.";

        var result = _sut.Restore(translated, protectedString);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Kind == TokenValidationIssueKind.UnexpectedToken);
    }

    [Fact]
    public void Restore_ModifiedPlaceholder_IsFlagged()
    {
        var protectedString = _sut.Protect("Ask {0.SimFirstName} to stay.");
        var translated = "__TS4_TOKEN_01__ to stay.";

        var result = _sut.Restore(translated, protectedString);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Kind == TokenValidationIssueKind.MissingToken);
        Assert.Contains(result.Issues, i => i.Kind == TokenValidationIssueKind.ModifiedPlaceholder);
    }

    [Fact]
    public void Restore_NoTokensExpected_AndNoneFound_IsValid()
    {
        var protectedString = _sut.Protect("Plain sentence.");

        var result = _sut.Restore("Frase simples.", protectedString);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }
}
