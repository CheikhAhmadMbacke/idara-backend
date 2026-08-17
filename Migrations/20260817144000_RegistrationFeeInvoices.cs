using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationFeeInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_StudentId_PeriodStart",
                table: "Invoices");

            migrationBuilder.AddColumn<long>(
                name: "RegistrationFeeFcfa",
                table: "SchoolPaymentSettings",
                type: "bigint",
                nullable: true);

            // 🔴 CORRIGÉ À LA MAIN (précédent §152) : EF avait généré defaultValue: 0.
            // Ce défaut décide de la nature des factures DÉJÀ en production — toutes
            // des mensualités. À 0 (aucune nature valide), le cron les aurait
            // ignorées et régénérées en double. 1 = InvoiceType.MonthlyFee.
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StudentId_PeriodStart_Type",
                table: "Invoices",
                columns: new[] { "StudentId", "PeriodStart", "Type" },
                unique: true,
                filter: "\"Status\" <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_StudentId_PeriodStart_Type",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RegistrationFeeFcfa",
                table: "SchoolPaymentSettings");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StudentId_PeriodStart",
                table: "Invoices",
                columns: new[] { "StudentId", "PeriodStart" },
                unique: true,
                filter: "\"Status\" <> 3");
        }
    }
}
