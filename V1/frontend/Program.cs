using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNetEnv;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.YouTube.v3;
using Google.Apis.Util.Store;
using Microsoft.Extensions.FileProviders;
using YouTubeAutomation.Discovery;
using YouTubeAutomation.Models;
using YouTubeAutomation.Services;

var appDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var frontendDirectory = Path.Combine(appDirectory, "frontend");
var privateDirectory = Path.Combine(appDirectory, ".wildframe-private");
Directory.CreateDirectory(privateDirectory);
Environment.CurrentDirectory = appDirectory;
var envPath = Path.Combine(appDirectory, ".env");
if (File.Exists(envPath)) Env.Load(envPath);
AppFiles.Initialize(appDirectory, privateDirectory);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5077");
builder.Services.AddSingleton<AutomationWorkflow>();
builder.Services.AddHttpClient();
var app = builder.Build();

app.MapGet("/api/status", async (AutomationWorkflow workflow) =>
{
    var provider = await AppFiles.ReadProviderAsync();
    return Results.Ok(new
    {
        success = true,
        youtube = new
        {
            state = workflow.ConnectionState,
            message = workflow.ConnectionMessage,
            clientSecretConfigured = File.Exists(AppFiles.ClientSecretPath)
        },
        provider = new
        {
            configured = !string.IsNullOrWhiteSpace(provider.ApiKey),
            provider = provider.Provider,
            baseUrl = provider.BaseUrl,
            model = provider.Model
        },
        app = "online",
        sessionOperations = workflow.GetSessionOperations()
    });
});

app.MapPost("/api/youtube/client-secret", async (ClientSecretRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Json) || request.Json.Length > 1_000_000)
        return Results.BadRequest(new { message = "Choose a valid OAuth client JSON file smaller than 1 MB." });
    try
    {
        using var doc = JsonDocument.Parse(request.Json);
        if (!doc.RootElement.TryGetProperty("installed", out var installed)
            || !installed.TryGetProperty("client_id", out var clientId)
            || !installed.TryGetProperty("client_secret", out var clientSecret)
            || string.IsNullOrWhiteSpace(clientId.GetString())
            || string.IsNullOrWhiteSpace(clientSecret.GetString()))
            return Results.BadRequest(new { message = "This file does not look like a Google OAuth desktop client JSON." });
        await File.WriteAllTextAsync(AppFiles.ClientSecretPath, request.Json, Encoding.UTF8);
        return Results.Ok(new { success = true, message = "OAuth client saved locally." });
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { message = "The selected file is not valid JSON." });
    }
});

app.MapPost("/api/youtube/connect", async (AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () =>
    {
        await workflow.ConnectYouTube(ct);
        return Results.Ok(new { success = true, state = workflow.ConnectionState, message = workflow.ConnectionMessage });
    }));

app.MapPost("/api/provider/configure", async (ProviderConfigRequest request, IHttpClientFactory clients, CancellationToken ct) =>
{
    var prior = await AppFiles.ReadProviderAsync();
    var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? prior.ApiKey : request.ApiKey;
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(request.BaseUrl) || string.IsNullOrWhiteSpace(request.Model))
        return Results.BadRequest(new { message = "Provider, base URL, model, and API key are required." });
    if (!Uri.TryCreate(request.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Host != "127.0.0.1" && uri.Host != "localhost"))
        return Results.BadRequest(new { message = "Enter a valid HTTPS provider URL (or a localhost URL for a local provider)." });
    try
    {
        using var http = clients.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, request.BaseUrl.TrimEnd('/') + "/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new[] { new { role = "user", content = "Reply with OK." } },
            max_tokens = 4
        }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
            return Results.BadRequest(new { message = $"Provider validation failed (HTTP {(int)response.StatusCode}). Check the URL, model, and key." });
        await AppFiles.WriteProviderAsync(new ProviderSettings(request.Provider, request.BaseUrl.TrimEnd('/'), request.Model, apiKey));
        return Results.Ok(new { success = true, configured = true, provider = request.Provider, baseUrl = request.BaseUrl.TrimEnd('/'), model = request.Model });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch
    {
        return Results.BadRequest(new { message = "Could not validate the provider connection. Check the URL and try again." });
    }
});

app.MapGet("/api/settings", async () => Results.Ok(await AppFiles.ReadPreferencesAsync()));
app.MapPost("/api/settings", async (Preferences preferences) =>
{
    if (preferences.MinViews < 0 || preferences.MaxViews < 0 || preferences.MinSeconds < 0 || preferences.MaxSeconds < 0)
        return Results.BadRequest(new { message = "Search limits must be zero or greater." });
    await AppFiles.WritePreferencesAsync(preferences);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/candidates", async (CandidateRequest? request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, candidates = await workflow.FetchCandidates(ct, request) })));

app.MapPost("/api/download", async (ActionRequest request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, videoPath = await workflow.Download(request.VideoId, ct) })));

