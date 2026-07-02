using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase5PlatformOutflowSenePayRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SenePayReference",
                table: "PlatformOutflows",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOutflows_SenePayReference",
                table: "PlatformOutflows",
                column: "SenePayReference",
                unique: true,
                filter: "\"SenePayReference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformOutflows_SenePayReference",
                table: "PlatformOutflows");

            migrationBuilder.DropColumn(
                name: "SenePayReference",
                table: "PlatformOutflows");
        }
    }
}
