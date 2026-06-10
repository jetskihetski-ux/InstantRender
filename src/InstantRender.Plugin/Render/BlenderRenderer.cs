using System.Diagnostics;
using System.Text;

namespace InstantRender.Render;

/// <summary>
/// Launches Blender in headless mode to render a scene.json via render_scene.py.
/// Geometry is built from the plan coordinates, so renders are deterministic
/// and faithful to the floor plan.
/// </summary>
public sealed class BlenderRenderer
{
    private readonly BlenderSettings _settings;
    private readonly string _scriptPath;

    public BlenderRenderer(BlenderSettings settings, string pluginDirectory)
    {
        _settings = settings;
        // render_scene.py is copied next to the DLL at build time (see csproj).
        _scriptPath = Path.Combine(pluginDirectory, "scripts", "render_scene.py");
    }

    /// <summary>
    /// Render <paramref name="scenePath"/> into <paramref name="outDir"/>.
    /// Returns (exitCode, log). Throws if Blender can't be located.
    /// </summary>
    public (int ExitCode, string Log) Render(string scenePath, string outDir)
    {
        var blender = ResolveBlenderPath()
            ?? throw new FileNotFoundException(
                "blender.exe not found. Install Blender and set blender.exePath " +
                "in instantrender.config.json, or add Blender to PATH.");

        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException($"Render script missing: {_scriptPath}");

        // blender --background --python render_scene.py -- <script args>
        var args = new StringBuilder()
            .Append("--background --python \"").Append(_scriptPath).Append("\" -- ")
            .Append("--scene \"").Append(scenePath).Append("\" ")
            .Append("--out \"").Append(outDir).Append("\" ")
            .Append("--engine ").Append(_settings.Engine).Append(' ')
            .Append("--samples ").Append(_settings.Samples).Append(' ')
            .Append("--resx ").Append(_settings.ResolutionX).Append(' ')
            .Append("--resy ").Append(_settings.ResolutionY);
        if (_settings.AddCeiling) args.Append(" --ceiling");

        var psi = new ProcessStartInfo
        {
            FileName = blender,
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var log = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return (proc.ExitCode, log.ToString());
    }

    /// <summary>Configured path, else PATH, else common install locations.</summary>
    private string? ResolveBlenderPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ExePath) && File.Exists(_settings.ExePath))
            return _settings.ExePath;

        // On PATH?
        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir.Trim(), "blender.exe"))
            .FirstOrDefault(File.Exists);
        if (onPath != null) return onPath;

        // Standard Windows install: C:\Program Files\Blender Foundation\Blender X.Y\blender.exe
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Blender Foundation");
        if (Directory.Exists(root))
        {
            var exe = Directory.EnumerateDirectories(root, "Blender*")
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "blender.exe"))
                .FirstOrDefault(File.Exists);
            if (exe != null) return exe;
        }
        return null;
    }
}
