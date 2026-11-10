
namespace YouTubeAutomation.Models;

public class VideoModel
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public long Views { get; set; }

    public long Likes { get; set; }

    public long Comments { get; set; }

    public DateTime PublishedAt { get; set; }

    public double DurationSeconds { get; set; }

    public double ViewsPerHour { get; set; }

    public double ViralScore { get; set; }

    public string Url { get; set; } = "";
}
