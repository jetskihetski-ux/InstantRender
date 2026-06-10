using System.Text.Json;
using System.Text.Json.Serialization;
using InstantRender.Gemini;

namespace InstantRender.Render;

/// <summary>
/// Plugin configuration, loaded from instantrender.config.json next to the DLL.
/// The default render backend is Blender (geometry-accurate, consistent).
/// Gemini settings are kept for the optional "style pass" feature.
/// </summary>
public sealed class RenderSettings
{
    /// <summary>"blender" (accurate, default) or "gemini" (concept image).</summary>
    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "blender";

    /// <summary>Override the output folder; empty = beside the DWG.</summary>
    [JsonPropertyName("outputDirectory")]
    public string? OutputDirectory { get; set; }

    [JsonPropertyName("blender")]
    public BlenderSettings Blender { get; set; } = new();

    [JsonPropertyName("gemini")]
    public GeminiSettings Gemini { get; set; } = new();

    public static RenderSettings Load(string pluginDirectory)
    {
        var settings = new RenderSettings();
        var path = Path.Combine(pluginDirectory, "instantrender.config.json");
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<RenderSettings>(File.ReadAllText(path));
                if (loaded is not null) settings = loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Keep defaults; the command reports a clear error if needed.
            }
        }

        // Secret always overridable by environment.
        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) settings.Gemini.ApiKey = envKey;

        return settings;
    }

    public bool UseBlender => !Backend.Equals("gemini", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Blender invocation settings.</summary>
public sealed class BlenderSettings
{
    /// <summary>Path to blender.exe. Empty = auto-detect common install paths.</summary>
    [JsonPropertyName("exePath")]
    public string? ExePath { get; set; }

    /// <summary>"eevee" (fast preview) or "cycles" (high quality).</summary>
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "eevee";

    [JsonPropertyName("samples")]
    public int Samples { get; set; } = 64;

    [JsonPropertyName("resolutionX")]
    public int ResolutionX { get; set; } = 1920;

    [JsonPropertyName("resolutionY")]
    public int ResolutionY { get; set; } = 1080;

    [JsonPropertyName("addCeiling")]
    public bool AddCeiling { get; set; } = false;
}
