using Microsoft.AspNetCore.Mvc;
using Spot.Auth.Api.Data;
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
public class DevController(ITokenService tokenService, IHostEnvironment env, AuthDbContext db) : ControllerBase
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
        await db.SaveChangesAsync(ct);

        var token = tokenService.IssueAccessToken(user.Id.ToString(), parsedRole.ToString());

        return Ok(token);
    }
}
