using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCodeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalFileBrowser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrowserMessageId",
                table: "Navigation",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrowserPath",
                table: "Navigation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowHiddenDirectories",
                table: "Navigation",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CanonicalPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastOpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CanonicalPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_OwnerUserId_CanonicalPath",
                table: "Bookmarks",
                columns: new[] { "OwnerUserId", "CanonicalPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roots_CanonicalPath",
                table: "Roots",
                column: "CanonicalPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "Roots");

            migrationBuilder.DropColumn(
                name: "BrowserMessageId",
                table: "Navigation");

            migrationBuilder.DropColumn(
                name: "BrowserPath",
                table: "Navigation");

            migrationBuilder.DropColumn(
                name: "ShowHiddenDirectories",
                table: "Navigation");
        }
    }
}
