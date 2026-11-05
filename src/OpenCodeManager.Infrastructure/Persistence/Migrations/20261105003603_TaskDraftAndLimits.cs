using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCodeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskDraftAndLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ObservedToolCalls",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DraftAgent",
                table: "Navigation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftModelId",
                table: "Navigation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftProviderId",
                table: "Navigation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileSearchQuery",
                table: "Navigation",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ObservedToolCalls",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DraftAgent",
                table: "Navigation");

            migrationBuilder.DropColumn(
                name: "DraftModelId",
                table: "Navigation");

            migrationBuilder.DropColumn(
                name: "DraftProviderId",
                table: "Navigation");

            migrationBuilder.DropColumn(
                name: "FileSearchQuery",
                table: "Navigation");
        }
    }
}
