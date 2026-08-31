using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTieredRoutingAndProjectPipelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DualEngineEnabled",
                table: "TranslationPipelines",
                newName: "TieredRoutingEnabled");

            // The scaffolded version renamed DifficultyThreshold to MediumThreshold, which would have
            // turned the user's "send everything at or above 35 to the strong engine" into "35 is the
            // MIDDLE band" while DifficultThreshold defaulted to 0 - routing every single string to the
            // most expensive engine on the next run. The old value is the *difficult* threshold and has
            // to stay that.
            migrationBuilder.RenameColumn(
                name: "DifficultyThreshold",
                table: "TranslationPipelines",
                newName: "DifficultThreshold");

            migrationBuilder.AddColumn<int>(
                name: "MediumThreshold",
                table: "TranslationPipelines",
                type: "INTEGER",
                nullable: false,
                defaultValue: TS4Translator.Core.Providers.TranslationPipelineSettings.DefaultMediumThreshold);

            migrationBuilder.AddColumn<int>(
                name: "MediumProfileId",
                table: "TranslationPipelines",
                type: "INTEGER",
                nullable: true);

            // A row written before this migration has no medium band; keeping the two thresholds
            // ordered means the new band simply sits unused until the user configures an engine for it.
            migrationBuilder.Sql(
                $"UPDATE TranslationPipelines SET MediumThreshold = MIN(DifficultThreshold, {TS4Translator.Core.Providers.TranslationPipelineSettings.DefaultMediumThreshold});");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "TranslationPipelines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationPipelines_ProjectId",
                table: "TranslationPipelines",
                column: "ProjectId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TranslationPipelines_Projects_ProjectId",
                table: "TranslationPipelines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TranslationPipelines_Projects_ProjectId",
                table: "TranslationPipelines");

            migrationBuilder.DropIndex(
                name: "IX_TranslationPipelines_ProjectId",
                table: "TranslationPipelines");

            migrationBuilder.DropColumn(
                name: "DifficultThreshold",
                table: "TranslationPipelines");

            migrationBuilder.DropColumn(
                name: "MediumProfileId",
                table: "TranslationPipelines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "TranslationPipelines");

            migrationBuilder.RenameColumn(
                name: "TieredRoutingEnabled",
                table: "TranslationPipelines",
                newName: "DualEngineEnabled");

            migrationBuilder.RenameColumn(
                name: "MediumThreshold",
                table: "TranslationPipelines",
                newName: "DifficultyThreshold");
        }
    }
}
