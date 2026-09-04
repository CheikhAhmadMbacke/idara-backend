using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class LegalAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedLegalAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedLegalVersion",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedLegalAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AcceptedLegalVersion",
                table: "Users");
        }
    }
}
