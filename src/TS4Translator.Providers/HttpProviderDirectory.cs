using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TS4Translator.Core.Providers;

namespace TS4Translator.Providers;

/// <summary>
/// <see cref="IProviderDirectory"/> over each provider's own discovery endpoints.
/// </summary>
/// <remarks>
/// Every one of these is a plain GET whose response shape is read defensively: a provider adding
/// fields must never break the app, and a provider the user has no access to must surface as a clear
/// failure rather than an empty list that looks like "no models exist".
/// </remarks>
public sealed class HttpProviderDirectory(IProviderProfileService profileService, HttpClient httpClient) : IProviderDirectory
{
    private const string AnthropicApiVersion = "2023-06-01";

    public bool SupportsModelListing(ProviderKind kind) => kind is
        ProviderKind.OpenAI or ProviderKind.OpenAICompatible or ProviderKind.GoogleGemini or ProviderKind.Anthropic;

    // DeepL is the only one of these that reports quota to an ordinary API key. The others keep it in
    // a billing console behind a different credential.
    public bool SupportsUsage(ProviderKind kind) => kind is ProviderKind.DeepL;

    public async Task<IReadOnlyList<ModelDescriptor>> ListModelsAsync(int profileId, CancellationToken cancellationToken = default)
    {
        var profile = await profileService.GetAsync(profileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Perfil não encontrado.");

        if (!SupportsModelListing(profile.Kind))
        {
            return [];
        }

        var apiKey = await profileService.GetApiKeyAsync(profileId, cancellationToken).ConfigureAwait(false);
        var endpoint = profile.Endpoint.TrimEnd('/');

        return profile.Kind switch
        {
            ProviderKind.GoogleGemini => await ListGeminiModelsAsync(endpoint, apiKey, cancellationToken).ConfigureAwait(false),
            ProviderKind.Anthropic => await ListAnthropicModelsAsync(endpoint, apiKey, cancellationToken).ConfigureAwait(false),
            _ => await ListOpenAiShapedModelsAsync(endpoint, apiKey, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<ProviderUsage?> GetUsageAsync(int profileId, CancellationToken cancellationToken = default)
    {
        var profile = await profileService.GetAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null || !SupportsUsage(profile.Kind))
        {
            return null;
        }

        var apiKey = await profileService.GetApiKeyAsync(profileId, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{profile.Endpoint.TrimEnd('/')}/v2/usage");
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var used = ReadLong(document.RootElement, "character_count");
        var limit = ReadLong(document.RootElement, "character_limit");

        if (used is null || limit is null || limit == 0)
        {
            return null;
        }

        var percentage = 100.0 * used.Value / limit.Value;
        return new ProviderUsage(
            $"{used.Value:N0} de {limit.Value:N0} caracteres usados ({percentage:F1}%)",
            percentage >= 80);
    }

    /// <summary>OpenAI and every server that copies its contract answer <c>GET /models</c> with <c>{ "data": [ { "id": ... } ] }</c>.</summary>
    private async Task<IReadOnlyList<ModelDescriptor>> ListOpenAiShapedModelsAsync(string endpoint, string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return Sorted(data.EnumerateArray()
            .Select(item => ReadString(item, "id"))
            .Where(id => id is not null)
            .Select(id => new ModelDescriptor(id!, null)));
    }

    /// <summary>Gemini answers <c>GET /v1beta/models</c> with names like <c>models/gemini-2.5-flash</c>.</summary>
    private async Task<IReadOnlyList<ModelDescriptor>> ListGeminiModelsAsync(string endpoint, string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/v1beta/models");
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var descriptors = new List<ModelDescriptor>();
        foreach (var model in models.EnumerateArray())
        {
            var name = ReadString(model, "name");
            if (name is null)
            {
                continue;
            }

            // Only models that can actually generate text are useful here; the list also carries
            // embedding models, which would fail the moment they were selected.
            if (model.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array &&
                !methods.EnumerateArray().Any(method => method.ValueKind == JsonValueKind.String && method.GetString() == "generateContent"))
            {
                continue;
            }

            // The id the app has to send omits the "models/" prefix the listing uses.
            var id = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
            descriptors.Add(new ModelDescriptor(id, ReadString(model, "displayName")));
        }

        return Sorted(descriptors);
    }

    /// <summary>Anthropic answers <c>GET /v1/models</c> with <c>{ "data": [ { "id": ..., "display_name": ... } ] }</c>.</summary>
    private async Task<IReadOnlyList<ModelDescriptor>> ListAnthropicModelsAsync(string endpoint, string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/v1/models");
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        }

        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicApiVersion);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return Sorted(data.EnumerateArray()
            .Select(item => (Id: ReadString(item, "id"), Display: ReadString(item, "display_name")))
            .Where(item => item.Id is not null)
            .Select(item => new ModelDescriptor(item.Id!, item.Display)));
    }

    /// <summary>Newest-looking first: these lists are long and the user is almost always after a recent model.</summary>
    private static IReadOnlyList<ModelDescriptor> Sorted(IEnumerable<ModelDescriptor> descriptors) =>
        [.. descriptors.DistinctBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .OrderByDescending(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase)];

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
