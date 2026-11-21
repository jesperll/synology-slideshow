using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SynologySlideshow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelIsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "Channels");
        }
    }
}
