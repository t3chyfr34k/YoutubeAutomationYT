using YouTubeAutomation.Models;

namespace YouTubeAutomation.Discovery;

public class ViralAnalyzer
{
    public static List<VideoModel> Analyze(
        List<VideoModel> videos)
    {
        foreach (var video in videos)
        {
            // Estimate current momentum from view velocity and engagement.
            // This is a ranking heuristic, not a measurement of future growth.
            double engagementRate = video.Views == 0
                ? 0
                : (double)(video.Likes + video.Comments) / video.Views;

            // Cap engagement's influence so a small denominator cannot
            // overwhelm view velocity.
            engagementRate = Math.Clamp(engagementRate, 0, 0.25);

            video.ViralScore = video.ViewsPerHour * (1 + engagementRate);
        }

        return videos
            .OrderByDescending(video => video.ViralScore)
            .ToList();
    }
}
