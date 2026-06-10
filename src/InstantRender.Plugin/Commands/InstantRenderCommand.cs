using System.Diagnostics;
using System.Text.Json;
using Autodesk.AutoCAD.Runtime;
using InstantRender.Cad;
using InstantRender.Gemini;
using InstantRender.Infrastructure;
using InstantRender.Model;
using InstantRender.Prompt;
using InstantRender.Render;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(InstantRender.Commands.InstantRenderCommand))]

namespace InstantRender.Commands;

/// <summary>
/// The INSTANTRENDER command: scan the floor plan, export scene.json, then
/// render. Default backend is Blender (geometry-accurate, consistent); Gemini
/// is an optional concept-image backend.
/// </summary>
public sealed class InstantRenderCommand
{
    [CommandMethod("INSTANTRENDER", CommandFlags.Modal)]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        try
        {
            Log.Info("Scanning floor plan geometry...");
            var layers = new LayerMapper();
            var raw = new GeometryReader(doc.Database, layers).Read(doc);
            var model = new PlanAnalyzer().Build(raw, doc.Name);

            if (model.IsEmpty)
            {
                Log.Warn("No walls or rooms found. Check wall layers (WALL / A-WALL " +
                         "etc.) or map layers manually.");
                return;
            }

            Log.Info($"Detected {model.Walls.Count} walls, {model.Rooms.Count} rooms, " +
                     $"{model.Doors.Count} doors, {model.Windows.Count} windows.");

            var pluginDir = Path.GetDirectoryName(typeof(InstantRenderCommand).Assembly.Location)!;
            var settings = RenderSettings.Load(pluginDir);
            var outDir = ResolveOutputDir(settings, doc.Name);
            Directory.CreateDirectory(outDir);

            // Export the neutral, documented scene model.
            var scenePath = Path.Combine(outDir, "scene.json");
            File.WriteAllText(scenePath, JsonSerializer.Serialize(
                model, new JsonSerializerOptions { WriteIndented = true }));
            Log.Info($"Scene exported: {scenePath}");

            string? firstImage = settings.UseBlender
                ? RenderWithBlender(settings, pluginDir, scenePath, outDir)
                : RenderWithGemini(settings, model, outDir);

            if (firstImage is not null)
                OpenInViewer(firstImage);
            else
                Log.Warn("No render was produced. See messages above.");
        }
        catch (System.Exception ex)
        {
            Log.Error(ex.Message);
        }
    }

    // ---- Blender backend (default, accurate) ------------------------------
    private static string? RenderWithBlender(
        RenderSettings settings, string pluginDir, string scenePath, string outDir)
    {
        Log.Info($"Building 3D model and rendering in Blender ({settings.Blender.Engine})...");
        try
        {
            var renderer = new BlenderRenderer(settings.Blender, pluginDir);
            var (exitCode, log) = renderer.Render(scenePath, outDir);

            if (exitCode != 0)
            {
                Log.Error($"Blender exited with code {exitCode}. Tail of log:");
                Log.Error(Tail(log, 600));
                return null;
            }

            // render_scene.py writes render_<View>.png files.
            var images = Directory.GetFiles(outDir, "render_*.png");
            if (images.Length == 0)
            {
                Log.Warn("Blender finished but produced no images. Log tail:");
                Log.Warn(Tail(log, 600));
                return null;
            }
            foreach (var img in images) Log.Info($"Saved render: {img}");
            return images[0];
        }
        catch (FileNotFoundException ex)
        {
            Log.Error(ex.Message);
            return null;
        }
    }

    // ---- Gemini backend (optional concept image) --------------------------
    private static string? RenderWithGemini(
        RenderSettings settings, PlanModel model, string outDir)
    {
        if (!settings.Gemini.HasApiKey)
        {
            Log.Error("Gemini backend selected but no API key. Set GEMINI_API_KEY " +
                      "or add it to instantrender.config.json.");
            return null;
        }

        var builder = new SuperPromptBuilder();
        string? first = null;
        using var client = new GeminiClient(settings.Gemini);
        foreach (var view in builder.SelectViews(model))
        {
            var prompt = builder.Build(model, view);
            File.WriteAllText(Path.Combine(outDir, $"prompt_{view}.txt"), prompt);
            Log.Info($"Rendering '{view}' concept image with Gemini...");
            var imagePath = Path.Combine(outDir, $"render_{view}.png");
            try
            {
                client.GenerateImageAsync(prompt, imagePath).GetAwaiter().GetResult();
                Log.Info($"Saved render: {imagePath}");
                first ??= imagePath;
            }
            catch (System.Exception ex)
            {
                Log.Error($"Gemini render failed for '{view}': {ex.Message}");
            }
        }
        return first;
    }

    private static string ResolveOutputDir(RenderSettings settings, string docPath)
    {
        if (!string.IsNullOrWhiteSpace(settings.OutputDirectory))
            return settings.OutputDirectory!;

        var baseDir = string.IsNullOrWhiteSpace(docPath)
            ? Path.Combine(Path.GetTempPath(), "InstantRender")
            : Path.Combine(Path.GetDirectoryName(docPath)!, "InstantRender");
        return Path.Combine(baseDir, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    }

    private static void OpenInViewer(string imagePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(imagePath) { UseShellExecute = true });
            Log.Info("Opened render in the default image viewer.");
        }
        catch (System.Exception ex)
        {
            Log.Warn($"Could not auto-open the image: {ex.Message}");
        }
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}
