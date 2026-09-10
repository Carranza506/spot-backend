using Microsoft.EntityFrameworkCore;
using Spot.AiSearch.Api.Models;

namespace Spot.AiSearch.Api.Data;

public class AiSearchDbContext(DbContextOptions<AiSearchDbContext> options) : DbContext(options)
{
    public DbSet<AiRequest> AiRequests => Set<AiRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiRequest>(e =>
        {
            e.ToTable("ai_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
            e.Property(x => x.Model).HasColumnName("model").HasMaxLength(100).IsRequired();
            e.Property(x => x.RequestType).HasColumnName("request_type")
                .HasConversion<string>().IsRequired();
            e.Property(x => x.Prompt).HasColumnName("prompt").IsRequired();
            e.Property(x => x.Response).HasColumnName("response");
            e.Property(x => x.ExtractedParameters).HasColumnName("extracted_parameters")
                .HasColumnType("jsonb");
            e.Property(x => x.ToolCalls).HasColumnName("tool_calls")
                .HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status")
                .HasConversion<string>().IsRequired();
            e.Property(x => x.InputTokens).HasColumnName("input_tokens");
            e.Property(x => x.OutputTokens).HasColumnName("output_tokens");
            e.Property(x => x.TotalTokens).HasColumnName("total_tokens");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.IpAddress).HasColumnName("ip_address");
            e.Property(x => x.UserAgent).HasColumnName("user_agent");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            // The audit endpoint (GET /ai-search/requests) always sorts by created_at DESC and
            // filters by user_id / status / request_type. This table only grows (one row per AI
            // call), so back the common filter + sort combinations with indexes.
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.RequestType, x.CreatedAt });
        });
    }
}
