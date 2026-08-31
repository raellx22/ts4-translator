using System.Text.RegularExpressions;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Interchange;

/// <summary>How a returned translation was tied back to a string in the original bundle.</summary>
public enum BundleMatchStrategy
{
    /// <summary>The reply kept the <c>#id</c> line. Authoritative.</summary>
    ById,

    /// <summary>The id was gone but the <c>EN:</c> line still matched exactly one string that was sent.</summary>
    ByOriginalText,

    /// <summary>Neither id nor original survived, but the reply has exactly as many translations as were sent, in order.</summary>
    ByPosition,
}

public enum BundleProblemSeverity
{
    /// <summary>Worth knowing, nothing was lost. Never a reason to distrust the import.</summary>
    Info,

    /// <summary>Something came back other than expected and a human should look at that string.</summary>
    Warning,

    /// <summary>Part or all of the reply could not be applied.</summary>
    Error,
}

/// <summary>
/// What went wrong, as a machine-readable category. The UI groups by this, so a hundred instances of the
/// same thing read as one line with a count instead of a hundred lines of noise.
/// </summary>
public enum BundleProblemKind
{
    EmptyReply,
    NoRecords,
    UnknownId,
    DuplicateId,
    CountMismatch,
    MissingStrings,
    NoTranslation,
    SameAsOriginal,
    PlaceholdersLost,
    GlossaryMissed,
    DraftKept,
    MatchedWithoutId,
    MatchedByPosition,
}

/// <param name="Severity">How much this should worry the user.</param>
/// <param name="Kind">What category of problem this is, for grouping.</param>
/// <param name="Message">Ready-to-show explanation.</param>
/// <param name="StringId">Which project string this is about, when it is about one.</param>
public sealed record BundleProblem(
    BundleProblemSeverity Severity,
    BundleProblemKind Kind,
    string Message,
    int? StringId = null);

/// <param name="Id">Which project string this translation belongs to.</param>
/// <param name="Translation">The translated text, unescaped.</param>
/// <param name="Strategy">How it was matched — anything other than <see cref="BundleMatchStrategy.ById"/> deserves a look.</param>
/// <param name="PlaceholdersMatch">Whether the placeholders of the original all survived.</param>
/// <param name="GlossaryRespected">Whether every glossary term that applies to this string was honoured.</param>
public sealed record BundleTranslation(
    int Id,
    string Translation,
    BundleMatchStrategy Strategy,
    bool PlaceholdersMatch,
    bool GlossaryRespected = true)
{
    /// <summary>Whether this can be stored as finished work rather than parked for a human to check.</summary>
    public bool IsTrustworthy => Strategy == BundleMatchStrategy.ById && PlaceholdersMatch && GlossaryRespected;
}

