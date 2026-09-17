using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;
using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Services;

public sealed class UserProfileServiceTests : IDisposable
{
    private readonly AuthDbContext _db;
    private readonly UserProfileService _service;

    public UserProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AuthDbContext(options);
        _service = new UserProfileService(new UserRepository(_db));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetProfileAsync_ExistingUser_ReturnsMappedDto()
    {
        var user = await SeedUserAsync(phone: "+506 8888-1234");

        var profile = await _service.GetProfileAsync(user.Id);

        Assert.NotNull(profile);
        Assert.Equal(user.Email, profile!.Email);
        Assert.Equal("+506 8888-1234", profile.Phone);
        Assert.Equal("CLIENT", profile.Role);
        Assert.Empty(profile.LinkedProviders);
    }

    [Fact]
    public async Task GetProfileAsync_UnknownUser_ReturnsNull()
    {
        var profile = await _service.GetProfileAsync(Guid.NewGuid());

        Assert.Null(profile);
    }

    [Fact]
    public async Task UpdateProfileAsync_OnlySetFieldsAreChanged()
    {
        var user = await SeedUserAsync(phone: "+506 8888-1234");
        var request = new UpdateProfileRequest { FirstName = Optional<string>.Of("Nuevo Nombre") };

        var profile = await _service.UpdateProfileAsync(user.Id, request);

        Assert.NotNull(profile);
        Assert.Equal("Nuevo Nombre", profile!.FirstName);
        // Untouched fields keep their original value.
        Assert.Equal(user.LastName, profile.LastName);
        Assert.Equal("+506 8888-1234", profile.Phone);
    }

    [Fact]
    public async Task UpdateProfileAsync_ExplicitNullClearsNullableField()
    {
        var user = await SeedUserAsync(phone: "+506 8888-1234");
        var request = new UpdateProfileRequest { Phone = Optional<string?>.Of(null) };

        var profile = await _service.UpdateProfileAsync(user.Id, request);

        Assert.NotNull(profile);
        Assert.Null(profile!.Phone);
    }

    [Fact]
    public async Task UpdateProfileAsync_UnsetFields_NeverTouchDatabase()
    {
        var user = await SeedUserAsync(phone: "+506 8888-1234");
        var request = new UpdateProfileRequest();

        var profile = await _service.UpdateProfileAsync(user.Id, request);

        Assert.NotNull(profile);
        Assert.Equal(user.FirstName, profile!.FirstName);
        Assert.Equal(user.LastName, profile.LastName);
        Assert.Equal("+506 8888-1234", profile.Phone);
    }

    [Fact]
    public async Task UpdateProfileAsync_UnknownUser_ReturnsNull()
    {
        var request = new UpdateProfileRequest { FirstName = Optional<string>.Of("Nombre") };

        var profile = await _service.UpdateProfileAsync(Guid.NewGuid(), request);

        Assert.Null(profile);
    }

    [Fact]
    public async Task UpdateProfileAsync_TrimsWhitespaceFromTextFields()
    {
        var user = await SeedUserAsync();
        var request = new UpdateProfileRequest
        {
            FirstName = Optional<string>.Of("  María José  "),
            Phone = Optional<string?>.Of("  +506 8888-1234  "),
        };

        var profile = await _service.UpdateProfileAsync(user.Id, request);

        Assert.Equal("María José", profile!.FirstName);
        Assert.Equal("+506 8888-1234", profile.Phone);
    }

    private async Task<User> SeedUserAsync(string? phone = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "maria@example.com",
            FirstName = "María",
            LastName = "Rodríguez",
            Phone = phone,
            Role = UserRole.CLIENT,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return user;
    }
}
