using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionRecapState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastOpenedAt",
                table: "ChatSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecapActivityAt",
                table: "ChatSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecapText",
                table: "ChatSessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastOpenedAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "RecapActivityAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "RecapText",
                table: "ChatSessions");
        }
    }
}
