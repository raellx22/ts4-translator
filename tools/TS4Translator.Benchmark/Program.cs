using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Tokens;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;
using TS4Translator.Providers.GoogleGemini;
using TS4Translator.Providers.OpenAICompatible;
using TS4Translator.Providers.Argos;
using TS4Translator.Sims4;
using TS4Translator.Translation;

const string GeminiKeyEnvironmentVariable = "TS4TRANSLATOR_GEMINI_KEY";
var options = BenchmarkOptions.Parse(args);
var sourceLocale = CultureInfo.GetCultureInfo("en-US");
var targetLocale = CultureInfo.GetCultureInfo("pt-BR");

var packageService = new Sims4PackageService();
var stringTableService = new SmartSimStringTableService();
var tables = await packageService.FindStringTablesAsync(options.PackagePath);
var entries = new List<SourceEntry>();
var sourceTableCount = 0;

foreach (var table in tables)
{
    CultureInfo locale;
    try
    {
        locale = stringTableService.GetLocale(table);
    }
    catch (Exception exception) when (exception is NotSupportedException or FormatException)
    {
        continue;
    }

    if (!string.Equals(locale.Name, sourceLocale.Name, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    sourceTableCount++;
    var tableEntries = await packageService.ReadStringTableAsync(options.PackagePath, table);
    entries.AddRange(tableEntries
        .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
        .Select(entry => new SourceEntry($"{table.Group:X8}:{table.FullInstance:X16}:{entry.KeyHash:X8}", entry.Value)));
}

var lengths = entries.Select(entry => entry.Text.Length).Order().ToArray();
var inventory = new InventoryResult(
    Path.GetFileName(options.PackagePath),
    new FileInfo(options.PackagePath).Length,
    tables.Count,
    sourceTableCount,
    entries.Count,
    lengths.Sum(length => (long)length),
    Percentile(lengths, 0.50),
    Percentile(lengths, 0.90),
    lengths.Length == 0 ? 0 : lengths[^1]);

if (options.SampleSize == 0)
{
    Console.WriteLine(JsonSerializer.Serialize(inventory, BenchmarkJson.Options));
    return;
}

var benchmarkEntries = options.Provider.StartsWith("hy-", StringComparison.Ordinal)
    ? entries
        .Where(entry => CharacterCount(entry.Text) <= 500)
        .GroupBy(entry => entry.Text, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToList()
    : entries;

var sample = benchmarkEntries
    .OrderBy(entry => StableSampleKey(entry.Id, entry.Text), StringComparer.Ordinal)
    .Take(Math.Min(options.SampleSize, benchmarkEntries.Count))
    .ToList();

using var usageHandler = new UsageRecordingHandler(new HttpClientHandler(), options.Provider);
using var httpClient = new HttpClient(usageHandler) { Timeout = TimeSpan.FromMinutes(5) };
await using var argosRuntime = new ArgosRuntimeManager(new ArgosRuntimeOptions(options.ArgosRoot));
if (options.Provider == "argos" && options.InstallArgos)
{
    await argosRuntime.InstallAsync(new Progress<string>(message => Console.Error.WriteLine(message)));
}
ITranslationProvider provider = options.Provider switch
{
    "argos" => new ArgosTranslateProvider(argosRuntime),
    "local" => new OpenAICompatibleProvider(
        httpClient,
        new OpenAICompatibleProviderOptions(options.Endpoint, options.Model)),
    "gemini" => new GoogleGeminiProvider(
        httpClient,
        new GoogleGeminiProviderOptions(
            options.Endpoint,
            options.Model,
            Environment.GetEnvironmentVariable(GeminiKeyEnvironmentVariable)
                ?? throw new InvalidOperationException($"Set {GeminiKeyEnvironmentVariable} for a Gemini benchmark."))),
    "hy-direct" => new HyMtDirectProvider(httpClient, options.Endpoint, options.Model, options.Concurrency),
    "hy-structured" => new CapabilitiesOverrideProvider(
        new OpenAICompatibleProvider(httpClient, new OpenAICompatibleProviderOptions(options.Endpoint, options.Model)),
        new ProviderCapabilities(
            SupportsBatch: true,
            SupportsStreaming: false,
            SupportsCustomPrompt: true,
            SupportsGlossary: true,
            SupportsLocalExecution: true,
            RequiresApiKey: false,
            MaxContextSize: 8192,
            RecommendedConcurrency: Math.Clamp(options.Concurrency, 1, 8),
            RecommendedBatchItems: 8,
            RecommendedBatchCharacters: 1200)),
    "hy-hybrid" => HyMtHybridProvider.Create(httpClient, options.Endpoint, options.Model, options.Concurrency),
    _ => throw new InvalidOperationException($"Unsupported provider '{options.Provider}'."),
};

var orchestrator = new TranslationOrchestrator(
    new TokenProtector(),
    new TranslationValidator(),
    new BatchBuilder(),
    new NoOpTranslationMemory());
var sourceItems = sample
    .Select((entry, index) => new TranslationSourceItem((index + 1).ToString(CultureInfo.InvariantCulture), entry.Text))
    .ToList();

var stopwatch = Stopwatch.StartNew();
IReadOnlyList<TranslationOutcome> outcomes;
try
{
    outcomes = await orchestrator.TranslateAsync(
        sourceItems,
        provider,
        sourceLocale,
        targetLocale,
        customInstructions: "Use natural Brazilian Portuguese suitable for The Sims 4. Prefer concise, consistent UI terminology. Preserve brand names and technical identifiers.");
}
catch (HttpRequestException) when (!string.IsNullOrWhiteSpace(usageHandler.LastErrorContent))
{
    Console.Error.WriteLine(usageHandler.LastErrorContent);
    throw;
}
stopwatch.Stop();

var itemReports = outcomes.Select((outcome, index) => new ItemResult(
    sample[index].Id,
    outcome.OriginalText,
    outcome.TranslatedText,
    outcome.Success,
    outcome.Issues.Select(issue => $"{issue.Code}:{issue.Severity}").ToArray())).ToList();
var projectedSeconds = sample.Count == 0 ? 0 : stopwatch.Elapsed.TotalSeconds * benchmarkEntries.Count / sample.Count;
var report = new BenchmarkResult(
    DateTimeOffset.UtcNow,
    options.Provider,
    options.Model,
    options.Concurrency,
    inventory,
    benchmarkEntries.Count,
    sample.Count,
    stopwatch.Elapsed.TotalSeconds,
    projectedSeconds,
    outcomes.Count(outcome => outcome.Success),
    outcomes.Count(outcome => outcome.TranslatedText is null),
    outcomes.Count(outcome => !outcome.Success && outcome.TranslatedText is not null),
    usageHandler.Usage,
    itemReports);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(report, BenchmarkJson.Options));
Console.WriteLine(JsonSerializer.Serialize(report with { Items = [] }, BenchmarkJson.Options));

static int Percentile(int[] sortedValues, double percentile)
{
    if (sortedValues.Length == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
    return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
}

static string StableSampleKey(string id, string text)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{id}\n{text}"));
    return Convert.ToHexString(bytes);
}

static int CharacterCount(string text) => text.EnumerateRunes().Count();

internal sealed record BenchmarkOptions(
    string PackagePath,
    string Provider,
    string Endpoint,
    string Model,
    int SampleSize,
    int Concurrency,
    string OutputPath,
    string ArgosRoot,
    bool InstallArgos)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = args
            .Chunk(2)
            .Where(chunk => chunk.Length == 2)
            .ToDictionary(chunk => chunk[0], chunk => chunk[1], StringComparer.OrdinalIgnoreCase);
        var provider = values.GetValueOrDefault("--provider", "local").ToLowerInvariant();
        return new BenchmarkOptions(
            values.GetValueOrDefault("--package") ?? throw new ArgumentException("--package is required."),
            provider,
            values.GetValueOrDefault("--endpoint", provider == "gemini" ? "https://generativelanguage.googleapis.com" : "http://127.0.0.1:1234/v1"),
            values.GetValueOrDefault("--model", provider switch
            {
                "gemini" => "gemini-3.5-flash-lite",
                "argos" => "translate-en_pb-1_9",
                _ => "qwen3.5-4b",
            }),
            int.Parse(values.GetValueOrDefault("--sample-size", "0"), CultureInfo.InvariantCulture),
            int.Parse(values.GetValueOrDefault("--concurrency", provider.StartsWith("hy-", StringComparison.Ordinal) ? "8" : "1"), CultureInfo.InvariantCulture),
            values.GetValueOrDefault("--output", Path.Combine("benchmark-results", $"{provider}.json")),
            values.GetValueOrDefault("--argos-root", Path.Combine("tools", "argos-prototype", ".runtime")),
            bool.Parse(values.GetValueOrDefault("--install-argos", "false")));
    }
}

