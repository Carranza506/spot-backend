using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Configuration;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;
using Spot.Auth.Api.Tests.Fakes;

namespace Spot.Auth.Api.Tests.Services;

public class AuthServiceTests
{
    private static RegisterRequest ValidRequest() => new()
    {
        Email = "  Maria.Rodriguez@Example.com  ",
        Password = "SuperClave#2026",
        FirstName = "  María José  ",
        LastName = "  Rodríguez Solís  ",
        Phone = "+506 8888-1234",
    };

    [Fact]
    public async Task RegisterAsync_NormalizesEmail_TrimmedAndLowerCased()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);

        await service.RegisterAsync(ValidRequest());

        Assert.Equal("maria.rodriguez@example.com", repo.CreatedUser!.Email);
    }

    [Fact]
    public async Task RegisterAsync_HashesPassword_NeverStoresPlainText()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var service = CreateService(repo, out _, hasher);
        var request = ValidRequest();

        await service.RegisterAsync(request);

        var storedHash = repo.CreatedUser!.PasswordHash;
        Assert.False(string.IsNullOrWhiteSpace(storedHash));
        Assert.NotEqual(request.Password, storedHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(repo.CreatedUser, storedHash!, request.Password));
    }

    [Fact]
    public async Task RegisterAsync_DefaultsRoleToClient()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);

        await service.RegisterAsync(ValidRequest());

        Assert.Equal(UserRole.CLIENT, repo.CreatedUser!.Role);
    }

    [Fact]
    public async Task RegisterAsync_WithBusinessRole_UsesTheRequestedRole()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);
        var request = ValidRequest();
        request.Role = UserRole.BUSINESS;

        await service.RegisterAsync(request);

        Assert.Equal(UserRole.BUSINESS, repo.CreatedUser!.Role);
    }

    [Fact]
    public async Task RegisterAsync_WithBusinessRole_NeverStoresAPersonName_EvenIfTheRequestSentOne()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);
        var request = ValidRequest();
        request.Role = UserRole.BUSINESS;

        await service.RegisterAsync(request);

        Assert.Null(repo.CreatedUser!.FirstName);
        Assert.Null(repo.CreatedUser.LastName);
    }

    [Fact]
    public async Task RegisterAsync_IssuesAccessTokenForTheNewUserWithClientRole()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out var tokenService);

        await service.RegisterAsync(ValidRequest());

        Assert.Equal(repo.CreatedUser!.Id.ToString(), tokenService.LastUserId);
        Assert.Equal("CLIENT", tokenService.LastRole);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsAuthResponseWithAccessAndRefreshTokens()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);

        var response = await service.RegisterAsync(ValidRequest());

        Assert.Equal("fake-access-token", response.Tokens.AccessToken);
        Assert.Equal(3600, response.Tokens.ExpiresIn);
        Assert.Equal("Bearer", response.Tokens.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(response.Tokens.RefreshToken));
        Assert.Empty(response.User.LinkedProviders);
    }

    [Fact]
    public async Task RegisterAsync_PropagatesDuplicateEmailException()
    {
        var repo = new FakeUserRepository { ExceptionToThrow = new DuplicateEmailException("taken@example.com") };
        var service = CreateService(repo, out _);

        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.RegisterAsync(ValidRequest()));
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_ReturnsAuthResponseWithTokens()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var service = CreateService(repo, out var tokenService, hasher);
        var user = SeedUser(repo, hasher, "SuperClave#2026");

        var response = await service.LoginAsync(new LoginRequest { Email = user.Email, Password = "SuperClave#2026" });

        Assert.NotNull(response);
        Assert.Equal(user.Id, response.User.Id);
        Assert.Equal("fake-access-token", response.Tokens.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(response.Tokens.RefreshToken));
        Assert.Equal(user.Id.ToString(), tokenService.LastUserId);
        Assert.NotNull(repo.LastAddedRefreshToken);
    }

    [Fact]
    public async Task LoginAsync_NormalizesEmail_TrimmedAndCaseInsensitive()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var service = CreateService(repo, out _, hasher);
        var user = SeedUser(repo, hasher, "SuperClave#2026", email: "maria.rodriguez@example.com");

        var response = await service.LoginAsync(
            new LoginRequest { Email = "  Maria.Rodriguez@Example.com  ", Password = "SuperClave#2026" });

        Assert.NotNull(response);
        Assert.Equal(user.Id, response.User.Id);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var service = CreateService(repo, out _, hasher);
        var user = SeedUser(repo, hasher, "SuperClave#2026");

        var response = await service.LoginAsync(new LoginRequest { Email = user.Email, Password = "OtraClave#0000" });

        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsNull()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);

        var response = await service.LoginAsync(
            new LoginRequest { Email = "nobody@example.com", Password = "SuperClave#2026" });

        Assert.Null(response);
    }

    /// <summary>
    /// The security-critical assertion for this ticket: an unknown email and a wrong password
    /// must be indistinguishable to the caller — both null, nothing else observable differs.
    /// </summary>
    [Fact]
    public async Task LoginAsync_UnknownEmailAndWrongPassword_ReturnTheSameResult()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var service = CreateService(repo, out _, hasher);
        var user = SeedUser(repo, hasher, "SuperClave#2026");

        var unknownEmailResult = await service.LoginAsync(
            new LoginRequest { Email = "nobody@example.com", Password = "SuperClave#2026" });
        var wrongPasswordResult = await service.LoginAsync(
            new LoginRequest { Email = user.Email, Password = "OtraClave#0000" });

        Assert.Null(unknownEmailResult);
        Assert.Null(wrongPasswordResult);
    }

    [Fact]
    public async Task LoginAsync_UserWithNoPasswordSet_ReturnsNull_DoesNotThrow()
    {
        var repo = new FakeUserRepository();
        // Simulates a Google-only account: never went through RegisterAsync, so PasswordHash is null.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "google.only@example.com",
            FirstName = "Google",
            LastName = "User",
            Role = UserRole.CLIENT,
        };
        repo.Seed(user);
        var service = CreateService(repo, out _);

        var response = await service.LoginAsync(
            new LoginRequest { Email = user.Email, Password = "AnyPassword#123" });

        Assert.Null(response);
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_SavesTheNewHash_AndRevokesAllSessions()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var refreshTokenService = new FakeRefreshTokenService();
        var user = SeedUser(repo, hasher, "OldPassword#2026");
        var service = CreateService(repo, out _, hasher, refreshTokenService);

        var result = await service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest { CurrentPassword = "OldPassword#2026", NewPassword = "NewPassword#2026" });

        Assert.True(result);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, user.PasswordHash!, "NewPassword#2026"));
        Assert.Equal(user.Id, refreshTokenService.LastRevokeAllForUserId);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsAndChangesNothing()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var refreshTokenService = new FakeRefreshTokenService();
        var user = SeedUser(repo, hasher, "OldPassword#2026");
        var originalHash = user.PasswordHash;
        var service = CreateService(repo, out _, hasher, refreshTokenService);

        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(() => service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest { CurrentPassword = "WrongPassword#0000", NewPassword = "NewPassword#2026" }));

        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Null(refreshTokenService.LastRevokeAllForUserId);
    }

    [Fact]
    public async Task ChangePasswordAsync_UserWithNoPasswordSet_Throws_DoesNotCrash()
    {
        var repo = new FakeUserRepository();
        var user = new User { Id = Guid.NewGuid(), Email = "google.only@example.com", Role = UserRole.CLIENT };
        repo.Seed(user);
        var service = CreateService(repo, out _);

        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(() => service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest { CurrentPassword = "AnyPassword#123", NewPassword = "NewPassword#2026" }));
    }

    [Fact]
    public async Task ChangePasswordAsync_UnknownUserId_ReturnsFalse()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _);

        var result = await service.ChangePasswordAsync(
            Guid.NewGuid(), new ChangePasswordRequest { CurrentPassword = "Whatever#123", NewPassword = "NewPassword#2026" });

        Assert.False(result);
    }

    private static User SeedUser(
        FakeUserRepository repo, IPasswordHasher<User> hasher, string password, string email = "maria@example.com")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "María",
            LastName = "Rodríguez",
            Role = UserRole.CLIENT,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        repo.Seed(user);
        return user;
    }

    private static AuthService CreateService(
        FakeUserRepository repo,
        out FakeTokenService tokenService,
        IPasswordHasher<User>? hasher = null,
        IRefreshTokenService? refreshTokenService = null)
    {
        tokenService = new FakeTokenService();
        var refreshTokenIssuer = new RefreshTokenIssuer(Options.Create(new RefreshTokenOptions()), new Sha256RefreshTokenHasher());
        return new AuthService(
            repo,
            hasher ?? new PasswordHasher<User>(),
            tokenService,
            refreshTokenIssuer,
            refreshTokenService ?? new FakeRefreshTokenService());
    }
}
