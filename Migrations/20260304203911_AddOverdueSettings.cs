using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LostAndFoundApp.Migrations
{
    /// <inheritdoc />
    public partial class AddOverdueSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoPath2",
                table: "LostFoundItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoPath3",
                table: "LostFoundItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoPath4",
                table: "LostFoundItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OverdueSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShortOverdueDays = table.Column<int>(type: "int", nullable: false, defaultValue: 7),
                    LongOverdueDays = table.Column<int>(type: "int", nullable: false, defaultValue: 30)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverdueSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OverdueSettings");

            migrationBuilder.DropColumn(
                name: "PhotoPath2",
                table: "LostFoundItems");

            migrationBuilder.DropColumn(
                name: "PhotoPath3",
                table: "LostFoundItems");

            migrationBuilder.DropColumn(
                name: "PhotoPath4",
                table: "LostFoundItems");
        }
    }
}
