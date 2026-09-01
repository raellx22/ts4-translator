using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// Procura candidatos a "nunca traduzir" nos mods que o usuário já traduziu.
//
// O sinal é retenção: com que frequência a palavra inglesa continua, literal, na
// tradução. Alta retenção com baixa "nativa" (a palavra não é portuguesa) é a
// assinatura de um nome próprio que o motor já reconhece como nome — e cada
// ocorrência em que ele NÃO reteve é uma corrupção que ninguém viu.
//
// Uma primeira métrica creditava a falha de cada string a todas as suas palavras
// e devolveu vocabulário comum (Sex, Bed, Bar): a string falhava por outro motivo
// e a palavra só estava junto. Retenção mede a palavra.
// ---------------------------------------------------------------------------

var dir = @"C:\Users\israe\AppData\Local\TS4Translator\Data";
var snapshot = Path.Combine(Path.GetTempPath(), "ts4mine");
Directory.CreateDirectory(snapshot);
foreach (var name in new[] { "ts4translator.db", "ts4translator.db-wal", "ts4translator.db-shm" })
{
    var from = Path.Combine(dir, name);
    if (File.Exists(from))
    {
        File.Copy(from, Path.Combine(snapshot, name), true);
    }
}

using var connection = new SqliteConnection($"Data Source={Path.Combine(snapshot, "ts4translator.db")}");
connection.Open();

var rows = new List<(string Project, string Original, string Translated)>();
using (var command = connection.CreateCommand())
{
    command.CommandText = """
        SELECT pr.Name, p.OriginalText, p.TranslatedText
        FROM ProjectStrings p JOIN Projects pr ON pr.Id = p.ProjectId
        WHERE p.TranslatedText IS NOT NULL
        """;
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }
}

// Marcadores e markup são copiados literalmente por definição; contá-los mediria a mecânica do
// TokenProtector, não o vocabulário — foi o que fez "font" e "color" aparecerem numa tentativa anterior.
var noise = new Regex(@"\{[^{}]*\}|<[^<>]+>", RegexOptions.CultureInvariant);
static IEnumerable<Match> WordMatches(string text) =>
    Regex.Matches(text, @"[A-Za-z][A-Za-z0-9'’-]{2,}", RegexOptions.CultureInvariant);

var stats = new Dictionary<string, Term>(StringComparer.OrdinalIgnoreCase);
var native = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

foreach (var (project, rawOriginal, rawTranslated) in rows)
{
    var original = noise.Replace(rawOriginal, " ");
    var translated = noise.Replace(rawTranslated, " ");

    if (string.Equals(original.Trim(), translated.Trim(), StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var originalWords = new HashSet<string>(WordMatches(original).Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
    var translatedWords = new HashSet<string>(WordMatches(translated).Select(m => m.Value), StringComparer.OrdinalIgnoreCase);

    // Palavra que surge numa tradução sem estar no original é portuguesa também ("normal", "menu",
    // "total"). Retenção alta nessas não diz nada, e sem este filtro elas dominam a lista.
    foreach (var word in translatedWords.Where(word => !originalWords.Contains(word)))
    {
        native[word] = native.GetValueOrDefault(word) + 1;
    }

    foreach (var match in WordMatches(original).DistinctBy(m => m.Value, StringComparer.OrdinalIgnoreCase))
    {
        var word = match.Value;
        if (!stats.TryGetValue(word, out var term))
        {
            stats[word] = term = new Term(word);
        }

        term.Total++;
        term.Projects.Add(project);

        // Maiúscula no meio da frase é o sinal clássico de nome próprio em inglês. Sozinho não decide,
        // mas separa "Sims" de "settings" quando os dois têm retenção alta.
        if (match.Index > 0 && char.IsUpper(word[0]) && !IsSentenceStart(original, match.Index))
        {
            term.CapitalizedMidSentence++;
        }

        if (translatedWords.Contains(word))
        {
            term.Kept++;
        }
        else if (term.CorruptionExamples.Count < 2 && rawOriginal.Length < 70)
        {
            term.CorruptionExamples.Add((rawOriginal, rawTranslated));
        }
    }
}

foreach (var term in stats.Values)
{
    term.Native = native.GetValueOrDefault(term.Word);
}

static bool IsSentenceStart(string text, int index)
{
    for (var i = index - 1; i >= 0; i--)
    {
        if (char.IsWhiteSpace(text[i]))
        {
            continue;
        }

        return text[i] is '.' or '!' or '?' or ':' or '\n';
    }

    return true;
}

var candidates = stats.Values
    .Where(term => term.Total >= 40
        && term.KeepRate >= 0.55
        && term.NativeRatio < 0.03
        && term.CapitalizedRate >= 0.5)
    .OrderByDescending(term => term.Corrupted)
    .ToList();

Console.WriteLine($"{rows.Count:N0} strings traduzidas em {rows.Select(r => r.Project).Distinct().Count()} mods");
Console.WriteLine($"{candidates.Count} candidatos a \"nunca traduzir\"\n");
Console.WriteLine($"{"termo",-22} {"usos",7} {"mods",5} {"retenção",9} {"corrompidas",12} {"maiúsc.",8}");
Console.WriteLine(new string('-', 72));

foreach (var term in candidates.Take(45))
{
    Console.WriteLine($"{term.Word,-22} {term.Total,7} {term.Projects.Count,5} {term.KeepRate,8:P0} {term.Corrupted,12} {term.CapitalizedRate,7:P0}");
}

Console.WriteLine("\n=== evidência de corrupção (as vezes em que NÃO reteve) ===");
foreach (var term in candidates.Where(term => term.Corrupted >= 5).Take(18))
{
    Console.WriteLine($"\n## {term.Word} — {term.Corrupted} de {term.Total}");
    foreach (var (original, translated) in term.CorruptionExamples)
    {
        Console.WriteLine($"   {original}");
        Console.WriteLine($"     -> {translated}");
    }
}

internal sealed class Term(string word)
{
    public string Word { get; } = word;

    public int Total { get; set; }

    public int Kept { get; set; }

    public int Native { get; set; }

    public int CapitalizedMidSentence { get; set; }

    public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Original, string Translated)> CorruptionExamples { get; } = [];

    public int Corrupted => Total - Kept;

    public double KeepRate => Total == 0 ? 0 : (double)Kept / Total;

    public double NativeRatio => Total == 0 ? 0 : (double)Native / Total;

    public double CapitalizedRate => Total == 0 ? 0 : (double)CapitalizedMidSentence / Total;
}
