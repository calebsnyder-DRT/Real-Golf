"""Puttshack Pro asset helpers for Blender 5.x (procedural, meter scale, base-center origins)."""
import math
import os
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector, noise

ROOT = "/Users/calebsnyder/Real-Golf/ArtSource"
BLEND_DIR = os.path.join(ROOT, "Blender")
EXPORT_DIR = os.path.join(ROOT, "Exports")
REVIEW_DIR = os.path.join(ROOT, "Review")
TEX_DIR = os.path.join(BLEND_DIR, "textures")
SCENE = "PP_Assets"
STUDIO = "PP_Studio"

CREAM = "#F2EDDF"
INK = "#192020"
CYAN = "#00CBE7"


def srgb(h, a=1.0):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (a,)


def T(loc=(0, 0, 0), rot=(0, 0, 0), scl=(1, 1, 1)):
    if not hasattr(scl, "__len__"):
        scl = (scl, scl, scl)
    return Matrix.LocRotScale(Vector(loc), Euler(rot).to_quaternion(), Vector(scl))


def rad(d):
    return math.radians(d)


# ---------------------------------------------------------------- scene / collections
def get_scene():
    sc = bpy.data.scenes.get(SCENE)
    if sc is None:
        sc = bpy.data.scenes.new(SCENE)
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    return sc


def activate():
    sc = get_scene()
    if bpy.context.window.scene != sc:
        bpy.context.window.scene = sc
    return sc


def _remove_obj(o):
    data = o.data
    bpy.data.objects.remove(o, do_unlink=True)
    if data is not None and data.users == 0:
        if isinstance(data, bpy.types.Mesh):
            bpy.data.meshes.remove(data)
        elif isinstance(data, bpy.types.Curve):
            bpy.data.curves.remove(data)


def asset_coll(name):
    sc = get_scene()
    c = bpy.data.collections.get(name)
    if c is not None:
        for o in list(c.objects):
            _remove_obj(o)
    else:
        c = bpy.data.collections.new(name)
    if c.name not in sc.collection.children:
        sc.collection.children.link(c)
    return c


def asset_colls():
    sc = get_scene()
    return [c for c in sc.collection.children if c.name.startswith("PP_") and c.name != STUDIO]


def show_only(names, viewport=True):
    sc = get_scene()
    vl = sc.view_layers[0]
    for lc in vl.layer_collection.children:
        vis = lc.name in names or lc.name == STUDIO
        lc.collection.hide_render = not vis
        if viewport:
            lc.hide_viewport = not vis
            lc.exclude = False


# ---------------------------------------------------------------- materials
def mat(name, color, rough=0.5, metal=0.0, **inputs):
    full = "PP_" + name
    m = bpy.data.materials.get(full) or bpy.data.materials.new(full)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    b = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if b is None:
        b = nt.nodes.new("ShaderNodeBsdfPrincipled")
        out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None) or nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(b.outputs[0], out.inputs[0])
    col = srgb(color) if isinstance(color, str) else tuple(color)
    b.inputs["Base Color"].default_value = col
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    for k, v in inputs.items():
        b.inputs[k].default_value = v
    m.diffuse_color = col
    m.roughness = rough
    m.metallic = metal
    return m


def palette():
    return dict(
        cream=mat("Cream", CREAM, 0.55),
        ink=mat("Ink", INK, 0.6),
        cyan=mat("Cyan", CYAN, 0.45),
        white=mat("WhitePlastic", "#F4F4F0", 0.35),
        chrome=mat("Chrome", "#C9CDD2", 0.18, 1.0),
        steel=mat("BrushedSteel", "#9CA2A8", 0.35, 1.0),
        darksteel=mat("DarkSteel", "#3A3F44", 0.35, 1.0),
        rubber=mat("Rubber", "#1C1E1F", 0.85),
        wood=mat("Timber", "#8A5A35", 0.7),
        darkwood=mat("DarkTimber", "#5A3A22", 0.75),
        bark=mat("Bark", "#4E3B2C", 0.9),
        leaf_oak=mat("LeafOak", "#3F6B2A", 0.75),
        leaf_oak2=mat("LeafOakLight", "#5E8A33", 0.75),
        leaf_pine=mat("NeedlePine", "#2C4F2C", 0.8),
        leaf_shrub=mat("LeafShrub", "#4B7A34", 0.75),
        rock=mat("Rock", "#8C877D", 0.85),
        rock2=mat("RockDark", "#6E6A62", 0.9),
        stone=mat("StoneWall", "#A79C88", 0.85),
        plaster=mat("Plaster", CREAM, 0.8),
        roof=mat("RoofSlate", "#2E3436", 0.7),
        glass=mat("Glass", "#2F4A57", 0.08, 0.0),
        grass=mat("Turf", "#4F8A34", 0.9),
        soil=mat("Soil", "#3B2E24", 0.95),
    )


