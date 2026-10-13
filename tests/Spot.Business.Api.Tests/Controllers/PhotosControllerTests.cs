using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Spot.Business.Api.Models;
using Spot.Business.Api.Storage;
using Spot.Business.Api.Tests.Fakes;
using Spot.Business.Api.Tests.Photos;
using BusinessEntity = Spot.Business.Api.Models.Business;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Controllers;

/// <summary>
/// Business photos, service photos and photo files (#65) through the real pipeline: routing, JWT
/// and roles, the hand-read multipart form, the real PhotoService and the real SkiaSharp
/// processing. Only the rows and the stored files are faked.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class PhotosControllerTests(BusinessesApiFactory factory) : IClassFixture<BusinessesApiFactory>
{
    private static byte[] Png => TestImages.Encode(SKEncodedImageFormat.Png, 24, 16);

    // ---------- GET /business/businesses/{businessId}/photos ----------

    [Fact]
    public async Task ListBusinessPhotos_NoToken_Returns200InContractShape()
    {
        var (business, _) = Reset();
        var key = PhotoStorageKey.Create(PhotoFormat.Jpeg);
        var photo = factory.PhotoRepository.SeedBusinessPhoto(new BusinessPhoto
        {
            BusinessId = business.Id, StorageKey = key, IsPrimary = true, DisplayOrder = 2,
        });

        var response = await factory.CreateClient().GetAsync($"/business/businesses/{business.Id}/photos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("data").EnumerateArray());
        Assert.Equal(photo.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(FakePhotoStorage.PublicBaseUrl + key, item.GetProperty("url").GetString());
        Assert.True(item.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(2, item.GetProperty("displayOrder").GetInt32());
        Assert.True(item.TryGetProperty("createdAt", out _));
        // Internal details stay internal.
        Assert.False(item.TryGetProperty("storageKey", out _));
        Assert.False(item.TryGetProperty("businessId", out _));
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListBusinessPhotos_UnknownOrInactiveBusiness_Returns404(bool existsButInactive)
    {
        var (business, _) = Reset(businessActive: false);
        var id = existsButInactive ? business.Id : Guid.NewGuid();

        var response = await factory.CreateClient().GetAsync($"/business/businesses/{id}/photos");

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "El negocio indicado no existe.");
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=abc")]
    public async Task ListBusinessPhotos_InvalidPaging_Returns400(string query)
    {
        var (business, _) = Reset();

        var response = await factory.CreateClient().GetAsync($"/business/businesses/{business.Id}/photos{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- POST /business/businesses/{businessId}/photos ----------

    [Fact]
    public async Task UploadBusinessPhoto_Owner_Returns201WithThePhoto()
    {
        var (business, ownerId) = Reset();

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync(
            $"/business/businesses/{business.Id}/photos",
            Form(Png, fields: [("isPrimary", "true"), ("displayOrder", "3")]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(factory.PhotoRepository.BusinessPhotos);
        Assert.Equal(row.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(FakePhotoStorage.PublicBaseUrl + row.StorageKey, body.GetProperty("url").GetString());
        Assert.True(body.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(3, body.GetProperty("displayOrder").GetInt32());
        Assert.EndsWith(".png", row.StorageKey);
        Assert.True(factory.PhotoStorage.Files.ContainsKey(row.StorageKey));
    }

    [Fact]
    public async Task UploadBusinessPhoto_Primary_ReplacesThePreviousPrimary()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");

        await client.PostAsync($"/business/businesses/{business.Id}/photos", Form(Png, fields: [("isPrimary", "true")]));
        var second = await client.PostAsync($"/business/businesses/{business.Id}/photos", Form(Png, fields: [("isPrimary", "true")]));

        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var primary = Assert.Single(factory.PhotoRepository.BusinessPhotos, p => p.IsPrimary);
        Assert.Equal(secondId, primary.Id);
    }

    [Fact]
    public async Task UploadBusinessPhoto_NoToken_Returns401()
    {
        var (business, _) = Reset();

        var response = await factory.CreateClient().PostAsync($"/business/businesses/{business.Id}/photos", Form(Png));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhoto_ClientRole_Returns403()
    {
        var (business, ownerId) = Reset();

        var response = await CreateClient(ownerId, "CLIENT").PostAsync($"/business/businesses/{business.Id}/photos", Form(Png));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhoto_AnotherBusinessAccount_Returns403AndStoresNothing()
    {
        var (business, _) = Reset();

        var response = await CreateClient(Guid.NewGuid(), "BUSINESS").PostAsync($"/business/businesses/{business.Id}/photos", Form(Png));

        await AssertError(response, HttpStatusCode.Forbidden, "FORBIDDEN", "No tienes permiso para realizar esta acción.");
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhoto_UnknownBusiness_Returns404()
    {
        Reset();

        var response = await CreateClient(Guid.NewGuid(), "BUSINESS").PostAsync($"/business/businesses/{Guid.NewGuid()}/photos", Form(Png));

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "El negocio indicado no existe.");
    }

    public static TheoryData<string, string> InvalidForms => new()
    {
        { "missing file", "missing-file" },
        { "two files", "two-files" },
        { "wrong field name", "wrong-field" },
        { "empty file", "empty-file" },
        { "negative displayOrder", "displayOrder=-1" },
        { "non-numeric displayOrder", "displayOrder=abc" },
        { "decimal displayOrder", "displayOrder=1.5" },
        { "overflowing displayOrder", "displayOrder=99999999999" },
        { "repeated displayOrder", "displayOrder-twice" },
        { "invalid isPrimary", "isPrimary=maybe" },
    };

    [Theory]
    [MemberData(nameof(InvalidForms))]
    public async Task UploadBusinessPhoto_InvalidForm_Returns400WithTheField(string name, string variant)
    {
        _ = name;
        var (business, ownerId) = Reset();
        var (content, field) = variant switch
        {
            "missing-file" => (FormWithoutFile([("displayOrder", "1")]), "file"),
            "two-files" => (TwoFiles(), "file"),
            "wrong-field" => (Form(Png, fileField: "photo"), "file"),
            "empty-file" => (Form([]), "file"),
            "displayOrder-twice" => (Form(Png, fields: [("displayOrder", "1"), ("displayOrder", "2")]), "displayOrder"),
            _ when variant.StartsWith("displayOrder=") => (Form(Png, fields: [("displayOrder", variant["displayOrder=".Length..])]), "displayOrder"),
            _ => (Form(Png, fields: [("isPrimary", variant["isPrimary=".Length..])]), "isPrimary"),
        };

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync($"/business/businesses/{business.Id}/photos", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Equal(field, body.GetProperty("details").GetProperty("field").GetString());
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhoto_JsonBody_Returns400()
    {
        var (business, ownerId) = Reset();

        var response = await CreateClient(ownerId, "BUSINESS").PostAsJsonAsync(
            $"/business/businesses/{business.Id}/photos", new { file = Convert.ToBase64String(Png) });

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST", "La foto debe enviarse como multipart/form-data.");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><body><script>alert(1)</script></body></html>")]
    public async Task UploadBusinessPhoto_DisguisedAsJpeg_Returns400AndStoresNothing(string payload)
    {
        // File name and Content-Type both claim JPEG; only the bytes count.
        var (business, ownerId) = Reset();

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync(
            $"/business/businesses/{business.Id}/photos", Form(Encoding.UTF8.GetBytes(payload), "photo.jpg", "image/jpeg"));

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST", "La foto debe ser JPG, PNG o WebP.");
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhoto_HeaderClaimingTooManyPixels_Returns400()
    {
        var (business, ownerId) = Reset();
        var bomb = TestImages.WithFakeJpegDimensions(TestImages.Encode(SKEncodedImageFormat.Jpeg, 8, 8), 10_000, 10_000);

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync($"/business/businesses/{business.Id}/photos", Form(bomb));

        await AssertError(response, HttpStatusCode.BadRequest, "BAD_REQUEST", "La foto no puede superar los 40 megapíxeles.");
    }

    [Theory]
    [InlineData(5 * 1024 * 1024 + 1)] // just over the file limit, within the request limit
    [InlineData(7 * 1024 * 1024)]     // over the whole request limit
    public async Task UploadBusinessPhoto_TooLarge_Returns413(int size)
    {
        var (business, ownerId) = Reset();

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync($"/business/businesses/{business.Id}/photos", Form(new byte[size]));

        await AssertError(response, HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE", "La foto no puede pesar más de 5 MB.");
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhoto_AtTheLimit_Returns409()
    {
        var (business, ownerId) = Reset();
        for (var i = 0; i < 20; i++)
            factory.PhotoRepository.SeedBusinessPhoto(new BusinessPhoto { BusinessId = business.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg) });

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync($"/business/businesses/{business.Id}/photos", Form(Png));

        await AssertError(response, HttpStatusCode.Conflict, "PHOTO_LIMIT_REACHED", "El negocio ya tiene el máximo de 20 fotos.");
    }

    // ---------- DELETE /business/businesses/{businessId}/photos/{photoId} ----------

    [Fact]
    public async Task DeleteBusinessPhoto_Owner_Returns204AndTheFileIsGone()
    {
        var (business, ownerId) = Reset();
        var client = CreateClient(ownerId, "BUSINESS");
        var uploaded = await (await client.PostAsync($"/business/businesses/{business.Id}/photos", Form(Png)))
            .Content.ReadFromJsonAsync<JsonElement>();
        var fileUrl = FilePath(uploaded.GetProperty("url").GetString()!);

        var response = await client.DeleteAsync($"/business/businesses/{business.Id}/photos/{uploaded.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.PhotoRepository.BusinessPhotos);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(fileUrl)).StatusCode);
    }

    [Fact]
    public async Task DeleteBusinessPhoto_UnknownPhoto_Returns404()
    {
        var (business, ownerId) = Reset();

        var response = await CreateClient(ownerId, "BUSINESS").DeleteAsync($"/business/businesses/{business.Id}/photos/{Guid.NewGuid()}");

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "La foto indicada no existe.");
    }

    [Fact]
    public async Task DeleteBusinessPhoto_AnotherBusinessAccount_Returns403AndKeepsIt()
    {
        var (business, _) = Reset();
        var photo = factory.PhotoRepository.SeedBusinessPhoto(new BusinessPhoto { BusinessId = business.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg) });

        var response = await CreateClient(Guid.NewGuid(), "BUSINESS").DeleteAsync($"/business/businesses/{business.Id}/photos/{photo.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(factory.PhotoRepository.BusinessPhotos);
    }

    [Fact]
    public async Task DeleteBusinessPhoto_NoToken_Returns401()
    {
        var (business, _) = Reset();

        var response = await factory.CreateClient().DeleteAsync($"/business/businesses/{business.Id}/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Service photos ----------

    [Fact]
    public async Task ListServicePhotos_NoToken_Returns200WithoutIsPrimary()
    {
        var (business, _) = Reset();
        var service = SeedService(business);
        factory.PhotoRepository.SeedServicePhoto(new ServicePhoto { ServiceId = service.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Webp) });

        var response = await factory.CreateClient().GetAsync($"/business/services/{service.Id}/photos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray());
        Assert.False(item.TryGetProperty("isPrimary", out _));
    }

    [Fact]
    public async Task ListServicePhotos_InactiveService_Returns404()
    {
        var (business, _) = Reset();
        var service = SeedService(business, isActive: false);

        var response = await factory.CreateClient().GetAsync($"/business/services/{service.Id}/photos");

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "El servicio indicado no existe.");
    }

    [Fact]
    public async Task UploadServicePhoto_Owner_Returns201_AndIgnoresIsPrimary()
    {
        var (business, ownerId) = Reset();
        var service = SeedService(business);

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync(
            $"/business/services/{service.Id}/photos", Form(Png, fields: [("isPrimary", "maybe"), ("displayOrder", "1")]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("isPrimary", out _));
        Assert.Equal(1, body.GetProperty("displayOrder").GetInt32());
        Assert.Single(factory.PhotoRepository.ServicePhotos);
    }

    [Fact]
    public async Task UploadServicePhoto_AnotherBusinessAccount_Returns403()
    {
        var (business, _) = Reset();
        var service = SeedService(business);

        var response = await CreateClient(Guid.NewGuid(), "BUSINESS").PostAsync($"/business/services/{service.Id}/photos", Form(Png));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.PhotoStorage.Files);
    }

    [Fact]
    public async Task UploadServicePhoto_UnknownService_Returns404()
    {
        Reset();

        var response = await CreateClient(Guid.NewGuid(), "BUSINESS").PostAsync($"/business/services/{Guid.NewGuid()}/photos", Form(Png));

        await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "El servicio indicado no existe.");
    }

    [Fact]
    public async Task UploadServicePhoto_AtTheLimit_Returns409()
    {
        var (business, ownerId) = Reset();
        var service = SeedService(business);
        for (var i = 0; i < 10; i++)
            factory.PhotoRepository.SeedServicePhoto(new ServicePhoto { ServiceId = service.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg) });

        var response = await CreateClient(ownerId, "BUSINESS").PostAsync($"/business/services/{service.Id}/photos", Form(Png));

        await AssertError(response, HttpStatusCode.Conflict, "PHOTO_LIMIT_REACHED", "El servicio ya tiene el máximo de 10 fotos.");
    }

    [Fact]
    public async Task DeleteServicePhoto_Owner_Returns204()
    {
        var (business, ownerId) = Reset();
        var service = SeedService(business);
        var client = CreateClient(ownerId, "BUSINESS");
        var uploaded = await (await client.PostAsync($"/business/services/{service.Id}/photos", Form(Png)))
            .Content.ReadFromJsonAsync<JsonElement>();

        var response = await client.DeleteAsync($"/business/services/{service.Id}/photos/{uploaded.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.PhotoStorage.Files);
    }

    // ---------- GET /business/media/photos/{fileName} ----------

    [Fact]
    public async Task GetPhotoFile_UploadedPhoto_IsServedWithSecurityHeaders()
    {
        var (business, ownerId) = Reset();
        var upload = TestImages.WithExif(TestImages.Encode(SKEncodedImageFormat.Jpeg, 40, 20), orientation: 1);
        var uploaded = await (await CreateClient(ownerId, "BUSINESS").PostAsync($"/business/businesses/{business.Id}/photos", Form(upload)))
            .Content.ReadFromJsonAsync<JsonElement>();
        var path = FilePath(uploaded.GetProperty("url").GetString()!);

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("default-src 'none'; sandbox", Single(response, "Content-Security-Policy"));
        Assert.Equal("cross-origin", Single(response, "Cross-Origin-Resource-Policy"));
        Assert.Equal("public, max-age=31536000, immutable", Single(response, "Cache-Control"));
        Assert.NotNull(response.Headers.ETag);
        // What's served is the re-encoded image: decodable, and without the uploaded EXIF.
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotNull(SKBitmap.Decode(bytes));
        Assert.False(TestImages.Contains(bytes, TestImages.SecretMarker));
    }

    [Fact]
    public async Task GetPhotoFile_IfNoneMatch_Returns304()
    {
        Reset();
        var key = await factory.PhotoStorage.SaveAsync(new MemoryStream(Png), PhotoFormat.Png);
        var client = factory.CreateClient();
        var first = await client.GetAsync($"/business/media/photos/{key}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/business/media/photos/{key}");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Theory]
    [InlineData("..%2Fappsettings.json")]
    [InlineData("%2e%2e%2f%2e%2e%2fetc%2fpasswd")]
    [InlineData("..%5C..%5Cappsettings.json")]
    [InlineData("3F2B9C1E8D7A4B6C9E0F1A2B3C4D5E6F.JPG")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.html")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")] // well-formed, but no such file
    public async Task GetPhotoFile_InvalidOrUnknownName_Returns404(string fileName)
    {
        Reset();

        var response = await factory.CreateClient().GetAsync($"/business/media/photos/{fileName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private (BusinessEntity Business, Guid OwnerId) Reset(bool businessActive = true)
    {
        factory.BusinessRepository.Reset();
        factory.PhotoRepository.Reset();
        factory.PhotoStorage.Reset();

        var ownerId = Guid.NewGuid();
        var business = factory.BusinessRepository.Seed(new BusinessEntity { AccountId = ownerId, Name = "Bella", IsActive = businessActive });
        return (business, ownerId);
    }

    private ServiceEntity SeedService(BusinessEntity business, bool isActive = true) =>
        factory.PhotoRepository.SeedService(new ServiceEntity
        {
            Name = "Corte", Price = 5000, DurationMinutes = 30, IsActive = isActive, Business = business,
        });

    private static MultipartFormDataContent Form(
        byte[] file,
        string fileName = "photo.png",
        string contentType = "image/png",
        (string Name, string Value)[]? fields = null,
        string fileField = "file")
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, fileField, fileName);
        foreach (var (name, value) in fields ?? [])
            form.Add(new StringContent(value), name);
        return form;
    }

    private static MultipartFormDataContent FormWithoutFile((string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
            form.Add(new StringContent(value), name);
        return form;
    }

    private static MultipartFormDataContent TwoFiles()
    {
        var form = Form(Png);
        form.Add(new ByteArrayContent(Png), "file", "second.png");
        return form;
    }

    /// <summary>The path of a Photo.url, to request it from the test server.</summary>
    private static string FilePath(string url) =>
        "/business/media/photos/" + url[FakePhotoStorage.PublicBaseUrl.Length..];

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.TryGetValues(header, out var values) ? values : []);

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId, role));
        return client;
    }
}
