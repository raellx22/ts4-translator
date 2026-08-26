using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using TS4Translator.Core.Security;

namespace TS4Translator.Infrastructure;

/// <summary>
/// <see cref="ISecretStore"/> backed by Windows DPAPI (<see cref="ProtectedData"/>), scoped to the
/// current Windows user. Ciphertext lives under <see cref="AppDataPaths.Data"/>, never in SQLite or
/// plaintext JSON (brief section 19). Simpler than Windows Credential Manager for this app's needs —
/// one secret per named key, no credential-target UI — while still never touching plaintext at rest.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore() : this(Path.Combine(AppDataPaths.Data, "Secrets"))
    {
    }

    /// <param name="directory">Where secret files are written. Exposed for tests; production code should use the parameterless constructor.</param>
    public DpapiSecretStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public void Save(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePathFor(key), ciphertext);
    }

    public string? Load(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = FilePathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var ciphertext = File.ReadAllBytes(path);
        var plaintext = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }

    public void Delete(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = FilePathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string FilePathFor(string key)
    {
        var safeName = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_directory, $"{safeName}.bin");
    }
}
