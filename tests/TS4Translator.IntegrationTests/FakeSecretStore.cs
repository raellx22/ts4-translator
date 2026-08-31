using TS4Translator.Core.Security;

namespace TS4Translator.IntegrationTests;

/// <summary>
/// In-memory <see cref="ISecretStore"/>. Lets profile tests run anywhere, including sandboxes with no
/// Windows user profile loaded, where the real DPAPI store cannot work. DPAPI itself has its own tests.
/// </summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public void Save(string key, string secret) => _secrets[key] = secret;

    public string? Load(string key) => _secrets.GetValueOrDefault(key);

    public void Delete(string key) => _secrets.Remove(key);
}
