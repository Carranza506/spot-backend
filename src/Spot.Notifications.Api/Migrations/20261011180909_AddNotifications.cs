using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Spot.Notifications.Api.Models;

#nullable disable

namespace Spot.Notifications.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:device_platform", "android,ios,web")
                .Annotation("Npgsql:Enum:notification_type", "BOOKING_STATUS_CHANGED,GENERAL")
                .OldAnnotation("Npgsql:Enum:device_platform", "android,ios,web");

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<NotificationType>(type: "notification_type", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    related_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_notifications_user_created",
                table: "notifications",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:device_platform", "android,ios,web")
                .OldAnnotation("Npgsql:Enum:device_platform", "android,ios,web")
                .OldAnnotation("Npgsql:Enum:notification_type", "BOOKING_STATUS_CHANGED,GENERAL");
        }
    }
}
