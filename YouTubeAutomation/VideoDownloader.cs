
using System.Diagnostics;

namespace YouTubeAutomation.Services;

public class VideoDownloader
{
    public static async Task<string> DownloadOneAsync(string videoUrl)
    {
        Directory.CreateDirectory("Downloads");

        var start = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        start.ArgumentList.Add("--no-playlist");

        start.ArgumentList.Add("--no-progress");
        start.ArgumentList.Add("--newline");

        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("bv*+ba/b");

        start.ArgumentList.Add("--merge-output-format");
        start.ArgumentList.Add("mp4");

        start.ArgumentList.Add("-P");
        start.ArgumentList.Add(Path.GetFullPath("Downloads"));

        // Use the video ID as the filename. Titles can contain characters that
        // Windows cannot represent in a path, and console encodings can also
        // corrupt non-ASCII title characters when yt-dlp prints the path.
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("%(id)s.%(ext)s");

        start.ArgumentList.Add("--print");
        start.ArgumentList.Add("after_move:filepath");

        start.ArgumentList.Add(videoUrl);

        using var process = new Process
        {
            StartInfo = start
        };

        process.Start();

        Task<string> outputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string output = await outputTask;
        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new Exception(
                "yt-dlp failed. Exit code: " + process.ExitCode +
                "\n" + error + "\n" + output
            );
        }

        string[] lines = output
            .Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

        string? filePath = lines.LastOrDefault();

        if (string.IsNullOrWhiteSpace(filePath) ||
            !File.Exists(filePath))
        {
            throw new Exception(
                "Download completed, but output file was not found.\n" +
                "yt-dlp output:\n" + output +
                "\nErrors:\n" + error
            );
        }

        return Path.GetFullPath(filePath);
    }
}
