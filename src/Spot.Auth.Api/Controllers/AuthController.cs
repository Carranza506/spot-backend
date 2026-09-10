using Microsoft.AspNetCore.Mvc;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;
using Spot.Shared.Errors;

namespace Spot.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
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
}
