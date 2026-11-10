using System.Collections.Concurrent;
using DotNetEnv;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.FileProviders;
using YouTubeAutomation.Discovery;
using YouTubeAutomation.Models;
using YouTubeAutomation.Services;

var appDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var frontendDirectory = Path.Combine(appDirectory, "frontend");
Environment.CurrentDirectory = appDirectory;
var envPath = Path.Combine(appDirectory, ".env");
if (File.Exists(envPath)) Env.Load(envPath);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5077");
builder.Services.AddSingleton<AutomationWorkflow>();
var app = builder.Build();

app.MapGet("/api/status", (AutomationWorkflow workflow) => Results.Ok(new
{
    success = true,
    connected = workflow.IsConnected,
    message = "Local automation service ready",
    app = "online",
    sessionOperations = workflow.GetSessionOperations(),
    groqConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_BASE_URL"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_MODEL")),
    youtubeCredentialsFound = File.Exists(Path.Combine(appDirectory,
        "client_secret.json"))
}));

app.MapPost("/api/candidates", async (CandidateRequest? request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, candidates = await workflow.FetchCandidates(ct, request) })));

app.MapPost("/api/download", async (ActionRequest request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, videoPath = await workflow.Download(request.VideoId, ct) })));

app.MapPost("/api/metadata", async (ActionRequest request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, metadata = await workflow.GenerateMetadata(request.VideoId, ct) })));

app.MapPost("/api/publish", async (PublishRequest request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, visibility = "public", url = await workflow.Publish(request.VideoId, request.Metadata, ct) })));

var staticProvider = new PhysicalFileProvider(frontendDirectory);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = staticProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = staticProvider });
app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(frontendDirectory, "index.html"));
});

await app.RunAsync();

static async Task<IResult> Execute(Func<Task<IResult>> action)
{
    try { return await action(); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

public sealed record ActionRequest(string VideoId);
public sealed record PublishRequest(string VideoId, GeneratedMetadata Metadata);
public sealed record CandidateRequest(string? Region = null, string? Language = null, int MinSeconds = 30, int MaxSeconds = 50, int TargetCount = 60);

public sealed class AutomationWorkflow
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, VideoModel> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> downloads = new(StringComparer.OrdinalIgnoreCase);
    private YouTubeService? youtube;
    private long fetchOperations, downloadOperations, metadataOperations, publishOperations;

    public bool IsConnected => youtube is not null;
    public object GetSessionOperations() => new
    {
        fetchedCandidateSets = Interlocked.Read(ref fetchOperations),
        downloads = Interlocked.Read(ref downloadOperations),
        metadataGenerations = Interlocked.Read(ref metadataOperations),
        uploads = Interlocked.Read(ref publishOperations)
    };

    public async Task<List<VideoModel>> FetchCandidates(CancellationToken ct, CandidateRequest? query = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref fetchOperations);
            youtube ??= await YoutubeService.ConnectAsync();
            var processed = await ProcessedVideoLog.ReadIdsAsync();
            var videos = await ShortsFetcher.Fetch(
                youtube,
                processed,
                targetCount: Math.Clamp(query?.TargetCount ?? 60, 51, 200),
                region: query?.Region ?? Environment.GetEnvironmentVariable("YOUTUBE_REGION") ?? "IN",
                language: query?.Language ?? "en",
                minDurationSeconds: Math.Clamp(query?.MinSeconds ?? 30, 1, 240),
                maxDurationSeconds: Math.Clamp(query?.MaxSeconds ?? 50, 1, 240)
            );
            var ranked = ViralAnalyzer.Analyze(videos);
            candidates.Clear();
            foreach (var video in ranked) candidates[video.Id] = video;
            return ranked;
        }
        finally { gate.Release(); }
    }

    public async Task<string> Download(string videoId, CancellationToken ct)
    {
        if (!candidates.TryGetValue(videoId, out var video))
            throw new InvalidOperationException("Fetch candidates and select a video first.");
        await gate.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref downloadOperations);
            string path = await VideoDownloader.DownloadOneAsync(video.Url);
            downloads[videoId] = path;
            await ProcessedVideoLog.RecordAsync(video.Id, video.Title);
            return path;
        }
        finally { gate.Release(); }
    }

    public async Task<GeneratedMetadata> GenerateMetadata(string videoId, CancellationToken ct)
    {
        if (!candidates.TryGetValue(videoId, out var video))
            throw new InvalidOperationException("Fetch candidates and select a video first.");
        await gate.WaitAsync(ct);
        try { Interlocked.Increment(ref metadataOperations); return await MetadataRewriter.Rewrite(video); }
        finally { gate.Release(); }
    }

    public async Task<string> Publish(string videoId, GeneratedMetadata metadata, CancellationToken ct)
    {
        if (!candidates.TryGetValue(videoId, out var video))
            throw new InvalidOperationException("Fetch candidates and select a video first.");
        if (!downloads.TryGetValue(videoId, out var path) || !File.Exists(path))
            throw new InvalidOperationException("Download this video before publishing.");
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Title))
            throw new InvalidOperationException("Generate and review metadata before publishing.");
        await gate.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref publishOperations);
            youtube ??= await YoutubeService.ConnectAsync();
            return await YouTubeUploader.UploadVideoAsync(youtube, path, metadata);
        }
        finally { gate.Release(); }
    }
}
