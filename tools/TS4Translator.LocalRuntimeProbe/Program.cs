using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.LocalModel;
using TS4Translator.Translation;

Console.OutputEncoding = Encoding.UTF8;

// Smoke test do provider real, pelo mesmo caminho que o app usa: LocalModelOptions -> LocalModelRuntime
// -> LocalModelProvider, com o TokenProtector e o TranslationValidator de producao pontuando o resultado.

var options = new LocalModelOptions(@"C:\Users\israe\AppData\Local\TS4Translator\Models\Local");
await using var runtime = new LocalModelRuntime(options);

var status = runtime.GetStatus(null);
Console.WriteLine($"backend : {status.Backend}");
Console.WriteLine($"pronto  : {status.IsReady}");
Console.WriteLine($"mensagem: {status.Message}");
Console.WriteLine();

if (!status.IsReady)
{
    return;
}

var provider = new LocalModelProvider(runtime, null);
Console.WriteLine($"capacidades: glossario={provider.Capabilities.SupportsGlossary} prompt={provider.Capabilities.SupportsCustomPrompt} lote={provider.Capabilities.RecommendedBatchItems}");
Console.WriteLine();

var corpusPath = Path.Combine(AppContext.BaseDirectory, "corpus-200.json");
var corpus = JsonSerializer.Deserialize<List<CorpusItem>>(File.ReadAllText(corpusPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var protector = new TokenProtector();
var validator = new TranslationValidator();
var en = CultureInfo.GetCultureInfo("en-US");
var pt = CultureInfo.GetCultureInfo("pt-BR");

// Um termo de glossario que o Argos nao teria como honrar - e o ponto inteiro deste provider.
var glossary = new List<GlossaryTerm> { new(1, "Sims", "Sims", NeverTranslate: true, CaseSensitive: false, Notes: null, ProjectId: null) };

var protectedByIndex = corpus.Select(item => protector.Protect(item.Text)).ToList();
var requests = corpus.Select((item, index) => new TranslationRequestItem(item.Id, protectedByIndex[index].ProtectedText)).ToList();

var watch = Stopwatch.StartNew();
var results = await provider.TranslateBatchAsync(requests, en, pt, glossary, "Use tratamento informal.");
watch.Stop();

var byId = results.ToDictionary(result => result.Id, result => result.TranslatedProtectedText);
var approved = 0;
var issuesByCode = new Dictionary<string, int>();

for (var index = 0; index < corpus.Count; index++)
{
    var answer = byId.GetValueOrDefault(corpus[index].Id) ?? "";
    var restore = protector.Restore(answer, protectedByIndex[index]);
    var issues = validator.Validate(corpus[index].Text, restore.RestoredText, restore);
    if (issues.Count == 0) { approved++; continue; }
    foreach (var issue in issues)
    {
        var key = issue.Code.ToString();
        issuesByCode[key] = issuesByCode.GetValueOrDefault(key) + 1;
    }
}

Console.WriteLine($"200 strings em {watch.Elapsed.TotalSeconds:F2}s · {approved}/200 aprovadas");
foreach (var (code, count) in issuesByCode.OrderByDescending(p => p.Value)) Console.WriteLine($"  {code}: {count}");
Console.WriteLine();
for (var index = 0; index < 6; index++)
{
    var answer = byId.GetValueOrDefault(corpus[index].Id) ?? "";
    var restored = protector.Restore(answer, protectedByIndex[index]).RestoredText;
    Console.WriteLine($"EN: {Flatten(corpus[index].Text)}");
    Console.WriteLine($"PT: {Flatten(restored)}");
}

static string Flatten(string text)
{
    var single = text.ReplaceLineEndings(" \n ").Trim();
    return single.Length <= 105 ? single : single[..105] + "…";
}

internal sealed record CorpusItem(string Id, string Text);
