using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OsuRotate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchStartedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Match_StartedAt",
                table: "Match",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Match_StartedAt",
                table: "Match");
        }
    }
}
