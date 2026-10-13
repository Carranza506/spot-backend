using Microsoft.Extensions.Logging;
using SkiaSharp;
using Spot.Business.Api.Models;
using Spot.Business.Api.Options;
using Spot.Business.Api.Photos;
using Spot.Business.Api.Repositories;
using Spot.Business.Api.Services;
using Spot.Business.Api.Storage;
using Spot.Business.Api.Tests.Fakes;
using Spot.Business.Api.Tests.Photos;
using BusinessEntity = Spot.Business.Api.Models.Business;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Tests.Services;

public sealed class PhotoServiceTests : IDisposable
{
    private readonly FakeBusinessRepository _businesses = new();
    private readonly FakePhotoRepository _photos = new();
    private readonly FakePhotoStorage _storage = new();
    private readonly CountingPhotoImageProcessor _processor = new();
    private readonly CapturingLogger<PhotoService> _logger = new();
    private readonly PhotoService _service;

    private readonly Guid _ownerId = Guid.NewGuid();

    public PhotoServiceTests()
    {
        _service = new PhotoService(
            _businesses,
            _photos,
            _processor,
            _storage,
            Microsoft.Extensions.Options.Options.Create(new PhotoLimitsOptions { MaxPerBusiness = 3, MaxPerService = 2 }),
            _logger);
    }

    public void Dispose() => _processor.Dispose();

    // ---------- Business photos: listing ----------

    [Fact]
    public async Task ListBusinessPhotosAsync_UnknownOrInactiveBusiness_ReturnsNull()
    {
        var inactive = SeedBusiness(isActive: false);
        _photos.SeedBusinessPhoto(new BusinessPhoto { BusinessId = inactive.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg) });

