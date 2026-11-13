using Microsoft.AspNetCore.Mvc;
using Spot.Shared.Errors;

namespace Spot.Auth.Api.Configuration;

public static class ApiBehaviorExtensions
{
    /// <summary>
    /// Same convention as Spot.AiSearch.Api: without this, [ApiController]'s automatic model
    /// validation (e.g. a missing/empty RefreshRequest.RefreshToken) would answer with
    /// ASP.NET Core's default ValidationProblemDetails instead of the
    /// { code, message, timestamp } Error shape defined in contracts/spot-api.yaml.
    /// </summary>
    /// <remarks>
    /// A method (not inlined at each call site) so Program.cs and any test host that builds its
    /// own minimal pipeline (see AuthControllerTests) configure the exact same behavior instead
    /// of two copies that can silently drift apart.
    /// </remarks>
    public static IMvcBuilder ConfigureSpotApiErrorShape(this IMvcBuilder builder) =>
        builder.ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var invalidField = context.ModelState
                    .FirstOrDefault(entry => entry.Value?.Errors.Count > 0).Key;

                var error = new ApiError(
                    "BAD_REQUEST",
                    "Uno o más parámetros de la petición no son válidos.",
                    string.IsNullOrEmpty(invalidField) ? null : new { field = invalidField });

                return new BadRequestObjectResult(error);
            };
        });
}
