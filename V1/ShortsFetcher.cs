using Google.Apis.YouTube.v3;
using YouTubeAutomation.Models;

namespace YouTubeAutomation.Discovery;

public class ShortsFetcher
{
    private const int PageSize = 50;
    private const int MaxSearchPages = 10;
    private const string EntertainmentCategoryId = "24";

    public static async Task<List<VideoModel>> Fetch(
        YouTubeService youtube,
        ISet<string>? excludedIds = null,
        int targetCount = 60,
        string region = "IN",
        string language = "en",
        DateTimeOffset? publishedAfter = null,
        DateTimeOffset? publishedBefore = null,
        string license = "any",
        string query = "")
    {
        var search = youtube.Search.List("id");
        search.Q = string.IsNullOrWhiteSpace(query) ? "#shorts" : query.Trim();
        search.Type = "video";
        search.Order = SearchResource.ListRequest.OrderEnum.ViewCount;
        search.VideoCategoryId = EntertainmentCategoryId;
        if (string.Equals(license, "creativeCommon", StringComparison.OrdinalIgnoreCase))
            search.VideoLicense = SearchResource.ListRequest.VideoLicenseEnum.CreativeCommon;
        else if (string.Equals(license, "youtube", StringComparison.OrdinalIgnoreCase))
            search.VideoLicense = SearchResource.ListRequest.VideoLicenseEnum.Youtube;
        search.PublishedAfterDateTimeOffset = publishedAfter;
        search.PublishedBeforeDateTimeOffset = publishedBefore;
        search.VideoDuration =
            SearchResource.ListRequest.VideoDurationEnum.Short__;
        search.RegionCode = string.IsNullOrWhiteSpace(region)
            ? Environment.GetEnvironmentVariable("YOUTUBE_REGION") ?? "IN"
            : region.Trim().ToUpperInvariant();
        search.RelevanceLanguage = string.IsNullOrWhiteSpace(language)
            ? "en"
            : language.Trim();
        search.MaxResults = PageSize;

        var videos = new List<VideoModel>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? pageToken = null;
        int pageCount = 0;
        int categoryRejected = 0;

        while (pageCount < MaxSearchPages && videos.Count < targetCount)
        {
            search.PageToken = pageToken;
            var searchResult = await search.ExecuteAsync();
            pageCount++;

            var ids = searchResult.Items
                .Where(item => item.Id.VideoId != null)
                .Select(item => item.Id.VideoId!)
                .Where(id => seenIds.Add(id))
                .Where(id => excludedIds == null || !excludedIds.Contains(id))
                .ToList();

            Console.WriteLine(
                $"Search page {pageCount}: {ids.Count} new videos"
            );

            if (ids.Count > 0)
            {
                var request = youtube.Videos.List(
                    "snippet,statistics,contentDetails"
                );
                request.Id = string.Join(",", ids);
                var result = await request.ExecuteAsync();

                foreach (var video in result.Items)
                {
                    if (video.Snippet.CategoryId != EntertainmentCategoryId)
                    {
                        categoryRejected++;
                        continue;
                    }

                    double duration = System.Xml.XmlConvert
                        .ToTimeSpan(video.ContentDetails.Duration)
                        .TotalSeconds;

                    DateTime publishedAt =
                        video.Snippet.PublishedAtDateTimeOffset?
                            .UtcDateTime ?? DateTime.UtcNow;
                    long views = (long)(video.Statistics.ViewCount ?? 0);
                    long likes = (long)(video.Statistics.LikeCount ?? 0);
                    long comments = (long)(video.Statistics.CommentCount ?? 0);
                    double hours = Math.Max(
                        (DateTime.UtcNow - publishedAt).TotalHours,
                        0.01
                    );

                    videos.Add(new VideoModel
                    {
                        Id = video.Id,
                        Title = video.Snippet.Title,
                        Description = video.Snippet.Description ?? "",
                        Views = views,
                        Likes = likes,
                        Comments = comments,
                        PublishedAt = publishedAt,
                        DurationSeconds = duration,
                        License = video.Snippet.License ?? "unknown",
                        ViewsPerHour = views / hours,
                        Url = "https://www.youtube.com/watch?v=" + video.Id
                    });
                }
            }

            pageToken = searchResult.NextPageToken;
            if (string.IsNullOrWhiteSpace(pageToken))
            {
                break;
            }
        }

        Console.WriteLine($"Search pages fetched: {pageCount}");
        Console.WriteLine("Rejected by category: " + categoryRejected);
        Console.WriteLine("Returned before local metadata filters: " + videos.Count);

        return videos;
    }
}
