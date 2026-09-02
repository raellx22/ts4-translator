using System.Text;

namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// What a <c>.gguf</c> file says about itself, read straight from its header.
/// </summary>
/// <param name="Name">The model's own name, when it carries one.</param>
/// <param name="Architecture">e.g. <c>qwen3</c>, <c>gemma3</c>, <c>llama</c>.</param>
/// <param name="ChatTemplate">The Jinja template the model was trained to be addressed with.</param>
public sealed record GgufModelInfo(string? Name, string? Architecture, string? ChatTemplate)
{
    /// <summary>
    /// Whether this model is a reasoning model, decided by its own chat template.
    /// </summary>
    /// <remarks>
    /// The template is where the evidence actually is. A thinking model's template has to deal with
    /// <c>&lt;think&gt;</c> — stripping it out of previous turns, opening one for the assistant, or gating it
    /// behind an <c>enable_thinking</c> flag — and a model that does not reason has no reason to mention
    /// it. Guessing from the file name instead would be wrong for every renamed or requantised download,
    /// which is most of what people actually have on disk.
    /// </remarks>
    public bool SupportsReasoning => ChatTemplate?.Contains("think", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Whether the template exposes Qwen3's <c>enable_thinking</c> switch, which those models also honour
    /// as a <c>/no_think</c> line in the prompt.
    /// </summary>
    public bool HasEnableThinkingSwitch => ChatTemplate?.Contains("enable_thinking", StringComparison.Ordinal) == true;
}

/// <summary>
/// Reads the key/value header of a GGUF file without loading the model.
/// </summary>
/// <remarks>
/// Needed because the one thing the app has to know before it can offer a reasoning switch — does this
/// model reason at all? — lives in <c>tokenizer.chat_template</c>, and the alternative was loading several
/// gigabytes of weights onto the GPU to answer a question about a settings screen. The header sits at the
/// front of the file; the tensor data behind it is never touched.
///
/// Every failure here is answered with null rather than an exception. This runs to decorate a status
/// line, and a file that turns out not to be a GGUF, or is half-downloaded, must not be able to take a
/// screen down with it.
/// </remarks>
public static class GgufMetadata
{
    private const uint Magic = 0x4655_4747; // "GGUF", little-endian.

    /// <summary>Refuses obvious garbage rather than trying to allocate it.</summary>
    private const long MaximumStringBytes = 32 * 1024 * 1024;

    private const long MaximumKeyValueCount = 100_000;

    private const long MaximumArrayLength = 100_000_000;

    private static readonly string[] WantedKeys =
    [
        "general.name",
        "general.architecture",
        "tokenizer.chat_template",
    ];

    public static GgufModelInfo? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (reader.ReadUInt32() != Magic)
            {
                return null;
            }

            var version = reader.ReadUInt32();

            // Version 1 wrote 32-bit lengths and is long extinct in the wild; refusing it is honest and
            // keeps the reader from misinterpreting every field after the first.
            if (version is < 2 or > 3)
            {
                return null;
            }

            _ = reader.ReadUInt64(); // tensor count - the tensors themselves are never read.
            var keyValueCount = (long)reader.ReadUInt64();
            if (keyValueCount is < 0 or > MaximumKeyValueCount)
            {
                return null;
            }

            var found = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var index = 0; index < keyValueCount && found.Count < WantedKeys.Length; index++)
            {
                var key = ReadString(reader);
                var type = (GgufType)reader.ReadUInt32();

                if (type == GgufType.String && Array.IndexOf(WantedKeys, key) >= 0)
                {
                    found[key] = ReadString(reader);
                    continue;
                }

                SkipValue(reader, type);
            }

