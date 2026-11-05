using System.Net.Http.Json;
using System.Text.Json;

namespace YouTubeAutomation.Mobile;

public sealed class AutomationApi(LocalSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient CreateClient() => new() { BaseAddress = new Uri(settings.ApiUrl + "/") };

    public async Task<JsonElement> GetStatusAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        return await SendAsync(client, HttpMethod.Get, "api/status", null, ct);
    }

    public async Task<List<VideoCandidate>> FetchCandidatesAsync(CandidateQuery query, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var json = await SendAsync(client, HttpMethod.Post, "api/candidates", query, ct);
        var data = json.TryGetProperty("candidates", out var candidates) ? candidates : json;
        return JsonSerializer.Deserialize<List<VideoCandidate>>(data.GetRawText(), JsonOptions) ?? [];
    }

    public async Task DownloadAsync(string videoId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        await SendAsync(client, HttpMethod.Post, "api/download", new { videoId }, ct);
    }

    public async Task<GeneratedMetadataDto> GenerateMetadataAsync(string videoId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var json = await SendAsync(client, HttpMethod.Post, "api/metadata", new { videoId }, ct);
        var metadata = json.TryGetProperty("metadata", out var value) ? value : json;
        return JsonSerializer.Deserialize<GeneratedMetadataDto>(metadata.GetRawText(), JsonOptions) ?? new();
    }

    public async Task<string> PublishAsync(string videoId, GeneratedMetadataDto metadata, CancellationToken ct = default)
    {
        using var client = CreateClient();
        var json = await SendAsync(client, HttpMethod.Post, "api/publish", new { videoId, metadata }, ct);
        return json.TryGetProperty("url", out var value) ? value.GetString() ?? "Upload completed" : "Upload completed";
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string message = text;
            try
            {
                using var error = JsonDocument.Parse(text);
                message = error.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? text
                    : error.RootElement.TryGetProperty("error", out var errorText) ? errorText.GetString() ?? text : text;
            }
            catch (JsonException) { }
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}

public sealed record CandidateQuery(string Region = "IN", string Language = "en", int MinSeconds = 30, int MaxSeconds = 50, int TargetCount = 60);
public sealed class VideoCandidate
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public long Views { get; set; }
    public double ViewsPerHour { get; set; }
    public double ViralScore { get; set; }
    public double DurationSeconds { get; set; }
    public string Url { get; set; } = "";
}
public sealed class GeneratedMetadataDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Hashtags { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
}
