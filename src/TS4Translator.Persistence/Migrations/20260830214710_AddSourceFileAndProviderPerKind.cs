using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceFileAndProviderPerKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectStrings_ProjectId_StblGroup_StblFullInstance_KeyHash",
                table: "ProjectStrings");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProviderConfigurations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Before this migration the table held at most one row and it was, by definition, the
            // active configuration. Without this the new column would default it to 0, GetActiveAsync
            // would return null, and the user would silently lose the provider they had configured -
            // the same class of data loss as DEC-020, this time caused by a schema change.
            migrationBuilder.Sql("UPDATE ProviderConfigurations SET IsActive = 1;");

            migrationBuilder.AddColumn<string>(
                name: "SourceFile",
                table: "ProjectStrings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderConfigurations_Kind",
                table: "ProviderConfigurations",
                column: "Kind",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStrings_ProjectId_SourceFile_StblGroup_StblFullInstance_KeyHash",
                table: "ProjectStrings",
                columns: new[] { "ProjectId", "SourceFile", "StblGroup", "StblFullInstance", "KeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProviderConfigurations_Kind",
                table: "ProviderConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectStrings_ProjectId_SourceFile_StblGroup_StblFullInstance_KeyHash",
                table: "ProjectStrings");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "SourceFile",
                table: "ProjectStrings");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStrings_ProjectId_StblGroup_StblFullInstance_KeyHash",
                table: "ProjectStrings",
                columns: new[] { "ProjectId", "StblGroup", "StblFullInstance", "KeyHash" },
                unique: true);
        }
    }
}
