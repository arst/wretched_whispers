using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations.Postgres;

[DbContext(typeof(PostgresWwDbContext))]
[Migration("20260807160001_OneTerminalEventPerTurnPostgres")]
public partial class OneTerminalEventPerTurnPostgres : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_TurnEvents_OneTerminal",
            table: "TurnEvents",
            column: "TurnId",
            unique: true,
            filter: "\"EventType\" IN ('done', 'error')");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(name: "IX_TurnEvents_OneTerminal", table: "TurnEvents");
}
