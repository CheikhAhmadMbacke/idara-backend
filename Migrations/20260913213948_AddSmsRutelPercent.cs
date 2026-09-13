using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsRutelPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SmsRutelPercent",
                table: "PlatformSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // 🔴 §193/§202/§207/§232/§254 — SIXIÈME occurrence du même piège.
            // La ligne PlatformSettings EXISTE (singleton créé en juin) : elle
            // hérite du `defaultValue: 0.0` ci-dessus, que le défaut C# soit 5.0
            // ou non. Sans cette ligne, la RUTEL vaudrait zéro en production, le
            // multiplicateur resterait 1,18 — et le correctif serait mort-né,
            // sans une seule erreur nulle part.
            migrationBuilder.Sql(
                "UPDATE \"PlatformSettings\" SET \"SmsRutelPercent\" = 5.0 WHERE \"SmsRutelPercent\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmsRutelPercent",
                table: "PlatformSettings");
        }
    }
}
