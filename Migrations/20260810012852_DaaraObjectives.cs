using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class DaaraObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DaaraObjectiveId",
                table: "DaaraEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DaaraObjectives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MeasureMode = table.Column<int>(type: "integer", nullable: false),
                    TargetValue = table.Column<long>(type: "bigint", nullable: false),
                    CurrentValue = table.Column<long>(type: "bigint", nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TargetDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AchievedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaaraObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DaaraObjectives_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DaaraObjectiveSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DaaraObjectiveId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDone = table.Column<bool>(type: "boolean", nullable: false),
                    DoneAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaaraObjectiveSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DaaraObjectiveSteps_DaaraObjectives_DaaraObjectiveId",
                        column: x => x.DaaraObjectiveId,
                        principalTable: "DaaraObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DaaraEvents_DaaraObjectiveId",
                table: "DaaraEvents",
                column: "DaaraObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_DaaraObjectives_SchoolId_Status",
                table: "DaaraObjectives",
                columns: new[] { "SchoolId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DaaraObjectiveSteps_DaaraObjectiveId",
                table: "DaaraObjectiveSteps",
                column: "DaaraObjectiveId");

            migrationBuilder.AddForeignKey(
                name: "FK_DaaraEvents_DaaraObjectives_DaaraObjectiveId",
                table: "DaaraEvents",
                column: "DaaraObjectiveId",
                principalTable: "DaaraObjectives",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DaaraEvents_DaaraObjectives_DaaraObjectiveId",
                table: "DaaraEvents");

            migrationBuilder.DropTable(
                name: "DaaraObjectiveSteps");

            migrationBuilder.DropTable(
                name: "DaaraObjectives");

            migrationBuilder.DropIndex(
                name: "IX_DaaraEvents_DaaraObjectiveId",
                table: "DaaraEvents");

            migrationBuilder.DropColumn(
                name: "DaaraObjectiveId",
                table: "DaaraEvents");
        }
    }
}
