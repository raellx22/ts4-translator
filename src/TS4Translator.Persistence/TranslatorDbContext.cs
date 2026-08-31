using Microsoft.EntityFrameworkCore;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class TranslatorDbContext(DbContextOptions<TranslatorDbContext> options) : DbContext(options)
{
    internal DbSet<TranslationMemoryRecord> TranslationMemory => Set<TranslationMemoryRecord>();

    internal DbSet<ProjectRecord> Projects => Set<ProjectRecord>();

    internal DbSet<ProjectStringRecord> ProjectStrings => Set<ProjectStringRecord>();

    internal DbSet<ProviderProfileRecord> ProviderProfiles => Set<ProviderProfileRecord>();

    internal DbSet<TranslationPipelineRecord> TranslationPipelines => Set<TranslationPipelineRecord>();

    internal DbSet<ProjectStringRevisionRecord> ProjectStringRevisions => Set<ProjectStringRevisionRecord>();

    internal DbSet<GlossaryTermRecord> GlossaryTerms => Set<GlossaryTermRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TranslationMemoryRecord>()
            .HasIndex(r => new { r.OriginalHash, r.SourceLocale, r.TargetLocale })
            .IsUnique();

        modelBuilder.Entity<ProjectRecord>()
            .HasMany(p => p.SourceFiles)
            .WithOne(f => f.Project)
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProjectStringRecord>(entity =>
        {
            // SourceFile is part of the key: two unrelated mods imported into one project may ship an
            // STBL with the same group and instance, and without the file here the second import would
            // silently overwrite the first mod's strings.
            entity.HasIndex(r => new { r.ProjectId, r.SourceFile, r.StblGroup, r.StblFullInstance, r.KeyHash }).IsUnique();
            entity.HasOne(r => r.Project)
                .WithMany()
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectStringRevisionRecord>(entity =>
        {
            entity.HasOne(r => r.StringEntry)
                .WithMany()
                .HasForeignKey(r => r.StringEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GlossaryTermRecord>(entity =>
        {
            entity.HasOne(r => r.Project)
                .WithMany()
                .HasForeignKey(r => r.ProjectId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Stored as the enum's name ("DeepL", not 1) so the column stays readable and stable even if
        // ProviderKind's members are ever reordered. Deliberately NOT unique per kind: having two
        // profiles of the same engine (a local server and a remote one, two Gemini keys) is the point.
        modelBuilder.Entity<ProviderProfileRecord>()
            .Property(r => r.Kind)
            .HasConversion<string>();

        modelBuilder.Entity<TranslationPipelineRecord>(entity =>
        {
            // At most one pipeline per scope: one global row (ProjectId null) and at most one override
            // per project. SQLite treats NULLs as distinct in a unique index, which is exactly right
            // here - it constrains the per-project rows without blocking the single global one.
            entity.HasIndex(r => r.ProjectId).IsUnique();

            // Deleting a project takes its override with it, rather than leaving a row pointing at
            // nothing.
            entity.HasOne(r => r.Project)
                .WithMany()
                .HasForeignKey(r => r.ProjectId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
