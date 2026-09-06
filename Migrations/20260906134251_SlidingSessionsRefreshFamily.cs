using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// Sessions glissantes (§223) : chaque refresh token appartient désormais à
    /// une FAMILLE (une connexion, un appareil), et les sessions vivantes sont
    /// prolongées pour que le changement de régime ne déconnecte personne.
    /// </summary>
    public partial class SlidingSessionsRefreshFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔴 EF avait généré `defaultValue: ""` — QUATRIÈME occurrence du
            // §193/§202/§207, et de loin la plus grave : toutes les lignes
            // existantes auraient partagé la MÊME famille, si bien que le premier
            // rejeu détecté aurait révoqué les sessions de TOUTE la plateforme.
            //
            // `gen_random_uuid()` est VOLATILE : PostgreSQL réécrit la table et
            // l'évalue LIGNE PAR LIGNE, donc chaque session existante reçoit sa
            // propre famille. Native depuis PG 13, aucune extension à installer.
            migrationBuilder.AddColumn<string>(
                name: "FamilyId",
                table: "RefreshTokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValueSql: "replace(gen_random_uuid()::text, '-', '')");

            // Le défaut a rempli l'existant ; on le retire pour que le schéma
            // colle au modèle EF (qui, lui, n'en déclare aucun).
            migrationBuilder.Sql(
                @"ALTER TABLE ""RefreshTokens"" ALTER COLUMN ""FamilyId"" DROP DEFAULT;");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId");

            // ⏳ Prolongation des sessions VIVANTES.
            //
            // Sans elle, le correctif arriverait trop tard pour le parc déjà
            // installé : jusqu'ici l'access token ET le refresh token naissaient
            // ensemble avec 30 jours de vie, si bien que le refresh expirait le
            // jour même où il devenait utile. Chaque compte serait déconnecté une
            // dernière fois, au pire moment — juste après le correctif censé
            // régler le problème. On repousse donc l'échéance des jetons encore
            // valides ; les comptes privilégiés (SuperAdmin) gardent une session
            // courte, c'est le sens du réglage dédié.
            migrationBuilder.Sql(@"
                UPDATE ""RefreshTokens"" AS rt
                SET ""ExpiresAt"" = now() + interval '400 days'
                FROM ""Users"" u
                WHERE u.""Id"" = rt.""UserId""
                  AND rt.""RevokedAt"" IS NULL
                  AND rt.""ExpiresAt"" > now()
                  AND u.""Role"" <> 'SuperAdmin';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "RefreshTokens");
        }
    }
}
