using System.Text;
using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// Exercises the GGUF header reader against files written here byte by byte — the point of the reader is
/// to answer "does this model reason?" without loading several gigabytes of weights, so the tests cannot
/// use a real model either.
/// </summary>
public sealed class GgufMetadataTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ts4translator-gguf-tests-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Abbreviated from Qwen3's real template: what matters is that it deals in thinking.</summary>
    private const string ThinkingTemplate =
        "{%- if enable_thinking is defined and enable_thinking is false %}{{- '<think>\\n\\n</think>\\n\\n' }}{%- endif %}";

    private const string PlainTemplate =
        "{% for message in messages %}{{ '<|im_start|>' + message['role'] + '\\n' + message['content'] }}{% endfor %}";

    [Fact]
    public void TryRead_ThinkingTemplate_ReportsAReasoningModel()
    {
        var path = WriteGguf(("general.architecture", "qwen3"), ("general.name", "Qwen3 4B"), ("tokenizer.chat_template", ThinkingTemplate));

        var info = GgufMetadata.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal("qwen3", info.Architecture);
        Assert.Equal("Qwen3 4B", info.Name);
        Assert.True(info.SupportsReasoning);
        Assert.True(info.HasEnableThinkingSwitch);
    }

    [Fact]
    public void TryRead_OrdinaryTemplate_ReportsNoReasoning()
    {
        var path = WriteGguf(("general.architecture", "llama"), ("tokenizer.chat_template", PlainTemplate));

        var info = GgufMetadata.TryRead(path);

        Assert.False(info!.SupportsReasoning);
        Assert.False(info.HasEnableThinkingSwitch);
    }

    /// <summary>
    /// DeepSeek-R1 opens a thinking block but has no <c>enable_thinking</c> flag, so the in-prompt
    /// directive does not apply to it while the empty-block prefill still does.
    /// </summary>
    [Fact]
    public void TryRead_ThinkingWithoutTheQwenSwitch_ReportsReasoningButNoSoftSwitch()
    {
        var path = WriteGguf(("tokenizer.chat_template", "{{ '<|Assistant|><think>\\n' }}"));

        var info = GgufMetadata.TryRead(path);

        Assert.True(info!.SupportsReasoning);
        Assert.False(info.HasEnableThinkingSwitch);
    }

    /// <summary>
    /// The vocabulary array sits between the keys that matter in a real file and holds six figures of
    /// strings. Walking past it rather than materialising it is what keeps this cheap enough to run
    /// behind a button.
    /// </summary>
    [Fact]
    public void TryRead_ChatTemplateBehindTheVocabularyArray_StillFindsIt()
    {
        var path = Path.Combine(_directory, "with-vocab.gguf");
        using (var writer = new BinaryWriter(File.Create(path), Encoding.UTF8))
        {
            WriteHeader(writer, keyValueCount: 4);
            WriteStringValue(writer, "general.architecture", "qwen3");

            // A float array and a string array, the two shapes the skipping code takes different paths for.
            WriteKey(writer, "qwen3.attention.head_count");
            writer.Write(4u); // UINT32
            writer.Write(32u);

            WriteKey(writer, "tokenizer.ggml.tokens");
            writer.Write(9u); // ARRAY
            writer.Write(8u); // of STRING
            writer.Write(50_000UL);
            for (var index = 0; index < 50_000; index++)
            {
                WriteRawString(writer, $"token{index}");
            }

            WriteStringValue(writer, "tokenizer.chat_template", ThinkingTemplate);
        }

        Assert.True(GgufMetadata.TryRead(path)!.SupportsReasoning);
    }

    [Fact]
    public void TryRead_NotAGgufAtAll_ReturnsNullInsteadOfThrowing()
    {
        var path = Path.Combine(_directory, "not-a-model.gguf");
        File.WriteAllText(path, "isto aqui é um texto qualquer, não um modelo");

        Assert.Null(GgufMetadata.TryRead(path));
    }

    /// <summary>A download interrupted halfway is the likeliest bad file a user will actually point at.</summary>
    [Fact]
    public void TryRead_TruncatedFile_ReturnsNullInsteadOfThrowing()
    {
        var complete = WriteGguf(("tokenizer.chat_template", ThinkingTemplate));
        var truncated = Path.Combine(_directory, "truncated.gguf");
        var bytes = File.ReadAllBytes(complete);
        File.WriteAllBytes(truncated, bytes[..(bytes.Length / 2)]);

        Assert.Null(GgufMetadata.TryRead(truncated));
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsNull() =>
        Assert.Null(GgufMetadata.TryRead(Path.Combine(_directory, "não-existe.gguf")));

    [Fact]
    public void TryRead_NoPathConfigured_ReturnsNull() => Assert.Null(GgufMetadata.TryRead(null));

    private string WriteGguf(params (string Key, string Value)[] entries)
    {
        var path = Path.Combine(_directory, $"model-{Guid.NewGuid():N}.gguf");
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);

        WriteHeader(writer, entries.Length);
        foreach (var (key, value) in entries)
        {
            WriteStringValue(writer, key, value);
        }

        return path;
    }

    private static void WriteHeader(BinaryWriter writer, int keyValueCount)
    {
        writer.Write(Encoding.ASCII.GetBytes("GGUF"));
        writer.Write(3u); // version
        writer.Write(0UL); // tensor count
        writer.Write((ulong)keyValueCount);
    }

    private static void WriteStringValue(BinaryWriter writer, string key, string value)
    {
        WriteKey(writer, key);
        writer.Write(8u); // STRING
        WriteRawString(writer, value);
    }

    private static void WriteKey(BinaryWriter writer, string key) => WriteRawString(writer, key);

    private static void WriteRawString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
