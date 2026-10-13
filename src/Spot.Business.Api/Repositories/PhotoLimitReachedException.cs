namespace Spot.Business.Api.Repositories;

/// <summary>
/// The business or service already has <see cref="MaxPhotos"/> photos, so a new one can't be
/// added. Mapped to 409 PHOTO_LIMIT_REACHED by the controllers.
/// </summary>
public sealed class PhotoLimitReachedException(int maxPhotos)
    : Exception($"The photo limit of {maxPhotos} has been reached.")
{
    public int MaxPhotos { get; } = maxPhotos;
}
