using Spot.Notifications.Api.Options;
using Spot.Notifications.Api.Services.Push;

namespace Spot.Notifications.Api.Tests.Services.Push;

public sealed class FcmConfigurationTests
{
    private static readonly FcmOptionsValidator Validator = new();

    [Fact]
    public void Validator_Unconfigured_IsValid_PushIsOptional()
    {
        Assert.True(Validator.Validate(null, new FcmOptions()).Succeeded);
    }

    [Fact]
    public void Validator_ConfiguredWithValidValues_Succeeds()
    {
        var options = new FcmOptions { ProjectId = "spot-demo-123", CredentialsFile = "/run/secrets/fcm_credentials" };

        Assert.True(Validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("Spot_Demo", "https://fcm.googleapis.com/v1", 10)]
    [InlineData("spot", "https://fcm.googleapis.com/v1", 10)]
    [InlineData("spot-demo-123", "http://fcm.googleapis.com/v1", 10)]
    [InlineData("spot-demo-123", "https://fcm.googleapis.com/v1", 0)]
    [InlineData("spot-demo-123", "https://fcm.googleapis.com/v1", 61)]
    public void Validator_ConfiguredWithBadValues_Fails(string projectId, string baseUrl, int timeoutSeconds)
    {
        var options = new FcmOptions
        {
            ProjectId = projectId,
            CredentialsFile = "/run/secrets/fcm_credentials",
            BaseUrl = baseUrl,
            TimeoutSeconds = timeoutSeconds,
        };

        Assert.True(Validator.Validate(null, options).Failed);
    }

    [Fact]
    public async Task AccessTokenProvider_MissingKeyFile_IsNotConfigured()
    {
        var provider = new GoogleFcmAccessTokenProvider(Microsoft.Extensions.Options.Options.Create(new FcmOptions
        {
            ProjectId = "spot-demo-123",
            CredentialsFile = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.json"),
        }));

        await Assert.ThrowsAsync<FcmNotConfiguredException>(() => provider.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"type":"authorized_user","client_id":"x","client_secret":"y","refresh_token":"z"}""")]
    [InlineData("not json")]
    public async Task AccessTokenProvider_KeyFileThatIsNotAServiceAccount_IsNotConfigured(string content)
    {
        // "{}" is the placeholder docker-compose mounts when no key is configured.
        var path = Path.Combine(Path.GetTempPath(), $"fcm-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, content);
        try
        {
            var provider = new GoogleFcmAccessTokenProvider(Microsoft.Extensions.Options.Options.Create(new FcmOptions
            {
                ProjectId = "spot-demo-123",
                CredentialsFile = path,
            }));

            var ex = await Assert.ThrowsAsync<FcmNotConfiguredException>(() => provider.GetAccessTokenAsync());
            Assert.DoesNotContain(content, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
