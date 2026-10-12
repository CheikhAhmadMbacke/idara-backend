using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class WaveLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WaveLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<string>(type: "text", nullable: true),
                    AmountFcfa = table.Column<long>(type: "bigint", nullable: false),
                    FeeFcfa = table.Column<long>(type: "bigint", nullable: false),
                    BalanceFcfa = table.Column<long>(type: "bigint", nullable: false),
                    IsReversal = table.Column<bool>(type: "boolean", nullable: false),
                    ClientReference = table.Column<string>(type: "text", nullable: true),
                    PaymentReason = table.Column<string>(type: "text", nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "text", nullable: true),
                    CounterpartyName = table.Column<string>(type: "text", nullable: true),
                    CounterpartyMobile = table.Column<string>(type: "text", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaveLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaveLedgerEntries_CheckoutSessionId",
                table: "WaveLedgerEntries",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WaveLedgerEntries_Timestamp",
                table: "WaveLedgerEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_WaveLedgerEntries_TransactionId",
                table: "WaveLedgerEntries",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaveLedgerEntries");
        }
    }
}
