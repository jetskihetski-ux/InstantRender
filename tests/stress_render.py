"""
Stress test for scripts/render_scene.py WITHOUT Blender.

Blender's Python API (bpy/bmesh/mathutils) is mocked so we exercise the pure
logic of render_scene.py: scene parsing, geometry loops, opening->wall lookup,
floor/camera math. We fire pathological scene.json inputs at main() and report
any that raise. Run:  python tests/stress_render.py
"""
import sys
import os
import json
import tempfile
import importlib.util
from unittest.mock import MagicMock

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "scripts", "render_scene.py")


def install_blender_mocks():
    """Put fake bpy/bmesh/mathutils into sys.modules before import."""
    bpy = MagicMock(name="bpy")
    # bmesh.new() must yield an object whose .verts.new returns a vert and
    # whose .faces.new accepts a list (and can raise ValueError on demand).
    bmesh = MagicMock(name="bmesh")
    mathutils = MagicMock(name="mathutils")
    mathutils.Vector = MagicMock(name="Vector")

    sys.modules["bpy"] = bpy
    sys.modules["bmesh"] = bmesh
    sys.modules["mathutils"] = mathutils
    return bpy


def load_render_scene():
    spec = importlib.util.spec_from_file_location("render_scene", SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def run_case(mod, name, scene):
    out = tempfile.mkdtemp(prefix="ir_stress_")
    scene_path = os.path.join(out, "scene.json")
    with open(scene_path, "w", encoding="utf-8") as f:
        json.dump(scene, f)
    sys.argv = ["blender", "--background", "--python", "render_scene.py",
                "--", "--scene", scene_path, "--out", os.path.join(out, "r"),
                "--engine", "eevee"]
    try:
        mod.main()
        return (name, "OK", "")
    except Exception as e:  # noqa: BLE001 - we want to catch everything
        return (name, "FAIL", f"{type(e).__name__}: {e}")


# Reusable fragments
W = lambda i, sx, sy, ex, ey: {
    "id": i, "start": {"x": sx, "y": sy}, "end": {"x": ex, "y": ey},
    "thickness": 0.2}
ROOM = lambda i, name: {
    "id": i, "name": name, "area": 20.0,
    "outline": [{"x": 0, "y": 0}, {"x": 5, "y": 0},
                {"x": 5, "y": 4}, {"x": 0, "y": 4}]}
BOUNDS = {"min": {"x": 0, "y": 0}, "max": {"x": 5, "y": 4}}


def cases():
    base = {
        "wallHeight": 3.0, "bounds": BOUNDS,
        "walls": [W(1, 0, 0, 5, 0), W(2, 5, 0, 5, 4),
                  W(3, 5, 4, 0, 4), W(4, 0, 4, 0, 0)],
        "doors": [{"id": 1, "kind": "Door", "center": {"x": 1, "y": 0},
                   "width": 0.9, "sillHeight": 0.0, "height": 2.1,
                   "hostWallId": 1}],
        "windows": [{"id": 2, "kind": "Window", "center": {"x": 2.5, "y": 4},
                     "width": 1.2, "sillHeight": 0.9, "height": 1.2,
                     "hostWallId": 3}],
        "rooms": [ROOM(1, "LIVING")],
    }
    yield ("happy_path", base)
    yield ("empty_object", {})
    yield ("no_walls_no_rooms", {"walls": [], "rooms": [], "bounds": BOUNDS})
    yield ("zero_length_wall", {"walls": [W(1, 2, 2, 2, 2)], "bounds": BOUNDS})
    yield ("door_orphan_hostwall",
           {"walls": [W(1, 0, 0, 5, 0)],
            "doors": [{"id": 1, "center": {"x": 1, "y": 0}, "hostWallId": 99}]})
    yield ("door_no_hostwall_key",
           {"walls": [W(1, 0, 0, 5, 0)],
            "doors": [{"id": 1, "center": {"x": 1, "y": 0}}]})
    yield ("room_outline_2pts",
           {"rooms": [{"id": 1, "name": "X",
                       "outline": [{"x": 0, "y": 0}, {"x": 1, "y": 1}]}]})
    yield ("no_bounds_with_rooms", {"rooms": [ROOM(1, "BED 1")]})
    yield ("bedroom_and_living",
           {**base, "rooms": [ROOM(1, "LIVING"), ROOM(2, "MASTER BEDROOM")]})
    yield ("room_null_name",
           {"rooms": [{"id": 1, "name": None,
                       "outline": ROOM(1, "x")["outline"]}], "bounds": BOUNDS})
    yield ("many_walls",
           {"walls": [W(i, i, 0, i, 3) for i in range(1, 2001)],
            "bounds": BOUNDS})
    # --- nastier / malformed inputs ---
    yield ("null_collections",
           {"walls": None, "doors": None, "windows": None, "rooms": None})
    yield ("wall_missing_id",
           {"walls": [{"start": {"x": 0, "y": 0}, "end": {"x": 5, "y": 0}}]})
    yield ("wall_missing_endpoints", {"walls": [{"id": 1}]})
    yield ("room_missing_id",
           {"rooms": [{"name": "x", "outline": ROOM(1, "x")["outline"]}]})
    yield ("wallheight_string", {**base, "wallHeight": "tall"})
    yield ("huge_coords",
           {"walls": [W(1, 0, 0, 1e9, 1e9)],
            "bounds": {"min": {"x": 0, "y": 0},
                       "max": {"x": 1e9, "y": 1e9}}})


def main():
    install_blender_mocks()
    mod = load_render_scene()
    results = [run_case(mod, name, scene) for name, scene in cases()]

    width = max(len(r[0]) for r in results)
    fails = 0
    print("\n=== render_scene.py stress test ===")
    for name, status, detail in results:
        mark = "ok " if status == "OK" else "XXX"
        print(f"[{mark}] {name.ljust(width)}  {detail}")
        if status != "OK":
            fails += 1
    print(f"\n{len(results) - fails}/{len(results)} passed, {fails} failed.")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
