using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranslationMemory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginalHash = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalText = table.Column<string>(type: "TEXT", nullable: false),
                    SourceLocale = table.Column<string>(type: "TEXT", nullable: false),
                    TargetLocale = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedText = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    ProfileName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationMemory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationMemory_OriginalHash_SourceLocale_TargetLocale",
                table: "TranslationMemory",
                columns: new[] { "OriginalHash", "SourceLocale", "TargetLocale" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranslationMemory");
        }
    }
}
