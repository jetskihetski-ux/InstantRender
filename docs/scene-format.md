# Instant Render — Scene Format (`scene.json`)

The plugin reads the DWG/DXF, normalizes everything to **meters**, and writes a
neutral scene description to `scene.json`. This file is:

1. the documented, renderer-agnostic export of the plan, and
2. the source the **super prompt** is generated from (for Gemini), and
3. the format a future Blender/glTF exporter would consume.

It maps 1:1 to the C# types in [`Model/PlanModel.cs`](../src/InstantRender.Plugin/Model/PlanModel.cs).

## Top-level object

| Field           | Type        | Notes                                              |
|-----------------|-------------|----------------------------------------------------|
| `schemaVersion` | string      | `"1.0"`.                                            |
| `sourceFile`    | string      | Originating DWG/DXF path.                          |
| `wallHeight`    | number (m)  | Storey height walls are extruded to. Default `3.0`.|
| `walls`         | Wall[]      | Wall centerline segments.                          |
| `doors`         | Opening[]   | Door openings.                                     |
| `windows`       | Opening[]   | Window openings.                                   |
| `rooms`         | Room[]      | Closed room boundaries.                            |
| `bounds`        | BoundingBox | Overall plan extents (for camera framing).         |

### Point2
```json
{ "x": 0.0, "y": 0.0 }
```
2D plan coordinate in meters. `x`/`y` are the plan axes; `z` is height, added by
the renderer (0 = floor, `wallHeight` = ceiling).

### Wall
```json
{ "id": 1, "start": {"x":0,"y":0}, "end": {"x":5,"y":0}, "thickness": 0.2 }
```
A centerline from `start` to `end`. The renderer extrudes it from `z=0` to
`z=wallHeight` with the given `thickness` (meters).

### Opening (door or window)
```json
{
  "id": 1, "kind": "Door",
  "center": {"x":1.0,"y":0.0},
  "width": 0.9, "sillHeight": 0.0, "height": 2.1,
  "hostWallId": 1
}
```
- `kind`: `"Door"` or `"Window"`.
- `center`: opening center on the plan (meters).
- `width`: span along the wall (meters).
- `sillHeight`: bottom above floor — `0` for doors, `0.9` for windows (default).
- `height`: `2.1` doors, `1.2` windows (default).
- `hostWallId`: id of the nearest wall, or `null` if unresolved.

### Room
```json
{
  "id": 1, "name": "LIVING",
  "outline": [ {"x":0,"y":0}, {"x":5,"y":0}, {"x":5,"y":4}, {"x":0,"y":4} ],
  "area": 20.0
}
```
- `name`: nearest text label, or `null`.
- `outline`: closed polygon (meters).
- `area`: floor area (m²).

### BoundingBox
```json
{ "min": {"x":0,"y":0}, "max": {"x":5,"y":4} }
```

## MVP dimensional assumptions
Tunable in [`AnalyzerOptions`](../src/InstantRender.Plugin/Cad/PlanAnalyzer.cs):
wall height 3.0 m, wall thickness 0.2 m, door height 2.1 m, window sill 0.9 m,
window height 1.2 m.

## From scene → render
The default backend, [`scripts/render_scene.py`](../scripts/render_scene.py),
reads this file in Blender and builds the 3D model directly from the
coordinates: walls extruded `0 → wallHeight`, openings boolean-cut into their
host walls, floor (and optional ceiling) slabs from room outlines, then
materials, lights, and cameras. Because geometry comes straight from these
numbers, the render is deterministic and matches the plan.

The optional Gemini backend instead feeds this model to
[`SuperPromptBuilder`](../src/InstantRender.Plugin/Prompt/SuperPromptBuilder.cs)
to produce a text "super prompt" and a *concept* image (not geometrically
exact).
