using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceAgent.Host.Api.Migrations
{
    /// <inheritdoc />
    public partial class OutboundCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AsteriskChannelId",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "Calls",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DispositionCode",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCampaignId",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalLeadId",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSystem",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InboundUseCaseCode",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneFrom",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneTo",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartReason",
                table: "Calls",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DoNotCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoNotCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoNotCalls_TenantId_Phone",
                table: "DoNotCalls",
                columns: new[] { "TenantId", "Phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoNotCalls");

            migrationBuilder.DropColumn(
                name: "AsteriskChannelId",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "DispositionCode",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "ExternalCampaignId",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "ExternalLeadId",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "ExternalSystem",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "InboundUseCaseCode",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "PhoneFrom",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "PhoneTo",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "StartReason",
                table: "Calls");
        }
    }
}
