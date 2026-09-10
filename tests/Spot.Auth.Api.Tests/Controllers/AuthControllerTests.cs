using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Tests.Controllers;

public class AuthControllerTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Register_WithValidBody_Returns201WithAuthResponse()
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "new.user@example.com",
            password = "SuperClave#2026",
            firstName = "María",
            lastName = "Rodríguez",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("new.user@example.com", body.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("CLIENT", body.GetProperty("user").GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("tokens").GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("tokens").GetProperty("refreshToken").GetString()));
        Assert.Equal("Bearer", body.GetProperty("tokens").GetProperty("tokenType").GetString());
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409WithConflictBody()
    {
        factory.UserRepository.Reset();
        factory.UserRepository.ExceptionToThrow = new DuplicateEmailException("taken@example.com");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "taken@example.com",
            password = "SuperClave#2026",
            firstName = "María",
            lastName = "Rodríguez",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EMAIL_ALREADY_REGISTERED", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
    }

    [Theory]
    [InlineData("not-an-email", "SuperClave#2026", "María", "Rodríguez")] // invalid email
    [InlineData("valid@example.com", "short", "María", "Rodríguez")] // password too short
    [InlineData("valid@example.com", "SuperClave#2026", "", "Rodríguez")] // missing first name
    public async Task Register_WithInvalidBody_Returns400WithErrorShape(
        string email, string password, string firstName, string lastName)
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new { email, password, firstName, lastName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));

        // The invalid request must never have reached the repository.
        Assert.Null(factory.UserRepository.CreatedUser);
    }
}
