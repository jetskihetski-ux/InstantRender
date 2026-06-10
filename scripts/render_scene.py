"""
Instant Render - Blender headless renderer.

Reads a scene.json (the neutral plan model exported by the AutoCAD plugin),
builds an accurate 3D model from it, assigns materials, adds lights and cameras,
and renders one PNG per camera view.

Run (headless):
    blender --background --python render_scene.py -- \
        --scene scene.json --out C:\\path\\to\\out --engine eevee

The geometry is built directly from the plan coordinates, so the same scene.json
always produces the same model -> the render is consistent and matches the plan.
"""

import bpy
import bmesh
import sys
import os
import json
import math
import argparse
from mathutils import Vector


# --------------------------------------------------------------------------- #
# Args
# --------------------------------------------------------------------------- #
def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser(description="Instant Render Blender backend")
    p.add_argument("--scene", required=True, help="Path to scene.json")
    p.add_argument("--out", required=True, help="Output directory for renders")
    p.add_argument("--engine", default="eevee", choices=["eevee", "cycles"])
    p.add_argument("--samples", type=int, default=64)
    p.add_argument("--resx", type=int, default=1920)
    p.add_argument("--resy", type=int, default=1080)
    p.add_argument("--ceiling", action="store_true", help="Add a ceiling slab")
    return p.parse_args(argv)


# --------------------------------------------------------------------------- #
# Scene reset + engine
# --------------------------------------------------------------------------- #
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_engine(args):
    scn = bpy.context.scene
    scn.render.resolution_x = args.resx
    scn.render.resolution_y = args.resy
    scn.render.resolution_percentage = 100
    scn.render.image_settings.file_format = "PNG"

    if args.engine == "cycles":
        scn.render.engine = "CYCLES"
        scn.cycles.samples = args.samples
        try:
            scn.cycles.device = "GPU"
        except Exception:
            pass
    else:
        # EEVEE's engine id changed in Blender 4.2 (EEVEE Next).
        for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
            try:
                scn.render.engine = name
                break
            except TypeError:
                continue


def setup_world():
    world = bpy.data.worlds.new("InstantRenderWorld")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.80, 0.85, 0.92, 1.0)  # soft sky
        bg.inputs[1].default_value = 1.0


# --------------------------------------------------------------------------- #
# Materials
# --------------------------------------------------------------------------- #
def make_material(name, color, roughness=0.8, metallic=0.0, transmission=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
        # Transmission socket renamed across versions.
        for key in ("Transmission Weight", "Transmission"):
            if key in bsdf.inputs:
                bsdf.inputs[key].default_value = transmission
                break
    if transmission > 0:
        try:
            mat.use_screen_refraction = True
        except Exception:
            pass
    return mat


def build_materials():
    return {
        "wall":  make_material("Plaster", (0.92, 0.92, 0.90), roughness=0.9),
        "floor": make_material("LightWood", (0.78, 0.62, 0.42), roughness=0.6),
        "glass": make_material("Glass", (0.6, 0.75, 0.85), roughness=0.05,
                               transmission=1.0),
        "door":  make_material("DarkWood", (0.18, 0.12, 0.08), roughness=0.5),
        "ceil":  make_material("Ceiling", (0.95, 0.95, 0.95), roughness=0.95),
    }


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


# --------------------------------------------------------------------------- #
# Safe accessors (scene.json may be hand-edited / from another tool)
# --------------------------------------------------------------------------- #
def _f(value, default=0.0):
    """Coerce to float, falling back to default on bad/missing values."""
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _xy(pt):
    """Return (x, y) from a {'x':..,'y':..} point, or None if unusable."""
    if not isinstance(pt, dict) or "x" not in pt or "y" not in pt:
        return None
    return _f(pt["x"]), _f(pt["y"])


def _list(scene, key):
    """scene[key] as a list, treating missing/null/non-list as empty."""
    val = scene.get(key)
    return val if isinstance(val, list) else []


# --------------------------------------------------------------------------- #
# Geometry
# --------------------------------------------------------------------------- #
def add_box(sx, sy, ex, ey, thickness, z0, z1, name):
    """A box spanning a centerline segment, from z0 to z1."""
    dx, dy = ex - sx, ey - sy
    length = math.hypot(dx, dy)
    if length < 1e-4:
        return None, 0.0
    angle = math.atan2(dy, dx)
    cx, cy = (sx + ex) / 2.0, (sy + ey) / 2.0
    cz = (z0 + z1) / 2.0
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(cx, cy, cz))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (length, thickness, (z1 - z0))
    obj.rotation_euler = (0.0, 0.0, angle)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj, angle


