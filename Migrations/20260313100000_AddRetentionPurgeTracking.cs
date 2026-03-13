using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LostAndFoundApp.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionPurgeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPurgedAt",
                table: "LogRetentionSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastPurgedCount",
                table: "LogRetentionSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPurgedAt",
                table: "ItemRetentionSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastPurgedCount",
                table: "ItemRetentionSettings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPurgedAt",
                table: "LogRetentionSettings");

            migrationBuilder.DropColumn(
                name: "LastPurgedCount",
                table: "LogRetentionSettings");

            migrationBuilder.DropColumn(
                name: "LastPurgedAt",
                table: "ItemRetentionSettings");

            migrationBuilder.DropColumn(
                name: "LastPurgedCount",
                table: "ItemRetentionSettings");
        }
    }
}
