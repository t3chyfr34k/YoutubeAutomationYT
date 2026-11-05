
using DotNetEnv;
using YouTubeAutomation.Services;
using YouTubeAutomation.Discovery;
using YouTubeAutomation.Models;

Env.Load();

try
{
    Console.WriteLine("===== YOUTUBE AUTOMATION =====");

    // Connect to YouTube

    Console.WriteLine("\nConnecting to YouTube...");

    var youtube = await YoutubeService.ConnectAsync();

    Console.WriteLine("YouTube connected!");

    // Fetch candidates

    Console.WriteLine("\nFetching video candidates...");
    Console.WriteLine(
        "Filters: Creative Commons license, Entertainment category, last 30 days, 30-50 seconds."
    );
    Console.WriteLine(
        "Creative Commons is an uploader-selected license, not a guarantee that every element (such as music) is cleared."
    );

    var processedIds = await ProcessedVideoLog.ReadIdsAsync();
    List<VideoModel> videos = await ShortsFetcher.Fetch(
        youtube,
        processedIds,
        targetCount: 60
    );
    Console.WriteLine(
        $"Previously processed videos excluded: {processedIds.Count}"
    );

    Console.WriteLine("\nAnalyzing videos...");

    var trending = ViralAnalyzer.Analyze(videos);

    if (trending.Count == 0)
    {
        Console.WriteLine("No new video candidates found.");
        return;
    }

    // Display top 10

    var candidates = trending.Take(10).ToList();

    Console.WriteLine("\n===== VIDEO CANDIDATES =====");

    for (int i = 0; i < candidates.Count; i++)
    {
        var video = candidates[i];

        Console.WriteLine($"\n{i + 1}. {video.Title}");
        Console.WriteLine("Views: " + video.Views);
        Console.WriteLine("Views/hour: " + video.ViewsPerHour.ToString("F0"));
        Console.WriteLine("Momentum score: " + video.ViralScore.ToString("F0"));
        Console.WriteLine("Duration: " + video.DurationSeconds);
        Console.WriteLine("URL: " + video.Url);
    }

    // Select video

    Console.Write("\nSelect ONE video: ");

    if (!int.TryParse(Console.ReadLine(), out int choice) ||
        choice < 1 || choice > candidates.Count)
    {
        Console.WriteLine("Invalid selection.");
        return;
    }

    VideoModel selected = candidates[choice - 1];

    Console.WriteLine("\nSelected: " + selected.Title);

    // Download

    Console.WriteLine("\nDownloading video...");

    string downloadedFile =
        await VideoDownloader.DownloadOneAsync(selected.Url);

    await ProcessedVideoLog.RecordAsync(selected.Id, selected.Title);

    Console.WriteLine("\nDownload completed!");
    Console.WriteLine("File: " + downloadedFile);

    // Generate metadata

    Console.WriteLine("\nGenerating metadata with Groq...");

    GeneratedMetadata metadata =
        await MetadataRewriter.Rewrite(selected);

    // Display metadata

    Console.WriteLine("\n===== GENERATED METADATA =====");

    Console.WriteLine("\nTITLE:");
    Console.WriteLine(metadata.Title);

    Console.WriteLine("\nDESCRIPTION:");
    Console.WriteLine(metadata.Description);

    Console.WriteLine("\nHASHTAGS:");
    Console.WriteLine(string.Join(" ", metadata.Hashtags));

    Console.WriteLine("\nKEYWORDS:");
    Console.WriteLine(string.Join(", ", metadata.Keywords));

    // Save metadata

    string downloadFolder =
        Path.GetDirectoryName(downloadedFile)!;

    string metadataFile = Path.Combine(
        downloadFolder,
        "metadata_" + selected.Id + ".json"
    );

    string json = System.Text.Json.JsonSerializer.Serialize(
        metadata,
        new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }
    );

    await File.WriteAllTextAsync(metadataFile, json);

    Console.WriteLine("\nMetadata saved: " + metadataFile);

    // Upload

    Console.WriteLine("\nPublishing video publicly to your YouTube channel...");

    string uploadedUrl = await YouTubeUploader.UploadVideoAsync(
        youtube,
        downloadedFile,
        metadata
    );

    Console.WriteLine("\n===== UPLOAD SUCCESSFUL =====");

    Console.WriteLine("YouTube URL: " + uploadedUrl);

    Console.WriteLine("\nProcess completed!");
}
catch (Exception ex)
{
    Console.WriteLine("\nSomething went wrong:");
    Console.WriteLine(ex.ToString());
}
