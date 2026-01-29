using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SingleStepViewer.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokenToPlaybackHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PlaybackHistory",
                type: "BLOB",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PlaybackHistory");
        }
    }
}
