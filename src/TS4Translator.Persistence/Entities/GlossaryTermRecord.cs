namespace TS4Translator.Persistence.Entities;

/// <summary>EF Core entity backing <see cref="TS4Translator.Core.Glossary.IGlossaryService"/>.</summary>
internal sealed class GlossaryTermRecord
{
    public int Id { get; set; }

    public required string Original { get; set; }

    public required string Translation { get; set; }

    public bool NeverTranslate { get; set; }

    public bool CaseSensitive { get; set; }

    public string? Notes { get; set; }

    /// <summary>Null for a global term; set for a term scoped to one project.</summary>
    public Guid? ProjectId { get; set; }

    public ProjectRecord? Project { get; set; }
}
