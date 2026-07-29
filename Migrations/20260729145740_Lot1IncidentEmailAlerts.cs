using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Lot1IncidentEmailAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AlertedAt",
                table: "ClientIncidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientIncidents_AlertedAt",
                table: "ClientIncidents",
                column: "AlertedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientIncidents_AlertedAt",
                table: "ClientIncidents");

            migrationBuilder.DropColumn(
                name: "AlertedAt",
                table: "ClientIncidents");
        }
    }
}
