namespace YouTubeAutomation.Services;

public static class ProcessedVideoLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.CurrentDirectory,
        "Downloads",
        "processed-videos.txt"
    );

    public static async Task<HashSet<string>> ReadIdsAsync()
    {
        if (!File.Exists(LogPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var ids = await File.ReadAllLinesAsync(LogPath);
        return new HashSet<string>(
            ids.Select(line => line.Split('\t', 2)[0].Trim())
                .Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public static async Task RecordAsync(string videoId, string title)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        string safeTitle = title.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        string entry = $"{videoId}\t{DateTimeOffset.Now:O}\t{safeTitle}{Environment.NewLine}";
        await File.AppendAllTextAsync(LogPath, entry);
    }
}
