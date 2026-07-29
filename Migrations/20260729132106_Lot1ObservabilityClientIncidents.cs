using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Lot1ObservabilityClientIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Device = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LocaleCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ExceptionType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StackTrace = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    RequestTrace = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UserComment = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Timeline = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientIncidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientIncidents_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClientIncidents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientIncidents_Code",
                table: "ClientIncidents",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_ClientIncidents_CreatedAt",
                table: "ClientIncidents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClientIncidents_RequestTrace",
                table: "ClientIncidents",
                column: "RequestTrace");

            migrationBuilder.CreateIndex(
                name: "IX_ClientIncidents_SchoolId",
                table: "ClientIncidents",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientIncidents_UserId",
                table: "ClientIncidents",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientIncidents");
        }
    }
}
