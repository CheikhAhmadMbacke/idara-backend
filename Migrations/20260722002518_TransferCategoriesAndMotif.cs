using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class TransferCategoriesAndMotif : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Motif",
                table: "Withdrawals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultCategoryLabel",
                table: "TransferBeneficiaries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Motif",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "DefaultCategoryLabel",
                table: "TransferBeneficiaries");
        }
    }
}
