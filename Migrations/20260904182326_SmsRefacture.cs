using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SmsRefacture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ℹ️ `defaultValue: 0` est CORRECT ici, contrairement au piège §193 /
            // §202 : ces colonnes décrivent ce qu'une facture a refacturé, et
            // les factures déjà émises n'ont effectivement refacturé aucun SMS.
            // Le zéro n'est pas un réglage manquant, c'est la vérité comptable.
            migrationBuilder.AddColumn<int>(
                name: "SmsRefactureCount",
                table: "SubscriptionInvoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SmsRefactureFcfa",
                table: "SubscriptionInvoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "BilledOnSubscriptionInvoiceId",
                table: "NotificationLogs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmsRefactureCount",
                table: "SubscriptionInvoices");

            migrationBuilder.DropColumn(
                name: "SmsRefactureFcfa",
                table: "SubscriptionInvoices");

            migrationBuilder.DropColumn(
                name: "BilledOnSubscriptionInvoiceId",
                table: "NotificationLogs");
        }
    }
}
