using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SynologySlideshow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentSlideId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentSlideId",
                table: "Channels",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentSlideId",
                table: "Channels");
        }
    }
}
