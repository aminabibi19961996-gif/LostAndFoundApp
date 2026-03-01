using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LostAndFoundApp.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCustomTrackingId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LostFoundItems_CustomTrackingId",
                table: "LostFoundItems",
                column: "CustomTrackingId",
                unique: true,
                filter: "[CustomTrackingId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LostFoundItems_CustomTrackingId",
                table: "LostFoundItems");
        }
    }
}
