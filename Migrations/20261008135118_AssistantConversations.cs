using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AssistantConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConversationId",
                table: "AssistantTurns",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HiddenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantConversations_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTurns_ConversationId",
                table: "AssistantTurns",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversations_SchoolId_UserId_UpdatedAt",
                table: "AssistantConversations",
                columns: new[] { "SchoolId", "UserId", "UpdatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AssistantTurns_AssistantConversations_ConversationId",
                table: "AssistantTurns",
                column: "ConversationId",
                principalTable: "AssistantConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssistantTurns_AssistantConversations_ConversationId",
                table: "AssistantTurns");

            migrationBuilder.DropTable(
                name: "AssistantConversations");

            migrationBuilder.DropIndex(
                name: "IX_AssistantTurns_ConversationId",
                table: "AssistantTurns");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "AssistantTurns");
        }
    }
}
