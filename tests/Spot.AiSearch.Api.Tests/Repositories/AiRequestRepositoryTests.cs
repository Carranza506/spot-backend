using Microsoft.EntityFrameworkCore;
using Spot.AiSearch.Api.Data;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Repositories;

namespace Spot.AiSearch.Api.Tests.Repositories;

/// <summary>
/// The real <see cref="AiRequestRepository"/> on EF Core's InMemory provider. The controller tests
/// use FakeAiRequestRepository, which has its own copy of the paging logic, so only this test fails
/// if the repository's offset goes back to int.
/// </summary>
public sealed class AiRequestRepositoryTests : IDisposable
{
    private readonly AiSearchDbContext _db;
    private readonly AiRequestRepository _repository;

    public AiRequestRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AiSearchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new InMemoryAiSearchDbContext(options);
        _repository = new AiRequestRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ListAsync_PageWhoseOffsetOverflowsInt_ReturnsNoRowsAndTheRealTotal()
    {
        _db.AiRequests.Add(new AiRequest
        {
            Provider = "gemini",
            Model = "gemini-test",
            RequestType = AiRequestType.BUSINESS_SEARCH,
            Prompt = "barberías en San José",
            Status = AiRequestStatus.SUCCESS,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Sanity check: the first page does return the row, so an empty result below isn't vacuous.
        var (firstPage, _) = await _repository.ListAsync(new AiSearchFilterQuery { Page = 1, PageSize = 100 });
        Assert.Single(firstPage);

        // (30,000,000 - 1) * 100 overflows int into a negative offset, which used to skip nothing
        // (or fail with a negative OFFSET on Postgres) instead of returning an empty page.
        var (items, total) = await _repository.ListAsync(new AiSearchFilterQuery { Page = 30_000_000, PageSize = 100 });

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    /// <summary>
    /// InMemory can't map the jsonb (JsonDocument) and inet (IPAddress) columns. Paging doesn't
    /// read them, so this test-only context ignores them; everything else is the real model.
    /// </summary>
    private sealed class InMemoryAiSearchDbContext(DbContextOptions<AiSearchDbContext> options) : AiSearchDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AiRequest>(e =>
            {
                e.Ignore(x => x.ExtractedParameters);
                e.Ignore(x => x.ToolCalls);
                e.Ignore(x => x.IpAddress);
            });
        }
    }
}
