using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Services;
using Spot.Shared.Auth;

namespace Spot.Auth.Api.Tests.Services;

public class JwtTokenServiceTests
{
    private const string UserId = "3fa85f64-5717-4562-b3fc-2c963f66afa6";
    private const string Role = "CLIENT";

    // Generated once for this test run only; never used outside the test process.
    private static readonly string PrivateKeyPem = GenerateRsaPrivateKeyPem();

    private static JwtOptions ValidOptions() => new()
    {
        Issuer = "https://api.spot.cr",
        Audience = "spot-clients",
        ExpiresInMinutes = 60,
        PrivateKeyPem = PrivateKeyPem,
    };

    private static JwtTokenService CreateService(JwtOptions options) =>
        new(Options.Create(options));

    [Fact]
    public void IssueAccessToken_ReturnsTokenWithSubAndRoleClaims()
    {
        var service = CreateService(ValidOptions());

        var result = service.IssueAccessToken(UserId, Role);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);
        Assert.Equal(UserId, jwt.Subject);
        Assert.Equal(Role, jwt.Claims.Single(c => c.Type == JwtTokenService.RoleClaimType).Value);
    }

    [Fact]
    public void IssueAccessToken_SetsIssuerAndAudienceClaims()
    {
        var options = ValidOptions();
        var service = CreateService(options);

        var result = service.IssueAccessToken(UserId, Role);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);
        Assert.Equal(options.Issuer, jwt.Issuer);
        Assert.Equal(options.Audience, jwt.Audiences.Single());
    }

    [Fact]
    public void IssueAccessToken_SetsExpirationAccordingToConfig()
    {
        var options = ValidOptions();
        var service = CreateService(options);

        var beforeIssue = DateTime.UtcNow;
        var result = service.IssueAccessToken(UserId, Role);
        var afterIssue = DateTime.UtcNow;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);

        Assert.Equal(options.ExpiresInMinutes * 60, result.ExpiresInSeconds);
        Assert.True(jwt.ValidTo >= beforeIssue.AddMinutes(options.ExpiresInMinutes).AddSeconds(-5));
        Assert.True(jwt.ValidTo <= afterIssue.AddMinutes(options.ExpiresInMinutes).AddSeconds(5));
        Assert.True(jwt.IssuedAt >= beforeIssue.AddSeconds(-5) && jwt.IssuedAt <= afterIssue.AddSeconds(5));
    }

    [Fact]
    public void IssueAccessToken_IsSignedWithRs256()
    {
        var service = CreateService(ValidOptions());

        var result = service.IssueAccessToken(UserId, Role);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);
        Assert.Equal("RS256", jwt.Header.Alg);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsWhenPrivateKeyIsMissing(string? privateKey)
    {
        var options = ValidOptions();
        options.PrivateKeyPem = privateKey;

        var ex = Assert.Throws<InvalidOperationException>(() => CreateService(options));
        Assert.Contains(nameof(JwtOptions.PrivateKeyPem), ex.Message);
    }

    [Fact]
    public void Constructor_ThrowsWhenIssuerIsMissing()
    {
        var options = ValidOptions();
        options.Issuer = "";

        var ex = Assert.Throws<InvalidOperationException>(() => CreateService(options));
        Assert.Contains(nameof(JwtOptions.Issuer), ex.Message);
    }

    [Fact]
    public void Constructor_ThrowsWhenExpiresInMinutesIsNotPositive()
    {
        var options = ValidOptions();
        options.ExpiresInMinutes = 0;

        var ex = Assert.Throws<InvalidOperationException>(() => CreateService(options));
        Assert.Contains(nameof(JwtOptions.ExpiresInMinutes), ex.Message);
    }

    [Fact]
    public void Constructor_ThrowsWhenPrivateKeyIsMalformed()
    {
        var options = ValidOptions();
        options.PrivateKeyPem = "this is not a PEM-encoded key at all";

        Assert.Throws<InvalidOperationException>(() => CreateService(options));
    }

    private static string GenerateRsaPrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }
}