internal sealed record SourceEntry(string Id, string Text);

internal sealed record InventoryResult(
    string PackageName,
    long PackageBytes,
    int AllStringTables,
    int SourceStringTables,
    int SourceStrings,
    long SourceCharacters,
    int MedianCharacters,
    int P90Characters,
    int MaximumCharacters);

internal sealed record UsageResult(long InputTokens, long OutputTokens, long ThinkingTokens, long TotalTokens, int Requests);

internal sealed record ItemResult(string SourceId, string Original, string? Translation, bool Success, IReadOnlyList<string> Issues);

internal sealed record BenchmarkResult(
    DateTimeOffset MeasuredAtUtc,
    string Provider,
    string Model,
    int Concurrency,
    InventoryResult Inventory,
    int ProjectedItems,
    int SampleItems,
    double ElapsedSeconds,
    double ProjectedFullSeconds,
    int SuccessfulItems,
    int MissingItems,
    int NeedsReviewItems,
    UsageResult Usage,
    IReadOnlyList<ItemResult> Items);

internal static class BenchmarkJson
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true };
}

internal sealed class NoOpTranslationMemory : ITranslationMemory
{
    public Task<TranslationMemoryEntry?> FindExactAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default) =>
        Task.FromResult<TranslationMemoryEntry?>(null);

    public Task<FuzzyMatchResult?> FindSimilarAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, double minSimilarity = 0.85, CancellationToken cancellationToken = default) =>
        Task.FromResult<FuzzyMatchResult?>(null);

    public Task<IReadOnlyList<FuzzyMatchResult?>> FindBestMatchesAsync(IReadOnlyList<string> originalTexts, CultureInfo sourceLocale, CultureInfo targetLocale, double minSimilarity = 0.85, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FuzzyMatchResult?>>(Enumerable.Repeat<FuzzyMatchResult?>(null, originalTexts.Count).ToList());

    public Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveManyAsync(IReadOnlyList<TranslationMemoryEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class UsageRecordingHandler(HttpMessageHandler innerHandler, string provider) : DelegatingHandler(innerHandler)
{
    private long _inputTokens;
    private long _outputTokens;
    private long _thinkingTokens;
    private long _totalTokens;
    private int _requests;

    public string? LastErrorContent { get; private set; }

    public UsageResult Usage => new(_inputTokens, _outputTokens, _thinkingTokens, _totalTokens, _requests);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            RecordUsage(content);
        }
        else
        {
            LastErrorContent = content;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        response.Content = new StringContent(content, Encoding.UTF8, mediaType);
        return response;
    }

    private void RecordUsage(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if ((provider == "local" || provider.StartsWith("hy-", StringComparison.Ordinal)) && root.TryGetProperty("usage", out var usage))
        {
            Add(ref _inputTokens, GetInt64(usage, "prompt_tokens"));
            Add(ref _outputTokens, GetInt64(usage, "completion_tokens"));
            Add(ref _totalTokens, GetInt64(usage, "total_tokens"));
            if (usage.TryGetProperty("completion_tokens_details", out var details))
            {
                Add(ref _thinkingTokens, GetInt64(details, "reasoning_tokens"));
            }
        }
        else if (provider == "gemini" && root.TryGetProperty("usageMetadata", out var metadata))
        {
            Add(ref _inputTokens, GetInt64(metadata, "promptTokenCount"));
            Add(ref _outputTokens, GetInt64(metadata, "candidatesTokenCount"));
            Add(ref _thinkingTokens, GetInt64(metadata, "thoughtsTokenCount"));
            Add(ref _totalTokens, GetInt64(metadata, "totalTokenCount"));
        }

        Interlocked.Increment(ref _requests);
    }

    private static long GetInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value) ? value : 0;

    private static void Add(ref long target, long value) => Interlocked.Add(ref target, value);
}