# ---------------------------------------------------------------- builder
def _tb():
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    return bm


def p_cyl(r1, r2, h, seg=16, caps=True):
    bm = _tb()
    r = bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=h, calc_uvs=True)
    bmesh.ops.translate(bm, vec=(0, 0, h / 2), verts=r["verts"])
    return bm


def p_tube(profile, seg=16, caps=True):
    """Lathe: profile = [(radius, z), ...] bottom to top."""
    bm = _tb()
    rings = []
    for (r, z) in profile:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if caps:
        if profile[0][0] > 1e-6:
            bm.faces.new(list(reversed(rings[0])))
        if profile[-1][0] > 1e-6:
            bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.normal_update()
    return bm


def p_sphere(r, u=16, v=8):
    bm = _tb()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r, calc_uvs=True)
    return bm


def p_ico(r, sub=2):
    bm = _tb()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r, calc_uvs=True)
    return bm


def p_box(sx, sy, sz, bevel=0.0, seg=1, base=True):
    bm = _tb()
    bmesh.ops.create_cube(bm, size=1.0, calc_uvs=True)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    if base:
        bmesh.ops.translate(bm, vec=(0, 0, sz / 2), verts=bm.verts)
    if bevel > 0:
        try:
            bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=bevel, offset_type="OFFSET",
                            segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        except TypeError:
            bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=bevel, segments=seg, profile=0.5,
                            affect="EDGES")
    return bm


