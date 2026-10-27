using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using Spot.Notifications.Api.Options;

namespace Spot.Notifications.Api.Services.Push;

/// <summary>
/// <see cref="IFcmAccessTokenProvider"/> on Google's official auth library: the key file is loaded
/// on first use (not at startup, so the service boots without it), only as a service account
/// (CredentialFactory refuses other credential types), and the library caches and refreshes the
/// access token itself. Neither the key nor the token is ever logged.
/// </summary>
public sealed class GoogleFcmAccessTokenProvider(IOptions<FcmOptions> options) : IFcmAccessTokenProvider
{
    /// <summary>The OAuth scope FCM HTTP v1 requires to send messages.</summary>
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private GoogleCredential? _credential;

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var credential = await GetCredentialAsync(ct);
        return await ((ITokenAccess)credential).GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    private async Task<GoogleCredential> GetCredentialAsync(CancellationToken ct)
    {
        if (_credential is not null)
            return _credential;

        await _loadLock.WaitAsync(ct);
        try
        {
            return _credential ??= await LoadAsync(options.Value.CredentialsFile, ct);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static async Task<GoogleCredential> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            throw new FcmNotConfiguredException("The FCM service account key file doesn't exist.");

        try
        {
            var serviceAccount = await CredentialFactory.FromFileAsync<ServiceAccountCredential>(path, ct);
            return serviceAccount.ToGoogleCredential().CreateScoped(Scope);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message is generic on purpose: the file's content is a private key.
            throw new FcmNotConfiguredException("The FCM key file isn't a valid service account key.", ex);
        }
    }
}
