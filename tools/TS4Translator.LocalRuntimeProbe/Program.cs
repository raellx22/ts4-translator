using System.Globalization;
using System.Text;
using System.Text.Json;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.LocalModel;

Console.OutputEncoding = Encoding.UTF8;

// A variante C provou que o problema e o modelo nao saber o que o jargao significa. Mas instrucao de
// estilo e o lugar errado para isso: o lugar certo e o glossario. Este teste confirma pelo caminho real.

var options = new LocalModelOptions(@"C:\Users\israe\AppData\Local\TS4Translator\Models\Local");
await using var runtime = new LocalModelRuntime(options);
var corpus = JsonSerializer.Deserialize<List<Item>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "corpus-200.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var en = CultureInfo.GetCultureInfo("en-US");
var pt = CultureInfo.GetCultureInfo("pt-BR");
var provider = new LocalModelProvider(runtime, null);
var requests = corpus.Select(item => new TranslationRequestItem(item.Id, item.Text)).ToList();

GlossaryTerm Term(int id, string o, string t) => new(id, o, t, false, false, null, null);

// Exatamente o que o usuario digitaria na tela de Glossario.
var glossary = new List<GlossaryTerm>
{
    Term(1, "Broadcaster", "Emissor"),
    Term(2, "Make", "Fazer"),
    Term(3, "recipe description", "descrição da receita"),
};

async Task<Dictionary<string, string>> Run(string label, IReadOnlyList<GlossaryTerm>? terms)
{
    var results = await provider.TranslateBatchAsync(requests, en, pt, terms, null);
    var map = results.ToDictionary(r => r.Id, r => r.TranslatedProtectedText ?? "");
    var unchanged = corpus.Count(i => string.Equals(map[i.Id].Trim(), i.Text.Trim(), StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"{label,-30} iguais ao original: {unchanged}/{corpus.Count}");
    return map;
}

var a = await Run("A · sem glossario", null);
var d = await Run("D · com 3 termos", glossary);

Console.WriteLine("\n=== A  ->  D ===");
foreach (var item in corpus.Take(20))
{
    if (string.Equals(a[item.Id].Trim(), d[item.Id].Trim(), StringComparison.Ordinal)) continue;
    Console.WriteLine($"EN: {item.Text}\n  antes : {a[item.Id]}\n  depois: {d[item.Id]}");
}

internal sealed record Item(string Id, string Text);
