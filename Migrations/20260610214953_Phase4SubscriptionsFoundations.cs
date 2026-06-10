using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase4SubscriptionsFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SubscriptionEnforcementEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StudentMin = table.Column<int>(type: "integer", nullable: true),
                    StudentMax = table.Column<int>(type: "integer", nullable: true),
                    MonthlyPriceFcfa = table.Column<long>(type: "bigint", nullable: false),
                    AnnualPriceFcfa = table.Column<long>(type: "bigint", nullable: false),
                    NotificationQuota = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsCustom = table.Column<bool>(type: "boolean", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlans_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    BillingCycle = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AmountFcfa = table.Column<long>(type: "bigint", nullable: false),
                    NotificationQuota = table.Column<int>(type: "integer", nullable: false),
                    NotificationUsedThisCycle = table.Column<int>(type: "integer", nullable: false),
                    TrialEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextBillingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GracePeriodEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadOnlyEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AmountFcfa = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WalletTransactionId = table.Column<int>(type: "integer", nullable: true),
                    PdfPath = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoices_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_SchoolId",
                table: "SubscriptionInvoices",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_SubscriptionId_PeriodStart",
                table: "SubscriptionInvoices",
                columns: new[] { "SubscriptionId", "PeriodStart" },
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Code",
                table: "SubscriptionPlans",
                column: "Code",
                unique: true,
                filter: "\"IsCustom\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_SchoolId",
                table: "SubscriptionPlans",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                table: "Subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_SchoolId",
                table: "Subscriptions",
                column: "SchoolId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Status_NextBillingAt",
                table: "Subscriptions",
                columns: new[] { "Status", "NextBillingAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionInvoices");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "SubscriptionEnforcementEnabled",
                table: "PlatformSettings");
        }
    }
}