app.MapPost("/api/metadata", async (ActionRequest request, AutomationWorkflow workflow, CancellationToken ct) =>
    await Execute(async () => Results.Ok(new { success = true, metadata = await workflow.GenerateMetadata(request.VideoId, ct) })));

app.MapPost("/api/publish", async (PublishRequest request, AutomationWorkflow workflow, CancellationToken ct) =>
{
    if (!request.RightsConfirmed) return Results.BadRequest(new { message = "Confirm that you have the necessary rights before uploading." });
    if (!new[] { "private", "unlisted", "public" }.Contains(request.Visibility, StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Choose Private, Unlisted, or Public visibility." });
    return await Execute(async () => Results.Ok(new { success = true, visibility = request.Visibility.ToLowerInvariant(), url = await workflow.Publish(request.VideoId, request.Metadata, request.Visibility.ToLowerInvariant(), ct) }));
});

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
    catch (Exception ex)
    {
        var status = ex is InvalidOperationException ? 400 : 500;
        return Results.Problem(ex.Message, statusCode: status);
    }
}

public sealed record ClientSecretRequest(string Json);
public sealed record ProviderConfigRequest(string Provider, string BaseUrl, string Model, string ApiKey);
public sealed record ActionRequest(string VideoId);
public sealed record PublishRequest(string VideoId, GeneratedMetadata Metadata, bool RightsConfirmed, string Visibility = "private");
public sealed record CandidateRequest(
    string? Query = null, string? Region = null, string? Language = null,
    DateTimeOffset? PublishedAfter = null, DateTimeOffset? PublishedBefore = null,
    long MinViews = 0, long MaxViews = 0, int MinSeconds = 0, int MaxSeconds = 0,
    string License = "any", int TargetCount = 60);
public sealed record Preferences(string Region = "IN", string Language = "en", long MinViews = 0, long MaxViews = 0, int MinSeconds = 0, int MaxSeconds = 60);
public sealed record ProviderSettings(string Provider, string BaseUrl, string Model, string ApiKey);

public static class AppFiles
{
    private static string privateDirectory = "";
    public static string ClientSecretPath => Path.Combine(privateDirectory, "client_secret.json");
    private static string ProviderPath => Path.Combine(privateDirectory, "provider.json");
    private static string PreferencesPath => Path.Combine(privateDirectory, "preferences.json");
    public static void Initialize(string root, string secretDirectory)
    {
        privateDirectory = secretDirectory;
        Environment.SetEnvironmentVariable("YOUTUBE_CLIENT_SECRET_PATH", ClientSecretPath);
        Environment.SetEnvironmentVariable("WILDFRAME_PRIVATE_DIR", secretDirectory);
    }
    public static async Task<ProviderSettings> ReadProviderAsync() => await ReadJson(ProviderPath, new ProviderSettings("Groq", "https://api.groq.com/openai/v1", "", ""));
    public static Task WriteProviderAsync(ProviderSettings value) => WriteJson(ProviderPath, value);
    public static async Task<Preferences> ReadPreferencesAsync() => await ReadJson(PreferencesPath, new Preferences());
    public static Task WritePreferencesAsync(Preferences value) => WriteJson(PreferencesPath, value);
    private static async Task<T> ReadJson<T>(string path, T fallback)
    {
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path)) ?? fallback; }
        catch { return fallback; }
    }
    private static Task WriteJson<T>(string path, T value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value));
}

