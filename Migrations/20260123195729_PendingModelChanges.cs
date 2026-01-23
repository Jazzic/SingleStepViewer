using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingleStepViewer.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlaybackHistory_PlaylistItems_PlaylistItemId",
                table: "PlaybackHistory");

            migrationBuilder.AddForeignKey(
                name: "FK_PlaybackHistory_PlaylistItems_PlaylistItemId",
                table: "PlaybackHistory",
                column: "PlaylistItemId",
                principalTable: "PlaylistItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlaybackHistory_PlaylistItems_PlaylistItemId",
                table: "PlaybackHistory");

            migrationBuilder.AddForeignKey(
                name: "FK_PlaybackHistory_PlaylistItems_PlaylistItemId",
                table: "PlaybackHistory",
                column: "PlaylistItemId",
                principalTable: "PlaylistItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
