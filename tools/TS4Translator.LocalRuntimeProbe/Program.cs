using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LLama;
using LLama.Batched;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using TS4Translator.Core.Tokens;
using TS4Translator.Translation;

Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// Measures running Hy-MT2 1.8B in-process through llama.cpp, against the same
// 200 real WickedWhims strings the LM Studio benchmark used. Three configurations,
// so the question "does going native help, and where" gets a number instead of an
// opinion. See docs/LOCAL_MODELS.md for the LM Studio baseline.
// ---------------------------------------------------------------------------

var modelPath = @"C:\Users\israe\.lmstudio\models\tencent\Hy-MT2-1.8B-GGUF\Hy-MT2-1.8B-Q8_0.gguf";
var corpusPath = Path.Combine(AppContext.BaseDirectory, "corpus-200.json");
if (!File.Exists(corpusPath))
{
    corpusPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "corpus-200.json");
}

// LLamaSharp probes for a CUDA *Toolkit* to decide whether the GPU build is usable, and this machine
// has only the driver — so its auto-detection picked the CPU build even with WithCuda(true). Naming the
// DLL bypasses detection: the app knows which backend it shipped, it does not need to guess.
var cudaLibrary = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "cuda12", "llama.dll");
NativeLibraryConfig.LLama.WithLibrary(cudaLibrary);
NativeLibraryConfig.All.WithLogCallback((level, message) =>
{
    if (level <= LLamaLogLevel.Warning)
    {
        Console.Error.Write($"[llama:{level}] {message}");
    }
});

// The Hunyuan chat template, read from the GGUF's own metadata.
const string BeginSentence = "<｜hy_begin▁of▁sentence｜>";
const string AfterSystem = "<｜hy_place▁holder▁no▁3｜>";
const string UserTurn = "<｜hy_User｜>";
const string AssistantTurn = "<｜hy_Assistant｜>";

// The app's real contract, minus the JSON envelope: docs/LOCAL_MODELS.md measured that Hy-MT2 wins
// item-by-item, and a per-item plain answer is what a native provider would ask for.
const string SystemPrompt = """
    You are a translation engine translating video game UI text from English to Brazilian Portuguese.
    Reply with ONLY the translation of the text you are given. No commentary, no quotes, no explanation.
    Preserve line breaks, punctuation, capitalization intent and UI brevity.
    A placeholder is the literal text __TS4_TOKEN_ followed by four digits and two underscores. Copy every placeholder that appears in the text into your translation exactly as-is, unchanged, in a natural position for Brazilian Portuguese grammar.
    NEVER write a placeholder that was not already in the text. If the text contains no placeholder, your translation must contain none: a number, a name or a code-like word is ordinary text.
    """;

var sharedPrefix = BeginSentence + SystemPrompt + AfterSystem + UserTurn;