            return found.Count == 0
                ? null
                : new GgufModelInfo(
                    found.GetValueOrDefault("general.name"),
                    found.GetValueOrDefault("general.architecture"),
                    found.GetValueOrDefault("tokenizer.chat_template"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or EndOfStreamException or InvalidDataException or OutOfMemoryException or ArgumentException)
        {
            return null;
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = (long)reader.ReadUInt64();
        if (length is < 0 or > MaximumStringBytes)
        {
            throw new InvalidDataException($"GGUF string length out of range: {length}.");
        }

        // Checked, because BinaryReader.ReadBytes returns a short buffer at end of file rather than
        // failing. A half-downloaded model would otherwise hand back the first half of its chat template
        // as though the file were whole, and the app would answer "this model reasons" from a fragment.
        var bytes = reader.ReadBytes((int)length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException($"GGUF string claims {length} bytes but the file ends after {bytes.Length}.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Steps over a value of <paramref name="type"/> without materialising it.
    /// </summary>
    /// <remarks>
    /// Most of the header is one array: <c>tokenizer.ggml.tokens</c> holds every token of the vocabulary,
    /// commonly 150 thousand strings, and it usually sits before the chat template. Skipping it by
    /// seeking, rather than reading it into a list nobody wants, is the difference between this taking a
    /// couple of milliseconds and it being too slow to put behind a button.
    /// </remarks>
    private static void SkipValue(BinaryReader reader, GgufType type)
    {
        switch (type)
        {
            case GgufType.UInt8 or GgufType.Int8 or GgufType.Bool:
                Skip(reader, 1);
                break;
            case GgufType.UInt16 or GgufType.Int16:
                Skip(reader, 2);
                break;
            case GgufType.UInt32 or GgufType.Int32 or GgufType.Float32:
                Skip(reader, 4);
                break;
            case GgufType.UInt64 or GgufType.Int64 or GgufType.Float64:
                Skip(reader, 8);
                break;
            case GgufType.String:
                Skip(reader, (long)reader.ReadUInt64());
                break;
            case GgufType.Array:
                SkipArray(reader);
                break;
            default:
                throw new InvalidDataException($"Unknown GGUF value type {(uint)type}.");
        }
    }

    private static void SkipArray(BinaryReader reader)
    {
        var elementType = (GgufType)reader.ReadUInt32();
        var count = (long)reader.ReadUInt64();
        if (count is < 0 or > MaximumArrayLength)
        {
            throw new InvalidDataException($"GGUF array length out of range: {count}.");
        }

        // A fixed-width array is one seek; only strings have to be walked element by element.
        var elementSize = elementType switch
        {
            GgufType.UInt8 or GgufType.Int8 or GgufType.Bool => 1,
            GgufType.UInt16 or GgufType.Int16 => 2,
            GgufType.UInt32 or GgufType.Int32 or GgufType.Float32 => 4,
            GgufType.UInt64 or GgufType.Int64 or GgufType.Float64 => 8,
            _ => 0,
        };

        if (elementSize > 0)
        {
            Skip(reader, count * elementSize);
            return;
        }

        for (var index = 0L; index < count; index++)
        {
            SkipValue(reader, elementType);
        }
    }

    private static void Skip(BinaryReader reader, long bytes)
    {
        if (bytes < 0)
        {
            throw new InvalidDataException($"GGUF field length out of range: {bytes}.");
        }

        var stream = reader.BaseStream;
        var target = stream.Position + bytes;
        if (target > stream.Length)
        {
            throw new EndOfStreamException("GGUF header claims a field that runs past the end of the file.");
        }

        stream.Seek(bytes, SeekOrigin.Current);
    }

    /// <summary>The value-type tags of the GGUF specification, in their defined order.</summary>
    private enum GgufType : uint
    {
        UInt8 = 0,
        Int8 = 1,
        UInt16 = 2,
        Int16 = 3,
        UInt32 = 4,
        Int32 = 5,
        Float32 = 6,
        Bool = 7,
        String = 8,
        Array = 9,
        UInt64 = 10,
        Int64 = 11,
        Float64 = 12,
    }
}
