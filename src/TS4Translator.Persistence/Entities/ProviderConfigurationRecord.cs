namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing <see cref="TS4Translator.Core.Providers.IProviderConfigurationService"/>. Never exposed outside this project.</summary>
internal sealed class ProviderConfigurationRecord
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Endpoint { get; set; }

    public required string Model { get; set; }

    public double Temperature { get; set; }

    public bool HasApiKey { get; set; }
}
