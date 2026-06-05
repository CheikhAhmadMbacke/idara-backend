using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Withdrawals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Withdrawals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    AmountFcfa = table.Column<long>(type: "bigint", nullable: false),
                    SepayAmountFcfa = table.Column<long>(type: "bigint", nullable: false),
                    FeesFcfa = table.Column<long>(type: "bigint", nullable: false),
                    NetReceivedFcfa = table.Column<long>(type: "bigint", nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    RecipientName = table.Column<string>(type: "text", nullable: false),
                    RecipientPhone = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SenePayDisbursementId = table.Column<string>(type: "text", nullable: true),
                    InitiatedById = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Withdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Withdrawals_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_SchoolId_CreatedAt",
                table: "Withdrawals",
                columns: new[] { "SchoolId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_SenePayDisbursementId",
                table: "Withdrawals",
                column: "SenePayDisbursementId",
                unique: true,
                filter: "\"SenePayDisbursementId\" IS NOT NULL");

            // Filet de sécurité DB (défense en profondeur) : même si un bug de
            // concurrence échappait au verrou FOR UPDATE, PG refuserait de
            // rendre un solde négatif (erreur 23514) plutôt que de sur-débiter.
            migrationBuilder.Sql(
                "ALTER TABLE \"SchoolWallets\" ADD CONSTRAINT \"CK_SchoolWallets_NonNegative\" " +
                "CHECK (\"AvailableBalance\" >= 0 AND \"PendingBalance\" >= 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"SchoolWallets\" DROP CONSTRAINT IF EXISTS \"CK_SchoolWallets_NonNegative\";");

            migrationBuilder.DropTable(
                name: "Withdrawals");
        }
    }
}
