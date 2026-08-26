using Microsoft.EntityFrameworkCore;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class TranslatorDbContext(DbContextOptions<TranslatorDbContext> options) : DbContext(options)
{
    internal DbSet<TranslationMemoryRecord> TranslationMemory => Set<TranslationMemoryRecord>();

    internal DbSet<ProjectRecord> Projects => Set<ProjectRecord>();

    internal DbSet<ProjectStringRecord> ProjectStrings => Set<ProjectStringRecord>();

    internal DbSet<ProviderConfigurationRecord> ProviderConfigurations => Set<ProviderConfigurationRecord>();

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
            entity.HasIndex(r => new { r.ProjectId, r.StblGroup, r.StblFullInstance, r.KeyHash }).IsUnique();
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
    }
}
