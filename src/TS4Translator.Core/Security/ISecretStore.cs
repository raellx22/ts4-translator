namespace TS4Translator.Core.Security;

/// <summary>
/// Stores small secrets (API keys) encrypted at rest, never in plaintext SQLite/JSON (brief section 19).
/// </summary>
public interface ISecretStore
{
    void Save(string key, string secret);

    string? Load(string key);

    void Delete(string key);
}
