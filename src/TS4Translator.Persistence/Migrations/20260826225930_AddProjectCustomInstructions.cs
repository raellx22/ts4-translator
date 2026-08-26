using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCustomInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomInstructions",
                table: "Projects",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomInstructions",
                table: "Projects");
        }
    }
}
