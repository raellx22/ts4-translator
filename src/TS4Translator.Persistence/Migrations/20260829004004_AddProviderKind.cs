using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TS4Translator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Model",
                table: "ProviderConfigurations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            // Every existing row predates ProviderKind - OpenAICompatible was the only provider that
            // ever existed, so that's the correct default for any config saved before this migration
            // (critically, this preserves the user's already-configured real NVIDIA NIM setup).
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ProviderConfigurations",
                type: "TEXT",
                nullable: false,
                defaultValue: "OpenAICompatible");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ProviderConfigurations");

            migrationBuilder.AlterColumn<string>(
                name: "Model",
                table: "ProviderConfigurations",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
