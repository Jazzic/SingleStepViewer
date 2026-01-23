using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingleStepViewer.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoveAfterPlayingToPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemoveAfterPlaying",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoveAfterPlaying",
                table: "Playlists");
        }
    }
}
