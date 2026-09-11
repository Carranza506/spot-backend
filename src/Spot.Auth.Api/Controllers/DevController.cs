using Microsoft.AspNetCore.Mvc;
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
public class DevController(ITokenService tokenService, IHostEnvironment env) : ControllerBase
{
    [HttpPost("token")]
    public IActionResult IssueToken([FromQuery] string role = nameof(UserRole.SUPERADMIN))
    {
        if (!env.IsDevelopment())
            return NotFound();

        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
            return BadRequest(new ApiError(
                "BAD_REQUEST",
                $"role debe ser uno de: {string.Join(", ", Enum.GetNames<UserRole>())}."));

        var userId = Guid.NewGuid().ToString();
        var token = tokenService.IssueAccessToken(userId, parsedRole.ToString());

        return Ok(token);
    }
}
