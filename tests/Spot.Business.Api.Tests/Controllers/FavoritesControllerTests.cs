using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Controllers;

/// <summary>
/// GET /booking/favorites, PUT/DELETE /booking/favorites/{businessId} (#71), served by Business.Api.
/// Same factory as BusinessesControllerTests: the real pipeline and BusinessService, repositories faked.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class FavoritesControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ---------- GET /booking/favorites ----------

    [Fact]
    public async Task ListFavorites_Client_Returns200WithEmbeddedBusinessesNewestFirst()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var older = SeedBusiness("Bella");
        var newer = SeedBusiness("Zen");
        factory.BusinessRepository.SeedFavorite(clientId, older.Id, T0);
        factory.BusinessRepository.SeedFavorite(clientId, newer.Id, T0.AddDays(1));

        var response = await CreateClient(clientId, "CLIENT").GetAsync("/booking/favorites");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal([newer.Id, older.Id], data.Select(f => f.GetProperty("businessId").GetGuid()));
        Assert.Equal(T0.AddDays(1), data[0].GetProperty("createdAt").GetDateTimeOffset());
        var business = data[0].GetProperty("business");
        Assert.Equal(newer.Id, business.GetProperty("id").GetGuid());
        Assert.Equal("Zen", business.GetProperty("name").GetString());
        Assert.Equal(newer.Slug, business.GetProperty("slug").GetString());
        Assert.True(business.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListFavorites_OnlyReturnsTheCallersFavorites()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var mine = SeedBusiness("Mío");
        var theirs = SeedBusiness("Ajeno");
        factory.BusinessRepository.SeedFavorite(clientId, mine.Id, T0);
        factory.BusinessRepository.SeedFavorite(Guid.NewGuid(), theirs.Id, T0);

        var response = await CreateClient(clientId, "CLIENT").GetAsync("/booking/favorites");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([mine.Id], body.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("businessId").GetGuid()));
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListFavorites_Pagination_ReturnsTheRequestedPage()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var businesses = Enumerable.Range(0, 5).Select(i => SeedBusiness($"B{i}")).ToList();
        for (var i = 0; i < businesses.Count; i++)
            factory.BusinessRepository.SeedFavorite(clientId, businesses[i].Id, T0.AddDays(i));

        var response = await CreateClient(clientId, "CLIENT").GetAsync("/booking/favorites?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            [businesses[2].Id, businesses[1].Id],
            body.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("businessId").GetGuid()));
        var pagination = body.GetProperty("pagination");
        Assert.Equal(2, pagination.GetProperty("page").GetInt32());
        Assert.Equal(5, pagination.GetProperty("total").GetInt32());
        Assert.Equal(3, pagination.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task ListFavorites_InvalidPagination_Returns400(string queryString)
    {
        factory.BusinessRepository.Reset();

        var response = await CreateClient(Guid.NewGuid(), "CLIENT").GetAsync($"/booking/favorites?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListFavorites_InactiveBusiness_IsExcludedAndNotCounted()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var active = SeedBusiness("Activo");
        var inactive = SeedBusiness("Inactivo", isActive: false);
        factory.BusinessRepository.SeedFavorite(clientId, active.Id, T0);
        factory.BusinessRepository.SeedFavorite(clientId, inactive.Id, T0.AddDays(1));

        var response = await CreateClient(clientId, "CLIENT").GetAsync("/booking/favorites?pageSize=1");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([active.Id], body.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("businessId").GetGuid()));
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("totalPages").GetInt32());
    }

    // ---------- PUT /booking/favorites/{businessId} ----------

    [Fact]
    public async Task AddFavorite_NewFavorite_Returns204AndCreatesTheRow()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var business = SeedBusiness("Bella");

        var response = await CreateClient(clientId, "CLIENT").PutAsync($"/booking/favorites/{business.Id}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(business.Id, factory.BusinessRepository.FavoritesOf(clientId).Single().BusinessId);
    }

    [Fact]
    public async Task AddFavorite_SameBusinessTwice_Returns204KeepsOneRowAndTheOriginalCreatedAt()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var business = SeedBusiness("Bella");
        factory.BusinessRepository.SeedFavorite(clientId, business.Id, T0);
        var client = CreateClient(clientId, "CLIENT");

        var first = await client.PutAsync($"/booking/favorites/{business.Id}", null);
        var second = await client.PutAsync($"/booking/favorites/{business.Id}", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var favorite = Assert.Single(factory.BusinessRepository.FavoritesOf(clientId));
        Assert.Equal(T0, favorite.CreatedAt);
    }

    [Fact]
    public async Task AddFavorite_UnknownBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();

        var response = await CreateClient(clientId, "CLIENT").PutAsync($"/booking/favorites/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
        Assert.Empty(factory.BusinessRepository.FavoritesOf(clientId));
    }

    [Fact]
    public async Task AddFavorite_InactiveBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var business = SeedBusiness("Bella", isActive: false);

        var response = await CreateClient(clientId, "CLIENT").PutAsync($"/booking/favorites/{business.Id}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.BusinessRepository.FavoritesOf(clientId));
    }

    [Fact]
    public async Task AddFavorite_InactiveBusinessAlreadyAFavorite_Returns404AndKeepsTheRow()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var business = SeedBusiness("Bella", isActive: false);
        factory.BusinessRepository.SeedFavorite(clientId, business.Id, T0);

        var response = await CreateClient(clientId, "CLIENT").PutAsync($"/booking/favorites/{business.Id}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(T0, factory.BusinessRepository.FavoritesOf(clientId).Single().CreatedAt);
    }

    // ---------- DELETE /booking/favorites/{businessId} ----------

    [Fact]
    public async Task RemoveFavorite_ExistingFavorite_Returns204AndDeletesTheRow()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var business = SeedBusiness("Bella");
        factory.BusinessRepository.SeedFavorite(clientId, business.Id, T0);

        var response = await CreateClient(clientId, "CLIENT").DeleteAsync($"/booking/favorites/{business.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.BusinessRepository.FavoritesOf(clientId));
    }

    [Fact]
    public async Task RemoveFavorite_NotAFavorite_Returns204()
    {
        factory.BusinessRepository.Reset();
        var business = SeedBusiness("Bella");

        var response = await CreateClient(Guid.NewGuid(), "CLIENT").DeleteAsync($"/booking/favorites/{business.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveFavorite_UnknownBusiness_Returns204()
    {
        factory.BusinessRepository.Reset();

        var response = await CreateClient(Guid.NewGuid(), "CLIENT").DeleteAsync($"/booking/favorites/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveFavorite_OnlyDeletesTheCallersRow()
    {
        factory.BusinessRepository.Reset();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var business = SeedBusiness("Bella");
        factory.BusinessRepository.SeedFavorite(clientId, business.Id, T0);
        factory.BusinessRepository.SeedFavorite(otherClientId, business.Id, T0);

        var response = await CreateClient(clientId, "CLIENT").DeleteAsync($"/booking/favorites/{business.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.BusinessRepository.FavoritesOf(clientId));
        Assert.Single(factory.BusinessRepository.FavoritesOf(otherClientId));
    }

    // ---------- Auth (all three operations) ----------

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task AnyOperation_NoToken_Returns401(string method)
    {
        factory.BusinessRepository.Reset();
        var business = SeedBusiness("Bella");

        var response = await factory.CreateClient().SendAsync(Request(method, business.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "BUSINESS")]
    [InlineData("PUT", "BUSINESS")]
    [InlineData("DELETE", "BUSINESS")]
    [InlineData("GET", "SUPERADMIN")]
    [InlineData("PUT", "SUPERADMIN")]
    [InlineData("DELETE", "SUPERADMIN")]
    public async Task AnyOperation_NonClientRole_Returns403(string method, string role)
    {
        factory.BusinessRepository.Reset();
        var userId = Guid.NewGuid();
        var business = SeedBusiness("Bella");
        factory.BusinessRepository.SeedFavorite(userId, business.Id, T0);

        var response = await CreateClient(userId, role).SendAsync(Request(method, business.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(factory.BusinessRepository.FavoritesOf(userId));
    }

    private BusinessEntity SeedBusiness(string name, bool isActive = true) =>
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = name, IsActive = isActive });

    private static HttpRequestMessage Request(string method, Guid businessId) => method == "GET"
        ? new HttpRequestMessage(HttpMethod.Get, "/booking/favorites")
        : new HttpRequestMessage(new HttpMethod(method), $"/booking/favorites/{businessId}");

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
