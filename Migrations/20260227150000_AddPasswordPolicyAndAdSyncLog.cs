using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LostAndFoundApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordPolicyAndAdSyncLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── Password Policy Settings (single-row config table) ───
            migrationBuilder.CreateTable(
                name: "PasswordPolicySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinimumLength = table.Column<int>(type: "int", nullable: false, defaultValue: 8),
                    RequireDigit = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RequireLowercase = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RequireUppercase = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RequireNonAlphanumeric = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordPolicySettings", x => x.Id);
                });

            // ─── AD Sync Log (history of sync operations) ─────────────
            migrationBuilder.CreateTable(
                name: "AdSyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    UsersCreated = table.Column<int>(type: "int", nullable: false),
                    UsersUpdated = table.Column<int>(type: "int", nullable: false),
                    UsersDeactivated = table.Column<int>(type: "int", nullable: false),
                    RolesUpdated = table.Column<int>(type: "int", nullable: false),
                    TriggerType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TriggeredBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ErrorSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdSyncLogs", x => x.Id);
                });

            // Index on Timestamp for efficient "latest sync" queries
            migrationBuilder.CreateIndex(
                name: "IX_AdSyncLogs_Timestamp",
                table: "AdSyncLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdSyncLogs");
            migrationBuilder.DropTable(name: "PasswordPolicySettings");
        }
    }
}
