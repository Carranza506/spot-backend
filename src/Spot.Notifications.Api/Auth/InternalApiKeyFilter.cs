using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Spot.Notifications.Api.Options;
using Spot.Shared.Errors;

namespace Spot.Notifications.Api.Auth;

/// <summary>
/// Requires the shared internal key (the contract's internalApiKey scheme) instead of a user JWT.
/// An authorization filter so it runs before model validation: a caller without the key gets a
/// 401, never a 400 describing the body.
/// </summary>
public sealed class InternalApiKeyFilter(IOptions<InternalApiOptions> options) : IAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = options.Value.Key;
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(expected) || !KeysMatch(expected, provided))
            context.Result = new UnauthorizedObjectResult(new ApiError("UNAUTHORIZED", "API key interna inválida o ausente."));
    }

    /// <summary>
    /// Compares hashes because FixedTimeEquals returns early on different lengths, which would leak
    /// the key's length.
    /// </summary>
    private static bool KeysMatch(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
}
