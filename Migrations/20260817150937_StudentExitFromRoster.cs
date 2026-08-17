using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class StudentExitFromRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExitDate",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExitReason",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExitReasonDetail",
                table: "Students",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExitRecordedAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExitRecordedById",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_SchoolId_IsDeleted_ExitDate",
                table: "Students",
                columns: new[] { "SchoolId", "IsDeleted", "ExitDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_SchoolId_IsDeleted_ExitDate",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ExitDate",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ExitReason",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ExitReasonDetail",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ExitRecordedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ExitRecordedById",
                table: "Students");
        }
    }
}
