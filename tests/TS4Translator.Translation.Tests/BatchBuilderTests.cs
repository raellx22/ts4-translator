using TS4Translator.Core.Translation;

namespace TS4Translator.Translation.Tests;

public class BatchBuilderTests
{
    [Fact]
    public void Build_EmptyList_ReturnsNoBatches()
    {
        var sut = new BatchBuilder();

        var batches = sut.Build([]);

        Assert.Empty(batches);
    }

    [Fact]
    public void Build_FewItemsUnderBothLimits_ReturnsSingleBatch()
    {
        var sut = new BatchBuilder(maxItemsPerBatch: 100, maxCharactersPerBatch: 8000);
        var items = new[] { new TranslationSourceItem("1", "a"), new TranslationSourceItem("2", "b") };

        var batches = sut.Build(items);

        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void Build_MoreItemsThanMaxItemsPerBatch_SplitsByCount()
    {
        var sut = new BatchBuilder(maxItemsPerBatch: 2, maxCharactersPerBatch: 8000);
        var items = new[]
        {
            new TranslationSourceItem("1", "a"),
            new TranslationSourceItem("2", "b"),
            new TranslationSourceItem("3", "c"),
        };

        var batches = sut.Build(items);

        Assert.Equal(2, batches.Count);
        Assert.Equal(2, batches[0].Count);
        Assert.Single(batches[1]);
    }

    [Fact]
    public void Build_TotalCharactersExceedLimit_SplitsByCharacterCount()
    {
        var sut = new BatchBuilder(maxItemsPerBatch: 100, maxCharactersPerBatch: 10);
        var items = new[]
        {
            new TranslationSourceItem("1", "1234567"),
            new TranslationSourceItem("2", "1234567"),
        };

        var batches = sut.Build(items);

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void Build_SingleItemLongerThanCharacterLimit_BecomesItsOwnBatchRatherThanBeingSplit()
    {
        var sut = new BatchBuilder(maxItemsPerBatch: 100, maxCharactersPerBatch: 5);
        var longText = new string('x', 50);
        var items = new[] { new TranslationSourceItem("1", longText) };

        var batches = sut.Build(items);

        var batch = Assert.Single(batches);
        var only = Assert.Single(batch);
        Assert.Equal(longText, only.Text);
    }

    [Fact]
    public void Build_OversizedItemFollowedByNormalItems_OversizedItemDoesNotBlockTheRest()
    {
        var sut = new BatchBuilder(maxItemsPerBatch: 100, maxCharactersPerBatch: 5);
        var items = new[]
        {
            new TranslationSourceItem("1", new string('x', 50)),
            new TranslationSourceItem("2", "ab"),
            new TranslationSourceItem("3", "cd"),
        };

        var batches = sut.Build(items);

        Assert.Equal(2, batches.Count);
        Assert.Single(batches[0]);
        Assert.Equal(2, batches[1].Count);
    }
}
