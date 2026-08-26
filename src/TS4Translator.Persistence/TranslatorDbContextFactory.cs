using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TS4Translator.Persistence;

/// <summary>
/// Design-time-only factory used by <c>dotnet ef migrations add</c>. Never used at runtime — the app
/// always resolves <see cref="TranslatorDbContext"/> through DI with the real database path
/// (see <see cref="ServiceCollectionExtensions.AddPersistenceServices"/>).
/// </summary>
public sealed class TranslatorDbContextFactory : IDesignTimeDbContextFactory<TranslatorDbContext>
{
    public TranslatorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TranslatorDbContext>();
        optionsBuilder.UseSqlite("Data Source=design-time.db");
        return new TranslatorDbContext(optionsBuilder.Options);
    }
}
