using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Anthropic;
using TS4Translator.Providers.GoogleGemini;
using TS4Translator.Providers.OpenAICompatible;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// Guards the one line of prompt that a real translation run proved dangerous.
/// </summary>
/// <remarks>
/// Skarrd_Perverted_Expansion_Pack through Gemini 3.1 Flash-Lite came back with 29 strings where a
/// trailing number had been replaced by a token that was never in the source — "clothing removal
/// pantsfun 13" became "Remoção de roupa: calça __TS4_TOKEN_0001__". The invented token was always
/// 0001, because the system prompt spelled out __TS4_TOKEN_0001__ as its example and the model copied
/// the literal it had been shown.
///
/// Every chat provider keeps its own copy of the system prompt, so all three are asserted here: a fix
/// applied to one and forgotten in the others is exactly the shape of this bug coming back.
/// </remarks>
public sealed class TranslationPromptTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Anything matching this is a token a model can lift straight out of its instructions.</summary>
    private static readonly Regex CopyableToken = new(@"__TS4_TOKEN_\d{4}__", RegexOptions.CultureInvariant);

    /// <summary>The item carries no token, so anything matching in the request came from the prompt.</summary>
    private static readonly TranslationRequestItem PlainItem = new("1", "clothing removal pantsfun 13");

    public static TheoryData<string> ProviderNames => ["anthropic", "openai", "gemini"];

    private static async Task<string> RequestBodyOf(string providerName)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ResponseFor(providerName));
        var client = new HttpClient(handler);

        ITranslationProvider provider = providerName switch
        {
            "anthropic" => new AnthropicProvider(client, new AnthropicProviderOptions("https://api.anthropic.com", "claude-sonnet-5", "test-key")),
            "openai" => new OpenAICompatibleProvider(client, new OpenAICompatibleProviderOptions("http://localhost:1234/v1", "test-model")),
            _ => new GoogleGeminiProvider(client, new GoogleGeminiProviderOptions("https://generativelanguage.googleapis.com", "gemini-2.5-flash", "test-key")),
        };

        await provider.TranslateBatchAsync([PlainItem], English, PortugueseBrazil);

        Assert.NotNull(handler.LastRequestBody);
        return handler.LastRequestBody!;
    }

    private static string ResponseFor(string providerName)
    {
        const string ModelJson = """[{"id":"1","translation":"Remoção de roupa: calça 13"}]""";

        return providerName switch
        {
            "anthropic" => System.Text.Json.JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = ModelJson } } }),
            "openai" => System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = ModelJson } } } }),
            _ => System.Text.Json.JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = ModelJson } } } } } }),
        };
    }

    /// <summary>
    /// The regression itself: no chat provider may hand a model a complete, copyable token.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task SystemPrompt_NeverContainsACopyableToken(string providerName)
    {
        var body = await RequestBodyOf(providerName);

        Assert.DoesNotMatch(CopyableToken, body);
    }

    /// <summary>
    /// Not even the marker's prefix may appear. A 7B model copied the bare stem as its entire answer for
    /// strings it would not translate, and the stem was in the prompt because the earlier fix only
    /// removed the four digits.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task SystemPrompt_NeverContainsTheMarkerStemEither(string providerName)
    {
        var body = await RequestBodyOf(providerName);

        Assert.DoesNotContain("__TS4_TOKEN", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rule still has to be stated, or the placeholders that are real stop being copied through.</summary>
    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task SystemPrompt_StillExplainsWhatAPlaceholderIs(string providerName)
    {
        var body = await RequestBodyOf(providerName);

        Assert.Contains("placeholder", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four-digit number", body, StringComparison.Ordinal);
    }

    /// <summary>Describing the shape is not enough on its own; inventing one has to be forbidden outright.</summary>
    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task SystemPrompt_ForbidsInventingAPlaceholder(string providerName)
    {
        var body = await RequestBodyOf(providerName);

        Assert.Contains("NEVER write a placeholder that was not already", body, StringComparison.Ordinal);
    }
}
