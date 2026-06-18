# Instant Render

One-click AutoCAD plugin that turns a 2D architectural floor plan into a
**consistent, accurate 3D render**. You click **Instant Render**, the plugin
scans the plan, builds a real 3D model from the geometry, and renders it with
**Blender** (free, headless) — then opens the image.

> **Why Blender, not an AI image generator?**
> The render must faithfully match the plan, every time. Text/image AI models
> (e.g. Gemini) *repaint* the scene and give a different, approximate result on
> each run — they can't reproduce your exact walls. So the render path builds
> actual geometry from the scan and renders it: **same plan → same model → same
> render**. Gemini remains available as an optional *concept-image* backend for
> quick, non-exact previews (`"backend": "gemini"`).

## How it works

```
AutoCAD DWG/DXF
   │  INSTANTRENDER command  (ribbon button)
   ▼
GeometryReader   ── reads lines/polylines/arcs/blocks/text, converts to meters
   ▼
PlanAnalyzer     ── builds walls, rooms (named from labels), doors, windows
   ▼
scene.json       ── neutral, documented scene model   (docs/scene-format.md)
   ▼
render_scene.py  ── Blender builds the 3D model:
                      • walls extruded to 3 m
                      • door/window openings cut (boolean) + glass/door fillers
                      • floor slab (+ optional ceiling)
                      • materials, sun + area lights, dollhouse + interior cameras
   ▼
render_<View>.png  (1920×1080)  →  opens automatically
```

## Project layout

| Path | What |
|------|------|
| [`src/InstantRender.Plugin/`](src/InstantRender.Plugin/) | The C# AutoCAD .NET 8 plugin. |
| ├─ [`Commands/InstantRenderCommand.cs`](src/InstantRender.Plugin/Commands/InstantRenderCommand.cs) | `INSTANTRENDER` — orchestrates scan → export → render. |
| ├─ [`Infrastructure/PluginApp.cs`](src/InstantRender.Plugin/Infrastructure/PluginApp.cs) | Entry point + ribbon button. |
| ├─ [`Cad/`](src/InstantRender.Plugin/Cad/) | Geometry reading, layer mapping, plan analysis. |
| ├─ [`Model/PlanModel.cs`](src/InstantRender.Plugin/Model/PlanModel.cs) | The neutral scene data model. |
| ├─ [`Render/`](src/InstantRender.Plugin/Render/) | Settings + Blender launcher (`BlenderRenderer`). |
| ├─ [`Prompt/SuperPromptBuilder.cs`](src/InstantRender.Plugin/Prompt/SuperPromptBuilder.cs) | Gemini super prompt (optional backend). |
| └─ [`Gemini/`](src/InstantRender.Plugin/Gemini/) | Gemini REST client (optional backend). |
| [`scripts/render_scene.py`](scripts/render_scene.py) | **Blender renderer** — builds + renders the 3D model. |
| [`samples/sample-room.dxf`](samples/sample-room.dxf) | Test plan: one 5 m × 4 m labelled room. |
| [`docs/scene-format.md`](docs/scene-format.md) | The `scene.json` export format. |
| [`docs/setup.md`](docs/setup.md) | Build, configure, install, run. |
| [`docs/demo-storyboard.md`](docs/demo-storyboard.md) | Script for the demo video. |

## Quick start
1. Install **AutoCAD 2025/2026**, the **.NET 8 SDK**, and **Blender 4.x** (free).
2. `dotnet build -c Release` (see [docs/setup.md](docs/setup.md) for the AutoCAD path flag).
3. `NETLOAD` the DLL, open `samples/sample-room.dxf`, click **Instant Render**.

No API keys needed for the default (Blender) path. Full details: **[docs/setup.md](docs/setup.md)**.

## Run it on another PC (the easy way)
Your friend's PC needs **AutoCAD 2025/2026**, the **.NET 8 SDK**, and **Blender 4.x**.

1. **Download:** on the repo page click **Code ▸ Download ZIP** (or
   `git clone https://github.com/jetskihetski-ux/InstantRender.git`) and unzip.
2. **Install Blender 4.x** from https://www.blender.org/download/ (default location).
3. Open **PowerShell** in the unzipped folder and run:
   ```powershell
   .\build-bundle.ps1 -Install
   ```
   This builds the plugin and installs it for AutoCAD automatically.
4. **Restart AutoCAD** → an **Instant Render** ribbon tab appears. Open a floor
   plan, click the button (or type `INSTANTRENDER`).

If `dotnet` is missing, install the .NET 8 SDK:
https://dotnet.microsoft.com/download/dotnet/8.0
If Blender isn't in the default folder, see [docs/setup.md](docs/setup.md) step 2.

## Supported input
- Entities: lines, polylines, arcs, block references, text/mtext.
- Layers (case-insensitive, substring match): `WALL`/`WALLS`/`A-WALL`,
  `DOOR`/`A-DOOR`, `WINDOW`/`A-GLAZ`, `ROOM`/`A-AREA`, `TEXT`/`ANNO`.
  Non-standard names can be remapped via
  [`LayerMapper.Override`](src/InstantRender.Plugin/Cad/LayerMapper.cs).
- Units: read from the drawing's `INSUNITS` and converted to meters.

## MVP scope (v1)
Simple rectangular rooms; wall height 3 m, door 2.1 m, window sill 0.9 m /
height 1.2 m; basic materials (white plaster / light wood / glass / dark door);
no furniture. Tunable in
[`AnalyzerOptions`](src/InstantRender.Plugin/Cad/PlanAnalyzer.cs) and the Blender
materials in [`render_scene.py`](scripts/render_scene.py).

## Status
Full project scaffold. The CAD-reading code targets the AutoCAD .NET API and
**only compiles/runs inside AutoCAD 2025/2026**. The analysis layer is plain
.NET and unit-testable. The Blender script runs standalone:
```
blender --background --python scripts/render_scene.py -- --scene scene.json --out out
```
so you can test rendering from a `scene.json` without AutoCAD.

## Roadmap
- Manual layer-mapping dialog (WPF) wired to `LayerMapper`.
- Better wall solids from double-line walls; auto wall joins/cleanup.
- Gemini **style pass**: post-process the accurate Blender render for polish
  (layout stays exact, only look varies).
- AI room detection, auto-furnishing, style presets, cloud rendering,
  Revit/SketchUp export.


  still work in progress
