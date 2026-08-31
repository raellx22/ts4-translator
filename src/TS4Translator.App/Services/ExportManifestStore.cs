using System.Text.Json;
using System.Text.Json.Serialization;
using TS4Translator.Infrastructure;

namespace TS4Translator.App.Services;

/// <param name="PartNumber">1-based index of this file within the export.</param>
/// <param name="FileName">The file the user was given, for naming it back to them.</param>
/// <param name="StringIds">Exactly which project strings went into that file, in order.</param>
public sealed record ExportManifestPart(int PartNumber, string FileName, IReadOnlyList<int> StringIds);

/// <param name="ProjectId">Which project was exported.</param>
/// <param name="ExportedAt">When, so a stale manifest can be recognised.</param>
/// <param name="Scope">The scope the user picked, echoed back at import time.</param>
/// <param name="Parts">One entry per file written.</param>
public sealed record ExportManifest(
    Guid ProjectId,
    DateTimeOffset ExportedAt,
    string Scope,
    IReadOnlyList<ExportManifestPart> Parts);

/// <summary>
/// Remembers what each exported chat bundle contained, so an import can tell a complete reply from a
/// truncated one.
/// </summary>
/// <remarks>
/// Knowing which strings were sent is what makes "23 de 1000 textos não voltaram" possible, and that is
/// the single most valuable thing the app can say about a chat round trip — a model that quietly stops
/// two thirds of the way through produces a file that looks perfectly healthy. The app used to hold that
/// knowledge in a field on the page's ViewModel, which meant it survived until the user closed the
/// window. The actual workflow is: export, close the app, spend twenty minutes in a chat window, reopen,
/// import. So it goes on disk.
///
/// It is a convenience, never a requirement: an import with no manifest still matches by id and simply
/// says that completeness could not be checked.
/// </remarks>
public sealed class ExportManifestStore
{
    private readonly string _directory = AppDataPaths.Exports;

    public async Task SaveAsync(ExportManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var json = JsonSerializer.Serialize(manifest, ExportManifestJsonContext.Default.ExportManifest);
        await File.WriteAllTextAsync(PathFor(manifest.ProjectId), json, cancellationToken);
    }

    public async Task<ExportManifest?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(projectId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize(json, ExportManifestJsonContext.Default.ExportManifest);
        }
        catch (JsonException)
        {
            // A manifest we cannot read is worth exactly as much as no manifest, and losing the import
            // over it would be absurd.
            return null;
        }
    }

    private string PathFor(Guid projectId) => Path.Combine(_directory, $"{projectId:N}.json");
}

/// <summary>
/// Source-generated serialization for the manifest.
/// </summary>
/// <remarks>
/// Not a style preference: the app builds Release with trimming analysis on, and the reflection-based
/// <see cref="JsonSerializer"/> overloads are an error there (IL2026) because the types a reflective
/// serializer needs cannot be proven reachable. A generated context is checked at compile time and
/// survives trimming.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExportManifest))]
internal sealed partial class ExportManifestJsonContext : JsonSerializerContext;
