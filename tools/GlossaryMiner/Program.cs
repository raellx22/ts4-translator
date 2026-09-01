using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using TS4Translator.Core.Translation;

Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// Procura o vocabulário que vale um glossário embutido, usando os mods que o
// usuário já traduziu.
//
// O sinal é a taxa de retenção: com que frequência uma palavra inglesa continua
// aparecendo, literalmente, na tradução. Uma palavra que o motor traduz sempre
// tem retenção baixa e não precisa de glossário. Uma com retenção alta é uma
// das duas coisas que o glossário existe para resolver:
//
//   - jargão que o motor não sabe traduzir  -> vira termo com tradução
//   - nome próprio que ele acerta em manter -> vira termo "nunca traduzir"
//
// Uma primeira tentativa creditou a falha de cada string a todas as suas
// palavras e devolveu vocabulário comum ("Sex", "Bed", "Bar"): a string falhava
// por outro motivo e a palavra só estava junto. Retenção mede a palavra.
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

var rows = new List<Row>();
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
        rows.Add(new Row(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }
}

// Marcadores e markup são copiados literalmente por definição; contá-los mediria a mecânica do
// TokenProtector, não o vocabulário. Foi o que fez "font" e "color" aparecerem na primeira tentativa.
var noise = new Regex(@"\{[^{}]*\}|<[^<>]+>", RegexOptions.CultureInvariant);
static IEnumerable<string> Words(string text) =>
    Regex.Matches(text, @"[A-Za-z][A-Za-z'-]{2,}", RegexOptions.CultureInvariant).Select(match => match.Value);

var stats = new Dictionary<string, Term>(StringComparer.OrdinalIgnoreCase);
var nativeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

foreach (var row in rows)
{
    var original = noise.Replace(row.Original, " ");
    var translated = noise.Replace(row.Translated, " ");

    // Uma tradução idêntica ao original não diz nada sobre palavra nenhuma - a linha inteira falhou.
    if (string.Equals(original.Trim(), translated.Trim(), StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var translatedWords = new HashSet<string>(Words(translated), StringComparer.OrdinalIgnoreCase);
    var originalWords = new HashSet<string>(Words(original), StringComparer.OrdinalIgnoreCase);
    // Uma palavra que aparece em traduções cujo original não a continha é portuguesa também
    // ("normal", "total", "menu", "bar"). Retenção alta nessas não significa nada, e sem este
    // discriminador elas dominam a lista. Contado à parte porque o termo pode ainda não existir em
    // `stats` quando a primeira ocorrência nativa aparece.
    foreach (var word in translatedWords.Where(word => !originalWords.Contains(word)))
    {
        nativeCounts[word] = nativeCounts.GetValueOrDefault(word) + 1;
    }

    var isLabel = StringTranslatability.Classify(row.Original) == StringKind.Term;

    foreach (var word in Words(original).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!stats.TryGetValue(word, out var term))
        {
            stats[word] = term = new Term(word);
        }

        term.Total++;
        term.Projects.Add(row.Project);

        if (translatedWords.Contains(word))
        {
            term.Kept++;
            if (isLabel && term.KeptExamples.Count < 3 && row.Original.Length < 50)
            {
                term.KeptExamples.Add((row.Original, row.Translated));
            }
        }
        else if (term.TranslatedExamples.Count < 3 && row.Original.Length < 50)
        {
            term.TranslatedExamples.Add((row.Original, row.Translated));
        }
    }
}

foreach (var term in stats.Values)
{
    term.AppearedNatively = nativeCounts.GetValueOrDefault(term.Word);
}

var ranked = stats.Values
    .Where(term => term.Projects.Count >= 4 && term.Total >= 30 && term.KeepRate >= 0.55 && term.NativeRatio < 0.05)
    .OrderByDescending(term => term.KeepRate * Math.Log(term.Total))
    .Take(45)
    .ToList();

Console.WriteLine($"{rows.Count:N0} strings traduzidas em {rows.Select(r => r.Project).Distinct().Count()} mods\n");
Console.WriteLine("termo                 mods    usos  mantido    taxa   nativa");
Console.WriteLine(new string('-', 60));
foreach (var term in ranked)
{
    Console.WriteLine($"{term.Word,-20} {term.Projects.Count,5} {term.Total,7} {term.Kept,8} {term.KeepRate,6:P0} {term.NativeRatio,7:P1}");
}

// Ponto cego da métrica acima: ela só olha linhas que foram traduzidas, e o jargão mais problemático
// está justamente nas que voltaram inteiras em inglês. "Broadcaster" não aparecia por isso.
var refused = new Dictionary<string, Refused>(StringComparer.OrdinalIgnoreCase);
foreach (var row in rows.Where(row =>
    string.Equals(noise.Replace(row.Original, " ").Trim(), noise.Replace(row.Translated, " ").Trim(), StringComparison.OrdinalIgnoreCase)))
{
    foreach (var word in Words(noise.Replace(row.Original, " ")).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!refused.TryGetValue(word, out var entry))
        {
            refused[word] = entry = new Refused(word);
        }

        entry.Count++;
        entry.Projects.Add(row.Project);
        if (entry.Examples.Count < 3 && row.Original.Length < 45)
        {
            entry.Examples.Add(row.Original);
        }
    }
}

Console.WriteLine("\n\n=== palavras nas linhas que voltaram 100% em inglês ===");
Console.WriteLine("(o jargão que o motor se recusa a traduzir - onde o glossário mais rende)\n");
Console.WriteLine("termo                 mods  linhas   exemplo");
Console.WriteLine(new string('-', 78));
foreach (var entry in refused.Values
    .Where(entry => entry.Projects.Count >= 3 && entry.Count >= 8 && nativeCounts.GetValueOrDefault(entry.Word) < 5)
    .OrderByDescending(entry => entry.Count)
    .Take(30))
{
    Console.WriteLine($"{entry.Word,-20} {entry.Projects.Count,5} {entry.Count,7}   {entry.Examples.FirstOrDefault()}");
}

Console.WriteLine("\n=== evidência, para curadoria ===");
foreach (var term in ranked.Take(20))
{
    Console.WriteLine($"\n## {term.Word} — {term.KeepRate:P0} mantido em {term.Projects.Count} mods");
    foreach (var (original, translated) in term.KeptExamples)
    {
        Console.WriteLine($"   MANTEVE : {original}  ->  {translated}");
    }

    foreach (var (original, translated) in term.TranslatedExamples.Take(2))
    {
        Console.WriteLine($"   TRADUZIU: {original}  ->  {translated}");
    }
}

internal sealed record Row(string Project, string Original, string Translated);

internal sealed class Term(string word)
{
    public string Word { get; } = word;

    public int Total { get; set; }

    public int Kept { get; set; }

    public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Original, string Translated)> KeptExamples { get; } = [];

    public List<(string Original, string Translated)> TranslatedExamples { get; } = [];

    public int AppearedNatively { get; set; }

    public double KeepRate => Total == 0 ? 0 : (double)Kept / Total;

    /// <summary>Quantas vezes a palavra surgiu numa traducao sem estar no original - sinal de que e portuguesa.</summary>
    public double NativeRatio => Total == 0 ? 0 : (double)AppearedNatively / Total;
}

internal sealed class Refused(string word)
{
    public string Word { get; } = word;

    public int Count { get; set; }

    public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Examples { get; } = [];
}
