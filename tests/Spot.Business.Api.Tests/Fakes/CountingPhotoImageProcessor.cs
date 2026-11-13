using Spot.Business.Api.Options;
using Spot.Business.Api.Photos;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>
/// The real SkiaPhotoImageProcessor, counting its calls — so tests can assert that a rejected
/// upload (not the owner, limit reached) never got as far as decoding the image.
/// </summary>
public sealed class CountingPhotoImageProcessor : IPhotoImageProcessor, IDisposable
{
    private readonly SkiaPhotoImageProcessor _inner =
        new(Microsoft.Extensions.Options.Options.Create(new PhotoProcessingOptions()));

    public int Calls { get; private set; }

    public void Reset() => Calls = 0;

    public Task<ProcessedPhoto> ProcessAsync(Stream upload, CancellationToken ct = default)
    {
        Calls++;
        return _inner.ProcessAsync(upload, ct);
    }

    public void Dispose() => _inner.Dispose();
}
