using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations.Postgres;

/// <inheritdoc />
public partial class AddTurnTraceCoveragePostgres : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Attempt",
            table: "TurnTraces",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "DurationMs",
            table: "TurnTraces",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Error",
            table: "TurnTraces",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "InputTokens",
            table: "TurnTraces",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Iterations",
            table: "TurnTraces",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ModelId",
            table: "TurnTraces",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Outcome",
            table: "TurnTraces",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "OutputTokens",
            table: "TurnTraces",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PromptHash",
            table: "TurnTraces",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ToolsOffered",
            table: "TurnTraces",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TurnId",
            table: "TurnTraces",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TurnTraces_TurnId",
            table: "TurnTraces",
            column: "TurnId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TurnTraces_TurnId",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "Attempt",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "DurationMs",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "Error",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "InputTokens",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "Iterations",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "ModelId",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "Outcome",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "OutputTokens",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "PromptHash",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "ToolsOffered",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "TurnId",
            table: "TurnTraces");
    }
}
