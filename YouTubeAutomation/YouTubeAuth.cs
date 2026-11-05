
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
        var secrets = GoogleClientSecrets
            .FromFile("client_secret_658034708680-psgma2csro9urq0u39i89egk68af3gfm.apps.googleusercontent.com.json").Secrets;

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
