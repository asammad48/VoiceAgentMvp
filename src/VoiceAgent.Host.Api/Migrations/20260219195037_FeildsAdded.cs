using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceAgent.Host.Api.Migrations
{
    /// <inheritdoc />
    public partial class FeildsAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallFieldHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    TurnId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallFieldHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallFieldHistories_TenantId_CallId_FieldName",
                table: "CallFieldHistories",
                columns: new[] { "TenantId", "CallId", "FieldName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallFieldHistories");
        }
    }
}