public sealed class AutomationWorkflow
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, VideoModel> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> downloads = new(StringComparer.OrdinalIgnoreCase);
    private YouTubeService? youtube;
    private string connectionState = "disconnected";
    private string connectionMessage = "Connect a Google account to use YouTube features.";
    private long fetchOperations, downloadOperations, metadataOperations, publishOperations;

    public string ConnectionState => connectionState;
    public string ConnectionMessage => connectionMessage;
    public object GetSessionOperations() => new
    {
        fetchedCandidateSets = Interlocked.Read(ref fetchOperations), downloads = Interlocked.Read(ref downloadOperations),
        metadataGenerations = Interlocked.Read(ref metadataOperations), uploads = Interlocked.Read(ref publishOperations)
    };

    public async Task ConnectYouTube(CancellationToken ct)
    {
        if (!File.Exists(AppFiles.ClientSecretPath)) throw new InvalidOperationException("Add a Google OAuth desktop client JSON file before connecting.");
        await gate.WaitAsync(ct);
        try
        {
            connectionState = "connecting";
            connectionMessage = "Complete Google sign-in in the browser window.";
            try
            {
                var secrets = GoogleClientSecrets.FromFile(AppFiles.ClientSecretPath).Secrets;
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    new[] { YouTubeService.Scope.YoutubeReadonly, YouTubeService.Scope.YoutubeUpload },
                    "user", ct, new FileDataStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wildframe", "YouTube.Token.Store"), true));
                youtube = new YouTubeService(new Google.Apis.Services.BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "WILDFRAME local creator workspace" });
                connectionState = "connected";
                connectionMessage = "YouTube account connected.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { connectionState = "disconnected"; connectionMessage = "Connection was cancelled."; throw; }
            catch (Exception ex) { youtube = null; connectionState = "expired"; connectionMessage = "Google sign-in did not complete. Reconnect and review the OAuth setup."; throw new InvalidOperationException(connectionMessage, ex); }
        }
        finally { gate.Release(); }
    }

    public async Task<List<VideoModel>> FetchCandidates(CancellationToken ct, CandidateRequest? query = null)
    {
        if (youtube is null) throw new InvalidOperationException("Connect YouTube before discovering videos.");
        await gate.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref fetchOperations);
            var processed = await ProcessedVideoLog.ReadIdsAsync();
            var videos = await ShortsFetcher.Fetch(youtube, processed, Math.Clamp(query?.TargetCount ?? 60, 1, 200), query?.Region ?? "IN", query?.Language ?? "en", query?.PublishedAfter, query?.PublishedBefore, query?.License ?? "any", query?.Query ?? "");
            var filtered = videos.Where(v => (query?.MinViews ?? 0) <= 0 || v.Views >= query!.MinViews)
                .Where(v => (query?.MaxViews ?? 0) <= 0 || v.Views <= query!.MaxViews)
                .Where(v => (query?.MinSeconds ?? 0) <= 0 || v.DurationSeconds >= query!.MinSeconds)
                .Where(v => (query?.MaxSeconds ?? 0) <= 0 || v.DurationSeconds <= query!.MaxSeconds).ToList();
            var ranked = ViralAnalyzer.Analyze(filtered);
            candidates.Clear(); foreach (var video in ranked) candidates[video.Id] = video;
            return ranked;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized || ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        { connectionState = "expired"; connectionMessage = "YouTube authorization needs attention. Reconnect your account."; youtube = null; throw new InvalidOperationException(connectionMessage); }
        finally { gate.Release(); }
    }

    public async Task<string> Download(string videoId, CancellationToken ct)
    {
        if (!candidates.TryGetValue(videoId, out var video)) throw new InvalidOperationException("Fetch candidates and select a video first.");
        await gate.WaitAsync(ct);
        try { Interlocked.Increment(ref downloadOperations); string path = await VideoDownloader.DownloadOneAsync(video.Url); downloads[videoId] = path; await ProcessedVideoLog.RecordAsync(video.Id, video.Title); return path; }
        finally { gate.Release(); }
    }
    public async Task<GeneratedMetadata> GenerateMetadata(string videoId, CancellationToken ct)
    {
        if (!candidates.TryGetValue(videoId, out var video)) throw new InvalidOperationException("Fetch candidates and select a video first.");
        await gate.WaitAsync(ct); try { Interlocked.Increment(ref metadataOperations); return await MetadataRewriter.Rewrite(video); } finally { gate.Release(); }
    }
    public async Task<string> Publish(string videoId, GeneratedMetadata metadata, string visibility, CancellationToken ct)
    {
        if (youtube is null) throw new InvalidOperationException("Connect YouTube before uploading.");
        if (!candidates.TryGetValue(videoId, out var video)) throw new InvalidOperationException("Fetch candidates and select a video first.");
        if (!downloads.TryGetValue(videoId, out var path) || !File.Exists(path)) throw new InvalidOperationException("Download this video before uploading.");
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Title)) throw new InvalidOperationException("Generate and review metadata before uploading.");
        await gate.WaitAsync(ct);
        try { Interlocked.Increment(ref publishOperations); return await YouTubeUploader.UploadVideoAsync(youtube, path, metadata, visibility); }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized || ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        { connectionState = "expired"; connectionMessage = "YouTube authorization needs attention. Reconnect your account."; youtube = null; throw new InvalidOperationException(connectionMessage); }
        finally { gate.Release(); }
    }
}
