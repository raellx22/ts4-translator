using System.Globalization;
using System.Text;
using TS4Translator.Core.Packages;

namespace TS4Translator.Core.Interchange;

/// <param name="SourceLocale">Language the originals are in.</param>
/// <param name="TargetLocale">Language the translations are in.</param>
/// <param name="CreatedAtUtc">When the pack was built.</param>
/// <param name="Pairs">Every translation, each carrying the package it came from.</param>
public sealed record TranslationPack(
    CultureInfo SourceLocale,
    CultureInfo TargetLocale,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<AlignedTranslation> Pairs)
{
    /// <summary>Everyone whose work is in here, by the package name they published under.</summary>
    public IReadOnlyList<string> Credits =>
        [.. Pairs.Select(pair => pair.SourcePackage)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)];
}

/// <summary>
/// Reads and writes a translation pack: finished translations as a portable, attributed file.
/// </summary>
/// <remarks>
/// The distributable form of what <see cref="IAlignedTranslationHarvester"/> recovers. Harvesting needs
/// the mods <b>and</b> the translation packages sitting on the same machine; a pack is the result,
/// carried on its own, so it can travel with an installer or between people who do not both have a
/// 15.000-package library.
///
/// <para><b>Attribution is per row, not a header.</b> Every pair names the translation package it came
/// from, which survives into translation memory as the entry's profile name. That means a translator's
/// work is credited on the row wherever it later shows up, and one translator's contribution can be
/// found and removed without touching anyone else's — the two things a credits list at the top of a file
/// cannot do. This is what makes shipping other people's translations something the app can be honest
/// about rather than quiet about.</para>
///
/// <para>Plain TSV with <c>#</c> comments, deliberately: three megabytes is nothing, and a format a
/// person can open, read, diff and hand-edit is worth more here than a compact one. The comment block is
/// informational — the reader derives everything real from the rows.</para>
/// </remarks>
public static class TranslationPackFile
{
    private const string Magic = "# TS4Translator translation pack v1";

    public static async Task WriteAsync(string path, TranslationPack pack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var builder = new StringBuilder();
        builder.AppendLine(Magic);
        builder.AppendLine(CultureInfo.InvariantCulture, $"# de: {pack.SourceLocale.Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"# para: {pack.TargetLocale.Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"# gerado: {pack.CreatedAtUtc:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"# traduções: {pack.Pairs.Count}");
        builder.AppendLine("#");
        builder.AppendLine("# Este arquivo reúne traduções feitas pela comunidade. Cada linha diz de qual");
        builder.AppendLine("# pacote ela veio, e esse crédito acompanha a tradução dentro do aplicativo.");
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Pacotes representados: {pack.Credits.Count}");

        foreach (var credit in pack.Credits)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"#   {credit}");
        }

        builder.AppendLine("#");
        builder.AppendLine("# origem\toriginal\ttradução");

        foreach (var pair in pack.Pairs)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{Escape(pair.SourcePackage)}\t{Escape(pair.OriginalText)}\t{Escape(pair.TranslatedText)}");
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <exception cref="InvalidDataException">The file is not a translation pack.</exception>
    public static async Task<TranslationPack> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);

        if (lines.Length == 0 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"\"{Path.GetFileName(path)}\" não é um pacote de traduções do TS4 Translator.");
        }

        var source = CultureInfo.GetCultureInfo(HeaderValue(lines, "# de:") ?? "en-US");
        var target = CultureInfo.GetCultureInfo(HeaderValue(lines, "# para:") ?? "pt-BR");
        var created = DateTimeOffset.TryParse(HeaderValue(lines, "# gerado:"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.UtcNow;

        var pairs = new List<AlignedTranslation>(lines.Length);
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length != 3 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                continue;
            }

            pairs.Add(new AlignedTranslation(Unescape(parts[1]), Unescape(parts[2]), Unescape(parts[0])));
        }

        return new TranslationPack(source, target, created, pairs);
    }

    private static string? HeaderValue(string[] lines, string prefix) =>
        lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();

    // A game string carries real newlines and the format is line-based, so they travel escaped. Tabs
    // likewise, since a tab is the column separator.
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\\' || index == text.Length - 1)
            {
                builder.Append(text[index]);
                continue;
            }

            index++;
            builder.Append(text[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                var other => other,
            });
        }

        return builder.ToString();
    }
}
