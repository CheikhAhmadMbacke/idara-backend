using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class DaaraEventInfoCategoryAndLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryLabel",
                table: "DaaraEvents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryLabel",
                table: "DaaraEvents");
        }
    }
}
