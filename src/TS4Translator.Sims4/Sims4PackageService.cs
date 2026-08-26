using LlamaLogic.Packages;
using LlamaLogic.Packages.Models;
using TS4Translator.Core.Packages;

namespace TS4Translator.Sims4;

/// <summary>
/// <see cref="IPackageService"/> backed by <c>LlamaLogic.Packages</c>. No LlamaLogic type crosses
/// this project's boundary — callers only ever see <see cref="StblKey"/>/<see cref="StringTableEntry"/>.
/// </summary>
public sealed class Sims4PackageService : IPackageService
{
    public async Task<IReadOnlyList<StblKey>> FindStringTablesAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        await using var package = await DataBasePackedFile.FromPathAsync(packagePath).ConfigureAwait(false);

        return package.Keys
            .Where(key => key.Type == ResourceType.StringTable)
            .Select(key => new StblKey(key.Group, key.FullInstance))
            .ToList();
    }

    public async Task<IReadOnlyList<StringTableEntry>> ReadStringTableAsync(string packagePath, StblKey key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        await using var package = await DataBasePackedFile.FromPathAsync(packagePath).ConfigureAwait(false);
        var resourceKey = SmartSimStringTableService.ToResourceKey(key);
        var model = await package.GetStringTableAsync(resourceKey, cancellationToken: cancellationToken).ConfigureAwait(false);

        return model.KeyHashes
            .Select(hash => new StringTableEntry(hash, model.Get(hash)))
            .ToList();
    }

    public async Task WriteStringTableAsync(string outputPackagePath, StblKey key, IReadOnlyList<StringTableEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPackagePath);
        ArgumentNullException.ThrowIfNull(entries);

        if (File.Exists(outputPackagePath))
        {
            throw new IOException($"The output package already exists: '{outputPackagePath}'.");
        }

        var model = new StringTableModel();
        foreach (var entry in entries)
        {
            model.Set(entry.KeyHash, entry.Value);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPackagePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var package = new DataBasePackedFile();
        var resourceKey = SmartSimStringTableService.ToResourceKey(key);
        await package.SetAsync(resourceKey, model, CompressionMode.Auto, cancellationToken).ConfigureAwait(false);
        await package.SaveAsAsync(outputPackagePath, ResourceKeyOrder.TypeGroupInstance, cancellationToken).ConfigureAwait(false);
    }
}
