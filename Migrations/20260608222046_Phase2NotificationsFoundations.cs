using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase2NotificationsFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true → la ligne singleton existante en prod hérite du
            // mode bilingue (FR+AR), conforme à la décision produit. Le SuperAdmin
            // peut basculer en mono-langue ensuite via Réglages plateforme.
            migrationBuilder.AddColumn<bool>(
                name: "SmsBilingual",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    TemplateCode = table.Column<string>(type: "text", nullable: false),
                    RelatedEntityId = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Cost = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_CreatedAt",
                table: "NotificationLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_TemplateCode_RelatedEntityId",
                table: "NotificationLogs",
                columns: new[] { "TemplateCode", "RelatedEntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "SmsBilingual",
                table: "PlatformSettings");
        }
    }
}
