using TS4Translator.Core.Translation;

namespace TS4Translator.Translation;

/// <summary>Default <see cref="IBatchBuilder"/>: greedy fill by item count and total character count.</summary>
public sealed class BatchBuilder(int maxItemsPerBatch = 100, int maxCharactersPerBatch = 8000) : IBatchBuilder
{
    public IReadOnlyList<IReadOnlyList<TranslationSourceItem>> Build(IReadOnlyList<TranslationSourceItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var batches = new List<IReadOnlyList<TranslationSourceItem>>();
        var current = new List<TranslationSourceItem>();
        var currentCharacters = 0;

        foreach (var item in items)
        {
            var itemLength = item.Text.Length;
            var wouldExceedItemLimit = current.Count >= maxItemsPerBatch;
            var wouldExceedCharLimit = current.Count > 0 && currentCharacters + itemLength > maxCharactersPerBatch;

            if (wouldExceedItemLimit || wouldExceedCharLimit)
            {
                batches.Add(current);
                current = [];
                currentCharacters = 0;
            }

            current.Add(item);
            currentCharacters += itemLength;
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }
}
