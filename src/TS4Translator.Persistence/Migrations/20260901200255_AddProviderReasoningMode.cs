using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderReasoningMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generated migration defaulted this to "", which is not the name of any ReasoningMode
            // member: every profile written before this column existed would have come back out of the
            // database as a conversion failure. The column stores the enum's name (like Kind does), so
            // the default has to be a real one - "Off", which is also what the engine did before there
            // was a setting.
            migrationBuilder.AddColumn<string>(
                name: "Reasoning",
                table: "ProviderProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: nameof(TS4Translator.Core.Providers.ReasoningMode.Off));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reasoning",
                table: "ProviderProfiles");
        }
    }
}
