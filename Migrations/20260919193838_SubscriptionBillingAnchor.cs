using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionBillingAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionAnchorBackfilledAt",
                table: "PlatformSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionBillingDay",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 🔴 La ligne de réglages EXISTE DÉJÀ : elle hérite du defaultValue
            // de la colonne, pas de la valeur C#. Sans ce UPDATE, le jour de
            // prélèvement vaudrait ZÉRO en production — le piège qui s'est
            // reproduit cinq fois (§193, §202, §207, §232, §254).
            migrationBuilder.Sql(
                "UPDATE \"PlatformSettings\" SET \"SubscriptionBillingDay\" = 8 WHERE \"SubscriptionBillingDay\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionAnchorBackfilledAt",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SubscriptionBillingDay",
                table: "PlatformSettings");
        }
    }
}
