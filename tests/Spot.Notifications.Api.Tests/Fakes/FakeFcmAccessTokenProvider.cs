using Spot.Notifications.Api.Services.Push;

namespace Spot.Notifications.Api.Tests.Fakes;

/// <summary>Hands out a fixed access token (or throws <see cref="FailWith"/>) instead of calling Google's OAuth endpoint.</summary>
public sealed class FakeFcmAccessTokenProvider : IFcmAccessTokenProvider
{
    public const string AccessToken = "ya29.fake-access-token-must-never-be-logged";

    public Exception? FailWith { get; set; }

    public int Calls { get; private set; }

    public Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        Calls++;
        return FailWith is not null ? Task.FromException<string>(FailWith) : Task.FromResult(AccessToken);
    }
}
