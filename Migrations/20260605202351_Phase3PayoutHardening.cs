using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase3PayoutHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckedAt",
                table: "Withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextVerificationAt",
                table: "Withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "Withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationAttempts",
                table: "Withdrawals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationStartedAt",
                table: "Withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayoutAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    WithdrawalId = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "jsonb", nullable: true),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutAlerts_Resolved_CreatedAt",
                table: "PayoutAlerts",
                columns: new[] { "Resolved", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutAlerts_WithdrawalId",
                table: "PayoutAlerts",
                column: "WithdrawalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayoutAlerts");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "NextVerificationAt",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "VerificationAttempts",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "VerificationStartedAt",
                table: "Withdrawals");
        }
    }
}
