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

    [Fact]
    public async Task LoginWithGoogleAsync_InvalidToken_ReturnsNull()
    {
        var repo = new FakeUserRepository();
        var service = CreateService(repo, out _); // IGoogleIdTokenValidator defaults to returning null

        var response = await service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = "not-a-real-token" });

        Assert.Null(response);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_NoPriorAccount_CreatesNewUserAndLinksGoogle()
    {
        var repo = new FakeUserRepository();
        var identity = new GoogleIdentity("google-sub-1", "new.googler@example.com", "María", "José", "https://example.com/photo.jpg");
        var service = CreateService(repo, out var tokenService, googleIdentity: identity);

        var response = await service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = "a-valid-token" });

        Assert.NotNull(repo.CreatedUser);
        Assert.Equal("new.googler@example.com", repo.CreatedUser!.Email);
        Assert.Null(repo.CreatedUser.PasswordHash);
        Assert.Equal(UserRole.CLIENT, repo.CreatedUser.Role);
        Assert.Equal("María", repo.CreatedUser.FirstName);
        Assert.Equal("https://example.com/photo.jpg", repo.CreatedUser.ProfilePhotoUrl);
        Assert.Single(repo.CreatedUser.AuthProviders);
        Assert.Equal(AuthProvider.GOOGLE, repo.CreatedUser.AuthProviders.Single().Provider);
        Assert.Equal("google-sub-1", repo.CreatedUser.AuthProviders.Single().ProviderUserId);
        Assert.Equal(repo.CreatedUser.Id.ToString(), tokenService.LastUserId);
        Assert.Single(response!.User.LinkedProviders);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_EmailMatchesExistingPasswordAccount_LinksInsteadOfDuplicating()
    {
        var repo = new FakeUserRepository();
        var existingUser = new User
        {
            Email = "maria@example.com",
            FirstName = "María",
            LastName = "Rodríguez",
            PasswordHash = "some-password-hash",
            Role = UserRole.CLIENT,
        };
        repo.Seed(existingUser);
        var identity = new GoogleIdentity("google-sub-2", "maria@example.com", "María", "Rodríguez", null);
        var service = CreateService(repo, out _, googleIdentity: identity);

        var response = await service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = "a-valid-token" });

        // No new user created — the existing password account was reused and linked.
        Assert.Null(repo.CreatedUser);
        Assert.Equal(existingUser.Id, response!.User.Id);
        Assert.Single(existingUser.AuthProviders);
        Assert.Equal("google-sub-2", existingUser.AuthProviders.Single().ProviderUserId);
        // The password stays usable — linking Google must not clear it.
        Assert.Equal("some-password-hash", existingUser.PasswordHash);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_AlreadyLinkedAccount_ReturnsExistingUserWithoutDuplicateLink()
    {
        var repo = new FakeUserRepository();
        var existingUser = new User { Email = "returning@example.com", FirstName = "Ana", LastName = "Solís", Role = UserRole.CLIENT };
        existingUser.AuthProviders.Add(new UserAuthProvider { Provider = AuthProvider.GOOGLE, ProviderUserId = "google-sub-3" });
        repo.Seed(existingUser);
        var identity = new GoogleIdentity("google-sub-3", "returning@example.com", "Ana", "Solís", null);
        var service = CreateService(repo, out _, googleIdentity: identity);

        var response = await service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = "a-valid-token" });

        Assert.Null(repo.CreatedUser);
        Assert.Equal(existingUser.Id, response!.User.Id);
        Assert.Single(existingUser.AuthProviders); // still exactly one link, not two
        Assert.False(string.IsNullOrWhiteSpace(response.Tokens.RefreshToken));
    }

    private static AuthService CreateService(
        FakeUserRepository repo,
        out FakeTokenService tokenService,
        IPasswordHasher<User>? hasher = null,
        GoogleIdentity? googleIdentity = null)
    {
        tokenService = new FakeTokenService();
        var refreshTokenIssuer = new RefreshTokenIssuer(Options.Create(new RefreshTokenOptions()), new Sha256RefreshTokenHasher());
        var googleIdTokenValidator = new FakeGoogleIdTokenValidator { IdentityToReturn = googleIdentity };
        return new AuthService(repo, hasher ?? new PasswordHasher<User>(), tokenService, refreshTokenIssuer, googleIdTokenValidator);
    }
}
