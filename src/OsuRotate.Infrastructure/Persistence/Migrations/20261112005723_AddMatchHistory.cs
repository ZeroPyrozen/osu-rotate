using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OsuRotate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Match",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: true),
                    RoomId = table.Column<long>(type: "INTEGER", nullable: true),
                    BeatmapId = table.Column<int>(type: "INTEGER", nullable: false),
                    HostName = table.Column<string>(type: "TEXT", nullable: true),
                    HostUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Match", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Match_LobbySession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LobbySession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Player",
                columns: table => new
                {
                    OsuUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Player", x => x.OsuUserId);
                });

            migrationBuilder.CreateTable(
                name: "MatchScore",
                columns: table => new
                {
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    OsuUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Score = table.Column<long>(type: "INTEGER", nullable: false),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchScore", x => new { x.MatchId, x.Position });
                    table.ForeignKey(
                        name: "FK_MatchScore_Match_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Match",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Match_BeatmapId",
                table: "Match",
                column: "BeatmapId");

            migrationBuilder.CreateIndex(
                name: "IX_Match_SessionId",
                table: "Match",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchScore_OsuUserId",
                table: "MatchScore",
                column: "OsuUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchScore");

            migrationBuilder.DropTable(
                name: "Player");

            migrationBuilder.DropTable(
                name: "Match");
        }
    }
}
