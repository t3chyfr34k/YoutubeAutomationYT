
using System.Text.Json;
using YouTubeAutomation.Models;

namespace YouTubeAutomation.Services;

public class MetadataRewriter
{
    public static async Task<GeneratedMetadata> Rewrite(VideoModel video)
    {
        string prompt = $$"""
        You are an expert YouTube Shorts SEO writer.

        Your task is to optimize the metadata for this video.

        RULES:
        1. Create a catchy, natural title.
        2. Keep the title relevant to the actual video.
        3. Write an engaging description.
        4. Preserve the original meaning.
        5. Never invent facts or events.
        6. Generate relevant hashtags.
        7. Generate useful search keywords.
        8. Avoid keyword stuffing.
        9. Avoid misleading clickbait.
        10. Never promise viral results.

        ORIGINAL TITLE:
        {{video.Title}}

        ORIGINAL DESCRIPTION:
        {{video.Description}}

        Return ONLY valid JSON.

        Use exactly this structure:

        {
            "title": "Optimized title",
            "description": "Optimized description",
            "hashtags": [
                "#Shorts",
                "#RelevantHashtag"
            ],
            "keywords": [
                "relevant keyword",
                "related search phrase"
            ]
        }

        Generate approximately 5 relevant hashtags
        and 10 relevant keywords.
        """;

        Console.WriteLine("Sending metadata to Groq...");

        // Get response from Groq.

        string response = await GroqService.AskAI(prompt);

        response = response.Trim();

        // Remove Markdown code blocks.

        if (response.StartsWith("```"))
        {
            int firstLine = response.IndexOf('\n');

            if (firstLine >= 0)
            {
                response = response[(firstLine + 1)..];
            }

            int lastBlock = response.LastIndexOf("```");

            if (lastBlock >= 0)
            {
                response = response[..lastBlock];
            }
        }

        response = response.Trim();

        // Convert JSON response into GeneratedMetadata.

        GeneratedMetadata? metadata;

        try
        {
            metadata = JsonSerializer.Deserialize<GeneratedMetadata>(
                response,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );
        }
        catch (JsonException ex)
        {
            throw new Exception(
                "Groq returned invalid JSON:\n" + response,
                ex
            );
        }

        // Validate response.

        if (metadata == null ||
            string.IsNullOrWhiteSpace(metadata.Title) ||
            string.IsNullOrWhiteSpace(metadata.Description))
        {
            throw new Exception(
                "Groq returned incomplete video metadata."
            );
        }

        // Clean hashtags.

        metadata.Hashtags = (metadata.Hashtags ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x =>
            {
                string tag = x.Trim();

                return tag.StartsWith("#") ? tag : "#" + tag;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        // Clean keywords.

        metadata.Keywords = (metadata.Keywords ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        Console.WriteLine("Metadata generation completed!");

        return metadata;
    }
}