def p_path_tube(points, radius, seg=8, caps=True, radii=None):
    """Tube along a 3D polyline."""
    bm = _tb()
    pts = [Vector(p) for p in points]
    rings = []
    for i, p in enumerate(pts):
        if i == 0:
            d = pts[1] - pts[0]
        elif i == len(pts) - 1:
            d = pts[-1] - pts[-2]
        else:
            d = (pts[i + 1] - pts[i - 1])
        d.normalize()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
        x = d.cross(up).normalized()
        y = d.cross(x).normalized()
        r = radii[i] if radii else radius
        rings.append([bm.verts.new(p + (x * math.cos(2 * math.pi * k / seg) + y * math.sin(2 * math.pi * k / seg)) * r)
                      for k in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            j = (k + 1) % seg
            bm.faces.new((a[k], a[j], b[j], b[k]))
    if caps:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def displace(bm, amp, freq, seed=0.0, axis_scale=(1, 1, 1)):
    off = Vector((seed * 13.1, seed * 7.7, seed * 3.3))
    for v in bm.verts:
        n = v.co.normalized() if v.co.length > 1e-6 else Vector((0, 0, 1))
        d = noise.noise(v.co * freq + off) + 0.5 * noise.noise(v.co * freq * 2.1 + off)
        v.co += Vector((n.x * axis_scale[0], n.y * axis_scale[1], n.z * axis_scale[2])) * d * amp
    return bm


class B:
    def __init__(self, name):
        self.name = name
        self.bm = _tb()
        self.mats = []

    def add(self, tb, m, M=None, smooth=True):
        if M is not None:
            bmesh.ops.transform(tb, matrix=M, verts=tb.verts)
        if m not in self.mats:
            self.mats.append(m)
        idx = self.mats.index(m)
        for f in tb.faces:
            f.material_index = idx
            f.smooth = smooth
        me = bpy.data.meshes.new("_pp_tmp")
        tb.to_mesh(me)
        tb.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        return self

    def finish(self, coll, angle=35.0, uv="box", ground=True):
        bm = self.bm
        bm.normal_update()
        if ground:
            zmin = min(v.co.z for v in bm.verts)
            xs = [v.co.x for v in bm.verts]
            ys = [v.co.y for v in bm.verts]
            if ground == "z":
                bmesh.ops.translate(bm, vec=(0, 0, -zmin), verts=bm.verts)
            else:
                bmesh.ops.translate(bm, vec=(-(min(xs) + max(xs)) / 2, -(min(ys) + max(ys)) / 2, -zmin),
                                    verts=bm.verts)
        if uv == "box":
            lay = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
            for f in bm.faces:
                n = f.normal
                ax = max(range(3), key=lambda i: abs(n[i]))
                for lp in f.loops:
                    c = lp.vert.co
                    lp[lay].uv = (c.y, c.z) if ax == 0 else ((c.x, c.z) if ax == 1 else (c.x, c.y))
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for m in self.mats:
            me.materials.append(m)
        try:
            me.set_sharp_from_angle(angle=rad(angle))
        except Exception as e:  # pragma: no cover
            print("sharp-from-angle failed:", e)
        ob = bpy.data.objects.new(self.name, me)
        coll.objects.link(ob)
        return ob


def tris(objs):
    t = 0
    for o in objs:
        if o.type == "MESH":
            t += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return t


def coll_tris(name):
    return tris(bpy.data.collections[name].all_objects)


# ---------------------------------------------------------------- studio / render
def studio():
    sc = activate()
    c = bpy.data.collections.get(STUDIO)
    if c is None:
        c = bpy.data.collections.new(STUDIO)
        sc.collection.children.link(c)
    # engine
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except TypeError as e:
        print(e)
    try:
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    try:
        sc.eevee.taa_render_samples = 32
    except Exception:
        pass
    for vt in ("Khronos PBR Neutral", "Standard"):
        try:
            sc.view_settings.view_transform = vt
            break
        except TypeError:
            continue
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    # world: soft sky
    w = bpy.data.worlds.get("PP_World") or bpy.data.worlds.new("PP_World")
    try:
        w.use_nodes = True
    except Exception:
        pass
    nt = w.node_tree
    bg = next((n for n in nt.nodes if n.type == "BACKGROUND"), None)
    if bg is None:
        bg = nt.nodes.new("ShaderNodeBackground")
        out = next((n for n in nt.nodes if n.type == "OUTPUT_WORLD"), None) or nt.nodes.new("ShaderNodeOutputWorld")
        nt.links.new(bg.outputs[0], out.inputs[0])
    bg.inputs[0].default_value = srgb("#CFE3F5")
    bg.inputs[1].default_value = 0.8
    sc.world = w
    # sun
    sun = bpy.data.objects.get("PP_Sun")
    if sun is None:
        ld = bpy.data.lights.new("PP_Sun", "SUN")
        sun = bpy.data.objects.new("PP_Sun", ld)
        c.objects.link(sun)
    sun.data.energy = 4.0
    sun.data.color = srgb("#FFF4E0")[:3]
    sun.data.angle = rad(4)
    sun.rotation_euler = (rad(48), rad(8), rad(35))
    # ground
    g = bpy.data.objects.get("PP_Ground")
    if g is None:
        bm = _tb()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5)
        me = bpy.data.meshes.new("PP_Ground")
        bm.to_mesh(me)
        bm.free()
        me.materials.append(mat("StudioGround", "#E4E6DE", 0.9))
        g = bpy.data.objects.new("PP_Ground", me)
        c.objects.link(g)
    cam = bpy.data.objects.get("PP_Cam")
    if cam is None:
        cd = bpy.data.cameras.new("PP_Cam")
        cam = bpy.data.objects.new("PP_Cam", cd)
        c.objects.link(cam)
    sc.camera = cam
    return sc


def bbox(objs):
    pts = []
    for o in objs:
        if o.type == "EMPTY" and o.instance_type == "COLLECTION" and o.instance_collection:
            sub = bbox(o.instance_collection.all_objects)
            if sub:
                lo, hi = sub
                off = o.instance_collection.instance_offset
                for x in (lo.x, hi.x):
                    for y in (lo.y, hi.y):
                        for z in (lo.z, hi.z):
                            pts.append(o.matrix_world @ (Vector((x, y, z)) - off))
        elif o.type == "MESH":
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    if not pts:
        return None
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def frame(objs, az=35, el=16, lens=70, res=(1280, 960), margin=1.08, ground=True, target_z=None):
    sc = studio()
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    cam = bpy.data.objects["PP_Cam"]
    lo, hi = bbox(objs)
    ctr = (lo + hi) / 2
    if target_z is not None:
        ctr.z = target_z
    cam.data.lens = lens
    cam.data.sensor_fit = "AUTO"
    sw = cam.data.sensor_width
    aspect = res[0] / res[1]
    fov_w = 2 * math.atan(sw / 2 / lens) if aspect >= 1 else 2 * math.atan(sw / 2 / lens) * aspect
    fov_h = fov_w / aspect if aspect >= 1 else 2 * math.atan(sw / 2 / lens)
    d = Vector((math.sin(rad(az)) * math.cos(rad(el)), -math.cos(rad(az)) * math.cos(rad(el)), math.sin(rad(el))))
    fwd = -d
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(fwd).normalized()
    # fit all bbox corners
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    need = 0.0
    tw, th = math.tan(fov_w / 2), math.tan(fov_h / 2)
    for c in corners:
        v = c - ctr
        depth = v.dot(fwd)
        need = max(need, abs(v.dot(right)) / tw * margin - depth, abs(v.dot(up)) / th * margin - depth)
    dist = max(need, 0.05)
    cam.location = ctr + d * dist
    cam.rotation_euler = fwd.to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = max(dist / 200, 0.001)
    cam.data.clip_end = dist * 20 + (hi - lo).length * 5
    g = bpy.data.objects["PP_Ground"]
    size = max((hi - lo).length, 0.1) * 60
    g.scale = (size, size, 1)
    g.location = (ctr.x, ctr.y, lo.z if not ground else 0.0)
    g.hide_render = not ground
    sun = bpy.data.objects["PP_Sun"]
    return sc


def render(names, filename, **kw):
    sc = studio()
    show_only(names)
    bpy.context.view_layer.update()
    objs = []
    for n in names:
        objs += list(bpy.data.collections[n].all_objects)
    frame(objs, **kw)
    sc.render.filepath = os.path.join(REVIEW_DIR, filename)
    bpy.ops.render.render(write_still=True)
    return sc.render.filepath


# ---------------------------------------------------------------- export / save
def _enum_ok(op, prop, want):
    try:
        items = [i.identifier for i in op.get_rna_type().properties[prop].enum_items]
        return want in items
    except Exception:
        return False


def export(coll_name, base=None):
    sc = activate()
    base = base or coll_name.replace("PP_", "")
    show_only([coll_name])
    for o in sc.objects:
        o.select_set(False)
    objs = [o for o in bpy.data.collections[coll_name].all_objects]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    glb = os.path.join(EXPORT_DIR, base + ".glb")
    fbx = os.path.join(EXPORT_DIR, base + ".fbx")
    gkw = dict(filepath=glb, use_selection=True, export_apply=True, export_yup=True)
    if _enum_ok(bpy.ops.export_scene.gltf, "export_format", "GLB"):
        gkw["export_format"] = "GLB"
    bpy.ops.export_scene.gltf(**gkw)
    fkw = dict(filepath=fbx, use_selection=True, use_mesh_modifiers=True, apply_unit_scale=True,
               bake_space_transform=True, add_leaf_bones=False, object_types={"MESH", "EMPTY"})
    op = bpy.ops.export_scene.fbx
    if _enum_ok(op, "apply_scale_options", "FBX_SCALE_ALL"):
        fkw["apply_scale_options"] = "FBX_SCALE_ALL"
    if _enum_ok(op, "mesh_smooth_type", "FACE"):
        fkw["mesh_smooth_type"] = "FACE"
    if _enum_ok(op, "path_mode", "COPY"):
        fkw["path_mode"] = "COPY"
        fkw["embed_textures"] = True
    if _enum_ok(op, "axis_forward", "-Z"):
        fkw["axis_forward"] = "-Z"
        fkw["axis_up"] = "Y"
    op(**fkw)
    for o in objs:
        o.select_set(False)
    return glb, fbx


def save_asset_blend(coll_name, base=None):
    base = base or coll_name.replace("PP_", "")
    tmp = bpy.data.scenes.new("PP_" + base + "_File")
    tmp.unit_settings.system = "METRIC"
    tmp.collection.children.link(bpy.data.collections[coll_name])
    tmp.world = bpy.data.worlds.get("PP_World")
    path = os.path.join(BLEND_DIR, base + ".blend")
    bpy.data.libraries.write(path, {tmp}, path_remap="ABSOLUTE", fake_user=False, compress=True)
    bpy.data.scenes.remove(tmp)
    return path