def add_walls(scene, height, mat):
    """Returns {wall_id: (object, angle)} for opening boolean cuts."""
    walls = {}
    for idx, w in enumerate(_list(scene, "walls")):
        start, end = _xy(w.get("start")), _xy(w.get("end"))
        if start is None or end is None:
            continue  # malformed wall: skip rather than crash
        wid = w.get("id", idx)
        obj, angle = add_box(
            start[0], start[1], end[0], end[1],
            _f(w.get("thickness", 0.2), 0.2), 0.0, height, f"Wall_{wid}")
        if obj:
            assign(obj, mat)
            walls[wid] = (obj, angle)
    return walls


def cut_opening(wall_obj, wall_angle, opening):
    """Boolean-difference a door/window void out of its host wall."""
    center = _xy(opening.get("center"))
    if center is None:
        return
    cx, cy = center
    w = _f(opening.get("width", 0.9), 0.9)
    sill = _f(opening.get("sillHeight", 0.0), 0.0)
    h = _f(opening.get("height", 2.1), 2.1)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(cx, cy, sill + h / 2.0))
    cutter = bpy.context.active_object
    cutter.scale = (w, 5.0, h)            # 5 m deep: pierces any wall thickness
    cutter.rotation_euler = (0.0, 0.0, wall_angle)
    bpy.ops.object.transform_apply(rotation=True, scale=True)

    mod = wall_obj.modifiers.new("Opening", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    try:
        mod.solver = "EXACT"
    except Exception:
        pass
    bpy.context.view_layer.objects.active = wall_obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def add_pane(opening, wall_angle, mat, depth, name):
    """A thin filler (glass for windows, slab for doors) inside an opening."""
    center = _xy(opening.get("center"))
    if center is None:
        return
    cx, cy = center
    w = _f(opening.get("width", 0.9), 0.9)
    sill = _f(opening.get("sillHeight", 0.0), 0.0)
    h = _f(opening.get("height", 2.1), 2.1)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(cx, cy, sill + h / 2.0))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (w * 0.98, depth, h * 0.98)
    obj.rotation_euler = (0.0, 0.0, wall_angle)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    assign(obj, mat)


def make_slab(outline, mat, z, thickness, name):
    """A floor/ceiling slab from a closed polygon outline."""
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bm = bmesh.new()
    pts = [_xy(p) for p in outline]
    verts = [bm.verts.new((p[0], p[1], z)) for p in pts if p is not None]
    if len(verts) >= 3:
        try:
            bm.faces.new(verts)
        except ValueError:
            pass  # degenerate / duplicate verts
    bm.to_mesh(mesh)
    bm.free()
    solid = obj.modifiers.new("Solid", "SOLIDIFY")
    solid.thickness = thickness
    solid.offset = -1 if z <= 0.01 else 1
    assign(obj, mat)
    return obj


def add_floor_and_ceiling(scene, height, mats, add_ceiling):
    rooms = _list(scene, "rooms")
    if rooms:
        for idx, r in enumerate(rooms):
            outline = r.get("outline") or []
            if len(outline) >= 3:
                rid = r.get("id", idx)
                make_slab(outline, mats["floor"], 0.0, 0.1, f"Floor_{rid}")
                if add_ceiling:
                    make_slab(outline, mats["ceil"], height, 0.1, f"Ceil_{rid}")
    else:
        b = scene.get("bounds")
        if isinstance(b, dict) and _xy(b.get("min")) and _xy(b.get("max")):
            rect = [
                {"x": b["min"]["x"], "y": b["min"]["y"]},
                {"x": b["max"]["x"], "y": b["min"]["y"]},
                {"x": b["max"]["x"], "y": b["max"]["y"]},
                {"x": b["min"]["x"], "y": b["max"]["y"]},
            ]
            make_slab(rect, mats["floor"], 0.0, 0.1, "Floor")
            if add_ceiling:
                make_slab(rect, mats["ceil"], height, 0.1, "Ceiling")


