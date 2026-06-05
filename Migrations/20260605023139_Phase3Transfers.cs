using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Transfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BeneficiaryId",
                table: "Withdrawals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "Withdrawals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TransferBeneficiaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    DefaultCategory = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferBeneficiaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferBeneficiaries_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_BeneficiaryId",
                table: "Withdrawals",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferBeneficiaries_SchoolId_IsArchived",
                table: "TransferBeneficiaries",
                columns: new[] { "SchoolId", "IsArchived" });

            migrationBuilder.AddForeignKey(
                name: "FK_Withdrawals_TransferBeneficiaries_BeneficiaryId",
                table: "Withdrawals",
                column: "BeneficiaryId",
                principalTable: "TransferBeneficiaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Withdrawals_TransferBeneficiaries_BeneficiaryId",
                table: "Withdrawals");

            migrationBuilder.DropTable(
                name: "TransferBeneficiaries");

            migrationBuilder.DropIndex(
                name: "IX_Withdrawals_BeneficiaryId",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "BeneficiaryId",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Withdrawals");
        }
    }
}
