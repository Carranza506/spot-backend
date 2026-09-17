namespace Spot.Auth.Api.Repositories;

/// <summary>Raised when a registration attempt targets an email that already has an account.</summary>
public sealed class DuplicateEmailException(string email)
    : Exception($"A user with email '{email}' already exists.")
{
    public string Email { get; } = email;
}
