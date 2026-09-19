using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPaymentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionInvoiceId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionPaymentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstOpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenCount = table.Column<int>(type: "integer", nullable: false),
                    LastSharedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPaymentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPaymentLinks_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SubscriptionInvoiceId",
                table: "Payments",
                column: "SubscriptionInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentLinks_SchoolId",
                table: "SubscriptionPaymentLinks",
                column: "SchoolId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentLinks_Token",
                table: "SubscriptionPaymentLinks",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_SubscriptionInvoices_SubscriptionInvoiceId",
                table: "Payments",
                column: "SubscriptionInvoiceId",
                principalTable: "SubscriptionInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_SubscriptionInvoices_SubscriptionInvoiceId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "SubscriptionPaymentLinks");

            migrationBuilder.DropIndex(
                name: "IX_Payments_SubscriptionInvoiceId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "SubscriptionInvoiceId",
                table: "Payments");
        }
    }
}
