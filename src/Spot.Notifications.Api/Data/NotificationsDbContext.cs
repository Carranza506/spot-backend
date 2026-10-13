using Microsoft.EntityFrameworkCore;
using Spot.Notifications.Api.Models;

namespace Spot.Notifications.Api.Data;

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<DevicePlatform>();

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
