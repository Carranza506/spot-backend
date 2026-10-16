using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessEntity = Spot.Business.Api.Models.Business;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class ServicesControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    // ---------- POST /business/businesses/{businessId}/services ----------

    [Fact]
    public async Task CreateService_Owner_Returns201WithTheCreatedService()
    {
        var (business, client) = SeedOwnedBusiness();

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/services", new
        {
            name = "Corte y peinado",
            description = "Corte de cabello con lavado y peinado incluido.",
            price = 12000.00,
            durationMinutes = 45,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(business.Id, body.GetProperty("businessId").GetGuid());
        Assert.Equal("Corte y peinado", body.GetProperty("name").GetString());
        Assert.Equal(12000.00m, body.GetProperty("price").GetDecimal());
        Assert.Equal(45, body.GetProperty("durationMinutes").GetInt32());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Single(factory.ServiceRepository.All);
    }

    [Fact]
    public async Task CreateService_BusinessIdAndIsActiveInBody_AreIgnored()
    {
        var (business, client) = SeedOwnedBusiness();

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/services", new
        {
            name = "Corte",
            price = 5000,
            durationMinutes = 30,
            businessId = Guid.NewGuid(),
            isActive = false,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = factory.ServiceRepository.All.Single();
        Assert.Equal(business.Id, stored.BusinessId);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task CreateService_FreeService_Returns201()
    {
        var (business, client) = SeedOwnedBusiness();

        var response = await client.PostAsJsonAsync(
            $"/business/businesses/{business.Id}/services", new { name = "Consulta", price = 0, durationMinutes = 15 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateService_NonOwnerBusinessAccount_Returns403AndCreatesNothing()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/services", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.Empty(factory.ServiceRepository.All);
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("SUPERADMIN")]
    public async Task CreateService_NonBusinessRole_Returns403(string role)
    {
        Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var client = CreateClient(accountId, role);

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/services", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.ServiceRepository.All);
    }

    [Fact]
    public async Task CreateService_NoToken_Returns401()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/services", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.ServiceRepository.All);
    }

    [Fact]
    public async Task CreateService_UnknownBusiness_Returns404()
    {
        Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{Guid.NewGuid()}/services", ValidBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    public static TheoryData<string> InvalidBodies => new()
    {
        """{ "price": 5000, "durationMinutes": 30 }""",
        """{ "name": "   ", "price": 5000, "durationMinutes": 30 }""",
        $$"""{ "name": "{{new string('a', 151)}}", "price": 5000, "durationMinutes": 30 }""",
        """{ "name": "Corte", "durationMinutes": 30 }""",
        """{ "name": "Corte", "price": -1, "durationMinutes": 30 }""",
        """{ "name": "Corte", "price": 10000000000, "durationMinutes": 30 }""",
        """{ "name": "Corte", "price": 5000 }""",
        """{ "name": "Corte", "price": 5000, "durationMinutes": 0 }""",
        """{ "name": "Corte", "price": 5000, "durationMinutes": -10 }""",
        """{ "name": "Corte", "price": "gratis", "durationMinutes": 30 }""",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task CreateService_InvalidBody_Returns400AndCreatesNothing(string json)
    {
        var (business, client) = SeedOwnedBusiness();

        var response = await client.PostAsync(
            $"/business/businesses/{business.Id}/services",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Empty(factory.ServiceRepository.All);
    }

    private static object ValidBody() => new { name = "Corte", price = 5000, durationMinutes = 30 };

    // ---------- GET /business/businesses/{businessId}/services (list, public) ----------

    [Fact]
    public async Task ListBusinessServices_NoToken_Returns200WithActiveOnly()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("total").GetInt32());
        Assert.Equal(["Corte"], body.GetProperty("data").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ListBusinessServices_Owner_SeesInactiveAndIsActiveFilterWorks()
    {
        Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var all = await (await client.GetAsync($"/business/businesses/{business.Id}/services")).Content.ReadFromJsonAsync<JsonElement>();
        var inactive = await (await client.GetAsync($"/business/businesses/{business.Id}/services?isActive=false")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(["Corte", "Viejo"], all.GetProperty("data").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(["Viejo"], inactive.GetProperty("data").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ListBusinessServices_PublicIsActiveFalse_StillOnlyActive()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        var response = await factory.CreateClient().GetAsync($"/business/businesses/{business.Id}/services?isActive=false");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["Corte"], body.GetProperty("data").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ListBusinessServices_NonOwnerToken_TreatedAsPublic()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var body = await (await client.GetAsync($"/business/businesses/{business.Id}/services")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(["Corte"], body.GetProperty("data").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ListBusinessServices_Owner_InactiveBusiness_Returns200()
    {
        Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella", IsActive = false });
        factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.GetAsync($"/business/businesses/{business.Id}/services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListBusinessServices_BusinessWithoutServices_Returns200WithEmptyData()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("data").EnumerateArray());
        Assert.Equal(0, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListBusinessServices_UnknownBusiness_Returns404()
    {
        Reset();
        var response = await factory.CreateClient().GetAsync($"/business/businesses/{Guid.NewGuid()}/services");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBusinessServices_InactiveBusiness_Returns404()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });

        var response = await factory.CreateClient().GetAsync($"/business/businesses/{business.Id}/services");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task ListBusinessServices_InvalidPagination_Returns400(string queryString)
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });

        var response = await factory.CreateClient().GetAsync($"/business/businesses/{business.Id}/services{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    // ---------- GET /business/services/{serviceId} (detail, public) ----------

    [Fact]
    public async Task GetService_ActiveServiceOfActiveBusiness_Returns200()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", Price = 12000m, DurationMinutes = 45, IsActive = true });

        var response = await factory.CreateClient().GetAsync($"/business/services/{service.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(service.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Corte", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetService_Unknown_Returns404()
    {
        Reset();
        var response = await factory.CreateClient().GetAsync($"/business/services/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetService_InactiveService_Returns404()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella" });
        var service = factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });

        var response = await factory.CreateClient().GetAsync($"/business/services/{service.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetService_OfInactiveBusiness_Returns404()
    {
        Reset();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Bella", IsActive = false });
        var service = factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Corte", IsActive = true });

        var response = await factory.CreateClient().GetAsync($"/business/services/{service.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetService_Owner_InactiveService_Returns200()
    {
        Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        var service = factory.ServiceRepository.Seed(new ServiceEntity { BusinessId = business.Id, Name = "Viejo", IsActive = false });
        var client = CreateClient(accountId, "BUSINESS");

        var response = await client.GetAsync($"/business/services/{service.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private void Reset()
    {
        factory.BusinessRepository.Reset();
        factory.ServiceRepository.Reset();
    }

    private (BusinessEntity Business, HttpClient Client) SeedOwnedBusiness()
    {
        Reset();
        var accountId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = accountId, Name = "Bella" });
        return (business, CreateClient(accountId, "BUSINESS"));
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
