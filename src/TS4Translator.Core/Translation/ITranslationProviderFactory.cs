using TS4Translator.Core.Providers;

namespace TS4Translator.Core.Translation;

/// <summary>
/// Builds ready-to-use <see cref="ITranslationProvider"/> instances from the user's saved profiles
/// (brief section 12) — hides which concrete provider type sits behind each one.
/// </summary>
public interface ITranslationProviderFactory
{
    /// <summary>Builds the engine described by a specific saved profile.</summary>
    /// <returns>The provider, or null if that profile no longer exists.</returns>
    Task<ITranslationProvider?> CreateForProfileAsync(int profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds whichever profile the user assigned to <paramref name="role"/>, applying the fallbacks in
    /// <see cref="TranslationPipelineSettings.ResolveProfileId"/>.
    /// </summary>
    /// <returns>The provider, or null if no usable profile is assigned to that role.</returns>
    Task<ITranslationProvider?> CreateForRoleAsync(TranslationRole role, CancellationToken cancellationToken = default);
}
