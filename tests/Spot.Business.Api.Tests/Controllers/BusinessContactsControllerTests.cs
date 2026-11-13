using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spot.Business.Api.Models;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class BusinessContactsControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    // ---------- GET /business/businesses/{businessId}/contacts ----------

    [Fact]
    public async Task ListContacts_NoToken_Returns200WithOnlyThatBusinessesContactsPrimaryFirst()
    {
        var (business, _) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222", CreatedAt = t0 });
        factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.WHATSAPP, Value = "8888", IsPrimary = true, CreatedAt = t0.AddMinutes(1) });
        factory.ContactRepository.Seed(new BusinessContact { BusinessId = other.Id, Type = ContactType.EMAIL, Value = "x@y.cr" });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/contacts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("WHATSAPP", data[0].GetProperty("type").GetString());
        Assert.True(data[0].GetProperty("isPrimary").GetBoolean());
        Assert.Equal(business.Id, data[0].GetProperty("businessId").GetGuid());
        Assert.Equal(2, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListContacts_Pagination_Page1And2AreDisjoint()
    {
        var (business, _) = Reset();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 3; i++)
            factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = $"tel-{i}", CreatedAt = t0.AddMinutes(i) });
        var client = factory.CreateClient();

        var page1 = await client.GetFromJsonAsync<JsonElement>($"/business/businesses/{business.Id}/contacts?page=1&pageSize=2");
        var page2 = await client.GetFromJsonAsync<JsonElement>($"/business/businesses/{business.Id}/contacts?page=2&pageSize=2");

        Assert.Equal(["tel-0", "tel-1"], page1.GetProperty("data").EnumerateArray().Select(c => c.GetProperty("value").GetString()));
        Assert.Equal(["tel-2"], page2.GetProperty("data").EnumerateArray().Select(c => c.GetProperty("value").GetString()));
        var pagination = page2.GetProperty("pagination");
        Assert.Equal(2, pagination.GetProperty("page").GetInt32());
        Assert.Equal(2, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, pagination.GetProperty("total").GetInt32());
        Assert.Equal(2, pagination.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task ListContacts_InvalidPagination_Returns400(string queryString)
    {
        var (business, _) = Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/contacts?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListContacts_UnknownBusiness_Returns404()
    {
        Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{Guid.NewGuid()}/contacts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListContacts_InactiveBusiness_Returns404()
    {
        var (business, _) = Reset();
        business.IsActive = false;
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/businesses/{business.Id}/contacts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- POST /business/businesses/{businessId}/contacts ----------

    [Fact]
    public async Task CreateContact_Owner_Returns201()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/contacts",
            new { type = "WHATSAPP", value = "  +506 8888-3344  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WHATSAPP", body.GetProperty("type").GetString());
        Assert.Equal("+50688883344", body.GetProperty("value").GetString());
        Assert.False(body.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(business.Id, body.GetProperty("businessId").GetGuid());
    }

    [Fact]
    public async Task CreateContact_PrimaryClearsPreviousPrimaryOfSameBusinessOnly()
    {
        var (business, ownerId) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        var oldPrimary = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222", IsPrimary = true });
        var otherPrimary = factory.ContactRepository.Seed(new BusinessContact { BusinessId = other.Id, Type = ContactType.PHONE, Value = "3333", IsPrimary = true });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/contacts",
            new { type = "EMAIL", value = "hola@bella.cr", isPrimary = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(oldPrimary.IsPrimary);
        Assert.True(otherPrimary.IsPrimary);
        Assert.Single(factory.ContactRepository.All, c => c.BusinessId == business.Id && c.IsPrimary);
    }

    [Fact]
    public async Task CreateContact_OwnerOfInactiveBusiness_Returns201()
    {
        var (business, ownerId) = Reset();
        business.IsActive = false;
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/contacts",
            new { type = "PHONE", value = "+506 2222-3344" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateContact_NonOwnerBusinessAccount_Returns403()
    {
        var (business, _) = Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/contacts",
            new { type = "PHONE", value = "+506 2222-3344" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", body.GetProperty("code").GetString());
        Assert.Empty(factory.ContactRepository.All);
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("SUPERADMIN")]
    public async Task CreateContact_NonBusinessRole_Returns403(string role)
    {
        var (business, _) = Reset();
        var client = CreateClient(Guid.NewGuid(), role);

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/contacts",
            new { type = "PHONE", value = "+506 2222-3344" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.ContactRepository.All);
    }

    [Fact]
    public async Task CreateContact_NoToken_Returns401()
    {
        var (business, _) = Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/business/businesses/{business.Id}/contacts",
            new { type = "PHONE", value = "+506 2222-3344" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateContact_UnknownBusiness_Returns404()
    {
        Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PostAsJsonAsync($"/business/businesses/{Guid.NewGuid()}/contacts",
            new { type = "PHONE", value = "+506 2222-3344" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"value\":\"2222\"}")]
    [InlineData("{\"type\":\"PHONE\"}")]
    [InlineData("{\"type\":\"PHONE\",\"value\":\"   \"}")]
    [InlineData("{\"type\":\"FAX\",\"value\":\"2222\"}")]
    [InlineData("{\"type\":1,\"value\":\"2222\"}")]
    [InlineData("{\"type\":\"PHONE\",\"value\":\"abc\"}")]
    [InlineData("{\"type\":\"WHATSAPP\",\"value\":\"8888-1234\"}")] // no country code
    public async Task CreateContact_InvalidBody_Returns400(string json)
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PostAsync($"/business/businesses/{business.Id}/contacts",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    // ---------- DELETE /business/businesses/{businessId}/contacts/{contactId} ----------

    // ---------- PUT /business/businesses/{businessId}/contacts/{contactId} ----------

    [Fact]
    public async Task UpdateContact_Owner_Returns200AndKeepsTheId()
    {
        var (business, ownerId) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}",
            new { type = "EMAIL", value = "  hola@bella.cr  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(contact.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("EMAIL", body.GetProperty("type").GetString());
        Assert.Equal("hola@bella.cr", body.GetProperty("value").GetString());
        var stored = Assert.Single(factory.ContactRepository.All);
        Assert.Equal(ContactType.EMAIL, stored.Type);
        Assert.Equal("hola@bella.cr", stored.Value);
    }

    [Fact]
    public async Task UpdateContact_MadePrimary_IsTheBusinessesOnlyPrimary()
    {
        var (business, ownerId) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        var oldPrimary = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222", IsPrimary = true });
        var otherPrimary = factory.ContactRepository.Seed(new BusinessContact { BusinessId = other.Id, Type = ContactType.PHONE, Value = "3333", IsPrimary = true });
        var target = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.EMAIL, Value = "a@b.cr" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{target.Id}",
            new { type = "EMAIL", value = "a@b.cr", isPrimary = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(target.IsPrimary);
        Assert.False(oldPrimary.IsPrimary);
        Assert.True(otherPrimary.IsPrimary);
    }

    [Fact]
    public async Task UpdateContact_PrimaryStaysPrimary()
    {
        var (business, ownerId) = Reset();
        var primary = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222", IsPrimary = true });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{primary.Id}",
            new { type = "PHONE", value = "+50622223344", isPrimary = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(primary.IsPrimary);
    }

    [Theory]
    [InlineData("PHONE", "  +506 2222-3344  ", "+50622223344")]
    [InlineData("WHATSAPP", "+506 8777-6655", "+50687776655")]
    public async Task UpdateContact_Phone_IsStoredAsE164(string type, string value, string expected)
    {
        var (business, ownerId) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.EMAIL, Value = "a@b.cr" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}", new { type, value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, body.GetProperty("value").GetString());
        Assert.Equal(expected, Assert.Single(factory.ContactRepository.All).Value);
    }

    [Fact]
    public async Task UpdateContact_InvalidPhone_Returns400AndKeepsTheContact()
    {
        var (business, ownerId) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "+50622223344" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}",
            new { type = "WHATSAPP", value = "8888-1234" }); // no country code

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("+50622223344", Assert.Single(factory.ContactRepository.All).Value);
    }

    [Theory]
    [InlineData("""{ "value": "2222" }""")]
    [InlineData("""{ "type": "PHONE", "value": "   " }""")]
    [InlineData("""{ "type": "FAX", "value": "2222" }""")]
    public async Task UpdateContact_InvalidBody_Returns400AndKeepsTheContact(string json)
    {
        var (business, ownerId) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("2222", Assert.Single(factory.ContactRepository.All).Value);
    }

    [Fact]
    public async Task UpdateContact_NonOwnerBusinessAccount_Returns403()
    {
        var (business, _) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222" });
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}",
            new { type = "PHONE", value = "+50688889999" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("2222", contact.Value);
    }

    [Fact]
    public async Task UpdateContact_ContactOfAnotherBusiness_Returns404AndKeepsIt()
    {
        var (business, ownerId) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        var foreign = factory.ContactRepository.Seed(new BusinessContact { BusinessId = other.Id, Type = ContactType.PHONE, Value = "3333" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.PutAsJsonAsync($"/business/businesses/{business.Id}/contacts/{foreign.Id}",
            new { type = "PHONE", value = "+50688889999" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("3333", foreign.Value);
    }

    [Fact]
    public async Task UpdateContact_NoToken_Returns401()
    {
        var (business, _) = Reset();

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/business/businesses/{business.Id}/contacts/{Guid.NewGuid()}", new { type = "PHONE", value = "+50688889999" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- DELETE /business/businesses/{businessId}/contacts/{contactId} ----------

    [Fact]
    public async Task DeleteContact_Owner_Returns204AndRemovesOnlyThatContact()
    {
        var (business, ownerId) = Reset();
        var target = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222" });
        var kept = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.EMAIL, Value = "a@b.cr" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/contacts/{target.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(kept.Id, Assert.Single(factory.ContactRepository.All).Id);
    }

    [Fact]
    public async Task DeleteContact_NonOwnerBusinessAccount_Returns403AndKeepsContact()
    {
        var (business, _) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222" });
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(factory.ContactRepository.All);
    }

    [Fact]
    public async Task DeleteContact_ClientRole_Returns403()
    {
        var (business, _) = Reset();
        var contact = factory.ContactRepository.Seed(new BusinessContact { BusinessId = business.Id, Type = ContactType.PHONE, Value = "2222" });
        var client = CreateClient(Guid.NewGuid(), "CLIENT");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(factory.ContactRepository.All);
    }

    [Fact]
    public async Task DeleteContact_UnknownContact_Returns404()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/contacts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteContact_UnknownBusiness_Returns404()
    {
        Reset();
        var client = CreateClient(Guid.NewGuid(), "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{Guid.NewGuid()}/contacts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteContact_ContactOfAnotherBusiness_Returns404AndKeepsIt()
    {
        // The caller owns business A and targets a contact of business B through A's route.
        var (business, ownerId) = Reset();
        var other = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = Guid.NewGuid(), Name = "Otro" });
        var foreign = factory.ContactRepository.Seed(new BusinessContact { BusinessId = other.Id, Type = ContactType.PHONE, Value = "3333" });
        var client = CreateClient(ownerId, "BUSINESS");

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/contacts/{foreign.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(factory.ContactRepository.All);
    }

    /// <summary>Clears both fakes and seeds one active business, returning it and its owner's account id.</summary>
    private (BusinessEntity Business, Guid OwnerId) Reset()
    {
        factory.BusinessRepository.Reset();
        factory.ContactRepository.Reset();
        var ownerId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = ownerId, Name = "Bella" });
        return (business, ownerId);
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