/// <param name="Translations">Everything that could be matched and is worth applying.</param>
/// <param name="Problems">What the reply got wrong, in the order it matters.</param>
public sealed record BundleImportResult(
    IReadOnlyList<BundleTranslation> Translations,
    IReadOnlyList<BundleProblem> Problems)
{
    public bool HasErrors => Problems.Any(problem => problem.Severity == BundleProblemSeverity.Error);

    public int PlaceholderMismatchCount => Translations.Count(translation => !translation.PlaceholdersMatch);

    public int GlossaryMissCount => Translations.Count(translation => !translation.GlossaryRespected);

    public int ErrorCount => Problems.Count(problem => problem.Severity == BundleProblemSeverity.Error);

    public int WarningCount => Problems.Count(problem => problem.Severity == BundleProblemSeverity.Warning);

    public int InfoCount => Problems.Count(problem => problem.Severity == BundleProblemSeverity.Info);

    /// <summary>
    /// One line that says how the import went, honestly. Errors and warnings are named; observations are
    /// counted but not dressed up as trouble, because an import where 128 tuning identifiers came back
    /// unchanged is a clean import, and telling the user otherwise is how a real problem gets missed.
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Translations.Count} tradução(ões) aplicada(s)" };

            if (ErrorCount > 0)
            {
                parts.Add($"{ErrorCount} erro(s)");
            }

            parts.Add(WarningCount > 0 ? $"{WarningCount} aviso(s)" : "nenhum aviso");

            if (InfoCount > 0)
            {
                parts.Add($"{InfoCount} observação(ões)");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Parses what a chat model gives back and matches it against the bundle that was sent.
/// </summary>
/// <remarks>
/// Written to be forgiving about presentation and strict about identity. A model will wrap the answer in
/// a code fence, bold a label, add a sentence before and after, or drop the id lines entirely — none of
/// that should lose the user's work. What it must never do is guess which translation belongs to which
/// string: every fallback beyond the id is reported, so the user knows how much to trust the result.
///
/// Severity is chosen by what the source string actually is, not by whether the text changed. A tuning
/// identifier such as <c>debug_savedActor2</c> coming back identical is the correct answer and gets an
/// observation; a sentence coming back identical is a warning. Before that distinction existed, one real
/// miss in a thousand strings arrived buried under 133 warnings about identifiers, which is the same as
/// not reporting it at all. See <see cref="StringTranslatability"/>.
/// </remarks>
public static partial class TranslationBundleReader
{
    [GeneratedRegex(@"^\s*#\s*(\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdLinePattern { get; }

    /// <summary>Placeholders and markup that must survive translation.</summary>
    [GeneratedRegex(@"\{[^{}]*\}|<[^<>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern { get; }

    /// <summary>Leading decoration a model adds around labels: bullets, quotes, bold, numbering.</summary>
    [GeneratedRegex(@"^[\s>*\-•]*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingDecorationPattern { get; }

    public static BundleImportResult Read(string? reply, TranslationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var problems = new List<BundleProblem>();

        if (string.IsNullOrWhiteSpace(reply))
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Error, BundleProblemKind.EmptyReply, "O arquivo está vazio."));
            return new BundleImportResult([], problems);
        }

        var records = ParseRecords(reply);
        if (records.Count == 0)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Error, BundleProblemKind.NoRecords,
                $"Nenhuma linha \"{TranslationBundleWriter.TranslationPrefix}\" foi encontrada. Confira se você colou a resposta inteira da IA, incluindo os blocos de cada texto."));
            return new BundleImportResult([], problems);
        }

        var byId = bundle.Items.ToDictionary(item => item.Id);

        // Only originals that are unique in the bundle can identify a string on their own; a repeated
        // source string would otherwise be matched arbitrarily.
        var uniqueOriginals = bundle.Items
            .GroupBy(item => item.Original, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

        var translations = new List<BundleTranslation>();
        var seenIds = new HashSet<int>();
        var unmatched = new List<ParsedRecord>();

        foreach (var record in records)
        {
            if (record.Id is int id)
            {
                if (!byId.ContainsKey(id))
                {
                    problems.Add(new BundleProblem(BundleProblemSeverity.Warning, BundleProblemKind.UnknownId,
                        $"A resposta trouxe o id #{id}, que não estava no arquivo enviado. Ignorado.", id));
                    continue;
                }

                if (!seenIds.Add(id))
                {
                    problems.Add(new BundleProblem(BundleProblemSeverity.Warning, BundleProblemKind.DuplicateId,
                        $"O id #{id} apareceu mais de uma vez na resposta. A primeira ocorrência foi usada.", id));
                    continue;
                }

                AddTranslation(translations, problems, byId[id], record.Translation, BundleMatchStrategy.ById, bundle.Glossary);
                continue;
            }

            // Id gone - the case the user asked to be defended against. The original text is the next
            // best anchor.
            if (record.Original is not null && uniqueOriginals.TryGetValue(record.Original, out var matchedId) && seenIds.Add(matchedId))
            {
                AddTranslation(translations, problems, byId[matchedId], record.Translation, BundleMatchStrategy.ByOriginalText, bundle.Glossary);
                continue;
            }

            unmatched.Add(record);
        }

        if (unmatched.Count > 0)
        {
            ResolveByPosition(unmatched, bundle, byId, seenIds, translations, problems);
        }

        ReportCoverage(bundle, seenIds, translations, problems);

        return new BundleImportResult(translations, problems);
    }

    /// <summary>
    /// Last resort when a reply carries no ids and no recognisable originals: if it has exactly as many
    /// translations as were sent, they are taken in order.
    /// </summary>
    private static void ResolveByPosition(
        List<ParsedRecord> unmatched,
        TranslationBundle bundle,
        Dictionary<int, BundleItem> byId,
        HashSet<int> seenIds,
        List<BundleTranslation> translations,
        List<BundleProblem> problems)
    {
        var remaining = bundle.Items.Where(item => !seenIds.Contains(item.Id)).ToList();

        if (unmatched.Count != remaining.Count)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Error, BundleProblemKind.CountMismatch,
                $"A resposta veio sem as linhas \"{TranslationBundleWriter.IdPrefix}numero\" e traz {unmatched.Count} tradução(ões) para {remaining.Count} texto(s) que faltavam. " +
                "Sem os ids e com quantidades diferentes não há como saber qual tradução é de qual texto - peça à IA para responder mantendo as linhas de id."));
            return;
        }

        problems.Add(new BundleProblem(BundleProblemSeverity.Warning, BundleProblemKind.MatchedByPosition,
            $"A resposta veio sem as linhas \"{TranslationBundleWriter.IdPrefix}numero\". Como o número de traduções bate exatamente com o de textos enviados, " +
            "elas foram associadas pela ordem. Confira o resultado antes de exportar o package."));

        for (var index = 0; index < unmatched.Count; index++)
        {
            var item = remaining[index];
            seenIds.Add(item.Id);
            AddTranslation(translations, problems, byId[item.Id], unmatched[index].Translation, BundleMatchStrategy.ByPosition, bundle.Glossary);
        }
    }

    private static void AddTranslation(
        List<BundleTranslation> translations,
        List<BundleProblem> problems,
        BundleItem item,
        string? translation,
        BundleMatchStrategy strategy,
        IReadOnlyList<GlossaryTerm>? glossary)
    {
        var text = BundleText.Unescape(translation).Trim();

        if (text.Length == 0)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Info, BundleProblemKind.NoTranslation,
                $"#{item.Id} voltou sem tradução e foi deixado como está.", item.Id));
            return;
        }

        if (string.Equals(text, item.Original, StringComparison.Ordinal))
        {
            ReportUnchanged(problems, item, text);
        }
        else if (item.CurrentTranslation is { Length: > 0 } draft && string.Equals(text, draft.Trim(), StringComparison.Ordinal))
        {
            // The user sends drafts precisely to have them improved. A reply that hands every draft back
            // untouched looks like a success and is not one, and nothing else in the app would say so.
            problems.Add(new BundleProblem(BundleProblemSeverity.Info, BundleProblemKind.DraftKept,
                $"#{item.Id} voltou igual ao rascunho enviado (\"{Preview(text)}\"). A IA não mudou nada nesta linha.", item.Id));
        }

        var lost = MissingPlaceholders(item.Original, text);
        if (lost.Count > 0)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Warning, BundleProblemKind.PlaceholdersLost,
                $"#{item.Id} perdeu ou alterou {string.Join(", ", lost)}. Vai entrar marcado para revisão.", item.Id));
        }

        var missedTerms = MissedGlossaryTerms(item.Original, text, glossary);
        if (missedTerms.Count > 0)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Warning, BundleProblemKind.GlossaryMissed,
                $"#{item.Id} não respeitou o glossário: {string.Join("; ", missedTerms)}. Vai entrar marcado para revisão.", item.Id));
        }

        translations.Add(new BundleTranslation(item.Id, text, strategy, lost.Count == 0, missedTerms.Count == 0));
    }

    /// <summary>
    /// Checks the terminology the user said was non-negotiable. Presence, not position: a glossary term
    /// lands wherever the target language puts it, and demanding a location would flag every correct
    /// translation. A term marked "never translate" has to survive verbatim; any other has to have its
    /// required translation somewhere in the line.
    /// </summary>
    private static List<string> MissedGlossaryTerms(string original, string translation, IReadOnlyList<GlossaryTerm>? glossary)
    {
        if (glossary is not { Count: > 0 })
        {
            return [];
        }

        var missed = new List<string>();
        foreach (var term in glossary)
        {
            if (!term.AppearsIn(original))
            {
                continue;
            }

            var comparison = term.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var required = term.NeverTranslate ? term.Original : term.Translation;

            if (string.IsNullOrWhiteSpace(required) || translation.Contains(required, comparison))
            {
                continue;
            }

            missed.Add(term.NeverTranslate
                ? $"\"{term.Original}\" devia ter sido mantido sem traduzir"
                : $"\"{term.Original}\" devia virar \"{term.Translation}\"");
        }

        return missed;
    }

    /// <summary>
    /// A string that came back exactly as it was sent. Whether that is a failure depends entirely on what
    /// the string was: see the remarks on <see cref="StringTranslatability"/>.
    /// </summary>
    private static void ReportUnchanged(List<BundleProblem> problems, BundleItem item, string text)
    {
        var (severity, explanation) = StringTranslatability.Classify(item.Original) switch
        {
            StringKind.Prose => (BundleProblemSeverity.Warning, "É uma frase inteira, então isso quase certamente é falha de tradução - a IA pode ter pulado ou recusado esta linha."),
            StringKind.Term => (BundleProblemSeverity.Info, "É um termo curto ou nome próprio, que muitas vezes fica igual mesmo traduzido."),
            _ => (BundleProblemSeverity.Info, "É um identificador interno do mod, que não deve mesmo ser traduzido."),
        };

        problems.Add(new BundleProblem(severity, BundleProblemKind.SameAsOriginal,
            $"#{item.Id} voltou igual ao original (\"{Preview(text)}\"). {explanation}", item.Id));
    }

    private static void ReportCoverage(
        TranslationBundle bundle,
        HashSet<int> seenIds,
        List<BundleTranslation> translations,
        List<BundleProblem> problems)
    {
        var missing = bundle.Items.Count(item => !seenIds.Contains(item.Id));
        if (missing > 0)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Warning, BundleProblemKind.MissingStrings,
                $"{missing} de {bundle.Items.Count} texto(s) não voltaram na resposta e continuam como estavam. " +
                "Modelos de chat costumam cortar respostas longas - reduza o tamanho das partes na exportação se isso se repetir."));
        }

        var byStrategy = translations.CountBy(translation => translation.Strategy).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (byStrategy.TryGetValue(BundleMatchStrategy.ByOriginalText, out var byText) && byText > 0)
        {
            problems.Add(new BundleProblem(BundleProblemSeverity.Info, BundleProblemKind.MatchedWithoutId,
                $"{byText} tradução(ões) foram identificadas pelo texto original porque a IA removeu o id."));
        }
    }

    /// <summary>
    /// Every placeholder in the source has to appear in the translation. Order and position are free -
    /// a different language puts the name somewhere else - but nothing may vanish or be reworded. What
    /// went missing is returned rather than a bare true/false, so the user is told which marker to look
    /// for instead of being sent to hunt through the line.
    /// </summary>
    private static List<string> MissingPlaceholders(string original, string translation)
    {
        var expected = PlaceholderPattern.Matches(original).Select(match => match.Value).ToList();
        if (expected.Count == 0)
        {
            return [];
        }

        var actual = PlaceholderPattern.Matches(translation).Select(match => match.Value).ToList();

        var lost = new List<string>();
        foreach (var placeholder in expected)
        {
            if (!actual.Remove(placeholder))
            {
                lost.Add(placeholder);
            }
        }

        return lost;
    }

    private static List<ParsedRecord> ParseRecords(string reply)
    {
        var records = new List<ParsedRecord>();
        ParsedRecord? current = null;

        foreach (var rawLine in Normalize(reply))
        {
            var line = LeadingDecorationPattern.Replace(rawLine, string.Empty).TrimEnd();

            if (IdLinePattern.Match(line) is { Success: true } idMatch)
            {
                Flush(records, ref current);
                current = new ParsedRecord { Id = int.Parse(idMatch.Groups[1].ValueSpan, provider: null) };
                continue;
            }

            if (TryReadField(line, TranslationBundleWriter.OriginalPrefix, out var original))
            {
                current ??= new ParsedRecord();
                current.Original = BundleText.Unescape(original);
                continue;
            }

            if (TryReadField(line, TranslationBundleWriter.TranslationPrefix, out var translation))
            {
                current ??= new ParsedRecord();
                current.Translation = translation;

                // PT: closes a record. Anything after it belongs to the next one.
                Flush(records, ref current);
                continue;
            }

            // ATUAL: and any prose the model added are deliberately ignored.
        }

        Flush(records, ref current);
        return records;
    }

    private static void Flush(List<ParsedRecord> records, ref ParsedRecord? current)
    {
        // A record only matters once it carries a translation line; a stray id or EN with no PT is the
        // model echoing the prompt back, not an answer.
        if (current is { Translation: not null })
        {
            records.Add(current);
        }

        current = null;
    }

    private static bool TryReadField(string line, string prefix, out string value)
    {
        // Tolerates "PT:", "pt:", "**PT:**" (after decoration stripping) and a space before the colon.
        var separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator > 0)
        {
            var label = StripBold(line[..separator]);
            if (label.Equals(prefix.TrimEnd(':'), StringComparison.OrdinalIgnoreCase))
            {
                // "**PT:**" leaves the closing "**" glued to the front of the value.
                value = StripBold(line[(separator + 1)..]);
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Removes the markdown emphasis a model wraps around labels. Only paired <c>**</c> is stripped, so a
    /// translation that legitimately starts or ends with a single asterisk survives intact.
    /// </summary>
    private static string StripBold(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("**", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.EndsWith("**", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2];
        }

        return trimmed.Trim();
    }

    /// <summary>
    /// Splits into lines and drops the decoration a chat model wraps around a file: code fences, and
    /// anything outside the begin/end markers when both are present.
    /// </summary>
    private static IEnumerable<string> Normalize(string reply)
    {
        var lines = reply.ReplaceLineEndings("\n").Split('\n');

        var start = 0;
        var end = lines.Length;

        var beginIndex = Array.FindIndex(lines, line => line.Contains(TranslationBundleWriter.BeginMarker, StringComparison.OrdinalIgnoreCase));
        if (beginIndex >= 0)
        {
            start = beginIndex + 1;
        }

        var endIndex = Array.FindLastIndex(lines, line => line.Contains(TranslationBundleWriter.EndMarker, StringComparison.OrdinalIgnoreCase));
        if (endIndex > start)
        {
            end = endIndex;
        }

        for (var index = start; index < end; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;
        }
    }

    private static string Preview(string text) => text.Length <= 40 ? text : text[..40] + "...";

    private sealed class ParsedRecord
    {
        public int? Id { get; set; }

        public string? Original { get; set; }

        public string? Translation { get; set; }
    }
}
