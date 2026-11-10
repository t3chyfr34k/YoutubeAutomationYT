
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace YouTubeAutomation.Services;

public class GroqService
{
    private static readonly HttpClient client = new();

    public static async Task<string> AskAI(string prompt)
    {
        var settings = await ProviderSettings.ReadAsync();
        string key = settings.ApiKey;
        string url = settings.BaseUrl;
        string model = settings.Model;

        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(url) ||
            string.IsNullOrWhiteSpace(model))
        {
            throw new Exception("AI provider settings are missing. Configure a provider in Settings.");
        }

        var requestData = new
        {
            model = model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        string json = JsonSerializer.Serialize(requestData);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url.TrimEnd('/') + "/chat/completions"
        );

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", key);

        request.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        using var response = await client.SendAsync(request);

        string result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("AI provider request failed (HTTP " + (int)response.StatusCode + "). Check provider settings.");
        }

        using JsonDocument doc = JsonDocument.Parse(result);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "Empty response from Groq.";
    }
}

internal sealed record ProviderSettings(string Provider, string BaseUrl, string Model, string ApiKey)
{
    public static async Task<ProviderSettings> ReadAsync()
    {
        var privateDir = Environment.GetEnvironmentVariable("WILDFRAME_PRIVATE_DIR");
        var path = string.IsNullOrWhiteSpace(privateDir) ? null : Path.Combine(privateDir, "provider.json");
        if (path != null && File.Exists(path))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<ProviderSettings>(await File.ReadAllTextAsync(path))
                    ?? new ProviderSettings("Groq", "", "", "");
            }
            catch { return new ProviderSettings("Groq", "", "", ""); }
        }
        return new ProviderSettings("Groq", Environment.GetEnvironmentVariable("GROQ_BASE_URL") ?? "", Environment.GetEnvironmentVariable("GROQ_MODEL") ?? "", Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "");
    }
}
