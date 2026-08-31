using System.Globalization;
using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Interchange;

/// <summary>
/// Cuts one selection of strings into the files the user will actually paste into a chat window.
/// </summary>
/// <remarks>
/// The point of this type is a guarantee, not an algorithm: every string handed in comes out in exactly
/// one part, in the order it was given, and the parts concatenated are the input. That is what makes
/// "exportar a tradução do mod inteiro" safe to trust — the previous flow made the user tick rows five
/// hundred at a time and hope nothing was missed, which is precisely the kind of bookkeeping a person
/// should never be asked to do by hand.
///
/// Splitting is a concession to chat models, not a preference: a reply longer than the model's output
/// budget comes back truncated, and the strings past the cut are silently absent. When the selection
/// fits, it stays one file.
/// </remarks>
public static class BundleSplitter
{
    /// <summary>
    /// Splits <paramref name="items"/> into bundles of at most <paramref name="maximumPerPart"/> strings.
    /// A value of zero or less means "one file, whatever the size".
    /// </summary>
    public static IReadOnlyList<TranslationBundle> Split(
        string projectName,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<BundleItem> items,
        int maximumPerPart,
        IReadOnlyList<GlossaryTerm>? glossary = null,
        string? customInstructions = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return [];
        }

        var size = maximumPerPart > 0 ? maximumPerPart : items.Count;
        var partCount = (items.Count + size - 1) / size;

        var parts = new List<TranslationBundle>(partCount);
        for (var index = 0; index < partCount; index++)
        {
            var slice = items.Skip(index * size).Take(size).ToArray();

            // Glossary and instructions ride along with every part: each file is pasted into its own
            // chat window, so a part that carries neither is translated with neither.
            parts.Add(new TranslationBundle(
                projectName, sourceLocale, targetLocale, slice, index + 1, partCount, glossary, customInstructions));
        }

        return parts;
    }

    /// <summary>
    /// The file name for one part. A single-part export keeps the name the user chose; a split one gets
    /// a suffix that sorts and reads correctly, so the folder itself shows whether a part is missing.
    /// </summary>
    public static string NameFor(string chosenPath, TranslationBundle part)
    {
        ArgumentNullException.ThrowIfNull(part);

        if (!part.IsSplit)
        {
            return chosenPath;
        }

        var directory = Path.GetDirectoryName(chosenPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(chosenPath);
        var extension = Path.GetExtension(chosenPath);

        // Zero-padded so parte02 sorts before parte10 in Explorer, which is where the user will be
        // picking the next file to paste.
        var width = part.PartCount.ToString(CultureInfo.InvariantCulture).Length;
        var number = part.PartNumber.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

        return Path.Combine(directory, $"{stem}_parte{number}de{part.PartCount}{extension}");
    }
}
