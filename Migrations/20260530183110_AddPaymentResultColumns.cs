using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentResultColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicResultToken",
                table: "Payments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TargetAmountFcfa",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PublicResultToken",
                table: "Payments",
                column: "PublicResultToken",
                unique: true,
                filter: "\"PublicResultToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_PublicResultToken",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PublicResultToken",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TargetAmountFcfa",
                table: "Payments");
        }
    }
}
