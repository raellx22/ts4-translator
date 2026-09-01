using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Anthropic;
using TS4Translator.Providers.Argos;
using TS4Translator.Providers.DeepL;
using TS4Translator.Providers.GoogleGemini;
using TS4Translator.Providers.GoogleTranslate;
using TS4Translator.Providers.LocalModel;
using TS4Translator.Providers.OpenAICompatible;

namespace TS4Translator.Providers;

/// <summary>
/// Builds the concrete <see cref="ITranslationProvider"/> behind a saved profile - the single place
/// that knows about every provider implementation this project has; nothing else needs to.
/// </summary>
public sealed class ProfileProviderFactory(
    IProviderProfileService profileService,
    HttpClient httpClient,
    IArgosRuntimeManager argosRuntimeManager,
    ILocalModelRuntime localModelRuntime) : ITranslationProviderFactory
{
    public async Task<ITranslationProvider?> CreateForProfileAsync(int profileId, CancellationToken cancellationToken = default)
    {
        var profile = await profileService.GetAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        var apiKey = profile.HasApiKey
            ? await profileService.GetApiKeyAsync(profileId, cancellationToken).ConfigureAwait(false)
            : null;

        return Build(profile, apiKey);
    }

    public async Task<ITranslationProvider?> CreateForRoleAsync(TranslationRole role, CancellationToken cancellationToken = default)
    {
        // Roles here are resolved from the global pipeline; the editor passes a project id explicitly
        // when it needs a project override.
        var settings = await profileService.GetPipelineSettingsAsync(projectId: null, cancellationToken).ConfigureAwait(false);
        var profileId = settings.ResolveProfileId(role);

        return profileId is int id
            ? await CreateForProfileAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private ITranslationProvider Build(ProviderProfile profile, string? apiKey) => profile.Kind switch
    {
        // OpenAI's hosted API and any OpenAI-compatible server speak the same wire contract; they are
        // separate kinds only so the UI can preset the right endpoint and make "paid cloud" versus
        // "your own machine" obvious at a glance.
        ProviderKind.OpenAICompatible or ProviderKind.OpenAI => new OpenAICompatibleProvider(
            httpClient,
            new OpenAICompatibleProviderOptions(profile.Endpoint, profile.Model ?? string.Empty, apiKey, profile.Temperature)),

        ProviderKind.DeepL => new DeepLProvider(
            httpClient, new DeepLProviderOptions(profile.Endpoint, apiKey)),

        ProviderKind.GoogleGemini => new GoogleGeminiProvider(
            httpClient, new GoogleGeminiProviderOptions(profile.Endpoint, profile.Model ?? "gemini-2.5-flash", apiKey)),

        ProviderKind.Anthropic => new AnthropicProvider(
            httpClient, new AnthropicProviderOptions(profile.Endpoint, profile.Model ?? "claude-sonnet-5", apiKey, profile.Temperature)),

        ProviderKind.Argos => new ArgosTranslateProvider(argosRuntimeManager),

        // Model is a path to a .gguf here rather than a remote model id - the file is the model.
        ProviderKind.LocalModel => new LocalModelProvider(localModelRuntime, profile.Model),

        ProviderKind.GoogleTranslate => new GoogleTranslateProvider(httpClient),

        _ => throw new NotSupportedException($"Unknown provider kind: {profile.Kind}."),
    };
}
