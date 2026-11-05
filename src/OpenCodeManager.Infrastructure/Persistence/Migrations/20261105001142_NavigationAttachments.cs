using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCodeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NavigationAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingAttachmentText",
                table: "Navigation",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingAttachmentText",
                table: "Navigation");
        }
    }
}
