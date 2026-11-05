
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace YouTubeAutomation.Services;

public class GroqService
{
    private static readonly HttpClient client = new();

    public static async Task<string> AskAI(string prompt)
    {
        string key =
            Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";

        string url =
            Environment.GetEnvironmentVariable("GROQ_BASE_URL") ?? "";

        string model =
            Environment.GetEnvironmentVariable("GROQ_MODEL") ?? "";

        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(url) ||
            string.IsNullOrWhiteSpace(model))
        {
            throw new Exception("Groq settings are missing in .env");
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
            throw new Exception(
                "Groq API error: " + (int)response.StatusCode +
                "\n" + result
            );
        }

        using JsonDocument doc = JsonDocument.Parse(result);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "Empty response from Groq.";
    }
}
