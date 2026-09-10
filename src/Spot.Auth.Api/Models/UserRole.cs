namespace Spot.Auth.Api.Models;

/// <summary>Matches the Postgres enum <c>user_role</c> and the <c>UserRole</c> contract schema.</summary>
public enum UserRole
{
    CLIENT,
    BUSINESS_OWNER,
    SUPERADMIN,
}
