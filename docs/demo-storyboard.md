# Instant Render — Demo Video Storyboard

A ~90-second screen recording. Record with the AutoCAD command line visible.

| # | Shot | On screen | Voiceover / caption |
|---|------|-----------|---------------------|
| 1 | Open plan | AutoCAD with `sample-room.dxf` open (a labelled floor plan). | "Here's a 2D floor plan in AutoCAD." |
| 2 | The button | Hover the **Instant Render** ribbon tab → button. | "One toolbar button: Instant Render." |
| 3 | Click + select | Click the button; window-select the plan; press Enter. | "Click it, select the plan." |
| 4 | Command output | Command line prints: walls/rooms/doors/windows detected, "Scene exported", "Building 3D model and rendering in Blender...". | "It detects walls, rooms, doors and windows, and builds a real 3D model." |
| 5 | Render appears | The default image viewer pops open with the Blender render (dollhouse 3/4 view) that matches the plan. | "Blender renders the actual model — accurate, every time." |
| 6 | Folder | Show the `InstantRender\<timestamp>\` folder: `scene.json`, `render_*.png`. | "Scene and renders are saved next to your drawing." |
| 7 | Extra views | Flip through `render_LivingRoom.png` if produced. | "Multiple camera views from the same plan." |

## Recording tips
- Use `"engine": "eevee"` for the demo — renders in seconds. Switch to
  `"cycles"` only for a glamour beauty-shot at the end.
- If Blender startup is slow on camera, pre-render once, delete the output,
  then record — or cut to the saved PNG to keep it tight.
- 1080p screen capture; the renders are already 1920×1080.

## Bonus shot (optional)
Run the Blender script standalone on a `scene.json` to show the 3D model
building in the viewport (drop `--background`), proving the geometry is real.
