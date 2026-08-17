using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WretchedWhispers.Infrastructure.Migrations.Postgres;

/// <inheritdoc />
public partial class InitialPostgres : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: true),
                SecurityStamp = table.Column<string>(type: "text", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                PhoneNumber = table.Column<string>(type: "text", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUsers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Campaigns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Data = table.Column<string>(type: "TEXT", nullable: false),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Campaigns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Characters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Data = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Characters", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ChatSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastOpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RecapText = table.Column<string>(type: "text", nullable: true),
                RecapActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SummaryText = table.Column<string>(type: "text", nullable: true),
                SummaryCoveredCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatSessions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DataProtectionKeys",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FriendlyName = table.Column<string>(type: "text", nullable: true),
                Xml = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Encounters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Data = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Encounters", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TurnTraces",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                ChatSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                Stage = table.Column<string>(type: "text", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OrderIndex = table.Column<int>(type: "integer", nullable: false),
                PlayerMessage = table.Column<string>(type: "TEXT", nullable: false),
                GameStateJson = table.Column<string>(type: "TEXT", nullable: true),
                ToolCallsJson = table.Column<string>(type: "TEXT", nullable: false),
                ToolResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                TurnDeltaJson = table.Column<string>(type: "TEXT", nullable: true),
                SuppressedNarrative = table.Column<string>(type: "TEXT", nullable: true),
                Narrative = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TurnTraces", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<string>(type: "text", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                ProviderKey = table.Column<string>(type: "text", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                UserId = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserTokens",
            columns: table => new
            {
                UserId = table.Column<string>(type: "text", nullable: false),
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChatMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(type: "text", nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: true),
                AuthorName = table.Column<string>(type: "text", nullable: true),
                ItemsJson = table.Column<string>(type: "TEXT", nullable: true),
                MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OrderIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChatMessages_ChatSessions_SessionId",
                    column: x => x.SessionId,
                    principalTable: "ChatSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserClaims_UserId",
            table: "AspNetUserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserLogins_UserId",
            table: "AspNetUserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "AspNetUsers",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "AspNetUsers",
            column: "NormalizedUserName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Campaigns_UserId",
            table: "Campaigns",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessages_SessionId",
            table: "ChatMessages",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatSessions_CampaignId",
            table: "ChatSessions",
            column: "CampaignId");

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
            name: "AspNetUserClaims");

        migrationBuilder.DropTable(
            name: "AspNetUserLogins");

        migrationBuilder.DropTable(
            name: "AspNetUserTokens");

        migrationBuilder.DropTable(
            name: "Campaigns");

        migrationBuilder.DropTable(
            name: "Characters");

        migrationBuilder.DropTable(
            name: "ChatMessages");

        migrationBuilder.DropTable(
            name: "DataProtectionKeys");

        migrationBuilder.DropTable(
            name: "Encounters");

        migrationBuilder.DropTable(
            name: "TurnTraces");

        migrationBuilder.DropTable(
            name: "AspNetUsers");

        migrationBuilder.DropTable(
            name: "ChatSessions");
    }
}
