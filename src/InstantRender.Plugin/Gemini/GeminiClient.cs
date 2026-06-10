using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace InstantRender.Gemini;

/// <summary>
/// Minimal client for the Gemini image-generation REST API
/// (generativelanguage.googleapis.com :generateContent). Sends a text prompt
/// and writes the returned image bytes to disk.
/// </summary>
public sealed class GeminiClient : IDisposable
{
    private readonly GeminiSettings _settings;
    private readonly HttpClient _http;

    public GeminiClient(GeminiSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    /// <summary>
    /// Generate one image from <paramref name="prompt"/> and save it to
    /// <paramref name="outputPath"/>. Returns the saved file path.
    /// </summary>
    public async Task<string> GenerateImageAsync(
        string prompt, string outputPath, CancellationToken ct = default)
    {
        if (!_settings.HasApiKey)
            throw new InvalidOperationException(
                "No Gemini API key. Set the GEMINI_API_KEY environment variable " +
                "or add it to instantrender.config.json.");

        var url = $"{_settings.EndpointBase}/models/{_settings.Model}:generateContent";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                // Ask the model to return an image (and optionally text).
                responseModalities = new[] { "TEXT", "IMAGE" }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-goog-api-key", _settings.ApiKey);
        req.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var payload = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Gemini API error {(int)resp.StatusCode}: {Truncate(payload, 500)}");

        var imageBytes = ExtractFirstImage(payload)
            ?? throw new InvalidOperationException(
                "Gemini response contained no image data. Raw: " + Truncate(payload, 500));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, imageBytes, ct).ConfigureAwait(false);
        return outputPath;
    }

    /// <summary>
    /// Pull the first inlineData image out of a :generateContent response.
    /// Shape: candidates[].content.parts[].inlineData.data (base64).
    /// </summary>
    private static byte[]? ExtractFirstImage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates))
            return null;

        foreach (var cand in candidates.EnumerateArray())
        {
            if (!cand.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts))
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                // camelCase ("inlineData") is what the REST API returns.
                if (part.TryGetProperty("inlineData", out var inline) &&
                    inline.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.String)
                {
                    return Convert.FromBase64String(data.GetString()!);
                }
            }
        }
        return null;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _http.Dispose();
}
