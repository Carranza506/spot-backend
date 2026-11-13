namespace Spot.Notifications.Api.Services.Push;

/// <summary>OAuth 2.0 access tokens for FCM HTTP v1, minted from the service account key.</summary>
public interface IFcmAccessTokenProvider
{
    /// <exception cref="FcmNotConfiguredException">The key file is missing or isn't a service account key.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

/// <summary>Push delivery can't run with the current configuration (e.g. no usable service account key).</summary>
public sealed class FcmNotConfiguredException(string message, Exception? innerException = null)
    : Exception(message, innerException);
