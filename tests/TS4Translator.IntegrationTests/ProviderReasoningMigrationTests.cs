using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

/// <summary>
/// Migrates a database that already holds engine profiles, the way a real user's does.
/// </summary>
/// <remarks>
/// Every other test here builds its database by running every migration against nothing, which proves
/// the schema and nothing about the upgrade. This project has twice shipped a migration that quietly
/// destroyed or orphaned what the user had already configured (DEC-020, and the ProviderConfigurations
/// rename), and the reasoning column was another candidate: the generated migration filled it with an
/// empty string, which is not the name of any <see cref="ReasoningMode"/> member and would have made
/// every existing profile unreadable. This is the test that actually walks that path.
/// </remarks>
public sealed class ProviderReasoningMigrationTests : IDisposable
{
    /// <summary>The last migration before the reasoning column existed.</summary>
    private const string BeforeReasoning = "20260830233243_AddTieredRoutingAndProjectPipelines";

    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-reasoning-migration-").FullName;
    private readonly ServiceProvider _services;

    public ProviderReasoningMigrationTests()
    {
        _services = new ServiceCollection()
            .AddPersistenceServices(Path.Combine(_tempDirectory, "upgrade.db"))
            .AddSingleton<ISecretStore>(new FakeSecretStore())
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task UpgradingADatabaseThatAlreadyHasProfiles_LeavesThemReadableAndSetToOff()
    {
        var factory = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>();

        await using (var old = await factory.CreateDbContextAsync())
        {
            await old.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeReasoning);

            // Written as raw SQL on purpose: the entity type has a Reasoning property now, and going
            // through EF would defeat the point of standing in for a row saved by an older build.
            await old.Database.ExecuteSqlRawAsync("""
                INSERT INTO ProviderProfiles (Name, Kind, Endpoint, Model, Temperature, HasApiKey)
                VALUES ('Qwen3 local', 'LocalModel', 'local://gguf', 'C:/models/qwen3.gguf', 0.0, 0);
                """);
        }

        await using (var upgraded = await factory.CreateDbContextAsync())
        {
            await upgraded.Database.MigrateAsync();
        }

        var profiles = await _services.GetRequiredService<IProviderProfileService>().ListAsync();

        var profile = Assert.Single(profiles);
        Assert.Equal("Qwen3 local", profile.Name);
        Assert.Equal(ProviderKind.LocalModel, profile.Kind);
        Assert.Equal("C:/models/qwen3.gguf", profile.Model);
        Assert.Equal(ReasoningMode.Off, profile.Reasoning);
    }
}
