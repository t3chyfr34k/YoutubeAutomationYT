
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Upload;
using YouTubeAutomation.Models;

namespace YouTubeAutomation.Services;

public class YouTubeUploader
{
    public static async Task<string> UploadVideoAsync(
        YouTubeService youtube,
        string videoPath,
        GeneratedMetadata metadata,
        string visibility = "private")
    {
        if (!File.Exists(videoPath))
        {
            throw new Exception("Video file not found.");
        }

        string description = metadata.Description;

        if (metadata.Hashtags.Count > 0)
        {
            description += "\n\n" +
                string.Join(" ", metadata.Hashtags);
        }

        var video = new Google.Apis.YouTube.v3.Data.Video
        {
            Snippet = new VideoSnippet
            {
                Title = metadata.Title,
                Description = description,
                Tags = metadata.Keywords
                    .Concat(metadata.Hashtags)
                    .Select(x => x.TrimStart('#'))
                    .Distinct()
                    .ToList(),
                CategoryId = "22"
            },

            Status = new VideoStatus
            {
                PrivacyStatus = visibility
            }
        };

        using var fileStream = File.OpenRead(videoPath);

        var request = youtube.Videos.Insert(
            video,
            "snippet,status",
            fileStream,
            "video/mp4"
        );

        request.ProgressChanged += progress =>
        {
            if (progress.Status == UploadStatus.Uploading)
            {
                Console.WriteLine(
                    "Uploaded: " +
                    progress.BytesSent / 1024 / 1024 + " MB"
                );
            }
        };

        Console.WriteLine("\nUploading to YouTube...");

        var result = await request.UploadAsync();

        if (result.Status != UploadStatus.Completed)
        {
            throw new Exception(
                "Upload failed: " + result.Exception
            );
        }

        string? videoId = request.ResponseBody?.Id;

        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new Exception("YouTube did not return a video ID.");
        }

        return "https://www.youtube.com/watch?v=" + videoId;
    }
}
