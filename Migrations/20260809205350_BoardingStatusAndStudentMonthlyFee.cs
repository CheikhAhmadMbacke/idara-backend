using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class BoardingStatusAndStudentMonthlyFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoardingStatus",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BoardingMonthlyFeeFcfa",
                table: "SchoolPaymentSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DayMonthlyFeeFcfa",
                table: "SchoolPaymentSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HalfBoardingMonthlyFeeFcfa",
                table: "SchoolPaymentSettings",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardingStatus",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "BoardingMonthlyFeeFcfa",
                table: "SchoolPaymentSettings");

            migrationBuilder.DropColumn(
                name: "DayMonthlyFeeFcfa",
                table: "SchoolPaymentSettings");

            migrationBuilder.DropColumn(
                name: "HalfBoardingMonthlyFeeFcfa",
                table: "SchoolPaymentSettings");
        }
    }
}
