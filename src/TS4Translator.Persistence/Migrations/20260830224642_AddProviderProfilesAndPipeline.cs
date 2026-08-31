using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderProfilesAndPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generated migration dropped ProviderConfigurations first, which would have thrown
            // away every provider the user had set up. The old rows are carried into the new tables
            // below and only then is the old table dropped - same data-loss trap as DEC-020 and the
            // IsActive column in AddSourceFileAndProviderPerKind, this time on a table rename.
            migrationBuilder.CreateTable(
                name: "ProviderProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Temperature = table.Column<double>(type: "REAL", nullable: false),
                    HasApiKey = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TranslationPipelines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrimaryProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    RefinementProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReviewProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    DualEngineEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DifficultProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    DifficultyThreshold = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationPipelines", x => x.Id);
                });

            // Every previously configured engine becomes a named profile. The name falls back to the
            // kind when the old row had none, so nothing shows up blank in the new profiles table.
            migrationBuilder.Sql("""
                INSERT INTO ProviderProfiles (Name, Kind, Endpoint, Model, Temperature, HasApiKey)
                SELECT
                    -- The old table let a row keep a name typed for a different engine (the user could
                    -- switch the kind dropdown without retyping), so names here are neither meaningful
                    -- nor unique. Profile names are how roles are picked, so the kind is appended
                    -- whenever the stored name would be blank or ambiguous.
                    CASE
                        WHEN Name IS NULL OR TRIM(Name) = '' THEN Kind
                        WHEN Name = Kind THEN Name
                        ELSE Name || ' (' || Kind || ')'
                    END,
                    Kind, Endpoint, Model, Temperature, HasApiKey
                FROM ProviderConfigurations;
                """);

            // Whichever engine was active becomes the primary role, so the first translation after
            // upgrading behaves exactly as it did before. The other roles stay null and fall back to
            // primary (see TranslationPipelineSettings.ResolveProfileId).
            migrationBuilder.Sql($"""
                INSERT INTO TranslationPipelines (Id, PrimaryProfileId, RefinementProfileId, ReviewProfileId, DualEngineEnabled, DifficultProfileId, DifficultyThreshold)
                SELECT
                    1,
                    (SELECT p.Id FROM ProviderProfiles p
                     JOIN ProviderConfigurations c ON c.Kind = p.Kind
                     WHERE c.IsActive = 1 LIMIT 1),
                    NULL, NULL, 0, NULL, {TS4Translator.Core.Providers.TranslationPipelineSettings.DefaultDifficultThreshold};
                """);

            migrationBuilder.DropTable(
                name: "ProviderConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderProfiles");

            migrationBuilder.DropTable(
                name: "TranslationPipelines");

            migrationBuilder.CreateTable(
                name: "ProviderConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    HasApiKey = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Temperature = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderConfigurations_Kind",
                table: "ProviderConfigurations",
                column: "Kind",
                unique: true);
        }
    }
}
