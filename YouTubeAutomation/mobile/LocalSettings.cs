using System.Text.Json;

namespace YouTubeAutomation.Mobile;

public sealed class LocalSettings
{
    private const string ApiUrlKey = "automation_api_url";
    public string ApiUrl
    {
        get
        {
            var saved = Preferences.Default.Get<string?>(ApiUrlKey, null);
            // Earlier builds used 5079; the current ASP.NET service is pinned to 5077.
            if (Uri.TryCreate(saved, UriKind.Absolute, out var previous) && previous.Port == 5079
                && (previous.Host is "127.0.0.1" or "localhost" or "10.0.2.2"))
            {
#if ANDROID
                saved = "http://10.0.2.2:5077";
#else
                saved = "http://127.0.0.1:5077";
#endif
                Preferences.Default.Set(ApiUrlKey, saved);
            }
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            return
#if ANDROID
            "http://10.0.2.2:5077"
#else
            "http://127.0.0.1:5077"
#endif
            ;
        }
        set => Preferences.Default.Set(ApiUrlKey, value.Trim().TrimEnd('/'));
    }

    public string? ImportedClientPath => Preferences.Default.Get<string?>("oauth_client_path", null);
    public string Region { get => Preferences.Default.Get("search_region", "IN"); set => Preferences.Default.Set("search_region", value); }
    public string Language { get => Preferences.Default.Get("search_language", "en"); set => Preferences.Default.Set("search_language", value); }
    public int MinSeconds { get => Preferences.Default.Get("duration_min", 30); set => Preferences.Default.Set("duration_min", value); }
    public int MaxSeconds { get => Preferences.Default.Get("duration_max", 50); set => Preferences.Default.Set("duration_max", value); }
    public string AiProvider { get => Preferences.Default.Get("ai_provider", "Groq"); set => Preferences.Default.Set("ai_provider", value); }
    public string AiModel { get => Preferences.Default.Get("ai_model", ""); set => Preferences.Default.Set("ai_model", value); }

    public string LocalDataDirectory => FileSystem.AppDataDirectory;
    public string HistoryPath => Path.Combine(LocalDataDirectory, "activity.json");

    public async Task<string> ImportClientConfigAsync(CancellationToken cancellationToken = default)
    {
        bool proceed = await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
            "Choose OAuth configuration",
            "Wildframe will ask Android to open the system file picker. The selected file will be copied into this app's private local storage.",
            "Choose file",
            "Cancel");
        if (!proceed) throw new OperationCanceledException();

        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select Google OAuth client configuration",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = new[] { "application/json", "text/json" },
                [DevicePlatform.WinUI] = new[] { ".json" }
            })
        });
        if (file is null) throw new OperationCanceledException();

        await using var input = await file.OpenReadAsync();
        var configBytes = await ReadAllAsync(input, cancellationToken);
        using var document = JsonDocument.Parse(configBytes);
        var root = document.RootElement;
        JsonElement client;
        if (!root.TryGetProperty("installed", out client) && !root.TryGetProperty("web", out client))
            throw new InvalidDataException("This JSON is not a Google OAuth client configuration. Choose a file containing an installed or web client.");
        if (!client.TryGetProperty("client_id", out var clientId) || string.IsNullOrWhiteSpace(clientId.GetString()))
            throw new InvalidDataException("The selected configuration has no client_id.");

        var destination = Path.Combine(LocalDataDirectory, "oauth-client.json");
        await File.WriteAllBytesAsync(destination, configBytes, cancellationToken);
        Preferences.Default.Set("oauth_client_path", destination);
        return $"OAuth client configuration imported to private app storage ({clientId.GetString()}).";
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async Task AddActivityAsync(string message)
    {
        List<ActivityEntry> items = [];
        if (File.Exists(HistoryPath))
        {
            try { await using var stream = File.OpenRead(HistoryPath); items = await JsonSerializer.DeserializeAsync<List<ActivityEntry>>(stream) ?? []; }
            catch (JsonException) { }
        }
        items.Insert(0, new ActivityEntry(DateTimeOffset.Now, message));
        await File.WriteAllTextAsync(HistoryPath, JsonSerializer.Serialize(items.Take(500)));
    }

    public async Task<List<ActivityEntry>> ReadActivityAsync()
    {
        if (!File.Exists(HistoryPath)) return [];
        try { await using var stream = File.OpenRead(HistoryPath); return await JsonSerializer.DeserializeAsync<List<ActivityEntry>>(stream) ?? []; }
        catch (JsonException) { return []; }
    }
}

public sealed record ActivityEntry(DateTimeOffset At, string Message);
