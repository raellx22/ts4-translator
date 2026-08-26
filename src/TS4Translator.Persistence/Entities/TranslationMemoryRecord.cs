namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing <see cref="TS4Translator.Core.Memory.ITranslationMemory"/>. Never exposed outside this project.</summary>
internal sealed class TranslationMemoryRecord
{
    public int Id { get; set; }

    /// <summary>SHA-256 hex digest of the trimmed <see cref="OriginalText"/>, used for exact-match lookup.</summary>
    public required string OriginalHash { get; set; }

    public required string OriginalText { get; set; }

    /// <summary>BCP-47 name of the source <see cref="System.Globalization.CultureInfo"/> (e.g. "en-US").</summary>
    public required string SourceLocale { get; set; }

    /// <summary>BCP-47 name of the target <see cref="System.Globalization.CultureInfo"/> (e.g. "pt-BR").</summary>
    public required string TargetLocale { get; set; }

    public required string TranslatedText { get; set; }

    public required string Provider { get; set; }

    public string? ProfileName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
