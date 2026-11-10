using Google.Apis.YouTube.v3;
using YouTubeAutomation.Authentication;

namespace YouTubeAutomation.Services;

public class YoutubeService
{
    public static async Task<YouTubeService> ConnectAsync()
    {
        return await YouTubeAuth.Authenticate();
    }
}