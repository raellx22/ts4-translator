using Microsoft.EntityFrameworkCore;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class TranslatorDbContext(DbContextOptions<TranslatorDbContext> options) : DbContext(options)
{
    internal DbSet<TranslationMemoryRecord> TranslationMemory => Set<TranslationMemoryRecord>();

    internal DbSet<ProjectRecord> Projects => Set<ProjectRecord>();

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
    }
}
