using Microsoft.EntityFrameworkCore;
using TS4Translator.Persistence.Entities;

namespace TS4Translator.Persistence;

public sealed class TranslatorDbContext(DbContextOptions<TranslatorDbContext> options) : DbContext(options)
{
    internal DbSet<TranslationMemoryRecord> TranslationMemory => Set<TranslationMemoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TranslationMemoryRecord>()
            .HasIndex(r => new { r.OriginalHash, r.SourceLocale, r.TargetLocale })
            .IsUnique();
    }
}
