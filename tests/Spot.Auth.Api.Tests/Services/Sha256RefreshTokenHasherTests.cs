using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Services;

public class Sha256RefreshTokenHasherTests
{
    private readonly Sha256RefreshTokenHasher _hasher = new();

    [Fact]
    public void Hash_SameInput_ReturnsSameHash()
    {
        const string rawToken = "a-high-entropy-refresh-token-value";

        Assert.Equal(_hasher.Hash(rawToken), _hasher.Hash(rawToken));
    }

    [Fact]
    public void Hash_DifferentInputs_ReturnDifferentHashes()
    {
        var first = _hasher.Hash("token-one");
        var second = _hasher.Hash("token-two");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_NeverReturnsTheRawToken()
    {
        const string rawToken = "a-high-entropy-refresh-token-value";

        Assert.NotEqual(rawToken, _hasher.Hash(rawToken));
    }

    [Fact]
    public void Hash_ReturnsLowercaseHex()
    {
        var hash = _hasher.Hash("a-high-entropy-refresh-token-value");

        // SHA-256 = 32 bytes = 64 hex chars.
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_NullOrWhiteSpaceInput_Throws(string? rawToken)
    {
        // ThrowIfNullOrWhiteSpace throws ArgumentNullException specifically for null, and
        // ArgumentException for "" / whitespace — ThrowsAny covers both, since callers only
        // care that an invalid input is rejected, not which subtype.
        Assert.ThrowsAny<ArgumentException>(() => _hasher.Hash(rawToken!));
    }
}
