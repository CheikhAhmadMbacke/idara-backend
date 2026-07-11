using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class CashCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "CashLedgerEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MonthlyBudgetFcfa = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashCategories_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashLedgerEntries_CategoryId",
                table: "CashLedgerEntries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCategories_SchoolId_Type",
                table: "CashCategories",
                columns: new[] { "SchoolId", "Type" });

            migrationBuilder.AddForeignKey(
                name: "FK_CashLedgerEntries_CashCategories_CategoryId",
                table: "CashLedgerEntries",
                column: "CategoryId",
                principalTable: "CashCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashLedgerEntries_CashCategories_CategoryId",
                table: "CashLedgerEntries");

            migrationBuilder.DropTable(
                name: "CashCategories");

            migrationBuilder.DropIndex(
                name: "IX_CashLedgerEntries_CategoryId",
                table: "CashLedgerEntries");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "CashLedgerEntries");
        }
    }
}
