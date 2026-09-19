using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionReprieve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReprieveAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReprieveById",
                table: "Subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReprieveReason",
                table: "Subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReprieveUntil",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReprieveAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ReprieveById",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ReprieveReason",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ReprieveUntil",
                table: "Subscriptions");
        }
    }
}
