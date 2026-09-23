using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;

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
    public async Task Register_WithBusinessRole_Returns201WithNullNames()
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "business@example.com",
            password = "SuperClave#2026",
            role = "BUSINESS",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BUSINESS", body.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("user").GetProperty("firstName").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("user").GetProperty("lastName").ValueKind);
    }

    [Fact]
    public async Task Register_ClientRoleWithoutNames_Returns400WithErrorShape()
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "no.names@example.com",
            password = "SuperClave#2026",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Null(factory.UserRepository.CreatedUser);
    }

    [Fact]
    public async Task Register_WithOldBusinessOwnerRoleLabel_Returns400WithErrorShape()
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "owner@example.com",
            password = "SuperClave#2026",
            role = "BUSINESS_OWNER",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Null(factory.UserRepository.CreatedUser);
    }

    [Fact]
    public async Task Register_WithSuperadminRole_Returns400WithErrorShape()
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "wannabe.admin@example.com",
            password = "SuperClave#2026",
            firstName = "María",
            lastName = "Rodríguez",
            role = "SUPERADMIN",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());

        // The invalid request must never have reached the repository — no account is created.
        Assert.Null(factory.UserRepository.CreatedUser);
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

    [Fact]
    public async Task Login_CorrectCredentials_Returns200WithAuthResponse()
    {
        factory.UserRepository.Reset();
        SeedLoginUser(out var email, out var password);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("user").GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("tokens").GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("tokens").GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithInvalidCredentialsBody()
    {
        factory.UserRepository.Reset();
        SeedLoginUser(out var email, out _);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password = "OtraClave#0000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_CREDENTIALS", body.GetProperty("code").GetString());
    }

    /// <summary>
    /// The security-critical check for this ticket, at the HTTP level: an unknown email must
    /// answer with the exact same status code and error code as a wrong password — never a
    /// different shape that would let a caller distinguish the two.
    /// </summary>
    [Fact]
    public async Task Login_UnknownEmail_Returns401WithTheSameBodyShapeAsWrongPassword()
    {
        factory.UserRepository.Reset();
        SeedLoginUser(out _, out _);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login", new { email = "nobody@example.com", password = "SomePassword#123" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_CREDENTIALS", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_MalformedBody_Returns400WithErrorShape()
    {
        factory.UserRepository.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email = "not-an-email", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    private void SeedLoginUser(out string email, out string password)
    {
        email = "login.user@example.com";
        password = "SuperClave#2026";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Login",
            LastName = "User",
            Role = UserRole.CLIENT,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        factory.UserRepository.Seed(user);
    }

    [Fact]
    public async Task ChangePassword_CorrectCurrentPassword_Returns204_AndRevokesAllSessions()
    {
        factory.RefreshTokenService.Reset();
        var user = SeedChangePasswordUser(out var currentPassword);
        var client = CreateAuthenticatedClient(user.Id);

        var response = await client.PostAsJsonAsync(
            "/auth/change-password", new { currentPassword, newPassword = "NewPassword#2026" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(user.Id, factory.RefreshTokenService.LastRevokeAllForUserId);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, "NewPassword#2026"));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns422WithInvalidCurrentPasswordBody()
    {
        factory.RefreshTokenService.Reset();
        var user = SeedChangePasswordUser(out _);
        var client = CreateAuthenticatedClient(user.Id);

        var response = await client.PostAsJsonAsync(
            "/auth/change-password", new { currentPassword = "WrongPassword#0000", newPassword = "NewPassword#2026" });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_CURRENT_PASSWORD", body.GetProperty("code").GetString());
        Assert.Null(factory.RefreshTokenService.LastRevokeAllForUserId);
    }

    [Fact]
    public async Task ChangePassword_NoAccessToken_Returns401_AndDoesNotCallAnything()
    {
        factory.RefreshTokenService.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/change-password", new { currentPassword = "Whatever#123", newPassword = "NewPassword#2026" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.RefreshTokenService.LastRevokeAllForUserId);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordTooShort_Returns400WithErrorShape()
    {
        factory.RefreshTokenService.Reset();
        var user = SeedChangePasswordUser(out var currentPassword);
        var client = CreateAuthenticatedClient(user.Id);

        var response = await client.PostAsJsonAsync(
            "/auth/change-password", new { currentPassword, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Null(factory.RefreshTokenService.LastRevokeAllForUserId);
    }

    private User SeedChangePasswordUser(out string password)
    {
        password = "OldPassword#2026";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "change.password.user@example.com",
            FirstName = "Change",
            LastName = "Password",
            Role = UserRole.CLIENT,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        factory.UserRepository.Seed(user);
        return user;
    }

    [Fact]
    public async Task Logout_ValidTokenAndBody_Returns204_AndRevokesForTheCallingUser()
    {
        factory.RefreshTokenService.Reset();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = "some-refresh-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(userId, factory.RefreshTokenService.LastCalledWithUserId);
        Assert.Equal("some-refresh-token", factory.RefreshTokenService.LastCalledWithRawToken);
    }

    [Fact]
    public async Task Logout_NoAccessToken_Returns401_AndDoesNotCallTheService()
    {
        factory.RefreshTokenService.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = "some-refresh-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(factory.RefreshTokenService.WasCalled);
    }

    [Fact]
    public async Task Logout_MissingRefreshToken_Returns400WithErrorBody()
    {
        factory.RefreshTokenService.Reset();
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/auth/logout", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.False(factory.RefreshTokenService.WasCalled);
    }

    [Fact]
    public async Task Logout_ExpiredAccessToken_Returns401_AndDoesNotCallTheService()
    {
        factory.RefreshTokenService.Reset();
        var client = factory.CreateClient();
        var expiredToken = factory.IssueAccessToken(
            Guid.NewGuid(), notBefore: DateTime.UtcNow.AddMinutes(-30), expires: DateTime.UtcNow.AddMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = "some-refresh-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(factory.RefreshTokenService.WasCalled);
    }

    [Fact]
    public async Task GetMe_ValidToken_Returns200WithTheCallersProfile()
    {
        factory.UserProfileService.Reset();
        var userId = Guid.NewGuid();
        factory.UserProfileService.ProfileToReturn = SampleProfile(userId);
        var client = CreateAuthenticatedClient(userId);

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, body.GetProperty("id").GetGuid());
        Assert.Equal(userId, factory.UserProfileService.LastRequestedUserId);
    }

    [Fact]
    public async Task GetMe_NoAccessToken_Returns401()
    {
        factory.UserProfileService.Reset();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.UserProfileService.LastRequestedUserId);
    }

    [Fact]
    public async Task GetMe_BusinessAccount_ReturnsNullNames()
    {
        factory.UserProfileService.Reset();
        var userId = Guid.NewGuid();
        factory.UserProfileService.ProfileToReturn = SampleProfile(userId) with { FirstName = null, LastName = null, Role = "BUSINESS" };
        var client = CreateAuthenticatedClient(userId);

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstName").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastName").ValueKind);
    }

    [Fact]
    public async Task GetMe_TokenNamesAUserThatNoLongerExists_Returns401()
    {
        factory.UserProfileService.Reset();
        factory.UserProfileService.ProfileToReturn = null;
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMe_ValidBody_Returns200_AndForwardsTheParsedRequest()
    {
        factory.UserProfileService.Reset();
        var userId = Guid.NewGuid();
        factory.UserProfileService.ProfileToReturn = SampleProfile(userId);
        var client = CreateAuthenticatedClient(userId);

        var response = await client.PatchAsJsonAsync("/auth/me", new { firstName = "Nuevo Nombre" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(userId, factory.UserProfileService.LastRequestedUserId);
        Assert.True(factory.UserProfileService.LastUpdateRequest!.FirstName.IsSet);
        Assert.Equal("Nuevo Nombre", factory.UserProfileService.LastUpdateRequest.FirstName.Value);
        Assert.False(factory.UserProfileService.LastUpdateRequest.Phone.IsSet);
    }

    [Fact]
    public async Task UpdateMe_ExplicitNullPhone_IsForwardedAsSetWithNullValue()
    {
        factory.UserProfileService.Reset();
        var userId = Guid.NewGuid();
        factory.UserProfileService.ProfileToReturn = SampleProfile(userId);
        var client = CreateAuthenticatedClient(userId);

        var response = await client.PatchAsJsonAsync("/auth/me", new { phone = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.UserProfileService.LastUpdateRequest!.Phone.IsSet);
        Assert.Null(factory.UserProfileService.LastUpdateRequest.Phone.Value);
    }

    [Fact]
    public async Task UpdateMe_NoAccessToken_Returns401_AndDoesNotCallTheService()
    {
        factory.UserProfileService.Reset();
        var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync("/auth/me", new { firstName = "Nuevo Nombre" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.UserProfileService.LastUpdateRequest);
    }

    [Fact]
    public async Task UpdateMe_EmptyFirstName_Returns400WithErrorShape_AndDoesNotCallTheService()
    {
        factory.UserProfileService.Reset();
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PatchAsJsonAsync("/auth/me", new { firstName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Null(factory.UserProfileService.LastUpdateRequest);
    }

    [Fact]
    public async Task UpdateMe_EmptyLastName_Returns400WithErrorShape_AndDoesNotCallTheService()
    {
        factory.UserProfileService.Reset();
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PatchAsJsonAsync("/auth/me", new { lastName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Null(factory.UserProfileService.LastUpdateRequest);
    }

    /// <summary>
    /// The role itself is loaded server-side by the real UserProfileService (see
    /// UserProfileServiceTests for that behavior against a real BUSINESS row) — this only proves
    /// AuthController correctly maps a thrown ProfileFieldNotAllowedException to a 400 ApiError
    /// with field-level details, using the fake to simulate what the service would throw.
    /// </summary>
    [Fact]
    public async Task UpdateMe_ServiceRejectsFieldForCallersRole_Returns400WithFieldDetails()
    {
        factory.UserProfileService.Reset();
        factory.UserProfileService.ExceptionToThrow = new ProfileFieldNotAllowedException(
            "FirstName", "business name is edited through PATCH /business/businesses/me");
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PatchAsJsonAsync("/auth/me", new { firstName = "Nombre" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
        Assert.Equal("business name is edited through PATCH /business/businesses/me", body.GetProperty("message").GetString());
        Assert.Equal("FirstName", body.GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task UpdateMe_TokenNamesAUserThatNoLongerExists_Returns401()
    {
        factory.UserProfileService.Reset();
        factory.UserProfileService.ProfileToReturn = null;
        var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PatchAsJsonAsync("/auth/me", new { firstName = "Nuevo Nombre" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static UserDto SampleProfile(Guid userId) => new(
        Id: userId,
        Email: "maria@example.com",
        FirstName: "María",
        LastName: "Rodríguez",
        Phone: "+506 8888-1234",
        ProfilePhotoUrl: null,
        Role: "CLIENT",
        IsActive: true,
        LinkedProviders: [],
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueAccessToken(userId));
        return client;
    }
}
