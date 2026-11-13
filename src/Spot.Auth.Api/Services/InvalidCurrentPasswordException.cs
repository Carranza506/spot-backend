namespace Spot.Auth.Api.Services;

/// <summary>Raised when a change-password attempt's currentPassword doesn't match the stored hash.</summary>
public sealed class InvalidCurrentPasswordException() : Exception("The current password is incorrect.");
