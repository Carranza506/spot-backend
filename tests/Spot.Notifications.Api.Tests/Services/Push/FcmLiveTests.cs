using Microsoft.Extensions.Logging.Abstractions;
using Spot.Notifications.Api.Options;
using Spot.Notifications.Api.Services.Push;

namespace Spot.Notifications.Api.Tests.Services.Push;

/// <summary>
/// Runs only when <see cref="ProjectIdVariable"/> and <see cref="CredentialsFileVariable"/> are set
/// (skipped otherwise, so CI and teammates without Firebase are unaffected). Not a substitute for
/// the fake-backed tests: it proves the real key, OAuth and payload against Google.
/// </summary>
public sealed class LiveFcmFactAttribute : FactAttribute
{
    public const string ProjectIdVariable = "FCM_LIVE_PROJECT_ID";
    public const string CredentialsFileVariable = "FCM_LIVE_CREDENTIALS_FILE";

    public LiveFcmFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProjectIdVariable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CredentialsFileVariable)))
        {
            Skip = $"Set {ProjectIdVariable} and {CredentialsFileVariable} (a service account key outside the repo) to run it.";
        }
    }
}

public sealed class FcmLiveTests
{
    /// <summary>
    /// A real call to FCM with validate_only to a topic: Google checks the credentials and the
    /// payload without delivering anything, so no device is needed. Run it with:
    /// <c>dotnet test --filter "FullyQualifiedName~FcmLiveTests"</c>.
    /// </summary>
    [LiveFcmFact]
    public async Task DryRunToTopic_AgainstRealFcm_IsAccepted()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new FcmOptions
        {
            ProjectId = Environment.GetEnvironmentVariable(LiveFcmFactAttribute.ProjectIdVariable)!,
            CredentialsFile = Environment.GetEnvironmentVariable(LiveFcmFactAttribute.CredentialsFileVariable)!,
        });
        using var httpClient = new HttpClient();
        var sender = new FcmPushSender(
            httpClient, options, new GoogleFcmAccessTokenProvider(options), NullLogger<FcmPushSender>.Instance);

        var result = await sender.DryRunToTopicAsync("spot-demo", "Spot", "Prueba de FCM (validate_only, no se entrega)");

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
    }
}
