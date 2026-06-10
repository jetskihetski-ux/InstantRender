# Instant Render — Setup & Install

## Prerequisites
- **AutoCAD 2025 or 2026** (these run on .NET 8).
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- **Blender 4.x** (free) — https://www.blender.org/download/ (the default render
  backend; no account or key needed).
- *Optional:* a **Gemini API key** (https://aistudio.google.com/apikey) only if
  you switch to the `"gemini"` concept-image backend.

## 1. Build the plugin
From the repo root:

```powershell
# If your AutoCAD isn't in the default path, pass it explicitly:
dotnet build -c Release `
  -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025\"
```

Output: `src\InstantRender.Plugin\bin\Release\net8.0-windows\`
containing `InstantRender.dll` and `scripts\render_scene.py` (copied in).

> The AutoCAD assemblies (`AcMgd`, `AcCoreMgd`, `AcDbMgd`, `AdWindows`,
> `AcWindows`) are referenced from your AutoCAD install and **not** copied to
> output — AutoCAD loads its own. If the build can't find them, fix `AcDir`.

## 2. Point at Blender
The plugin auto-detects `blender.exe` via PATH and the standard install folder
(`C:\Program Files\Blender Foundation\Blender X.Y\`). If yours is elsewhere,
copy `instantrender.config.sample.json` to `instantrender.config.json` next to
`InstantRender.dll` and set `blender.exePath`:

```json
{
  "backend": "blender",
  "blender": { "exePath": "D:\\Apps\\Blender\\blender.exe", "engine": "eevee" }
}
```
Use `"engine": "cycles"` for the high-quality final render (slower);
`"eevee"` for fast previews.

## 3. (Optional) Gemini concept backend
Only if you set `"backend": "gemini"`. Provide a key via the
`GEMINI_API_KEY` environment variable (`setx GEMINI_API_KEY "..."`, then restart
AutoCAD) or `gemini.apiKey` in the config file. Keep that file out of version
control — it holds a secret.

## 4. Load into AutoCAD
**Quick test (per session):**
1. Start AutoCAD, open a drawing.
2. Run `NETLOAD`, browse to `InstantRender.dll`.
3. An **Instant Render** ribbon tab appears. Or type `INSTANTRENDER`.

**Auto-load every session:** create a bundle in
`%APPDATA%\Autodesk\ApplicationPlugins\InstantRender.bundle\` with the DLL +
`scripts\` folder under `Contents\`, and a `PackageContents.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" Name="InstantRender"
                    AppVersion="1.0.0" ProductCode="{PUT-A-GUID-HERE}">
  <CompanyDetails Name="Instant Render" />
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="R25.0" />
    <ComponentEntry AppName="InstantRender"
                    ModuleName="./Contents/InstantRender.dll"
                    LoadOnAutoCADStartup="True" LoadOnCommandInvocation="True">
      <Commands GroupName="InstantRenderCmds">
        <Command Global="INSTANTRENDER" Local="INSTANTRENDER" />
      </Commands>
    </ComponentEntry>
  </Components>
</ApplicationPackage>
```
`SeriesMin="R25.0"` = AutoCAD 2025.

## 5. Run
1. Open `samples\sample-room.dxf` (or your own plan).
2. Click **Instant Render** (or type `INSTANTRENDER`).
3. Select plan objects (or press Enter for the whole drawing).
4. Next to your DWG, an `InstantRender\<timestamp>\` folder is written with:
   - `scene.json` — the parsed plan,
   - `render_TopPerspective.png` (+ `render_LivingRoom.png` etc. when detected),
   and the first render opens automatically.

## Test the renderer without AutoCAD
The Blender script is standalone — point it at any `scene.json`:
```powershell
& "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe" `
  --background --python scripts\render_scene.py -- `
  --scene scene.json --out out --engine eevee
```

## Troubleshooting
- **"blender.exe not found"** — install Blender, add it to PATH, or set
  `blender.exePath` in the config.
- **"No walls or rooms found"** — layers not recognized. Rename to
  `WALL`/`DOOR`/`WINDOW`/`ROOM` or extend the rules / use the manual override in
  [`LayerMapper`](../src/InstantRender.Plugin/Cad/LayerMapper.cs).
- **Blender ran but no image** — check the command-line log tail the plugin
  prints; usually a bad `scene.json` path or an unsupported engine name.
- **Wrong scale** — the drawing's `INSUNITS` is used; unitless drawings are
  assumed millimeters.
- **Build can't find AutoCAD DLLs** — set `-p:AcadDir=...` to your install path.
