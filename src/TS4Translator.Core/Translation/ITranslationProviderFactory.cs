using TS4Translator.Core.Providers;

namespace TS4Translator.Core.Translation;

/// <summary>
/// Builds ready-to-use <see cref="ITranslationProvider"/> instances from the user's saved profiles
/// (brief section 12) — hides which concrete provider type sits behind each one.
/// </summary>
public interface ITranslationProviderFactory
{
    /// <summary>Builds the engine described by a specific saved profile.</summary>
    /// <param name="reasoningOverride">
    /// Runs this engine with a different <see cref="ReasoningMode"/> than its profile stores, for one
    /// job only, without touching the saved setting.
    /// </param>
    /// <remarks>
    /// The override exists for "repensar": a handful of strings the bulk pass mangled are worth the cost
    /// of the model thinking, when the whole mod was not. Making the user open Configurações, flip the
    /// profile, come back, run it, and remember to flip it back is how that ends up not being done at
    /// all — and how the setting ends up left on for the next full run by accident.
    /// </remarks>
    /// <returns>The provider, or null if that profile no longer exists.</returns>
    Task<ITranslationProvider?> CreateForProfileAsync(
        int profileId,
        ReasoningMode? reasoningOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds whichever profile the user assigned to <paramref name="role"/>, applying the fallbacks in
    /// <see cref="TranslationPipelineSettings.ResolveProfileId"/>.
    /// </summary>
    /// <returns>The provider, or null if no usable profile is assigned to that role.</returns>
    Task<ITranslationProvider?> CreateForRoleAsync(TranslationRole role, CancellationToken cancellationToken = default);
}
