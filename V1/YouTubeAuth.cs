
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.Util.Store;
using System.Threading;

namespace YouTubeAutomation.Authentication;

public class YouTubeAuth
{
    public static async Task<YouTubeService> Authenticate()
    {
        var clientSecretPath = Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_SECRET_PATH");
        if (string.IsNullOrWhiteSpace(clientSecretPath))
            throw new InvalidOperationException("YOUTUBE_CLIENT_SECRET_PATH is not set in .env.");
        if (!File.Exists(clientSecretPath))
            throw new FileNotFoundException("YouTube OAuth client secret file was not found.", clientSecretPath);

        var secrets = GoogleClientSecrets
            .FromFile(clientSecretPath).Secrets;

        var scopes = new[]
        {
            YouTubeService.Scope.YoutubeReadonly,
            YouTubeService.Scope.YoutubeUpload
        };

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            scopes,
            "user",
            CancellationToken.None,
            new FileDataStore("YouTube.Token.Store", true)
        );

        return new YouTubeService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "YouTube Automation"
            });
    }
}
