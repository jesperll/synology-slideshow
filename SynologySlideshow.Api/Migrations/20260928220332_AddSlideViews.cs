using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SynologySlideshow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSlideViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlideViews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    SlideId = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlideViews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlideViews_ChannelId_SlideId",
                table: "SlideViews",
                columns: new[] { "ChannelId", "SlideId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlideViews");
        }
    }
}
