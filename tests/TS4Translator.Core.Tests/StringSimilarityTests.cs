using TS4Translator.Core.Memory;

namespace TS4Translator.Core.Tests;

public class StringSimilarityTests
{
    [Fact]
    public void Ratio_IdenticalStrings_Returns1()
    {
        var ratio = StringSimilarity.Ratio("Hello there.", "Hello there.");

        Assert.Equal(1.0, ratio);
    }

    [Fact]
    public void Ratio_BothEmpty_Returns1()
    {
        var ratio = StringSimilarity.Ratio(string.Empty, string.Empty);

        Assert.Equal(1.0, ratio);
    }

    [Fact]
    public void Ratio_OneWordDifferentInALongSentence_IsHighButNotNearIdentical()
    {
        // "old" -> "new" is a 3-character substitution in a 24-character sentence.
        var ratio = StringSimilarity.Ratio("Ask about their old job.", "Ask about their new job.");

        Assert.True(ratio is > 0.8 and < 0.95, $"Expected between 0.8 and 0.95, got {ratio}");
    }

    [Fact]
    public void Ratio_SingleTrailingCharacterDifferentInALongSentence_IsNearIdentical()
    {
        // Only the final punctuation differs (37-character sentence, single-character edit).
        var ratio = StringSimilarity.Ratio("Ask your sim to talk about their job.", "Ask your sim to talk about their job!");

        Assert.True(ratio > 0.97, $"Expected > 0.97, got {ratio}");
    }

    [Fact]
    public void Ratio_CompletelyDifferentStrings_IsLow()
    {
        var ratio = StringSimilarity.Ratio("Hello there.", "The quick brown fox jumps.");

        Assert.True(ratio < 0.5, $"Expected < 0.5, got {ratio}");
    }

    [Fact]
    public void Ratio_OneEmptyOneNonEmpty_ReflectsFullDistance()
    {
        var ratio = StringSimilarity.Ratio(string.Empty, "Hello");

        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public void Ratio_IsSymmetric()
    {
        var a = "Ask about their old job.";
        var b = "Ask about their new job.";

        Assert.Equal(StringSimilarity.Ratio(a, b), StringSimilarity.Ratio(b, a));
    }

    [Fact]
    public void RatioAtLeast_NearMatch_ReturnsTheSameRatioAsFullCalculation()
    {
        const string a = "Ask your sim to talk about their job.";
        const string b = "Ask your sim to talk about their job!";

        Assert.Equal(StringSimilarity.Ratio(a, b), StringSimilarity.RatioAtLeast(a, b, 0.97));
    }

    [Fact]
    public void RatioAtLeast_DistantText_ReturnsNull()
    {
        Assert.Null(StringSimilarity.RatioAtLeast("Completely different text.", "Nothing alike is written here.", 0.97));
    }

    [Fact]
    public void RatioAtLeast_LengthDifferenceAlreadyExceedsLimit_ReturnsNull()
    {
        Assert.Null(StringSimilarity.RatioAtLeast("A fairly long source sentence.", "Short.", 0.97));
    }
}