internal sealed class CapabilitiesOverrideProvider(ITranslationProvider inner, ProviderCapabilities capabilities) : ITranslationProvider
{
    public string Name => inner.Name;

    public ProviderCapabilities Capabilities { get; } = capabilities;

    public Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default) =>
        inner.TranslateBatchAsync(items, sourceLocale, targetLocale, glossaryTerms, customInstructions, cancellationToken);
}


internal sealed class HyMtHybridProvider(
    ITranslationProvider structured,
    ITranslationProvider direct,
    ProviderCapabilities capabilities) : ITranslationProvider
{
    public string Name => "Hy-MT2 Hybrid";

    public ProviderCapabilities Capabilities { get; } = capabilities;

    public static HyMtHybridProvider Create(HttpClient httpClient, string endpoint, string model, int concurrency)
    {
        var capabilities = new ProviderCapabilities(
            SupportsBatch: true,
            SupportsStreaming: false,
            SupportsCustomPrompt: true,
            SupportsGlossary: true,
            SupportsLocalExecution: true,
            RequiresApiKey: false,
            MaxContextSize: 8192,
            RecommendedConcurrency: Math.Clamp(concurrency, 1, 8),
            RecommendedBatchItems: 8,
            RecommendedBatchCharacters: 1200);
        return new HyMtHybridProvider(
            new OpenAICompatibleProvider(httpClient, new OpenAICompatibleProviderOptions(endpoint, model)),
            new HyMtDirectProvider(httpClient, endpoint, model, concurrency),
            capabilities);
    }

    public async Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var primary = await structured.TranslateBatchAsync(
            items, sourceLocale, targetLocale, glossaryTerms, customInstructions, cancellationToken).ConfigureAwait(false);
        var primaryById = primary
            .GroupBy(result => result.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var fallbackItems = items.Where(item =>
        {
            if (!primaryById.TryGetValue(item.Id, out var result))
            {
                return true;
            }

            return Regex.Matches(item.ProtectedText, "__TS4_TOKEN_[0-9]{4}__")
                .Select(match => match.Value)
                .Any(token => !result.TranslatedProtectedText.Contains(token, StringComparison.Ordinal));
        }).ToList();

        if (fallbackItems.Count == 0)
        {
            return primary;
        }

        var fallbackTasks = fallbackItems.Select(item => direct.TranslateBatchAsync(
            [item], sourceLocale, targetLocale, glossaryTerms, customInstructions, cancellationToken));
        var fallbackResults = (await Task.WhenAll(fallbackTasks).ConfigureAwait(false)).SelectMany(result => result);
        var fallbackIds = fallbackItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return primary.Where(result => !fallbackIds.Contains(result.Id)).Concat(fallbackResults).ToList();
    }
}


