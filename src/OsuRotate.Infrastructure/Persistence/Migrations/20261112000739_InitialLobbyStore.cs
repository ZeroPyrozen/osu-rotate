using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OsuRotate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLobbyStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DenyEntry",
                columns: table => new
                {
                    PlayerKey = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DenyEntry", x => x.PlayerKey);
                });

            migrationBuilder.CreateTable(
                name: "LobbyConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LobbyConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LobbySession",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    HostName = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LobbySession", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueEntry",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEntry", x => new { x.SessionId, x.Position });
                    table.ForeignKey(
                        name: "FK_QueueEntry_LobbySession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LobbySession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LobbySession_ClosedAt",
                table: "LobbySession",
                column: "ClosedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DenyEntry");

            migrationBuilder.DropTable(
                name: "LobbyConfig");

            migrationBuilder.DropTable(
                name: "QueueEntry");

            migrationBuilder.DropTable(
                name: "LobbySession");
        }
    }
}
