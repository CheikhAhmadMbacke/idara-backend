using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class TrustedSchoolLinkedToDaara : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "TrustedSchools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "TrustedSchools",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustedSchools_SchoolId",
                table: "TrustedSchools",
                column: "SchoolId",
                unique: true,
                filter: "\"SchoolId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_TrustedSchools_Schools_SchoolId",
                table: "TrustedSchools",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrustedSchools_Schools_SchoolId",
                table: "TrustedSchools");

            migrationBuilder.DropIndex(
                name: "IX_TrustedSchools_SchoolId",
                table: "TrustedSchools");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "TrustedSchools");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "TrustedSchools");
        }
    }
}