internal sealed class HyMtDirectProvider(HttpClient httpClient, string endpoint, string model, int concurrency) : ITranslationProvider
{
    private const string SystemPrompt =
        "Traduza para PT-BR adulto/erótico. Use gírias reais, sem censura. " +
        "Traduza apenas o texto dado — nunca invente informação nem complete frases, mesmo que o texto pareça curto ou sem contexto. " +
        "Nunca adicione tags HTML ou variáveis que não estejam no original; preserve exatamente as que existirem. " +
        "Retorne SOMENTE a tradução, sem aspas ou comentários.";

    public string Name => "Hy-MT2 Direct (Linux-compatible)";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsBatch: false,
        SupportsStreaming: false,
        SupportsCustomPrompt: true,
        SupportsGlossary: false,
        SupportsLocalExecution: true,
        RequiresApiKey: false,
        MaxContextSize: 8192,
        RecommendedConcurrency: Math.Clamp(concurrency, 1, 8),
        RecommendedBatchItems: 1,
        RecommendedBatchCharacters: 500);

    public async Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        if (items.Count != 1)
        {
            throw new ArgumentException("Hy-MT2 direct mode accepts exactly one item per request.", nameof(items));
        }

        var item = items[0];
        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = item.ProtectedText },
            },
            temperature = 0.15,
            stream = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(content);
        var translation = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?.Trim();

        return string.IsNullOrWhiteSpace(translation) || translation == "-"
            ? []
            : [new TranslationResultItem(item.Id, translation)];
    }
}
