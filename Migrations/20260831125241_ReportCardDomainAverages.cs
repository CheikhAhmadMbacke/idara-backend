using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class ReportCardDomainAverages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ArabicAverage",
                table: "ReportCards",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GeneralSubjectsAverage",
                table: "ReportCards",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubjectKind",
                table: "ReportCardLines",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArabicAverage",
                table: "ReportCards");

            migrationBuilder.DropColumn(
                name: "GeneralSubjectsAverage",
                table: "ReportCards");

            migrationBuilder.DropColumn(
                name: "SubjectKind",
                table: "ReportCardLines");
        }
    }
}
