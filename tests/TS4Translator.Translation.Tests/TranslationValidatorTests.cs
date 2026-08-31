using TS4Translator.Core.Validation;
using TS4Translator.Translation;

namespace TS4Translator.Translation.Tests;

public class TranslationValidatorTests
{
    private readonly TranslationValidator _sut = new();
    private readonly TokenProtector _tokenProtector = new();

    [Fact]
    public void Validate_GoodTranslation_ReturnsNoIssues()
    {
        var issues = _sut.Validate("Ask about their day.", "Pergunte sobre o dia deles.");

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_EmptyTranslation_ReturnsCriticalIssueAndStopsFurtherChecks()
    {
        var issues = _sut.Validate("Ask about their day.", "   ");

        var issue = Assert.Single(issues);
        Assert.Equal(ValidationIssueCode.EmptyTranslation, issue.Code);
        Assert.Equal(ValidationSeverity.Critical, issue.Severity);
    }

    [Fact]
    public void Validate_EmptySource_DoesNotFlagEmptyTranslation()
    {
        var issues = _sut.Validate("", "");

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_IdenticalToSource_ReturnsWarning()
    {
        var issues = _sut.Validate("Woohoo!", "Woohoo!");

        Assert.Contains(issues, i => i.Code == ValidationIssueCode.IdenticalToSource && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_IdenticalNumericOnlyStrings_DoesNotFlagAsIdentical()
    {
        var issues = _sut.Validate("100", "100");

        Assert.DoesNotContain(issues, i => i.Code == ValidationIssueCode.IdenticalToSource);
    }

    [Theory]
    [InlineData("<font>{0.String}</font>")]
    [InlineData("{0.SimName}")]
    [InlineData("# FLAG_VENUE_BEACH")]
    public void Validate_IdenticalNonTranslatableStructure_DoesNotFlagAsIdentical(string text)
    {
        var issues = _sut.Validate(text, text);

        Assert.DoesNotContain(issues, i => i.Code == ValidationIssueCode.IdenticalToSource);
    }

    [Fact]
    public void Validate_TranslationMuchShorterThanSource_ReturnsLengthDiscrepancyWarning()
    {
        var issues = _sut.Validate("This is a reasonably long sentence about a Sim's daily life.", "Oi.");

        Assert.Contains(issues, i => i.Code == ValidationIssueCode.LengthDiscrepancy && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_TranslationMuchLongerThanSource_ReturnsLengthDiscrepancyWarning()
    {
        var issues = _sut.Validate("Hi.", "Isto é uma tradução extremamente longa e desproporcional para um texto tão curto quanto o original.");

        Assert.Contains(issues, i => i.Code == ValidationIssueCode.LengthDiscrepancy);
    }

    [Fact]
    public void Validate_BalancedMarkup_ReturnsNoMarkupIssue()
    {
        var issues = _sut.Validate("Click <b>here</b>.", "Clique <b>aqui</b>.");

        Assert.DoesNotContain(issues, i => i.Code == ValidationIssueCode.UnbalancedMarkup);
    }

    [Fact]
    public void Validate_UnbalancedMarkup_ReturnsErrorIssue()
    {
        var issues = _sut.Validate("Click <b>here</b>.", "Clique <b>aqui.");

        Assert.Contains(issues, i => i.Code == ValidationIssueCode.UnbalancedMarkup && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_SourceMarkupAlreadyUnbalanced_TranslationPreservesIt_DoesNotFlagMarkup()
    {
        var issues = _sut.Validate("<font><b>Credits</b>", "<font><b>Créditos</b>");

        Assert.DoesNotContain(issues, i => i.Code == ValidationIssueCode.UnbalancedMarkup);
    }

    [Fact]
    public void Validate_SelfClosingMarkup_IsNotFlaggedAsUnbalanced()
    {
        var issues = _sut.Validate("Insert a <camera/> shot.", "Insira uma cena de <camera/>.");

        Assert.DoesNotContain(issues, i => i.Code == ValidationIssueCode.UnbalancedMarkup);
    }

    [Fact]
    public void Validate_WithTokenRestoreResult_FoldsMissingTokenIssueIn()
    {
        var protectedString = _tokenProtector.Protect("Ask {0.SimFirstName} to stay.");
        var restoreResult = _tokenProtector.Restore("Pedir para ficar.", protectedString);

        var issues = _sut.Validate(protectedString.OriginalText, restoreResult.RestoredText, restoreResult);

        Assert.Contains(issues, i => i.Code == ValidationIssueCode.MissingToken && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public void Validate_WithReorderedTokensOnly_DoesNotSurfaceReorderingAsIssue()
    {
        var protectedString = _tokenProtector.Protect("{0.SimFirstName} and {1.SimLastName}");
        var restoreResult = _tokenProtector.Restore("__TS4_TOKEN_0002__ e __TS4_TOKEN_0001__", protectedString);

        var issues = _sut.Validate(protectedString.OriginalText, restoreResult.RestoredText, restoreResult);

        Assert.Empty(issues);
    }
}
