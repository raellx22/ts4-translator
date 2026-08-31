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

    /// <summary>
    /// Returns the ratio only when it can meet <paramref name="minimumRatio"/>. Uses a narrow
    /// Levenshtein band, which is substantially cheaper than the full matrix for near-exact
    /// translation-memory matching.
    /// </summary>
    public static double? RatioAtLeast(string a, string b, double minimumRatio)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumRatio, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumRatio, 1.0);

        var maxLength = Math.Max(a.Length, b.Length);
        if (maxLength == 0)
        {
            return 1.0;
        }

        var maximumDistance = (int)Math.Floor((1.0 - minimumRatio) * maxLength + 1e-9);
        if (Math.Abs(a.Length - b.Length) > maximumDistance)
        {
            return null;
        }

        var distance = BoundedLevenshteinDistance(a, b, maximumDistance);
        return distance > maximumDistance ? null : 1.0 - (double)distance / maxLength;
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

    private static int BoundedLevenshteinDistance(string a, string b, int maximumDistance)
    {
        var lengthB = b.Length;
        var beyondLimit = maximumDistance + 1;
        var previousRow = new int[lengthB + 1];
        var currentRow = new int[lengthB + 1];
        Array.Fill(previousRow, beyondLimit);
        for (var j = 0; j <= Math.Min(lengthB, maximumDistance); j++)
        {
            previousRow[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            Array.Fill(currentRow, beyondLimit);
            if (i <= maximumDistance)
            {
                currentRow[0] = i;
            }

            var start = Math.Max(1, i - maximumDistance);
            var end = Math.Min(lengthB, i + maximumDistance);
            for (var j = start; j <= end; j++)
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
