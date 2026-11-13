using System.Text.Json;
using Spot.Auth.Api.DTOs;

namespace Spot.Auth.Api.Tests.DTOs;

public class OptionalJsonConverterTests
{
    // PropertyNameCaseInsensitive: true matches ASP.NET Core's actual [FromBody] defaults —
    // without it, System.Text.Json's own default (case-sensitive) would fail to match "name" in
    // the JSON below to the "Name" property, which is a mismatch these tests should not hide.
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private sealed class Payload
    {
        public Optional<string> Name { get; set; }
        public Optional<string?> Nickname { get; set; }
    }

    [Fact]
    public void Deserialize_FieldAbsent_LeavesItUnset()
    {
        var payload = JsonSerializer.Deserialize<Payload>("{}", Options)!;

        Assert.False(payload.Name.IsSet);
        Assert.False(payload.Nickname.IsSet);
    }

    [Fact]
    public void Deserialize_FieldPresentWithValue_IsSetWithThatValue()
    {
        var payload = JsonSerializer.Deserialize<Payload>("""{"name":"María"}""", Options)!;

        Assert.True(payload.Name.IsSet);
        Assert.Equal("María", payload.Name.Value);
        Assert.False(payload.Nickname.IsSet);
    }

    [Fact]
    public void Deserialize_FieldPresentAsNull_IsSetWithNullValue()
    {
        // The whole reason Optional<T> exists: absent and explicit-null must NOT look the same.
        var payload = JsonSerializer.Deserialize<Payload>("""{"nickname":null}""", Options)!;

        Assert.True(payload.Nickname.IsSet);
        Assert.Null(payload.Nickname.Value);
        Assert.False(payload.Name.IsSet);
    }

    [Fact]
    public void Of_ProducesAnIsSetInstanceWithTheGivenValue()
    {
        var optional = Optional<int>.Of(42);

        Assert.True(optional.IsSet);
        Assert.Equal(42, optional.Value);
    }

    [Fact]
    public void Unset_IsNotSet()
    {
        Assert.False(Optional<int>.Unset.IsSet);
    }
}
