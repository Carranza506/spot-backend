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

    private static AuthService CreateService(
        FakeUserRepository repo, out FakeTokenService tokenService, IPasswordHasher<User>? hasher = null)
    {
        tokenService = new FakeTokenService();
        var refreshTokenService = new RefreshTokenService(Options.Create(new RefreshTokenOptions()));
        return new AuthService(repo, hasher ?? new PasswordHasher<User>(), tokenService, refreshTokenService);
    }
}
