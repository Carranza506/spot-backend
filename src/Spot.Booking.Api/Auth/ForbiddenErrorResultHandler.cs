using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Spot.Shared.Errors;

namespace Spot.Booking.Api.Auth;

/// <summary>
/// Answers an authenticated caller without the required role (e.g. a BUSINESS token on a
/// CLIENT-only endpoint) with the contract's Error shape instead of ASP.NET Core's empty 403.
/// Spot.Shared's AddSpotJwtAuthentication only reshapes 401 (OnChallenge); this is kept local to
/// Booking so other services' behavior doesn't change. Everything else goes to the default handler.
/// </summary>
public sealed class ForbiddenErrorResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new ApiError("FORBIDDEN", "No tienes permiso para realizar esta acción."));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
