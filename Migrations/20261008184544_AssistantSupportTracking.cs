using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AssistantSupportTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToolsUsed",
                table: "AssistantTurns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnansweredTopic",
                table: "AssistantTurns",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToolsUsed",
                table: "AssistantTurns");

            migrationBuilder.DropColumn(
                name: "UnansweredTopic",
                table: "AssistantTurns");
        }
    }
}
