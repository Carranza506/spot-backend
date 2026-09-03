using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Spot.AiSearch.Api.Controllers;

[ApiController]
[Route("dev")]
public class DevController(IConfiguration config, IHostEnvironment env) : ControllerBase
{
    [HttpPost("token")]
    public IActionResult GenerateToken([FromQuery] string role = "SUPERADMIN")
    {
        if (!env.IsDevelopment())
            return NotFound();

        var signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey not configured.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role", role),
            new Claim("email", $"dev-{role.ToLower()}@spot.dev"),
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds
        );

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }
}