        Assert.Null(await _service.ListBusinessPhotosAsync(Guid.NewGuid(), 1, 20));
        Assert.Null(await _service.ListBusinessPhotosAsync(inactive.Id, 1, 20));
    }

    [Fact]
    public async Task ListBusinessPhotosAsync_ReturnsPublicUrlsAndPaginationMeta()
    {
        var business = SeedBusiness();
        var key = PhotoStorageKey.Create(PhotoFormat.Webp);
        _photos.SeedBusinessPhoto(new BusinessPhoto { BusinessId = business.Id, StorageKey = key, IsPrimary = true, DisplayOrder = 4 });
        _photos.SeedBusinessPhoto(new BusinessPhoto { BusinessId = business.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg), DisplayOrder = 5 });

        var result = await _service.ListBusinessPhotosAsync(business.Id, page: 1, pageSize: 1);

        Assert.NotNull(result);
        var photo = Assert.Single(result!.Data);
        Assert.Equal(FakePhotoStorage.PublicBaseUrl + key, photo.Url);
        Assert.True(photo.IsPrimary);
        Assert.Equal(4, photo.DisplayOrder);
        Assert.Equal((1, 1, 2, 2), (result.Pagination.Page, result.Pagination.PageSize, result.Pagination.Total, result.Pagination.TotalPages));
    }

    // ---------- Business photos: upload ----------

    [Fact]
    public async Task UploadBusinessPhotoAsync_Owner_StoresTheReencodedImageAndTheRow()
    {
        var business = SeedBusiness();
        var upload = TestImages.WithExif(TestImages.Encode(SKEncodedImageFormat.Jpeg, 60, 40), orientation: 1);

        var photo = await _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(upload, displayOrder: 2, isPrimary: true));

        var row = Assert.Single(_photos.BusinessPhotos);
        Assert.Equal(row.Id, photo.Id);
        Assert.True(row.IsPrimary);
        Assert.Equal(2, row.DisplayOrder);
        Assert.EndsWith(".jpg", row.StorageKey);
        Assert.Equal(FakePhotoStorage.PublicBaseUrl + row.StorageKey, photo.Url);
        // Only the re-encoded image is stored, never the uploaded bytes (or their EXIF).
        var stored = _storage.Files[row.StorageKey];
        Assert.NotEqual(upload, stored);
        Assert.False(TestImages.Contains(stored, TestImages.SecretMarker));
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_InactiveBusiness_IsStillAllowedForTheOwner()
    {
        var business = SeedBusiness(isActive: false);

        await _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes()));

        Assert.Single(_photos.BusinessPhotos);
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_UnknownBusiness_ThrowsWithoutProcessingTheImage()
    {
        await Assert.ThrowsAsync<BusinessNotFoundException>(
            () => _service.UploadBusinessPhotoAsync(Guid.NewGuid(), _ownerId, Upload(PngBytes())));

        AssertNothingProcessedOrStored();
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_NotTheOwner_ThrowsWithoutProcessingTheImage()
    {
        var business = SeedBusiness();

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.UploadBusinessPhotoAsync(business.Id, Guid.NewGuid(), Upload(PngBytes())));

        AssertNothingProcessedOrStored();
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_AtTheLimit_ThrowsWithoutProcessingTheImage()
    {
        var business = SeedBusiness();
        for (var i = 0; i < 3; i++)
            _photos.SeedBusinessPhoto(new BusinessPhoto { BusinessId = business.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg) });

        var ex = await Assert.ThrowsAsync<PhotoLimitReachedException>(
            () => _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes())));

        Assert.Equal(3, ex.MaxPhotos);
        Assert.Equal(0, _processor.Calls);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_NotAnImage_ThrowsAndStoresNothing()
    {
        var business = SeedBusiness();

        await Assert.ThrowsAsync<InvalidPhotoException>(
            () => _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload("<svg/>"u8.ToArray())));

        Assert.Empty(_photos.BusinessPhotos);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_InsertFailsAfterTheFileWasStored_DeletesTheFile()
    {
        // E.g. a concurrent upload took the last slot between the pre-check and the locked insert.
        var business = SeedBusiness();
        _photos.FailNextAddWith = new PhotoLimitReachedException(3);

        await Assert.ThrowsAsync<PhotoLimitReachedException>(
            () => _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes())));

        Assert.Equal(1, _processor.Calls);
        Assert.Empty(_storage.Files);
        Assert.Empty(_photos.BusinessPhotos);
    }

    [Fact]
    public async Task UploadBusinessPhotoAsync_InsertAndCleanupBothFail_RethrowsTheOriginalErrorAndLogsTheOrphan()
    {
        var business = SeedBusiness();
        var dbError = new InvalidOperationException("Simulated database failure.");
        _photos.FailNextAddWith = dbError;
        _storage.FailDeletes = true;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes())));

        Assert.Same(dbError, thrown);
        var orphan = Assert.Single(_storage.Files).Key;
        var entry = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(orphan, entry.Message);
    }

    // ---------- Business photos: delete ----------

    [Fact]
    public async Task DeleteBusinessPhotoAsync_Owner_RemovesTheRowAndTheFile()
    {
        var business = SeedBusiness();
        var photo = await _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes()));

        Assert.True(await _service.DeleteBusinessPhotoAsync(business.Id, photo.Id, _ownerId));

        Assert.Empty(_photos.BusinessPhotos);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task DeleteBusinessPhotoAsync_PhotoOfAnotherBusiness_ReturnsFalseAndKeepsIt()
    {
        var mine = SeedBusiness();
        var theirsOwner = Guid.NewGuid();
        var theirs = _businesses.Seed(new BusinessEntity { AccountId = theirsOwner, Name = "Otro" });
        var theirPhoto = await _service.UploadBusinessPhotoAsync(theirs.Id, theirsOwner, Upload(PngBytes()));

        Assert.False(await _service.DeleteBusinessPhotoAsync(mine.Id, theirPhoto.Id, _ownerId));

        Assert.Single(_photos.BusinessPhotos);
        Assert.Single(_storage.Files);
    }

    [Fact]
    public async Task DeleteBusinessPhotoAsync_NotTheOwner_ThrowsAndKeepsIt()
    {
        var business = SeedBusiness();
        var photo = await _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes()));

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.DeleteBusinessPhotoAsync(business.Id, photo.Id, Guid.NewGuid()));

        Assert.Single(_photos.BusinessPhotos);
        Assert.Single(_storage.Files);
    }

    [Fact]
    public async Task DeleteBusinessPhotoAsync_FileCantBeDeleted_StillSucceedsAndLogsTheOrphan()
    {
        var business = SeedBusiness();
        var photo = await _service.UploadBusinessPhotoAsync(business.Id, _ownerId, Upload(PngBytes()));
        _storage.FailDeletes = true;

        Assert.True(await _service.DeleteBusinessPhotoAsync(business.Id, photo.Id, _ownerId));

        Assert.Empty(_photos.BusinessPhotos);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    // ---------- Service photos ----------

    [Fact]
    public async Task ListServicePhotosAsync_HiddenUnlessTheServiceAndItsBusinessAreActive()
    {
        var unknown = Guid.NewGuid();
        var inactiveService = SeedService(serviceActive: false);
        var inactiveBusiness = SeedService(businessActive: false);
        var visible = SeedService();

        Assert.Null(await _service.ListServicePhotosAsync(unknown, 1, 20));
        Assert.Null(await _service.ListServicePhotosAsync(inactiveService.Id, 1, 20));
        Assert.Null(await _service.ListServicePhotosAsync(inactiveBusiness.Id, 1, 20));
        Assert.NotNull(await _service.ListServicePhotosAsync(visible.Id, 1, 20));
    }

    [Fact]
    public async Task UploadServicePhotoAsync_Owner_StoresItWithoutAPrimaryFlag()
    {
        var service = SeedService();

        var photo = await _service.UploadServicePhotoAsync(service.Id, _ownerId, Upload(PngBytes(), displayOrder: 1));

        Assert.Null(photo.IsPrimary);
        Assert.Equal(1, photo.DisplayOrder);
        var row = Assert.Single(_photos.ServicePhotos);
        Assert.Equal(service.Id, row.ServiceId);
        Assert.True(_storage.Files.ContainsKey(row.StorageKey));
    }

    [Fact]
    public async Task UploadServicePhotoAsync_InactiveService_IsStillAllowedForTheOwner()
    {
        var service = SeedService(serviceActive: false, businessActive: false);

        await _service.UploadServicePhotoAsync(service.Id, _ownerId, Upload(PngBytes()));

        Assert.Single(_photos.ServicePhotos);
    }

    [Fact]
    public async Task UploadServicePhotoAsync_UnknownService_ThrowsWithoutProcessingTheImage()
    {
        await Assert.ThrowsAsync<ServiceNotFoundException>(
            () => _service.UploadServicePhotoAsync(Guid.NewGuid(), _ownerId, Upload(PngBytes())));

        AssertNothingProcessedOrStored();
    }

    [Fact]
    public async Task UploadServicePhotoAsync_NotTheOwnerOfItsBusiness_ThrowsWithoutProcessingTheImage()
    {
        var service = SeedService();

        var ex = await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.UploadServicePhotoAsync(service.Id, Guid.NewGuid(), Upload(PngBytes())));

        Assert.Equal(service.BusinessId, ex.BusinessId);
        AssertNothingProcessedOrStored();
    }

    [Fact]
    public async Task UploadServicePhotoAsync_AtTheLimit_ThrowsWithoutProcessingTheImage()
    {
        var service = SeedService();
        for (var i = 0; i < 2; i++)
            _photos.SeedServicePhoto(new ServicePhoto { ServiceId = service.Id, StorageKey = PhotoStorageKey.Create(PhotoFormat.Jpeg) });

        await Assert.ThrowsAsync<PhotoLimitReachedException>(
            () => _service.UploadServicePhotoAsync(service.Id, _ownerId, Upload(PngBytes())));

        Assert.Equal(0, _processor.Calls);
    }

    [Fact]
    public async Task DeleteServicePhotoAsync_PhotoOfAnotherService_ReturnsFalse()
    {
        var service = SeedService();
        var other = SeedService();
        var otherPhoto = await _service.UploadServicePhotoAsync(other.Id, _ownerId, Upload(PngBytes()));

        Assert.False(await _service.DeleteServicePhotoAsync(service.Id, otherPhoto.Id, _ownerId));
        Assert.True(await _service.DeleteServicePhotoAsync(other.Id, otherPhoto.Id, _ownerId));
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task DeleteServicePhotoAsync_NotTheOwner_Throws()
    {
        var service = SeedService();
        var photo = await _service.UploadServicePhotoAsync(service.Id, _ownerId, Upload(PngBytes()));

        await Assert.ThrowsAsync<BusinessAccessDeniedException>(
            () => _service.DeleteServicePhotoAsync(service.Id, photo.Id, Guid.NewGuid()));

        Assert.Single(_storage.Files);
    }

    // ---------- Files ----------

    [Theory]
    [InlineData(PhotoFormat.Jpeg, "image/jpeg")]
    [InlineData(PhotoFormat.Png, "image/png")]
    [InlineData(PhotoFormat.Webp, "image/webp")]
    public async Task OpenPhotoFileAsync_StoredFile_ReturnsItWithTheContentTypeOfItsKey(PhotoFormat format, string contentType)
    {
        var key = await _storage.SaveAsync(new MemoryStream([1, 2, 3]), format);

        var file = await _service.OpenPhotoFileAsync(key);

        Assert.NotNull(file);
        Assert.Equal(contentType, file!.ContentType);
        await using var content = file.Content;
        Assert.Equal(3, content.Length);
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.html")]
    [InlineData("3f2b9c1e8d7a4b6c9e0f1a2b3c4d5e6f.jpg")] // well-formed, but no such file
    public async Task OpenPhotoFileAsync_InvalidOrUnknownName_ReturnsNull(string fileName)
    {
        Assert.Null(await _service.OpenPhotoFileAsync(fileName));
    }

    // ---------- Helpers ----------

    private BusinessEntity SeedBusiness(bool isActive = true) =>
        _businesses.Seed(new BusinessEntity { AccountId = _ownerId, Name = "Bella", IsActive = isActive });

    private ServiceEntity SeedService(bool serviceActive = true, bool businessActive = true) =>
        _photos.SeedService(new ServiceEntity
        {
            Name = "Corte",
            Price = 5000,
            DurationMinutes = 30,
            IsActive = serviceActive,
            Business = SeedBusiness(businessActive),
        });

    private static PhotoUpload Upload(byte[] content, int displayOrder = 0, bool isPrimary = false) =>
        new(new MemoryStream(content), displayOrder, isPrimary);

    private static byte[] PngBytes() => TestImages.Encode(SKEncodedImageFormat.Png, 20, 20);

    private void AssertNothingProcessedOrStored()
    {
        Assert.Equal(0, _processor.Calls);
        Assert.Empty(_storage.Files);
        Assert.Empty(_photos.BusinessPhotos);
        Assert.Empty(_photos.ServicePhotos);
    }
}
