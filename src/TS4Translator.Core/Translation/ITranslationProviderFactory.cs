namespace TS4Translator.Core.Translation;

/// <summary>
/// Builds a ready-to-use <see cref="ITranslationProvider"/> from whatever the user has configured
/// (brief section 12) — hides which concrete provider type and configuration source is behind it.
/// </summary>
public interface ITranslationProviderFactory
{
    /// <returns>The configured provider, or null if the user has not configured one yet.</returns>
    Task<ITranslationProvider?> CreateActiveProviderAsync(CancellationToken cancellationToken = default);
}
