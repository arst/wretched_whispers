using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTurnTraceModelRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ModelCallsJson",
            table: "TurnTraces",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RequestJson",
            table: "TurnTraces",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ModelCallsJson",
            table: "TurnTraces");

        migrationBuilder.DropColumn(
            name: "RequestJson",
            table: "TurnTraces");
    }
}
