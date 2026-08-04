using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableTurnQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TurnId",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TurnEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TurnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurnEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TurnRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerMessage = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TerminalError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurnRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_TurnId",
                table: "ChatMessages",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_TurnEvents_TurnId_Sequence",
                table: "TurnEvents",
                columns: new[] { "TurnId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TurnRequests_Status_CreatedAt",
                table: "TurnRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TurnRequests_UserId_ClientRequestId",
                table: "TurnRequests",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurnEvents");

            migrationBuilder.DropTable(
                name: "TurnRequests");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_TurnId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "ChatMessages");
        }
    }
}
