using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceAgent.Host.Api.Migrations
{
    /// <inheritdoc />
    public partial class StagesAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentStage",
                table: "Calls",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentStage",
                table: "Calls");
        }
    }
}
