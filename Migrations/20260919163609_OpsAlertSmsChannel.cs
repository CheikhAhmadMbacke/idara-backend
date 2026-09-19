using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class OpsAlertSmsChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SmsSentAt",
                table: "OpsAlerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpsAlerts_GroupingKey_SmsSentAt",
                table: "OpsAlerts",
                columns: new[] { "GroupingKey", "SmsSentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OpsAlerts_GroupingKey_SmsSentAt",
                table: "OpsAlerts");

            migrationBuilder.DropColumn(
                name: "SmsSentAt",
                table: "OpsAlerts");
        }
    }
}
