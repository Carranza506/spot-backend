using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Tests.Controllers;

[Collection(ApiFactoryCollection.Name)]
public class CategoriesControllerTests(CategoriesApiFactory factory) : IClassFixture<CategoriesApiFactory>
{
    [Fact]
    public async Task ListCategories_NoFilter_ReturnsAllPaginated()
    {
        factory.CategoryRepository.Reset();
        factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        factory.CategoryRepository.Seed(new Category { Name = "Belleza" });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/business/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());
        Assert.Equal(2, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListCategories_RootFilter_ExcludesSubcategories()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        factory.CategoryRepository.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/business/categories?parentCategoryId=null");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task ListCategories_SpecificParentFilter_ReturnsOnlyThatParentsChildren()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        factory.CategoryRepository.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/business/categories?parentCategoryId={root.Id}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("data").GetArrayLength());
        Assert.Equal("Farmacia", body.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListCategories_InvalidParentCategoryId_Returns400()
    {
        factory.CategoryRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/business/categories?parentCategoryId=not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListCategories_PageSizeTooLarge_Returns400()
    {
        factory.CategoryRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/business/categories?pageSize=1000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_NoToken_Returns401()
    {
        factory.CategoryRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/business/categories", new { name = "Salud" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_NonSuperadminToken_Returns403()
    {
        factory.CategoryRepository.Reset();
        var client = CreateAuthenticatedClient("CLIENT");

        var response = await client.PostAsJsonAsync("/business/categories", new { name = "Salud" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_ValidRootCategory_Returns201()
    {
        factory.CategoryRepository.Reset();
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PostAsJsonAsync("/business/categories", new { name = "Salud" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Salud", body.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("parentCategoryId").ValueKind);
    }

    [Fact]
    public async Task CreateCategory_ValidSubcategory_Returns201()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PostAsJsonAsync(
            "/business/categories", new { name = "Farmacia", parentCategoryId = root.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_NonexistentParent_Returns404()
    {
        factory.CategoryRepository.Reset();
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PostAsJsonAsync(
            "/business/categories", new { name = "Farmacia", parentCategoryId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_SubcategoryAsParent_Returns400()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var child = factory.CategoryRepository.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PostAsJsonAsync(
            "/business/categories", new { name = "Nieta", parentCategoryId = child.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_DuplicateNameSameParent_Returns409()
    {
        factory.CategoryRepository.Reset();
        factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PostAsJsonAsync("/business/categories", new { name = "Salud" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_MissingName_Returns400WithErrorShape()
    {
        factory.CategoryRepository.Reset();
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PostAsJsonAsync("/business/categories", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateCategory_NoToken_Returns401()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync($"/business/categories/{category.Id}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_NonSuperadminToken_Returns403()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("BUSINESS");

        var response = await client.PatchAsJsonAsync($"/business/categories/{category.Id}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_NonexistentCategory_Returns404()
    {
        factory.CategoryRepository.Reset();
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PatchAsJsonAsync($"/business/categories/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_SelfReference_Returns400()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PatchAsJsonAsync(
            $"/business/categories/{category.Id}", new { parentCategoryId = category.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_CategoryWithChildrenBecomesSubcategory_Returns400()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        factory.CategoryRepository.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var otherRoot = factory.CategoryRepository.Seed(new Category { Name = "Belleza" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PatchAsJsonAsync(
            $"/business/categories/{root.Id}", new { parentCategoryId = otherRoot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_DuplicateNameSameParent_Returns409()
    {
        factory.CategoryRepository.Reset();
        factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var belleza = factory.CategoryRepository.Seed(new Category { Name = "Belleza" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PatchAsJsonAsync($"/business/categories/{belleza.Id}", new { name = "Salud" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_ValidRename_Returns200()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PatchAsJsonAsync(
            $"/business/categories/{category.Id}", new { name = "Salud y Bienestar" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Salud y Bienestar", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateCategory_PromoteToRootViaExplicitNull_Returns200()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var child = factory.CategoryRepository.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.PatchAsJsonAsync(
            $"/business/categories/{child.Id}", new { parentCategoryId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("parentCategoryId").ValueKind);
    }

    [Fact]
    public async Task DeleteCategory_NoToken_Returns401()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/business/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_NonSuperadminToken_Returns403()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("CLIENT");

        var response = await client.DeleteAsync($"/business/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_NonexistentCategory_Returns404()
    {
        factory.CategoryRepository.Reset();
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.DeleteAsync($"/business/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_HasSubcategories_Returns409()
    {
        factory.CategoryRepository.Reset();
        var root = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        factory.CategoryRepository.Seed(new Category { Name = "Farmacia", ParentCategoryId = root.Id });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.DeleteAsync($"/business/categories/{root.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_LeafCategory_Returns204()
    {
        factory.CategoryRepository.Reset();
        var category = factory.CategoryRepository.Seed(new Category { Name = "Salud" });
        var client = CreateAuthenticatedClient("SUPERADMIN");

        var response = await client.DeleteAsync($"/business/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(Guid.NewGuid(), role));
        return client;
    }
}
