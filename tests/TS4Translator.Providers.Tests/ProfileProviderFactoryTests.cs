using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Argos;
using TS4Translator.Providers.LocalModel;

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

        public ProviderProfile Add(
            int id,
            ProviderKind kind,
            string model = "m",
            string? apiKey = null,
            string endpoint = "https://example.test",
            ReasoningMode reasoning = ReasoningMode.Off)
        {
            var profile = new ProviderProfile(id, $"perfil-{id}", kind, endpoint, model, 0.0, apiKey is not null, reasoning);
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

        public Task<ProviderProfile> SaveAsync(int? profileId, string name, ProviderKind kind, string endpoint, string? model, double temperature, string? apiKey, ReasoningMode reasoning = ReasoningMode.Off, CancellationToken cancellationToken = default) =>
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
        return (new ProfileProviderFactory(profiles, new HttpClient(), new FakeArgosRuntimeManager(), new FakeLocalModelRuntime()), profiles);
    }

    /// <summary>The factory only wires the runtime in; nothing here should load a model.</summary>
    private sealed class FakeLocalModelRuntime : ILocalModelRuntime
    {
        /// <summary>What the last translation call was told to do about reasoning.</summary>
        public ReasoningMode? ObservedReasoning { get; private set; }

        public LocalModelStatus GetStatus(string? modelPath = null) =>
            new(LocalModelBackend.None, null, 0, "teste");

        public Task<IReadOnlyList<TranslationResultItem>> TranslateAsync(
            IReadOnlyList<TranslationRequestItem> items,
            System.Globalization.CultureInfo sourceLocale,
            System.Globalization.CultureInfo targetLocale,
            IReadOnlyList<TS4Translator.Core.Glossary.GlossaryTerm>? glossaryTerms,
            string? customInstructions,
            string? modelPath,
            ReasoningMode reasoning = ReasoningMode.Off,
            CancellationToken cancellationToken = default)
        {
            ObservedReasoning = reasoning;
            return Task.FromResult<IReadOnlyList<TranslationResultItem>>(
                [.. items.Select(item => new TranslationResultItem(item.Id, item.ProtectedText))]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    [InlineData(ProviderKind.LocalModel, "local-gguf")]
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

    /// <summary>
    /// The reasoning switch is stored on the profile and has to reach the engine that honours it —
    /// a setting saved in Configurações and dropped on the way to llama.cpp would look exactly like a
    /// model that ignores it.
    /// </summary>
    [Theory]
    [InlineData(ReasoningMode.Off)]
    [InlineData(ReasoningMode.On)]
    [InlineData(ReasoningMode.ModelDefault)]
    public async Task CreateForProfileAsync_LocalModel_CarriesTheProfilesReasoningModeToTheRuntime(ReasoningMode mode)
    {
        var profiles = new FakeProfileService();
        var runtime = new FakeLocalModelRuntime();
        var factory = new ProfileProviderFactory(profiles, new HttpClient(), new FakeArgosRuntimeManager(), runtime);
        profiles.Add(1, ProviderKind.LocalModel, model: "C:/models/whatever.gguf", reasoning: mode);

        var provider = await factory.CreateForProfileAsync(1);
        await provider!.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Hello.")],
            System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal(mode, runtime.ObservedReasoning);
    }

    /// <summary>
    /// "Repensar" spends reasoning on a handful of damaged lines without the user having to go and flip
    /// the profile — and, more importantly, without the flip being left on for the next whole mod.
    /// </summary>
    [Fact]
    public async Task CreateForProfileAsync_ReasoningOverride_WinsForThisRunAndDoesNotTouchTheProfile()
    {
        var profiles = new FakeProfileService();
        var runtime = new FakeLocalModelRuntime();
        var factory = new ProfileProviderFactory(profiles, new HttpClient(), new FakeArgosRuntimeManager(), runtime);
        profiles.Add(1, ProviderKind.LocalModel, model: "C:/models/qwen3.gguf", reasoning: ReasoningMode.Off);

        var provider = await factory.CreateForProfileAsync(1, ReasoningMode.On);
        await provider!.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Attractiveness Tuner")],
            System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal(ReasoningMode.On, runtime.ObservedReasoning);

        // The stored profile is untouched, so the next ordinary run is back to what the user configured.
        Assert.Equal(ReasoningMode.Off, (await profiles.GetAsync(1))!.Reasoning);
    }
}
