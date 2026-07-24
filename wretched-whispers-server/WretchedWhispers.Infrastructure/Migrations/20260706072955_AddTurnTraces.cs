using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TurnTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerMessage = table.Column<string>(type: "TEXT", nullable: false),
                    GameStateJson = table.Column<string>(type: "TEXT", nullable: true),
                    ToolCallsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ToolResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SuppressedNarrative = table.Column<string>(type: "TEXT", nullable: true),
                    Narrative = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurnTraces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurnTraces_CampaignId",
                table: "TurnTraces",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_TurnTraces_ChatSessionId",
                table: "TurnTraces",
                column: "ChatSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurnTraces");
        }
    }
}
