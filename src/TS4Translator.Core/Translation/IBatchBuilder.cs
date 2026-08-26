namespace TS4Translator.Core.Translation;

/// <summary>
/// Groups translation items into batches sized to stay under provider limits. A single item is
/// never split across batches — if it alone exceeds the configured character limit, it becomes its
/// own single-item batch rather than being truncated.
/// </summary>
public interface IBatchBuilder
{
    IReadOnlyList<IReadOnlyList<TranslationSourceItem>> Build(IReadOnlyList<TranslationSourceItem> items);
}
