using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Argos;

namespace TS4Translator.Providers.Tests;

public class ProfileProviderFactoryTests
{
    private sealed class FakeArgosRuntimeManager : IArgosRuntimeManager
    {
        public Task<ArgosRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArgosRuntimeStatus(true, true, "Ready"));

        public Task<ArgosRuntimeStatus> InstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArgosRuntimeStatus(true, true, "Ready"));

        public Task<IReadOnlyList<TranslationResultItem>> TranslateAsync(IReadOnlyList<TranslationRequestItem> items, string sourceCode, string targetCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TranslationResultItem>>(items.Select(item => new TranslationResultItem(item.Id, item.ProtectedText)).ToList());
    }

    private sealed class FakeProfileService : IProviderProfileService
    {
        private readonly Dictionary<int, ProviderProfile> _profiles = [];
        private readonly Dictionary<int, string> _keys = [];
        private TranslationPipelineSettings _settings = new();

        public ProviderProfile Add(int id, ProviderKind kind, string model = "m", string? apiKey = null, string endpoint = "https://example.test")
        {
            var profile = new ProviderProfile(id, $"perfil-{id}", kind, endpoint, model, 0.0, apiKey is not null);
            _profiles[id] = profile;
            if (apiKey is not null)
            {
                _keys[id] = apiKey;
            }

            return profile;
        }

        public void SetPipeline(TranslationPipelineSettings settings) => _settings = settings;

        public Task<IReadOnlyList<ProviderProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderProfile>>([.. _profiles.Values]);

        public Task<ProviderProfile?> GetAsync(int profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.GetValueOrDefault(profileId));

        public Task<ProviderProfile> SaveAsync(int? profileId, string name, ProviderKind kind, string endpoint, string? model, double temperature, string? apiKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> GetApiKeyAsync(int profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.GetValueOrDefault(profileId));

        public Task<TranslationPipelineSettings> GetPipelineSettingsAsync(Guid? projectId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SavePipelineSettingsAsync(TranslationPipelineSettings settings, Guid? projectId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task RemovePipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static (ProfileProviderFactory Factory, FakeProfileService Profiles) Create()
    {
        var profiles = new FakeProfileService();
        return (new ProfileProviderFactory(profiles, new HttpClient(), new FakeArgosRuntimeManager()), profiles);
    }

    [Fact]
    public async Task CreateForProfileAsync_UnknownProfile_ReturnsNull()
    {
        var (factory, _) = Create();

        Assert.Null(await factory.CreateForProfileAsync(42));
    }

    [Theory]
    [InlineData(ProviderKind.OpenAICompatible, "openai-compatible")]
    [InlineData(ProviderKind.OpenAI, "openai-compatible")]
    [InlineData(ProviderKind.GoogleGemini, "google-gemini")]
    [InlineData(ProviderKind.Anthropic, "anthropic")]
    [InlineData(ProviderKind.Argos, "argos")]
    [InlineData(ProviderKind.GoogleTranslate, "google-translate")]
    [InlineData(ProviderKind.DeepL, "deepl")]
    public async Task CreateForProfileAsync_BuildsTheRightEngineForEveryKind(ProviderKind kind, string expectedNamePrefix)
    {
        var (factory, profiles) = Create();
        profiles.Add(1, kind, apiKey: "k");

        var provider = await factory.CreateForProfileAsync(1);

        Assert.NotNull(provider);
        Assert.StartsWith(expectedNamePrefix, provider.Name, StringComparison.Ordinal);
    }

    /// <summary>The role fallbacks are what let a user set only the primary engine and still refine.</summary>
    [Fact]
    public async Task CreateForRoleAsync_RefinementUnset_FallsBackToPrimary()
    {
        var (factory, profiles) = Create();
        profiles.Add(1, ProviderKind.Argos);
        profiles.Add(2, ProviderKind.GoogleTranslate);
        profiles.SetPipeline(new TranslationPipelineSettings(PrimaryProfileId: 1));

        var provider = await factory.CreateForRoleAsync(TranslationRole.Refinement);

        Assert.StartsWith("argos", provider!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateForRoleAsync_ReviewUnset_FallsBackThroughRefinement()
    {
        var (factory, profiles) = Create();
        profiles.Add(1, ProviderKind.Argos);
        profiles.Add(2, ProviderKind.GoogleTranslate);
        profiles.SetPipeline(new TranslationPipelineSettings(PrimaryProfileId: 1, RefinementProfileId: 2));

        var provider = await factory.CreateForRoleAsync(TranslationRole.Review);

        Assert.StartsWith("google-translate", provider!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateForRoleAsync_NothingAssigned_ReturnsNull()
    {
        var (factory, _) = Create();

        Assert.Null(await factory.CreateForRoleAsync(TranslationRole.Primary));
    }

    [Fact]
    public async Task CreateForProfileAsync_ProfileWithoutAKey_DoesNotAskForOne()
    {
        var (factory, profiles) = Create();
        profiles.Add(1, ProviderKind.Argos);

        Assert.NotNull(await factory.CreateForProfileAsync(1));
    }
}
