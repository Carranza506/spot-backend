using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class BusinessesControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    // ---------- POST /business/businesses ----------

    [Fact]
    public async Task CreateBusiness_BusinessAccount_Returns201WithGeneratedSlugAndAccountFromToken()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var client = CreateClient(accountId, "BUSINESS");

        // accountId in the body must be ignored in favour of the JWT sub.
        var response = await client.PostAsJsonAsync("/business/businesses",
            new { name = "Salón Bella Vista", email = "contacto@bellavista.cr", accountId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("salon-bella-vista", body.GetProperty("slug").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.TryGetProperty("accountId", out _));
        Assert.Equal(accountId, factory.BusinessRepository.All.Single().AccountId);
    }

    [Fact]
    public async Task CreateBusiness_SecondCreateBySameAccount_Returns409()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");
        await client.PostAsJsonAsync("/business/businesses", new { name = "Primero" });

        var response = await client.PostAsJsonAsync("/business/businesses", new { name = "Segundo" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BUSINESS_ALREADY_EXISTS", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateBusiness_AccountWithDeactivatedBusiness_Returns409()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Viejo", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.PostAsJsonAsync("/business/businesses", new { name = "Nuevo" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("SUPERADMIN")]
    public async Task CreateBusiness_NonBusinessRole_Returns403(string role)
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), role);

        var response = await client.PostAsJsonAsync("/business/businesses", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.BusinessRepository.All);
    }

    [Fact]
    public async Task CreateBusiness_NoToken_Returns401()
    {
        factory.BusinessRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/business/businesses", new { name = "X" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"   \"}")]
    [InlineData("{\"name\":\"Ok\",\"email\":\"not-an-email\"}")]
    [InlineData("{\"name\":\"Ok\",\"website\":\"not a url\"}")]
    [InlineData("{\"name\":\"Ok\",\"phone\":\"1234567890123456789012345678901\"}")]
    public async Task CreateBusiness_InvalidBody_Returns400(string json)
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsync("/business/businesses",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    // ---------- GET /business/businesses/{businessId} ----------

    [Fact]
    public async Task GetBusiness_ActiveBusiness_Returns200WithoutToken()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(business.Id, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetBusiness_UnknownId_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetBusiness_InactiveBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(
            new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Cerrado", IsActive = false });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBusiness_NonGuidId_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/business/businesses/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PATCH /business/businesses/{businessId} ----------

    [Fact]
    public async Task UpdateBusiness_Owner_Returns200AndKeepsSlugOnRename()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity
        {
            AccountId = accountId, Name = "Bella", Slug = "bella", Phone = "2222-3344", Description = "Salón",
        });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.PatchAsJsonAsync($"/business/businesses/{business.Id}",
            new { name = "Bella Renovada", description = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bella Renovada", body.GetProperty("name").GetString());
        Assert.Equal("bella", body.GetProperty("slug").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
        Assert.Equal("2222-3344", body.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task UpdateBusiness_OwnerReactivatesInactiveBusiness_Returns200()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "B", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.PatchAsJsonAsync($"/business/businesses/{business.Id}", new { isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(business.IsActive);
    }

    [Fact]
    public async Task UpdateBusiness_NonOwnerBusinessAccount_Returns403()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PatchAsJsonAsync($"/business/businesses/{business.Id}", new { name = "Robado" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.Equal("Bella", business.Name);
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("SUPERADMIN")]
    public async Task UpdateBusiness_NonBusinessRole_Returns403(string role)
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = CreateClient(Guid.NewGuid(), role);

        var response = await client.PatchAsJsonAsync($"/business/businesses/{business.Id}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBusiness_UnknownId_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PatchAsJsonAsync($"/business/businesses/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /business/businesses/{businessId} ----------

    [Fact]
    public async Task DeactivateBusiness_Owner_Returns204AndSoftDeletes()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = Assert.Single(factory.BusinessRepository.All);
        Assert.Equal(business.Id, stored.Id);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task DeactivateBusiness_AlreadyInactive_Returns204()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "B", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateBusiness_NonOwnerBusinessAccount_Returns403AndLeavesItActive()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(business.IsActive);
    }

    [Fact]
    public async Task DeactivateBusiness_Superadmin_Returns403()
    {
        factory.BusinessRepository.Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = CreateClient(Guid.NewGuid(), "SUPERADMIN");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(business.IsActive);
    }

    [Fact]
    public async Task DeactivateBusiness_UnknownId_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- GET / PATCH /business/businesses/me ----------

    [Fact]
    public async Task GetOwnBusiness_WithBusiness_Returns200()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Mío" });
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Ajeno" });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.GetAsync("/business/businesses/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(business.Id, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetOwnBusiness_InactiveBusiness_Returns200()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Mío", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.GetAsync("/business/businesses/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetOwnBusiness_WithoutBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.GetAsync("/business/businesses/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOwnBusiness_ClientRole_Returns403()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "CLIENT");

        var response = await client.GetAsync("/business/businesses/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOwnBusiness_WithBusiness_Returns200()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Mío" });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.PatchAsJsonAsync("/business/businesses/me",
            new { phone = "+506 8888-0000", website = "https://mio.cr" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("+506 8888-0000", body.GetProperty("phone").GetString());
        Assert.Equal("https://mio.cr", body.GetProperty("website").GetString());
    }

    [Fact]
    public async Task UpdateOwnBusiness_WithoutBusiness_Returns404()
    {
        factory.BusinessRepository.Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PatchAsJsonAsync("/business/businesses/me", new { name = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOwnBusiness_InvalidEmail_Returns400()
    {
        factory.BusinessRepository.Reset();
        var accountId = Guid.NewGuid();
        factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Mío" });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.PatchAsJsonAsync("/business/businesses/me", new { email = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
