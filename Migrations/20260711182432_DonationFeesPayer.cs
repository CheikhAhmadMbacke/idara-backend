using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class DonationFeesPayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue 1 = FeesPayer.School (daara paie les frais des dons) :
            // décision produit 2026-07-11 — les écoles EXISTANTES basculent sur ce
            // défaut (le donateur ne doit pas payer les frais), modifiable ensuite.
            migrationBuilder.AddColumn<int>(
                name: "DonationFeesPayer",
                table: "SchoolPaymentSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DonationFeesPayer",
                table: "SchoolPaymentSettings");
        }
    }
}
