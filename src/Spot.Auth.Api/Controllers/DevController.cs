using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Configuration;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Auth.Api.Controllers;

/// <summary>
/// Issues real RS256 test tokens while there is no login flow yet. Only reachable in
/// Development — returns 404 everywhere else, same as production would see a missing route.
/// </summary>
[ApiController]
[Route("dev")]
public class DevController(
    ITokenService tokenService,
    IHostEnvironment env,
    AuthDbContext db,
    IRefreshTokenHasher refreshTokenHasher,
    IOptions<RefreshTokenOptions> refreshTokenOptions) : ControllerBase
{
    [HttpPost("token")]
    public async Task<IActionResult> IssueToken([FromQuery] string role = nameof(UserRole.SUPERADMIN), CancellationToken ct = default)
    {
        if (!env.IsDevelopment())
            return NotFound();

        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
            return BadRequest(new ApiError(
                "BAD_REQUEST",
                $"role debe ser uno de: {string.Join(", ", Enum.GetNames<UserRole>())}."));

        // Also persists a matching row in `users` (Development-only, same as this whole
        // controller): with no /auth/register endpoint on this branch yet, a bare token isn't
        // enough to exercise endpoints like GET/PATCH /auth/me that load the caller's own user
        // record — they'd 401 with "valid token, no such user" otherwise.
        var user = new User
        {
            Email = $"dev-{Guid.NewGuid():N}@spot.cr",
            FirstName = "Dev",
            LastName = "User",
            Role = parsedRole,
        };
        db.Users.Add(user);

        // Must be saved before the refresh token below is created: User.Id is store-generated
        // (gen_random_uuid(), not set client-side), so it's still Guid.Empty until this
        // SaveChangesAsync round-trips and reads it back — the refresh_tokens FK would otherwise
        // point at a user row that doesn't exist yet.
        await db.SaveChangesAsync(ct);

        // Also persists a matching refresh_tokens row: with no /auth/login endpoint yet either,
        // this is the only way to get a real, active refresh token to exercise POST /auth/refresh
        // against (see Spot.Auth.Api.http).
        var rawRefreshToken = RefreshTokenGenerator.GenerateRaw();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshTokenHasher.Hash(rawRefreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(refreshTokenOptions.Value.ExpiresInDays),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        var accessToken = tokenService.IssueAccessToken(user.Id.ToString(), parsedRole.ToString());

        return Ok(new AuthTokensDto(accessToken.Value, rawRefreshToken, "Bearer", accessToken.ExpiresInSeconds));
    }
}
