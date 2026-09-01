using System.Globalization;
using System.Text;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.LocalModel;

Console.OutputEncoding = Encoding.UTF8;

// Testa a hipotese "o modelo so sabe portugues de Portugal": frases escolhidas para expor as
// diferencas classicas - gerundio (pt-BR "estou fazendo" x pt-PT "estou a fazer"), pronome de
// tratamento (voce x tu), e vocabulario (tela x ecra, celular x telemovel, trem x comboio).

var options = new LocalModelOptions(@"C:\Users\israe\AppData\Local\TS4Translator\Models\Local");
await using var runtime = new LocalModelRuntime(options);
var provider = new LocalModelProvider(runtime, null);
var en = CultureInfo.GetCultureInfo("en-US");

string[] probes =
[
    "She is making a drink right now.",
    "You are using the wrong screen.",
    "Check your mobile phone for the message.",
    "He is taking the train to work.",
    "Are you sure you want to delete this?",
    "The next drink is ready.",
    "Put the pill in her next drink.",
];

async Task Run(string label, CultureInfo target)
{
    var req = probes.Select((t, i) => new TranslationRequestItem(i.ToString(CultureInfo.InvariantCulture), t)).ToList();
    var res = await provider.TranslateBatchAsync(req, en, target);
    var map = res.ToDictionary(r => r.Id, r => r.TranslatedProtectedText ?? "");
    Console.WriteLine($"\n=== alvo: {label} ({target.Name} / \"{target.EnglishName}\") ===");
    for (var i = 0; i < probes.Length; i++)
        Console.WriteLine($"  {probes[i]}\n    -> {map[i.ToString(CultureInfo.InvariantCulture)]}");
}

await Run("português do Brasil", CultureInfo.GetCultureInfo("pt-BR"));
await Run("português de Portugal", CultureInfo.GetCultureInfo("pt-PT"));