# --------------------------------------------------------------------------- #
# Lights + cameras
# --------------------------------------------------------------------------- #
def plan_center_size(scene):
    b = scene.get("bounds")
    lo = _xy(b.get("min")) if isinstance(b, dict) else None
    hi = _xy(b.get("max")) if isinstance(b, dict) else None
    if lo is None or hi is None:
        return Vector((0, 0, 0)), 10.0
    cx, cy = (lo[0] + hi[0]) / 2.0, (lo[1] + hi[1]) / 2.0
    size = max(hi[0] - lo[0], hi[1] - lo[1], 1.0)
    return Vector((cx, cy, 0.0)), size


def add_lights(center, size):
    bpy.ops.object.light_add(type="SUN", location=(center.x, center.y, size * 2))
    sun = bpy.context.active_object
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(40))

    bpy.ops.object.light_add(type="AREA",
                             location=(center.x, center.y, size * 0.9))
    area = bpy.context.active_object
    area.data.size = size
    area.data.energy = size * size * 25.0


def look_at(cam, target):
    direction = (target - cam.location)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def add_camera(name, location, target):
    cam_data = bpy.data.cameras.new(name)
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = Vector(location)
    look_at(cam, Vector(target))
    return cam


def build_cameras(scene, height):
    """Always a dollhouse view; interior views for living/bed rooms found."""
    center, size = plan_center_size(scene)
    cams = []

    # Dollhouse 3/4 aerial.
    dist = size * 1.3
    cams.append(("TopPerspective", add_camera(
        "TopPerspective",
        (center.x - dist, center.y - dist, dist * 0.9),
        (center.x, center.y, height * 0.4))))

    # Interior views.
    def centroid(pts):
        x = sum(p[0] for p in pts) / len(pts)
        y = sum(p[1] for p in pts) / len(pts)
        return Vector((x, y, 1.5))

    for r in _list(scene, "rooms"):
        name = (r.get("name") or "").lower()
        outline = [p for p in (_xy(p) for p in (r.get("outline") or [])) if p]
        if len(outline) < 3:
            continue
        loc = centroid(outline)
        if any(k in name for k in ("living", "majlis", "lounge", "salon")):
            cams.append(("LivingRoom", add_camera(
                "LivingRoom", loc, (center.x, center.y, 1.4))))
        elif any(k in name for k in ("bed", "master")):
            cams.append(("Bedroom", add_camera(
                "Bedroom", loc, (center.x, center.y, 1.4))))
    return cams


# --------------------------------------------------------------------------- #
# Render
# --------------------------------------------------------------------------- #
def render_all(cams, out_dir):
    scn = bpy.context.scene
    os.makedirs(out_dir, exist_ok=True)
    saved = []
    for label, cam in cams:
        scn.camera = cam
        path = os.path.join(out_dir, f"render_{label}.png")
        scn.render.filepath = path
        print(f"[Instant Render] Rendering {label} -> {path}")
        bpy.ops.render.render(write_still=True)
        saved.append(path)
    return saved


def main():
    args = parse_args()
    with open(args.scene, "r", encoding="utf-8") as f:
        scene = json.load(f)

    height = _f(scene.get("wallHeight", 3.0), 3.0)

    reset_scene()
    setup_engine(args)
    setup_world()
    mats = build_materials()

    walls = add_walls(scene, height, mats["wall"])

    # Cut and fill openings.
    for idx, d in enumerate(_list(scene, "doors")):
        host = walls.get(d.get("hostWallId"))
        if host:
            cut_opening(host[0], host[1], d)
            add_pane(d, host[1], mats["door"], 0.05, f"Door_{d.get('id', idx)}")
    for idx, w in enumerate(_list(scene, "windows")):
        host = walls.get(w.get("hostWallId"))
        if host:
            cut_opening(host[0], host[1], w)
            add_pane(w, host[1], mats["glass"], 0.03, f"Win_{w.get('id', idx)}")

    add_floor_and_ceiling(scene, height, mats, args.ceiling)

    center, size = plan_center_size(scene)
    add_lights(center, size)
    cams = build_cameras(scene, height)

    saved = render_all(cams, args.out)
    print("[Instant Render] DONE: " + ";".join(saved))


if __name__ == "__main__":
    main()