var corpus = JsonSerializer.Deserialize<List<CorpusItem>>(
    File.ReadAllText(corpusPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
Console.WriteLine($"corpus: {corpus.Count} strings, {corpus.Sum(c => c.Text.Length)} caracteres");

var protector = new TokenProtector();
var validator = new TranslationValidator();
var protectedByIndex = corpus.Select(item => protector.Protect(item.Text)).ToList();
Console.WriteLine($"strings com marcador protegido: {protectedByIndex.Count(p => p.Tokens.Count > 0)}");

var parameters = new ModelParams(modelPath)
{
    ContextSize = 8192,
    GpuLayerCount = 99,
    FlashAttention = true,
    BatchSize = 2048,
    UBatchSize = 512,
};

var loadWatch = Stopwatch.StartNew();
using var weights = LLamaWeights.LoadFromFile(parameters);
Console.WriteLine($"modelo carregado em {loadWatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();

var results = new List<RunResult>();
results.Add(await RunAsync("A · 1 sequência, sem cache de prefixo", parallelism: 1, reusePrefix: false));
results.Add(await RunAsync("B · 8 sequências, sem cache de prefixo", parallelism: 8, reusePrefix: false));
results.Add(await RunAsync("C · 8 sequências, com cache de prefixo", parallelism: 8, reusePrefix: true));
var repeat = await RunAsync("C (repetição, para determinismo)", parallelism: 8, reusePrefix: true);

Report(results, repeat);

async Task<RunResult> RunAsync(string label, int parallelism, bool reusePrefix)
{
    Console.WriteLine($"--- {label} ---");

    using var executor = new BatchedExecutor(weights, parameters, null!);
    var context = executor.Context;

    Conversation? cachedPrefix = null;
    var promptTokens = 0;

    if (reusePrefix)
    {
        // Prompt the shared prefix once and let every item fork from it. This is the thing an HTTP
        // endpoint cannot expose: the system prompt is prefilled a single time for the whole run.
        cachedPrefix = executor.Create();
        cachedPrefix.Prompt(sharedPrefix, true, true);
        await executor.Infer();
        promptTokens += context.Tokenize(sharedPrefix, true, true).Length;
    }

    var answers = new string?[corpus.Count];
    var watch = Stopwatch.StartNew();
    var generated = 0;
    var next = 0;

    // One scheduler, not one task per sequence. BatchedExecutor.Infer() advances *every* live
    // conversation in a single decode - that shared step is where the parallel speedup comes from -
    // and calling it from several threads at once trips GGML_ASSERT(logits != nullptr) in the sampler.
    var slots = new List<Slot>();

    while (next < corpus.Count || slots.Count > 0)
    {
        while (slots.Count < parallelism && next < corpus.Count)
        {
            var index = next++;
            var body = protectedByIndex[index].ProtectedText;

            Conversation conversation;
            if (reusePrefix)
            {
                conversation = cachedPrefix!.Fork();
                conversation.Prompt(body + AssistantTurn, false, true);
                promptTokens += context.Tokenize(body + AssistantTurn, false, true).Length;
            }
            else
            {
                conversation = executor.Create();
                var full = sharedPrefix + body + AssistantTurn;
                conversation.Prompt(full, true, true);
                promptTokens += context.Tokenize(full, true, true).Length;
            }

            // A decoder rather than concatenating pieces: a Portuguese accent is often split across
            // two tokens, and decoding them separately produces mojibake.
            slots.Add(new Slot(index, conversation, new GreedySamplingPipeline(), new StreamingTokenDecoder(context)));
        }

        await executor.Infer();

        for (var slotIndex = slots.Count - 1; slotIndex >= 0; slotIndex--)
        {
            var slot = slots[slotIndex];
            var token = slot.Sampler.Sample(context.NativeHandle, slot.Conversation.GetSampleIndex(0));

            if (token.IsEndOfGeneration(weights.Vocab) || slot.Steps >= 320)
            {
                answers[slot.Index] = slot.Decoder.Read().Trim();
                slot.Conversation.Dispose();
                slots.RemoveAt(slotIndex);
                continue;
            }

            slot.Decoder.Add(token);
            slot.Steps++;
            generated++;
            slot.Conversation.Prompt(token);
        }
    }

    cachedPrefix?.Dispose();
    watch.Stop();

    var result = Score(label, watch.Elapsed, answers, promptTokens, generated);
    Console.WriteLine($"  {result.Elapsed.TotalSeconds:F2}s · {result.OutputTokensPerSecond:F0} tok/s saída · "
        + $"{result.Approved}/{corpus.Count} aprovadas · {result.Empty} vazias · prefill {result.PromptTokens} tokens");
    Console.WriteLine();
    return result;
}

RunResult Score(string label, TimeSpan elapsed, string?[] answers, int promptTokens, int generatedTokens)
{
    var approved = 0;
    var empty = 0;
    var tokenIssues = 0;
    var invented = 0;

    for (var index = 0; index < corpus.Count; index++)
    {
        var answer = answers[index];
        if (string.IsNullOrWhiteSpace(answer))
        {
            empty++;
            continue;
        }

        var restore = protector.Restore(answer, protectedByIndex[index]);
        var issues = validator.Validate(corpus[index].Text, restore.RestoredText, restore);

        if (issues.Count == 0)
        {
            approved++;
        }

        if (restore.Issues.Count > 0)
        {
            tokenIssues++;
        }

        if (restore.Issues.Any(issue => issue.Kind == TokenValidationIssueKind.UnexpectedToken))
        {
            invented++;
        }
    }

    return new RunResult(label, elapsed, approved, empty, tokenIssues, invented, promptTokens,
        generatedTokens / elapsed.TotalSeconds, [.. answers]);
}

void Report(List<RunResult> runs, RunResult repeated)
{
    Console.WriteLine("=================== RESULTADO ===================");
    Console.WriteLine($"{"configuração",-40} {"tempo",8} {"tok/s",7} {"aprov.",8} {"prefill",9}");
    foreach (var run in runs)
    {
        Console.WriteLine($"{run.Label,-40} {run.Elapsed.TotalSeconds,7:F2}s {run.OutputTokensPerSecond,7:F0} "
            + $"{run.Approved,4}/{corpus.Count,-3} {run.PromptTokens,9}");
    }

    Console.WriteLine();
    Console.WriteLine("Linha de base documentada (LM Studio, mesmo modelo/hardware):");
    Console.WriteLine("  Hy-MT2 Q8, item a item, 8 filas            34,24s   195/200");
    Console.WriteLine("  Hy-MT2 Q8, lote 8/1.200 + fallback         25,85s   192/200");
    Console.WriteLine();

    var cached = runs[^1];
    var identical = cached.Answers.Zip(repeated.Answers).Count(pair => string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
    Console.WriteLine($"Determinismo: {identical}/{corpus.Count} respostas idênticas entre duas execuções da config C.");
    Console.WriteLine($"Marcadores inventados: {string.Join(", ", runs.Select(r => $"{r.Label[0]}={r.InventedTokens}"))}");
    Console.WriteLine();

    Console.WriteLine("=== amostra (config C) ===");
    for (var index = 0; index < Math.Min(8, corpus.Count); index++)
    {
        Console.WriteLine($"EN: {Flatten(corpus[index].Text)}");
        Console.WriteLine($"PT: {Flatten(cached.Answers[index] ?? "(vazio)")}");
    }

    var payload = corpus.Select((item, index) => new
    {
        item.Id,
        original = item.Text,
        translation = cached.Answers[index],
    });
    var outPath = Path.Combine(AppContext.BaseDirectory, "resultado-config-c.json");
    File.WriteAllText(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"\nsaída completa: {outPath}");
}

static string Flatten(string text)
{
    var single = text.ReplaceLineEndings(" ").Trim();
    return single.Length <= 100 ? single : single[..100] + "…";
}

internal sealed record CorpusItem(string Id, string Text);

internal sealed record RunResult(
    string Label,
    TimeSpan Elapsed,
    int Approved,
    int Empty,
    int TokenIssues,
    int InventedTokens,
    int PromptTokens,
    double OutputTokensPerSecond,
    IReadOnlyList<string?> Answers);

internal sealed class Slot(int index, LLama.Batched.Conversation conversation, LLama.Sampling.GreedySamplingPipeline sampler, LLama.StreamingTokenDecoder decoder)
{
    public int Index { get; } = index;

    public LLama.Batched.Conversation Conversation { get; } = conversation;

    public LLama.Sampling.GreedySamplingPipeline Sampler { get; } = sampler;

    public LLama.StreamingTokenDecoder Decoder { get; } = decoder;

    public int Steps { get; set; }
}
