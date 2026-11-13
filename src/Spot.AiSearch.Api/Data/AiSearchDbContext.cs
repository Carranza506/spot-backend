using Microsoft.EntityFrameworkCore;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Data;

public class AiSearchDbContext(DbContextOptions<AiSearchDbContext> options) : DbContext(options)
{
    public DbSet<AiRequest> AiRequests => Set<AiRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // db/Spot.sql:8-9. Explicit UPPERCASE labels (not Npgsql's default lowercase naming
        // convention, e.g. HasPostgresEnum<AiRequestStatus>() alone) so the ALTER COLUMN ...
        // USING cast in this feature's migration can losslessly convert the existing text data
        // (written as enumValue.ToString(), i.e. "SUCCESS"/"ERROR"/etc.) into the new enum type,
        // and so the labels match Spot.sql exactly. NOTE: this intentionally does NOT match the
        // lowercase convention already in use for user_role/auth_provider/contact_type/
        // booking_status elsewhere in this codebase (see those services' DbContexts) — that
        // existing lowercase-vs-Spot.sql-uppercase mismatch is a separate, pre-existing
        // discrepancy, not something to replicate here.
        modelBuilder.HasPostgresEnum("ai_request_status", new[] { "SUCCESS", "ERROR", "TIMEOUT" });
        modelBuilder.HasPostgresEnum("ai_request_type", new[] { "BUSINESS_SEARCH", "GENERAL_QUERY", "OTHER" });

        // Owned and migrated by Spot.Auth.Api; mapped here only so EF Core can express the
        // user_id foreign key below. See Models/UserReference.cs.
        modelBuilder.Entity<UserReference>(e =>
        {
            e.ToTable("users", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<AiRequest>(e =>
        {
            e.ToTable("ai_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
            e.Property(x => x.Model).HasColumnName("model").HasMaxLength(100).IsRequired();
            e.Property(x => x.RequestType).HasColumnName("request_type")
                .HasColumnType("ai_request_type").IsRequired();
            e.Property(x => x.Prompt).HasColumnName("prompt").IsRequired();
            e.Property(x => x.Response).HasColumnName("response");
            e.Property(x => x.ExtractedParameters).HasColumnName("extracted_parameters")
                .HasColumnType("jsonb");
            e.Property(x => x.ToolCalls).HasColumnName("tool_calls")
                .HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status")
                .HasColumnType("ai_request_status").IsRequired();
            e.Property(x => x.InputTokens).HasColumnName("input_tokens");
            e.Property(x => x.OutputTokens).HasColumnName("output_tokens");
            e.Property(x => x.TotalTokens).HasColumnName("total_tokens");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
            e.Property(x => x.UserAgent).HasColumnName("user_agent");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            // The audit endpoint (GET /ai-search/requests) always sorts by created_at DESC and
            // filters by user_id / status / request_type. This table only grows (one row per AI
            // call), so back the common filter + sort combinations with indexes.
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.RequestType, x.CreatedAt });

            // db/Spot.sql:162
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
