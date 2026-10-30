using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Data;

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<DevicePlatform>();
        // UPPERCASE labels as-is (db/Spot.sql), same translator as Program.cs's MapEnum for it.
        modelBuilder.HasPostgresEnum<NotificationType>(
            name: "notification_type", nameTranslator: new NpgsqlNullNameTranslator());

        // Owned and migrated by Spot.Auth.Api; mapped here only so EF Core can express the
        // user_id foreign key below. See Models/UserReference.cs.
        modelBuilder.Entity<UserReference>(e =>
        {
            e.ToTable("users", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            // Without this, EF's default (unquoted-case) column name "Id" ends up in any FK
            // that references this stub — e.g. device_tokens' FK below — instead of the real,
            // lowercase "id" column Spot.Auth.Api's migration actually created.
            e.Property(x => x.Id).HasColumnName("id");
        });

        // Owned and migrated by Spot.Business.Api; read-only here. See Models/BusinessReference.cs.
        modelBuilder.Entity<BusinessReference>(e =>
        {
            e.ToTable("businesses", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AccountId).HasColumnName("account_id");
        });

        modelBuilder.Entity<DeviceToken>(e =>
        {
            e.ToTable("device_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Token).HasColumnName("token").IsRequired();
            e.Property(x => x.Platform).HasColumnName("platform").HasColumnType("device_platform");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            // A device push token identifies one app installation, so it can only ever belong to one user at a time.
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId).HasDatabaseName("idx_device_tokens_user");
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Type).HasColumnName("type").HasColumnType("notification_type");
            e.Property(x => x.Title).HasColumnName("title").IsRequired();
            e.Property(x => x.Body).HasColumnName("body").IsRequired();
            e.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(50);
            e.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id");
            e.Property(x => x.IsRead).HasColumnName("is_read").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            // GET /notifications lists one user's notifications newest first.
            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("idx_notifications_user_created");
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
            e.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
            e.Property(x => x.IpAddress).HasColumnName("ip_address");
            e.Property(x => x.UserAgent).HasColumnName("user_agent");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("idx_audit_logs_user_created");
            e.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("idx_audit_logs_entity");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_audit_logs_created");
            // db/Spot.sql:174
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
