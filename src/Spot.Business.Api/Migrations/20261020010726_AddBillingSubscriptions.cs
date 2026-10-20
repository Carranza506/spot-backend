using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Spot.Business.Api.Models;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Spot.Business.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .Annotation("Npgsql:Enum:subscription_payment_status", "PAID,FAILED")
                .Annotation("Npgsql:Enum:subscription_status", "ACTIVE,PAST_DUE,CANCELED,EXPIRED")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_crc = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stripe_price_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                    table.CheckConstraint("ck_plans_code", "code IN ('FREE','PRO')");
                    table.CheckConstraint("ck_plans_interval", "interval IN ('MONTHLY')");
                    table.CheckConstraint("ck_plans_price_crc", "price_crc >= 0");
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<SubscriptionStatus>(type: "subscription_status", nullable: false),
                    stripe_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    stripe_subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscriptions_users_account_id",
                        column: x => x.account_id,
                        principalTable: "users",
                        // Hand-corrected from the generated "Id", same as every earlier users FK in
                        // this project: UserReference maps no column name, but the real column
                        // (owned by Spot.Auth.Api) is the lowercase "id".
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_crc = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<SubscriptionPaymentStatus>(type: "subscription_payment_status", nullable: false),
                    stripe_invoice_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payments", x => x.id);
                    table.CheckConstraint("ck_subscription_payments_amount_crc", "amount_crc >= 0");
                    table.ForeignKey(
                        name: "FK_subscription_payments_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "plans",
                columns: new[] { "id", "code", "interval", "is_active", "name", "price_crc", "stripe_price_id" },
                values: new object[,]
                {
                    { new Guid("0f3c2a6e-5b1d-4c8e-9a7f-1e2d3c4b5a60"), "FREE", "MONTHLY", true, "Gratis", 0m, null },
                    { new Guid("7d9e1f2a-3b4c-4d5e-8f60-718293a4b5c6"), "PRO", "MONTHLY", true, "Pro", 10000m, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_subscription_payments_subscription",
                table: "subscription_payments",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_stripe_invoice_id",
                table: "subscription_payments",
                column: "stripe_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_subscriptions_account",
                table: "subscriptions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_stripe_subscription_id",
                table: "subscriptions",
                column: "stripe_subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_one_current",
                table: "subscriptions",
                column: "account_id",
                unique: true,
                filter: "status IN ('ACTIVE','PAST_DUE')");

            // Hand-added: "updated_at" trigger, reusing set_updated_at() from Spot.Auth.Api's
            // AddPgcryptoExtensionAndUsersUpdatedAtTrigger migration (must have already run), same as
            // the other Business tables. plans and subscription_payments have no updated_at.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_subscriptions_updated_at BEFORE UPDATE ON subscriptions FOR EACH ROW EXECUTE FUNCTION set_updated_at();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_subscriptions_updated_at ON subscriptions;");

            migrationBuilder.DropTable(
                name: "subscription_payments");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:Enum:contact_type", "phone,whatsapp,email,website,facebook,instagram,tiktok,other")
                .OldAnnotation("Npgsql:Enum:subscription_payment_status", "PAID,FAILED")
                .OldAnnotation("Npgsql:Enum:subscription_status", "ACTIVE,PAST_DUE,CANCELED,EXPIRED")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
