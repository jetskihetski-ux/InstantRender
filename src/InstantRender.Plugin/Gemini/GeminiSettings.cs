using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstantRender.Gemini;

/// <summary>
/// Configuration for the Gemini image API. Loaded from (in priority order):
///   1. the GEMINI_API_KEY environment variable, and
///   2. an instantrender.config.json file next to the plugin DLL.
/// The API key is never hard-coded or committed.
/// </summary>
public sealed class GeminiSettings
{
    /// <summary>Image-generation model id. "Nano Banana" family by default.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gemini-2.5-flash-image";

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>Where renders are written. Defaults to a folder by the DWG.</summary>
    [JsonPropertyName("outputDirectory")]
    public string? OutputDirectory { get; set; }

    /// <summary>Style preset name (see PromptStyle). "Modern" by default.</summary>
    [JsonPropertyName("style")]
    public string Style { get; set; } = "Modern";

    [JsonIgnore]
    public string EndpointBase { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Resolve settings from config file + environment.</summary>
    public static GeminiSettings Load(string pluginDirectory)
    {
        var settings = new GeminiSettings();

        var configPath = Path.Combine(pluginDirectory, "instantrender.config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var loaded = JsonSerializer.Deserialize<GeminiSettings>(json);
                if (loaded is not null) settings = loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Fall back to defaults; the command surfaces a clear error if
                // the API key ends up missing.
            }
        }

        // Environment variable always wins for the secret.
        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            settings.ApiKey = envKey;

        return settings;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
