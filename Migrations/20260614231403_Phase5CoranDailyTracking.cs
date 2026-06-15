using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CoranDailyTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuranRiwaya",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CoranCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoranCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoranCycles_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoranDailyRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    CycleId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    RecordedById = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoranDailyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoranDailyRecords_CoranCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "CoranCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoranDailyRecords_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoranDailyPortions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyRecordId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FromSurah = table.Column<int>(type: "integer", nullable: true),
                    FromAyah = table.Column<int>(type: "integer", nullable: true),
                    FromWordIndex = table.Column<int>(type: "integer", nullable: true),
                    FromWordText = table.Column<string>(type: "text", nullable: true),
                    ToSurah = table.Column<int>(type: "integer", nullable: true),
                    ToAyah = table.Column<int>(type: "integer", nullable: true),
                    ToWordIndex = table.Column<int>(type: "integer", nullable: true),
                    ToWordText = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoranDailyPortions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoranDailyPortions_CoranDailyRecords_DailyRecordId",
                        column: x => x.DailyRecordId,
                        principalTable: "CoranDailyRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoranCycles_StudentId",
                table: "CoranCycles",
                column: "StudentId",
                unique: true,
                filter: "\"IsComplete\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_CoranCycles_StudentId_Number",
                table: "CoranCycles",
                columns: new[] { "StudentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoranDailyPortions_DailyRecordId_Kind",
                table: "CoranDailyPortions",
                columns: new[] { "DailyRecordId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoranDailyRecords_CycleId",
                table: "CoranDailyRecords",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_CoranDailyRecords_StudentId_Date",
                table: "CoranDailyRecords",
                columns: new[] { "StudentId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoranDailyPortions");

            migrationBuilder.DropTable(
                name: "CoranDailyRecords");

            migrationBuilder.DropTable(
                name: "CoranCycles");

            migrationBuilder.DropColumn(
                name: "QuranRiwaya",
                table: "Schools");
        }
    }
}
