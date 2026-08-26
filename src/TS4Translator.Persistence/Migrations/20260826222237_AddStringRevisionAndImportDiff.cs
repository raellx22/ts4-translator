using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStringRevisionAndImportDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectStringRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StringEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousText = table.Column<string>(type: "TEXT", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectStringRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectStringRevisions_ProjectStrings_StringEntryId",
                        column: x => x.StringEntryId,
                        principalTable: "ProjectStrings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStringRevisions_StringEntryId",
                table: "ProjectStringRevisions",
                column: "StringEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectStringRevisions");
        }
    }
}
