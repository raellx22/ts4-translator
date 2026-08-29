using TS4Translator.Core.Providers;

namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing <see cref="TS4Translator.Core.Providers.IProviderConfigurationService"/>. Never exposed outside this project.</summary>
internal sealed class ProviderConfigurationRecord
{
    public int Id { get; set; }

    /// <summary>Stored as its enum name (e.g. "DeepL"), not an int, so the value stays readable/stable across enum reordering.</summary>
    public ProviderKind Kind { get; set; }

    public required string Name { get; set; }

    public required string Endpoint { get; set; }

    /// <summary>Null for provider kinds with no model concept (e.g. DeepL).</summary>
    public string? Model { get; set; }

    public double Temperature { get; set; }

    public bool HasApiKey { get; set; }
}
