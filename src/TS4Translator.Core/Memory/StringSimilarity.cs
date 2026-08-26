namespace TS4Translator.Core.Memory;

/// <summary>Levenshtein-based string similarity, backing fuzzy translation-memory matching (brief section 26).</summary>
public static class StringSimilarity
{
    /// <summary>Returns a 0.0-1.0 similarity ratio: 1.0 for identical strings, 0.0 for completely different ones.</summary>
    public static double Ratio(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var maxLength = Math.Max(a.Length, b.Length);
        if (maxLength == 0)
        {
            return 1.0;
        }

        var distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var lengthB = b.Length;
        var previousRow = new int[lengthB + 1];
        var currentRow = new int[lengthB + 1];

        for (var j = 0; j <= lengthB; j++)
        {
            previousRow[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            currentRow[0] = i;
            for (var j = 1; j <= lengthB; j++)
            {
                var substitutionCost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[lengthB];
    }
}
