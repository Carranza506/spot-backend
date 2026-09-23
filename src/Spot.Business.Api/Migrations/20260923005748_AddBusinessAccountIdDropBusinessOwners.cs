using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spot.Business.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessAccountIdDropBusinessOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable at first — EF can't express "add a NOT NULL column, backfilled from
            // another table" as a single typed operation. Backfilled below, then locked to
            // NOT NULL once every row actually has a value.
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "businesses",
                type: "uuid",
                nullable: true);

            // A business with zero or more than one row in business_owners must never reach this
            // migration silently: zero leaves account_id NULL (the AlterColumn below then fails
            // outright instead of guessing an owner), and more than one is structurally
            // impossible to backfill 1:1 (UPDATE...FROM picks an arbitrary matching row, which
            // would silently discard the others' ownership — this repo confirmed before writing
            // this migration that every business has exactly one row in business_owners).
            migrationBuilder.Sql("""
                UPDATE businesses b SET account_id = bo.user_id
                FROM business_owners bo WHERE bo.business_id = b.id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "businesses",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_businesses_account_id",
                table: "businesses",
                column: "account_id",
                unique: true);

            // "id" lowercase: the real physical column (see the identical note on
            // FK_business_owners_users_user_id in AddUserIdForeignKeysTriggersAndSeedData) — the
            // auto-scaffolded "Id" here would point at a column that doesn't exist, since
            // UserReference itself never gets its own HasColumnName (it's excluded from
            // migrations; the actual "users" table is created by Spot.Auth.Api with lowercase
            // columns).
            migrationBuilder.AddForeignKey(
                name: "FK_businesses_users_account_id",
                table: "businesses",
                column: "account_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "business_owners");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_owners",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_owners", x => new { x.business_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_business_owners_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_business_owners_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_business_owners_user",
                table: "business_owners",
                column: "user_id");

            // Restores the exact rows AddBusinessAccountIdDropBusinessOwners's Up() consumed —
            // each business had exactly one owner, so this is a lossless 1:1 reversal.
            migrationBuilder.Sql("""
                INSERT INTO business_owners (business_id, user_id)
                SELECT id, account_id FROM businesses;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_businesses_users_account_id",
                table: "businesses");

            migrationBuilder.DropIndex(
                name: "IX_businesses_account_id",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "businesses");
        }
    }
}
