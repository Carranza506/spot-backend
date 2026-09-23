namespace Spot.Auth.Api.Services;

/// <summary>
/// Thrown by <see cref="IUserProfileService.UpdateProfileAsync"/> when the caller's own role
/// (loaded from the database, never trusted from the request) forbids a field it sent — e.g. a
/// BUSINESS account sending firstName/lastName, which is edited via PATCH /business/businesses/me
/// instead. AuthController catches this and maps it to a 400 ApiError with field-level details;
/// it must never reach the client as a raw, unhandled exception.
/// </summary>
public sealed class ProfileFieldNotAllowedException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
