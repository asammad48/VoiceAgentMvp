using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceAgent.Host.Api.Migrations
{
    /// <inheritdoc />
    public partial class ValuesAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldsJson",
                table: "Calls",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FieldsJson",
                table: "Calls");
        }
    }
}
