using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Auth.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Auth" tag. <c>POST /auth/register</c>, <c>POST /auth/logout</c>
/// (#51) and <c>GET</c>/<c>PATCH /auth/me</c> (#52) are implemented so far — login/refresh/
/// change-password are separate, not-yet-implemented issues under the same parent (#42).
/// Register must stay reachable without a token, so authorization is applied per-action below
/// instead of at the class level.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthController(
    IAuthService authService,
    IRefreshTokenService refreshTokenService,
    IUserProfileService userProfileService) : ControllerBase
{
    /// <summary>POST /auth/register: public — no access token required (contract: security: []).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await authService.RegisterAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (DuplicateEmailException)
        {
            return Conflict(new ApiError("EMAIL_ALREADY_REGISTERED", "Ese correo ya está registrado."));
        }
    }

    /// <summary>
    /// POST /auth/logout: revokes the given refresh token. Requires a valid access token (any
    /// authenticated role) — enforced by <see cref="AuthorizeAttribute"/> on this action only,
    /// unlike Register above which must stay public.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        // Revokes the token if it's an active one owned by this user; silently no-ops
        // otherwise (already revoked, expired, unknown, or someone else's) — see
        // IRefreshTokenService.RevokeAsync for why that's the correct, non-leaky behavior.
        // Either way the caller's session (the access token they hold) has ended: 204.
        await refreshTokenService.RevokeAsync(userId, request.RefreshToken, ct);

        return NoContent();
    }

    /// <summary>GET /auth/me: the authenticated user's own profile.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        var profile = await userProfileService.GetProfileAsync(userId, ct);

        // The access token is valid, but the user it names no longer exists (e.g. deleted after
        // the token was issued) — there is no dedicated contract response for that, so this
        // treats it the same as "not a valid session" rather than inventing a new error shape.
        if (profile is null)
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        return Ok(profile);
    }

    /// <summary>
    /// PATCH /auth/me: updates editable profile fields (firstName, lastName, phone,
    /// profilePhotoUrl) for the authenticated user. Never changes email or role — those aren't
    /// even present on <see cref="UpdateProfileRequest"/>.
    /// </summary>
    [HttpPatch("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        var profile = await userProfileService.UpdateProfileAsync(userId, request, ct);
        if (profile is null)
            return Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

        return Ok(profile);
    }

    /// <summary>
    /// The access token itself (signature, issuer, audience, lifetime) is already validated by
    /// <see cref="AuthorizeAttribute"/> before any action here runs. AddSpotJwtAuthentication
    /// configures NameClaimType = "sub", so User.Identity.Name IS the caller's user id — this
    /// only guards against that claim somehow not being a Guid, which a token actually issued
    /// by JwtTokenService never produces.
    /// </summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
