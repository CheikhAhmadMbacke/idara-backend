using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase6CoranEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoranEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    EvaluatorId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HizbNumber = table.Column<int>(type: "integer", nullable: true),
                    FromSurah = table.Column<int>(type: "integer", nullable: true),
                    FromAyah = table.Column<int>(type: "integer", nullable: true),
                    FromWordIndex = table.Column<int>(type: "integer", nullable: true),
                    FromWordText = table.Column<string>(type: "text", nullable: true),
                    ToSurah = table.Column<int>(type: "integer", nullable: true),
                    ToAyah = table.Column<int>(type: "integer", nullable: true),
                    ToWordIndex = table.Column<int>(type: "integer", nullable: true),
                    ToWordText = table.Column<string>(type: "text", nullable: true),
                    RecitationScore = table.Column<int>(type: "integer", nullable: true),
                    TajwidScore = table.Column<int>(type: "integer", nullable: true),
                    MemorizationScore = table.Column<int>(type: "integer", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoranEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoranEvaluations_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoranEvaluations_Users_EvaluatorId",
                        column: x => x.EvaluatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CoranEvaluationIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EvaluationId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FaultType = table.Column<int>(type: "integer", nullable: true),
                    Surah = table.Column<int>(type: "integer", nullable: true),
                    Ayah = table.Column<int>(type: "integer", nullable: true),
                    WordIndex = table.Column<int>(type: "integer", nullable: true),
                    WordText = table.Column<string>(type: "text", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoranEvaluationIncidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoranEvaluationIncidents_CoranEvaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "CoranEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoranEvaluationIncidents_EvaluationId",
                table: "CoranEvaluationIncidents",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_CoranEvaluations_EvaluatorId",
                table: "CoranEvaluations",
                column: "EvaluatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CoranEvaluations_StudentId_Date",
                table: "CoranEvaluations",
                columns: new[] { "StudentId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoranEvaluationIncidents");

            migrationBuilder.DropTable(
                name: "CoranEvaluations");
        }
    }
}
