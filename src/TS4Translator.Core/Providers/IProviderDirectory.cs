namespace TS4Translator.Core.Providers;

/// <param name="Id">The exact model id to send in requests.</param>
/// <param name="DisplayName">A friendlier name when the API supplies one; otherwise the id again.</param>
public sealed record ModelDescriptor(string Id, string? DisplayName)
{
    public string Label => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Id ? Id : $"{DisplayName} ({Id})";
}

/// <param name="Description">Ready-to-show line, e.g. "1.234.567 de 500.000 caracteres usados este mês".</param>
/// <param name="IsNearLimit">True when the account is close enough to its cap to be worth warning about.</param>
public sealed record ProviderUsage(string Description, bool IsNearLimit);

/// <summary>
/// Asks a provider what it can actually do right now: which models the account may use, and how much
/// of its quota is gone.
/// </summary>
/// <remarks>
/// Support is uneven and that is a fact about the providers, not a gap here. Model listing exists for
/// every OpenAI-shaped API plus Gemini and Anthropic. Usage exists only for DeepL — Gemini, OpenAI and
/// Anthropic expose quota through their billing consoles, not through a key-scoped endpoint, so the app
/// says so rather than inventing a number.
/// </remarks>
public interface IProviderDirectory
{
    /// <summary>Whether asking this engine for its model list is meaningful at all.</summary>
    bool SupportsModelListing(ProviderKind kind);

    /// <summary>Whether this engine reports quota usage to an ordinary API key.</summary>
    bool SupportsUsage(ProviderKind kind);

    /// <summary>Models the profile's credentials can actually use, sorted for display.</summary>
    Task<IReadOnlyList<ModelDescriptor>> ListModelsAsync(int profileId, CancellationToken cancellationToken = default);

    /// <returns>The account's usage, or null when this engine does not report any.</returns>
    Task<ProviderUsage?> GetUsageAsync(int profileId, CancellationToken cancellationToken = default);
}
