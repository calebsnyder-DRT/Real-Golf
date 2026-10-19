"""Full modular golfer build (Caleb, Maya): parts, garments, textures, FBX for Unity.

Headless:  Blender -b --factory-startup --python pp_golfer_build.py -- Caleb [Maya] [--no-export] [--review]

Parts (SkinnedMeshRenderers, names are the GolferAppearance / CosmeticCatalog contract):
  Head_<Name>     face skin (blendshapes Blink_L/R, Smile, Frown, Open; Brows_ and Lashes_ carry the same keys)
  Eyes_<Name>     eyeballs + corneas
  Brows_<Name>, Lashes_<Name>, Hair_<Name>   alpha cards
  Body_Arms       neck/arms/hands skin left visible by every top
  Top_Polo, Top_Tee, Bottom_Chinos, Bottom_Shorts (+ leg skin), Socks_Ankle, Socks_Crew, Shoes_Golf (+ sock band), Belt
  Club_Driver/Iron/Wedge/Putter (rigid on ClubGrip.L; hidden unless held)
Material names follow Mat_<Role>_<Variant>; garment roles are recoloured in Unity by CosmeticPaint.
"""
import math
import os
import sys
from collections import defaultdict

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pp_golfer as G  # noqa: E402

# Staged outside Assets; pp_golfer_sync copies into Assets/_Game/Art/Characters under the Unity lock.
UNITY_CHAR = os.path.join(G.ROOT, "ArtSource", "Exports", "Characters")
SHARED_TEX = os.path.join(UNITY_CHAR, "Shared", "Textures")
MPFB_DATA = os.path.expanduser("~/Library/Application Support/Blender/5.2/extensions/.user/user_default/mpfb/data")
TEX_RES = 2048


# ------------------------------------------------------------------ small helpers

def obj_by_prefix(P, prefix):
    for k, o in P.items():
        if k.startswith(prefix):
            return o
    raise KeyError(prefix)


def link_new(name, me, coll=None):
    ob = bpy.data.objects.new(name, me)
    (coll or bpy.context.scene.collection).objects.link(ob)
    return ob


def bm_of(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    return bm


def bm_write(bm, ob):
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def duplicate(ob, name):
    me = ob.data.copy()
    me.name = name
    d = bpy.data.objects.new(name, me)
    for c in ob.users_collection:
        c.objects.link(d)
    d.parent = ob.parent
    d.matrix_world = ob.matrix_world.copy()
    for m in ob.modifiers:
        if m.type == "ARMATURE":
            nm = d.modifiers.new("Armature", "ARMATURE")
            nm.object = m.object
    return d


def keep_faces(ob, keep):
    """Delete faces whose index is not in keep (set), plus loose verts."""
    bm = bm_of(ob)
    kill = [f for f in bm.faces if f.index not in keep]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm_write(bm, ob)


def set_material(ob, mats, face_slots=None):
    ob.data.materials.clear()
    for m in mats:
        ob.data.materials.append(m)
    if face_slots is not None:
        for p in ob.data.polygons:
            p.material_index = face_slots.get(p.index, 0)


def mat(name, tex=None, rgb=(1, 1, 1), rough=0.6, metal=0.0, alpha=False, normal=None):
    """Blender-side preview material (Unity builds its own from the exported textures; only the name matters)."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    b = nt.nodes.new("ShaderNodeBsdfPrincipled")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    nt.links.new(b.outputs[0], out.inputs[0])
    if tex:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(tex, check_existing=True)
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs[7].default_value = (*rgb, 1)
        nt.links.new(t.outputs["Color"], mix.inputs[6])
        nt.links.new(mix.outputs[2], b.inputs["Base Color"])
        if alpha:
            nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
            m.blend_method = "HASHED" if hasattr(m, "blend_method") else None
    if normal:
        nt2 = nt.nodes.new("ShaderNodeTexImage")
        nt2.image = bpy.data.images.load(normal, check_existing=True)
        nt2.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nt2.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    m.diffuse_color = (*rgb, 1)
    return m


# ------------------------------------------------------------------ images (numpy)

def load_px(path):
    im = bpy.data.images.load(path, check_existing=False)
    w, h = im.size
    a = np.empty(w * h * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)


def save_px(arr, path, srgb=True):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), np.float32)], axis=2)
    im = bpy.data.images.new("tmp_save", w, h, alpha=True, float_buffer=False)
    if not srgb:
        im.colorspace_settings.name = "Non-Color"
    im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)
    return path


def resize(arr, size):
    h, w = arr.shape[:2]
    if (w, h) == (size, size):
        return arr
    ys = (np.arange(size) + 0.5) * h / size - 0.5
    xs = (np.arange(size) + 0.5) * w / size - 0.5
    return bilinear(arr, xs[None, :].repeat(size, 0), ys[:, None].repeat(size, 1))


def bilinear(arr, x, y):
    h, w = arr.shape[:2]
    x = np.clip(x, 0, w - 1.001)
    y = np.clip(y, 0, h - 1.001)
    x0 = np.floor(x).astype(int)
    y0 = np.floor(y).astype(int)
    fx = (x - x0)[..., None]
    fy = (y - y0)[..., None]
    a = arr[y0, x0] * (1 - fx) + arr[y0, x0 + 1] * fx
    b = arr[y0 + 1, x0] * (1 - fx) + arr[y0 + 1, x0 + 1] * fx
    return a * (1 - fy) + b * fy


def blur(arr, r):
    """Separable box blur (3 passes ~ gaussian), wraps at edges."""
    out = arr.copy()
    for _ in range(3):
        for ax in (0, 1):
            c = np.cumsum(np.concatenate([out.take(range(-r - 1, 0), axis=ax), out, out.take(range(0, r), axis=ax)], axis=ax), axis=ax)
            n = out.shape[ax]
            out = (c.take(range(2 * r + 1, 2 * r + 1 + n), axis=ax) - c.take(range(0, n), axis=ax)) / (2 * r + 1)
    return out


def luminance(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def detail_albedo(rgb, radius=24, lo=0.62, hi=1.12, keep=0.0):
    """Tintable albedo: local luminance detail around ~0.9 (weave, stitches, perforations), large tone zones removed."""
    L = luminance(rgb)
    m = blur(L[..., None], radius)[..., 0]
    d = np.clip(L / np.maximum(m, 1e-3), lo, hi) * 0.9
    d = d * (1 - keep) + L * keep
    return np.repeat(d[..., None], 3, axis=2)


def height_to_normal(hgt, strength=2.0):
    gy, gx = np.gradient(hgt)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(hgt)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def noise_tile(size, scale, seed, octaves=4):
    """Tileable value noise in [0,1]."""
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        cells = int(scale * (2 ** o))
        g = rng.random((cells, cells)).astype(np.float32)
        ys = np.arange(size) * cells / size
        y0 = np.floor(ys).astype(int)
        fy = ys - y0
        fy = fy * fy * (3 - 2 * fy)
        a = g[y0 % cells][:, (y0 % cells)]
        b = g[y0 % cells][:, ((y0 + 1) % cells)]
        c = g[(y0 + 1) % cells][:, (y0 % cells)]
        d = g[(y0 + 1) % cells][:, ((y0 + 1) % cells)]
        fx = fy[None, :]
        fyy = fy[:, None]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fyy) + (c * (1 - fx) + d * fx) * fyy)
        tot += amp
        amp *= 0.5
    return out / tot


# ------------------------------------------------------------------ build steps

def add_shape_keys(ob, deltas, index_map=None):
    """deltas: {key: [Vector per original vertex]}; index_map: current vert index -> original index."""
    if not deltas:
        return
    ob.shape_key_add(name="Basis", from_mix=False)
    for name, d in deltas.items():
        k = ob.shape_key_add(name=name, from_mix=False)
        k.value = 0.0
        k.slider_max = 1.0
        for i, v in enumerate(ob.data.vertices):
            j = index_map[i] if index_map else i
            k.data[i].co = v.co + d[j]


def coverage(body, garment, maxd=0.06):
    """Body verts whose outward normal ray hits the garment within maxd."""
    gm = garment.data
    bvh = BVHTree.FromPolygons([v.co for v in gm.vertices], [p.vertices[:] for p in gm.polygons])
    out = np.zeros(len(body.data.vertices), bool)
    for v in body.data.vertices:
        n = v.normal
        hit = bvh.ray_cast(v.co + n * 0.001, n, maxd)
        if hit[0] is not None:
            out[v.index] = True
    return out


def erode(ob, mask, rings=1):
    m = mask.copy()
    edges = [(e.vertices[0], e.vertices[1]) for e in ob.data.edges]
    for _ in range(rings):
        nxt = m.copy()
        for a, b in edges:
            if not m[a] or not m[b]:
                nxt[a] = nxt[a] and m[b]
                nxt[b] = nxt[b] and m[a]
        m = nxt
    return m


def vgroup_mask(ob, name, thr=0.5):
    g = ob.vertex_groups.get(name)
    out = np.zeros(len(ob.data.vertices), bool)
    if g is None:
        return out
    for v in ob.data.vertices:
        for ge in v.groups:
            if ge.group == g.index and ge.weight >= thr:
                out[v.index] = True
    return out


def uv_islands(ob):
    """List of face-index lists, connected through shared UV coordinates."""
    me = ob.data
    uv = me.uv_layers.active.data
    key_faces = defaultdict(list)
    for p in me.polygons:
        for li in p.loop_indices:
            l = me.loops[li]
            key_faces[(l.vertex_index, round(uv[li].uv.x, 5), round(uv[li].uv.y, 5))].append(p.index)
    adj = defaultdict(set)
    for fl in key_faces.values():
        for f in fl:
            adj[f].update(fl)
    seen, islands = set(), []
    for p in me.polygons:
        if p.index in seen:
            continue
        stack, isl = [p.index], []
        seen.add(p.index)
        while stack:
            f = stack.pop()
            isl.append(f)
            for g in adj[f]:
                if g not in seen:
                    seen.add(g)
                    stack.append(g)
        islands.append(isl)
    return islands


def face_uv_center(me, p):
    uv = me.uv_layers.active.data
    s = Vector((0, 0))
    for li in p.loop_indices:
        s += uv[li].uv
    return s / len(p.loop_indices)


class WaistLine:
    """Height of the trousers' top edge as a function of the angle around the body axis (front rise is lower)."""

    def __init__(self, pants, bins=48):
        me = pants.data
        edge_faces = defaultdict(int)
        for p in me.polygons:
            for k in p.edge_keys:
                edge_faces[k] += 1
        zmax = max(v.co.z for v in me.vertices)
        top = [me.vertices[i] for k, n in edge_faces.items() if n == 1 for i in k if me.vertices[i].co.z > zmax - 0.1]
        self.cy = sum(v.co.y for v in top) / max(1, len(top))
        self.bins = bins
        zs = [[] for _ in range(bins)]
        for v in top:
            zs[self.bin(v.co)].append(v.co.z)
        filled = [max(z) if z else None for z in zs]
        for i in range(bins):
            if filled[i] is None:
                j = 1
                while filled[(i - j) % bins] is None and filled[(i + j) % bins] is None:
                    j += 1
                filled[i] = filled[(i - j) % bins] if filled[(i - j) % bins] is not None else filled[(i + j) % bins]
        # ignore the fly opening and other narrow dips with a median (a local max lifts the line off the real edge where
        # the rise drops towards the back, and the belt then floats above the cloth), then a light smooth
        med = [sorted(filled[(i + k) % bins] for k in range(-2, 3))[2] for i in range(bins)]
        self.z = [(med[(i - 1) % bins] + 2 * med[i] + med[(i + 1) % bins]) / 4 for i in range(bins)]

    def angle(self, co):
        return math.atan2(co.y - self.cy, co.x) % (2 * math.pi)

    def bin(self, co):
        return int(self.angle(co) / (2 * math.pi) * self.bins) % self.bins

    def top(self, co):
        a = self.angle(co) / (2 * math.pi) * self.bins
        i = int(a) % self.bins
        f = a - int(a)
        return self.z[i] * (1 - f) + self.z[(i + 1) % self.bins] * f


def hug_waistband(pants, body, waist, band=0.05, clearance=0.009):
    """Pull the trousers' top band in onto the body (MakeHuman's waistband flares on wide hips), leaving room for the
    tucked shirt, so the belt sits flush instead of riding on a lip."""
    bm = bm_of(body)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    me = pants.data
    moved = 0
    for v in me.vertices:
        h = v.co.z - (waist.top(v.co) - band)
        if h <= 0:
            continue
        d = Vector((v.co.x, v.co.y - waist.cy, 0.0))
        cur = d.length
        if cur < 1e-4:
            continue
        d /= cur
        hit = bvh.ray_cast(Vector((0.0, waist.cy, v.co.z)) + d * 0.4, -d, 0.45)
        if hit[0] is None:
            continue
        target = (Vector((hit[0].x, hit[0].y - waist.cy, 0.0))).length + clearance
        if cur <= target:
            continue
        t = min(1.0, h / 0.02)
        t = t * t * (3 - 2 * t)
        r = cur + (target - cur) * t
        v.co.x, v.co.y = d.x * r, waist.cy + d.y * r
        moved += 1
    print("[build] waistband hugged %d verts" % moved)


def tuck(top, pants, waist, depth=0.025):
    """Tuck a shirt into the trousers: drop the hem below the waistband, pull the rest just inside the pants."""
    pm = pants.data
    bvh = BVHTree.FromPolygons([v.co for v in pm.vertices], [p.vertices[:] for p in pm.polygons])
    bm = bm_of(top)
    kill = [f for f in bm.faces if all(v.co.z < waist.top(v.co) - depth for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    for v in bm.verts:
        zt = waist.top(v.co)
        if v.co.z > zt + 0.03:
            continue
        loc, nrm, _, _ = bvh.find_nearest(v.co)
        if loc is None:
            continue
        inside = loc - nrm.normalized() * 0.005
        inside.z = v.co.z
        t = min(1.0, max(0.0, (zt + 0.03 - v.co.z) / 0.035))
        t = t * t * (3 - 2 * t)
        v.co = v.co.lerp(inside, t)
    bm_write(bm, top)


def decal(source, center, radius, name, material, lift=0.0015):
    """Small decal mesh (logo) cut from the garment surface around center, planar UVs 0..1, lifted along normals."""
    me = source.data
    keep = {p.index for p in me.polygons if (p.center - center).length < radius * 1.3}
    d = duplicate(source, name)
    keep_faces(d, keep)
    bm = bm_of(d)
    uv = bm.loops.layers.uv.active
    n = sum((f.normal for f in bm.faces), Vector()).normalized()
    up = Vector((0, 0, 1))
    right = up.cross(n).normalized()
    up = n.cross(right).normalized()
    for v in bm.verts:
        v.co += v.normal * lift
    for f in bm.faces:
        for l in f.loops:
            p = l.vert.co - center
            l[uv].uv = (0.5 - p.dot(right) / (2 * radius), 0.5 + p.dot(up) / (2 * radius))
    bm_write(bm, d)
    d.data.materials.clear()
    d.data.materials.append(material)
    return d


def cut_shorts(pants, hem_z, name):
    s = duplicate(pants, name)
    bm = bm_of(s)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, hem_z), plane_no=(0, 0, 1), clear_inner=True)
    cut = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    # turned-up hem: extrude the cut loop up and slightly inward (reads as fabric thickness from below)
    ext = bmesh.ops.extrude_edge_only(bm, edges=cut)
    newv = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    for v in newv:
        c = Vector((0.0, 0.0, v.co.z))
        side = 1 if v.co.x > 0 else -1
        leg_axis = Vector((0.1 * side, 0.0, v.co.z))
        inward = (leg_axis - v.co)
        inward.z = 0
        if inward.length > 1e-6:
            v.co += inward.normalized() * 0.006
        v.co.z += 0.022
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_write(bm, s)
    return s


def sock(body, legs_mask, z0, z1, name, material, push=0.0028):
    """Knit sock shell from the leg skin between z0 and z1 (rolled cuff at the top), cylindrical UVs."""
    me = body.data
    keep = set()
    for p in me.polygons:
        zs = [me.vertices[i].co.z for i in p.vertices]
        if min(zs) >= z0 - 0.002 and max(zs) <= z1 and all(legs_mask[i] for i in p.vertices):
            keep.add(p.index)
    s = duplicate(body, name)
    if s.data.shape_keys:
        s.shape_key_clear()
    keep_faces(s, keep)
    bm = bm_of(s)
    if not bm.loops.layers.uv:
        bm.loops.layers.uv.new()
    uv = bm.loops.layers.uv.active
    for v in bm.verts:
        t = (v.co.z - z0) / max(1e-4, z1 - z0)
        v.co += v.normal * (push + 0.0012 * max(0.0, (t - 0.85) / 0.15))
    for f in bm.faces:
        cx = sum(v.co.x for v in f.verts) / len(f.verts)
        ax = Vector((0.1 if cx > 0 else -0.1, 0.0))
        for l in f.loops:
            p = Vector((l.vert.co.x, l.vert.co.y)) - Vector((ax.x, -0.01))
            a = (math.atan2(p.y, p.x) / (2 * math.pi)) % 1.0
            l[uv].uv = (a * 3.0, (l.vert.co.z - z0) / max(1e-4, z1 - z0))
        # fix seam wrap
        us = [l[uv].uv.x for l in f.loops]
        if max(us) - min(us) > 1.5:
            for l in f.loops:
                if l[uv].uv.x < 1.5:
                    l[uv].uv.x += 3.0
    bm_write(bm, s)
    s.data.materials.clear()
    s.data.materials.append(material)
    return s


def belt(pants, waist, name, mat_belt, mat_buckle, width=0.034, gap=0.004, seg=56):
    """Leather belt hugging the pants waistband, with a brushed-metal buckle at the front."""
    pm = pants.data
    bvh = BVHTree.FromPolygons([v.co for v in pm.vertices], [p.vertices[:] for p in pm.polygons])
    ring_c = Vector((0.0, waist.cy, 0.0))
    bm = bmesh.new()
    rows = []
    for frac in (1.0, 0.5, 0.0):
        dirs, zs, radii = [], [], []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            d = Vector((math.cos(a), math.sin(a), 0.0))
            z = waist.top(Vector((d.x, ring_c.y + d.y, 0))) - gap - width * frac
            # near wall only: a ray that misses it must not land on the far side and pull the belt through the body
            hit = bvh.ray_cast(Vector((ring_c.x, ring_c.y, z)) + d * 0.4, -d, 0.39)
            dirs.append(d)
            zs.append(z)
            radii.append((hit[0] - Vector((ring_c.x, ring_c.y, z))).length if hit[0] is not None else None)
        known = [i for i, r in enumerate(radii) if r is not None]
        row = []
        for i in range(seg):
            r = radii[i]
            if r is None:
                lo = max((k for k in known if k < i), default=known[-1] - seg)
                hi = min((k for k in known if k > i), default=known[0] + seg)
                t = (i - lo) / max(1, hi - lo)
                r = radii[lo % seg] * (1 - t) + radii[hi % seg] * t
            p = Vector((ring_c.x, ring_c.y, zs[i])) + dirs[i] * (r + 0.0035)
            row.append(p)
        rows.append(row)
    outer = [[bm.verts.new(p) for p in row] for row in rows]
    inner = [[bm.verts.new(p - (p - Vector((ring_c.x, ring_c.y, p.z))).normalized() * 0.0035) for p in row]
             for row in (rows[0], rows[-1])]
    uvl = bm.loops.layers.uv.new()

    def quad(a, b, c, d, us, vs):
        f = bm.faces.new((a, b, c, d))
        for l, u, v in zip(f.loops, us, vs):
            l[uvl].uv = (u, v)
        f.material_index = 0
        f.smooth = True
        return f

    for r in range(len(outer) - 1):
        for i in range(seg):
            j = (i + 1) % seg
            u0, u1 = i / seg * 6, (i + 1) / seg * 6
            quad(outer[r][i], outer[r][j], outer[r + 1][j], outer[r + 1][i], (u0, u1, u1, u0), (r / 2, r / 2, (r + 1) / 2, (r + 1) / 2))
    for i in range(seg):
        j = (i + 1) % seg
        quad(inner[0][j], inner[0][i], outer[0][i], outer[0][j], (0, 0, 0.02, 0.02), (0, 0, 0, 0))
        quad(outer[-1][j], outer[-1][i], inner[1][i], inner[1][j], (0, 0, 0.02, 0.02), (1, 1, 1, 1))
    # buckle at the front (-Y)
    front = min(range(seg), key=lambda i: abs(2 * math.pi * i / seg - 1.5 * math.pi))
    c = rows[1][front] + Vector((0, -0.004, 0))
    bw, bh, t = 0.046, width + 0.008, 0.005
    ring = []
    for k in range(16):
        a = 2 * math.pi * k / 16
        cx, cz = math.cos(a), math.sin(a)
        e = 0.35
        ring.append((bw / 2 * math.copysign(abs(cx) ** e, cx), bh / 2 * math.copysign(abs(cz) ** e, cz)))
    out_ring = [bm.verts.new(c + Vector((x, 0, z))) for x, z in ring]
    in_ring = [bm.verts.new(c + Vector((x * 0.72, 0, z * 0.62))) for x, z in ring]
    out_b = [bm.verts.new(c + Vector((x, t, z))) for x, z in ring]
    in_b = [bm.verts.new(c + Vector((x * 0.72, t, z * 0.62))) for x, z in ring]
    n = len(ring)
    for i in range(n):
        j = (i + 1) % n
        for a, b, cc, d in ((out_ring[i], out_ring[j], in_ring[j], in_ring[i]), (out_b[j], out_b[i], out_ring[i], out_ring[j]),
                            (in_ring[i], in_ring[j], in_b[j], in_b[i])):
            f = bm.faces.new((a, b, cc, d))
            f.material_index = 1
            f.smooth = False
    # prong
    pr = [bm.verts.new(c + Vector((x, -0.0015, z))) for x, z in ((-0.003, -0.002), (0.016, -0.002), (0.016, 0.002), (-0.003, 0.002))]
    f = bm.faces.new(pr)
    f.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = link_new(name, me)
    ob.data.materials.append(mat_belt)
    ob.data.materials.append(mat_buckle)
    ob.parent = pants.parent
    transfer_weights(pants, ob)
    am = ob.modifiers.new("Armature", "ARMATURE")
    am.object = pants.parent
    return ob


def transfer_weights(src, dst):
    """Nearest-vertex weight copy (src and dst in the same space)."""
    sm = src.data
    kd_pts = [v.co for v in sm.vertices]
    from mathutils.kdtree import KDTree
    kd = KDTree(len(kd_pts))
    for i, p in enumerate(kd_pts):
        kd.insert(p, i)
    kd.balance()
    names = {g.index: g.name for g in src.vertex_groups}
    for g in list(dst.vertex_groups):
        dst.vertex_groups.remove(g)
    groups = {}
    for v in dst.data.vertices:
        _, i, _ = kd.find(v.co)
        for ge in sm.vertices[i].groups:
            nm = names[ge.group]
            if nm not in groups:
                groups[nm] = dst.vertex_groups.new(name=nm)
            groups[nm].add([v.index], ge.weight, "REPLACE")


def cornea(eyes, mat_cornea, name):
    """Clear cornea caps over each iris: eye faces facing forward, lifted and domed."""
    me = eyes.data
    comps = defaultdict(list)
    # split by side
    for p in me.polygons:
        comps[1 if p.center.x > 0 else -1].append(p.index)
    keep = set()
    centers = {}
    for side, faces in comps.items():
        c = sum((me.polygons[f].center for f in faces), Vector()) / len(faces)
        centers[side] = c
        for f in faces:
            d = (me.polygons[f].center - c).normalized()
            if d.dot(Vector((0, -1, 0))) > math.cos(math.radians(38)):
                keep.add(f)
    d = duplicate(eyes, name)
    keep_faces(d, keep)
    bm = bm_of(d)
    for v in bm.verts:
        side = 1 if v.co.x > 0 else -1
        c = centers[side]
        r = (v.co - c)
        ang = math.acos(max(-1, min(1, r.normalized().dot(Vector((0, -1, 0))))))
        dome = 0.0009 * max(0.0, math.cos(min(math.pi / 2, ang / math.radians(38) * math.pi / 2)))
        v.co = c + r * (1.0 + (0.0004 + dome) / max(r.length, 1e-4))
    bm_write(bm, d)
    d.data.materials.clear()
    d.data.materials.append(mat_cornea)
    return d


def join(objs, name):
    objs = [o for o in objs if o is not None]
    base = objs[0]
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = base
    bpy.ops.object.join()
    base.name = name
    base.data.name = name
    return base


def remove_empty_keys(ob, eps=1e-5):
    if not ob.data.shape_keys:
        return
    kb = ob.data.shape_keys.key_blocks
    basis = kb[0]
    for k in list(kb)[1:]:
        if max(((a.co - b.co).length for a, b in zip(k.data, basis.data)), default=0) < eps:
            ob.shape_key_remove(k)
    if len(ob.data.shape_keys.key_blocks) == 1:
        ob.shape_key_remove(ob.data.shape_keys.key_blocks[0])


# ------------------------------------------------------------------ head UV + textures

def head_uv_relayout(head, src_rgb, out_size=TEX_RES):
    """Give the head its own 0..1 UV space (islands scaled up ~1.7x) and resample the skin texture into it."""
    me = head.data
    uv = me.uv_layers.active.data
    isl = uv_islands(head)
    rects = []
    for faces in isl:
        us = [uv[li].uv.x for f in faces for li in me.polygons[f].loop_indices]
        vs = [uv[li].uv.y for f in faces for li in me.polygons[f].loop_indices]
        rects.append([min(us), min(vs), max(us), max(vs), faces])
    rects.sort(key=lambda r: -(r[2] - r[0]) * (r[3] - r[1]))
    main = rects[0]
    s = 0.985 / max(main[2] - main[0], main[3] - main[1])
    placed = []
    pad = 6 / out_size
    x, y, col_x, col_w = 0.008, 0.008, None, 0
    placed.append((main, s, 0.008, 0.008))
    col_x = 0.008 + (main[2] - main[0]) * s + pad * 2
    cy = 0.008
    for r in rects[1:]:
        w, h = (r[2] - r[0]) * s, (r[3] - r[1]) * s
        sc = s
        if col_x + w > 0.995:
            sc = s * (0.995 - col_x) / max(w, 1e-6)
            w, h = w * sc / s, h * sc / s
        if cy + h > 0.995:
            sc *= 0.5
            w, h = w * 0.5, h * 0.5
            cy = 0.008
            col_x += 0.1
        placed.append((r, sc, col_x, cy))
        cy += h + pad * 2
    src = src_rgb
    sh, sw = src.shape[:2]
    out = np.zeros((out_size, out_size, src.shape[2]), np.float32)
    m = int(8)
    for r, sc, ox, oy in placed:
        u0, v0, u1, v1, faces = r
        for f in faces:
            for li in me.polygons[f].loop_indices:
                p = uv[li].uv
                uv[li].uv = ((p.x - u0) * sc + ox, (p.y - v0) * sc + oy)
        px0 = max(0, int(ox * out_size) - m)
        py0 = max(0, int(oy * out_size) - m)
        px1 = min(out_size, int((ox + (u1 - u0) * sc) * out_size) + m)
        py1 = min(out_size, int((oy + (v1 - v0) * sc) * out_size) + m)
        xs = (np.arange(px0, px1) + 0.5) / out_size
        ys = (np.arange(py0, py1) + 0.5) / out_size
        U = (xs[None, :] - ox) / sc + u0
        V = (ys[:, None] - oy) / sc + v0
        U = np.repeat(U, len(ys), 0)
        V = np.repeat(V, len(xs), 1)
        # image rows are bottom-up in Blender pixel buffers, like UV v
        out[py0:py1, px0:px1] = bilinear(src, U * sw - 0.5, V * sh - 0.5)
    return out


# ------------------------------------------------------------------ main build

def build(name, export=True, review=False):
    G.enable_mpfb()
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    c = G.CHARS[name]
    body = G.build_human(name, clothes=("polo", "tee", "pants", "shoes"))
    P = G.parts_of(body)
    G.bake_and_clean(P)
    rig = P["rig"]
    brows = obj_by_prefix(P, c["eyebrows"].split("/")[0])
    lashes = obj_by_prefix(P, c["eyelashes"].split("/")[0])
    hair = obj_by_prefix(P, c["hair"].split("/")[0])
    eyes = P["high-poly"]
    polo = obj_by_prefix(P, "namuhekam")
    tee = obj_by_prefix(P, "toigo_basic")
    pants = obj_by_prefix(P, "toigo_wool")
    shoes = obj_by_prefix(P, "shoes05")
    meshes = [body, brows, lashes, hair, eyes, polo, tee, pants, shoes]
    ex = G.expression_deltas(body, [brows, lashes])
    kept = G.delete_helpers(body)
    G.rename_rig(rig)
    G.tpose(rig, meshes)
    G.ground_fix(rig, meshes, shoes)
    G.add_club_grip(rig, body)
    clubs_coll = bpy.context.scene.collection
    clubs = G.add_clubs(rig, clubs_coll)
    rig.name = name + "_Rig"
    rig.data.name = name + "_Rig"
    bones = {b.name: b for b in rig.data.bones}
    neck_z = bones["Neck"].head_local.z
    knee_z = bones["LowerLeg.L"].head_local.z
    hips_z = bones["Hips"].head_local.z

    # blendshapes (face + brows + lashes)
    add_shape_keys(body, {k: d[body.name] for k, d in ex.items()}, index_map=kept)
    add_shape_keys(brows, {k: d[brows.name] for k, d in ex.items()})
    add_shape_keys(lashes, {k: d[lashes.name] for k, d in ex.items()})

    # ---- garment edits that change coverage
    pants_top = max(v.co.z for v in pants.data.vertices)
    waist = WaistLine(pants)
    hug_waistband(pants, body, waist)
    tuck(polo, pants, waist)

    # ---- regions on the body skin
    cov_polo = coverage(body, polo)
    cov_tee = coverage(body, tee)
    cov_pants = coverage(body, pants, maxd=0.07)
    zs = np.array([v.co.z for v in body.data.vertices])
    hem_z = knee_z + 0.07
    feet = vgroup_mask(body, "Delete.shoes05", 0.01)
    under_tops = erode(body, cov_polo & cov_tee & (zs < neck_z - 0.035), 1)
    lower = (zs < hips_z + 0.06) & ~under_tops
    under_bottoms = erode(body, cov_pants & (zs > hem_z + 0.03) & lower, 1)
    shoes_upper_top = max(v.co.z for v in shoes.data.vertices if v.co.z < 0.135)
    gone = under_tops | under_bottoms | erode(body, feet & (zs < shoes_upper_top - 0.012), 1)
    legs = lower & ~gone & (zs < hem_z + 0.05)

    # ---- garment edits
    shorts = cut_shorts(pants, hem_z, "Bottom_Shorts")
    # shoes05 ships with tall socks: keep only a band of sock inside the shoe collar
    sm = shoes.data
    sock_faces = set()
    for faces in uv_islands(shoes):
        if max(sm.vertices[i].co.z for f in faces for i in sm.polygons[f].vertices) > 0.16:
            sock_faces.update(faces)
    shoe_top = max(sm.vertices[i].co.z for p in sm.polygons if p.index not in sock_faces for i in p.vertices)
    band = {f for f in sock_faces if max(sm.vertices[i].co.z for i in sm.polygons[f].vertices) < shoe_top + 0.014}
    keepf = {p.index for p in sm.polygons if p.index not in sock_faces} | band
    shoe_tex = load_px(os.path.join(MPFB_DATA, "clothes", "shoes05", "shoes05_diffuse.png"))

    # ---- materials
    T = textures(name, c, body, shoes, shoe_tex)
    M = {}
    M["skin"] = mat("Mat_Skin_" + name, T["skin"], rough=0.5)
    M["face"] = mat("Mat_Face_" + name, T["face"], rough=0.45)
    M["eye"] = mat("Mat_Eye_" + name, T["eye"], rough=0.3)
    M["cornea"] = mat("Mat_Cornea", None, rgb=(1, 1, 1), rough=0.02)
    M["brow"] = mat("Mat_Brow_" + name, T["brow"], rgb=c["hair_tint"], alpha=True)
    M["lash"] = mat("Mat_Lash_" + name, T["lash"], rgb=(0.05, 0.04, 0.035), alpha=True)
    M["hair"] = mat("Mat_Hair_" + name, T["hair"], rgb=c["hair_tint"], alpha=True)
    M["polo"] = mat("Mat_Top_Polo", T["polo"], normal=T.get("polo_n"))
    M["collar"] = mat("Mat_Collar_Polo", T["polo"], normal=T.get("polo_n"))
    M["logo_polo"] = mat("Mat_Logo_Polo", T["logo"], alpha=True)
    M["tee"] = mat("Mat_Top_Tee", T["tee"])
    M["logo_tee"] = mat("Mat_Logo_Tee", T["logo"], alpha=True)
    M["chinos"] = mat("Mat_Bottom_Chinos", T["pants"])
    M["shorts"] = mat("Mat_Bottom_Shorts", T["pants"])
    M["socks"] = mat("Mat_Socks_Knit", T["socks"])
    M["band"] = mat("Mat_Socks_Band", T["socks"])
    M["shoe"] = mat("Mat_Shoes_Golf", T["shoes"])
    M["accent"] = mat("Mat_ShoeAccent_Golf", T["shoes"])
    M["sole"] = mat("Mat_Sole_Golf", T["shoes"])
    M["belt"] = mat("Mat_Belt_Leather", T["belt"])
    M["buckle"] = mat("Mat_Buckle_Metal", None, rgb=(0.8, 0.8, 0.82), metal=1.0, rough=0.25)

    # ---- polo: collar + buttons get the collar role, logo decal on the left chest
    pm = polo.data
    pisl = uv_islands(polo)
    big = [f for f in pisl if len(f) > 60]
    collar = max(big, key=lambda fs: sum(pm.polygons[f].center.z for f in fs) / len(fs))
    cslots = {f: 1 for f in collar}
    for fs in pisl:
        if len(fs) <= 8:
            for f in fs:
                cslots[f] = 1
    set_material(polo, [M["polo"], M["collar"]], cslots)
    chest = chest_point(polo, Vector((0.095, -0.2, bones["Chest"].tail_local.z - 0.06)))
    logo_p = decal(polo, chest, 0.022, "Logo_Polo_tmp", M["logo_polo"])
    tee_chest = chest_point(tee, Vector((0.095, -0.2, bones["Chest"].tail_local.z - 0.06)))
    logo_t = decal(tee, tee_chest, 0.022, "Logo_Tee_tmp", M["logo_tee"])
    set_material(tee, [M["tee"]])
    set_material(pants, [M["chinos"]])
    set_material(shorts, [M["shorts"]])

    # ---- body split: head (by UV island of the face), arms/neck, legs into the shorts
    head_skin, arms_skin, leg_skin = split_body(body, gone, legs, name)
    set_material(head_skin, [M["face"]])
    set_material(arms_skin, [M["skin"]])
    set_material(leg_skin, [M["skin"]])
    remove_empty_keys(arms_skin)
    if leg_skin.data.shape_keys:
        leg_skin.shape_key_clear()
    face_rgb = load_px(T["skin_src"])[..., :3]
    head_tex = head_uv_relayout(head_skin, face_rgb)
    save_px(head_tex, T["face"])

    all_legs = np.ones(len(leg_skin.data.vertices), bool)
    socks_ankle = sock(leg_skin, all_legs, shoe_top - 0.02, shoe_top + 0.045, "Socks_Ankle", M["socks"])
    socks_crew = sock(leg_skin, all_legs, shoe_top - 0.02, shoe_top + 0.2, "Socks_Crew", M["socks"])

    keep_faces(shoes, keepf)
    set_material(shoes, [M["shoe"], M["accent"], M["sole"], M["band"]])
    reslot_shoes(shoes)
    belt_ob = belt(pants, waist, "Belt", M["belt"], M["buckle"])

    # MakeHuman's high-poly eyes carry their own cornea shells, UV-mapped to the corner swatch of the eye texture
    em = eyes.data
    cslot = {p.index: 1 for p in em.polygons if face_uv_center(em, p).x > 0.84 and face_uv_center(em, p).y < 0.17}
    set_material(eyes, [M["eye"], M["cornea"]], cslot)
    set_material(brows, [M["brow"]])
    set_material(lashes, [M["lash"]])
    set_material(hair, [M["hair"]])
    if name == "Maya":
        trim_hair(hair, bones["Chest"].head_local.z - 0.02)

    # Head pieces stay separate renderers on the one rig; the face skin, brows and lashes carry the expression keys.
    for ob, part in ((head_skin, "Head"), (eyes, "Eyes"), (brows, "Brows"), (lashes, "Lashes"), (hair, "Hair")):
        ob.name = ob.data.name = "%s_%s" % (part, name)
    head = head_skin
    top_polo = join([polo, logo_p], "Top_Polo")
    top_tee = join([tee, logo_t], "Top_Tee")
    pants.name = pants.data.name = "Bottom_Chinos"
    bottom_shorts = join([shorts, leg_skin], "Bottom_Shorts")
    shoes.name = shoes.data.name = "Shoes_Golf"
    arms_skin.name = arms_skin.data.name = "Body_Arms"
    for o in (socks_ankle, socks_crew, belt_ob):
        o.data.name = o.name
    parts = [head, eyes, brows, lashes, hair, arms_skin, top_polo, top_tee, pants, bottom_shorts, socks_ankle, socks_crew, shoes,
             belt_ob] + list(clubs.values())
    for o in parts:
        o.parent = rig
        for m in o.modifiers:
            if m.type == "ARMATURE":
                m.object = rig
        for p in o.data.polygons:
            p.use_smooth = True
        clean_groups(o, rig)
    report(name, parts)
    placeholder_idle(rig)
    out_blend = os.path.join(G.BLEND_DIR, "PuttshackPro_Golfer_%s.blend" % name)
    bpy.ops.wm.save_as_mainfile(filepath=out_blend)
    if export:
        export_fbx(rig, parts, os.path.join(UNITY_CHAR, name, "%s_Modular.fbx" % name))
    return rig, parts


def chest_point(garment, approx):
    gm = garment.data
    bvh = BVHTree.FromPolygons([v.co for v in gm.vertices], [p.vertices[:] for p in gm.polygons])
    hit = bvh.ray_cast(Vector((approx.x, -0.6, approx.z)), Vector((0, 1, 0)), 1.0)
    return hit[0] if hit[0] is not None else approx


def split_body(body, gone, legs, name):
    me = body.data
    head_faces, arm_faces, leg_faces = set(), set(), set()
    isl = uv_islands(body)
    uv = me.uv_layers.active.data
    # the head UV island is the one containing the nose tip (most -Y vertex above the neck)
    nose = min((v for v in me.vertices if v.co.z > 1.4), key=lambda v: v.co.y).index
    face_of_vert = defaultdict(list)
    for p in me.polygons:
        for i in p.vertices:
            face_of_vert[i].append(p.index)
    head_island = None
    for faces in isl:
        fs = set(faces)
        if any(f in fs for f in face_of_vert[nose]):
            head_island = fs
            break
    head_z = min(me.vertices[i].co.z for f in head_island for i in me.polygons[f].vertices)
    for p in me.polygons:
        if any(gone[i] for i in p.vertices):
            continue
        if p.index in head_island or (p.center.z > head_z + 0.06 and abs(p.center.x) < 0.12):
            head_faces.add(p.index)
        elif all(legs[i] for i in p.vertices):
            leg_faces.add(p.index)
        else:
            arm_faces.add(p.index)
    h = duplicate(body, "Head_skin")
    a = duplicate(body, "Arms_skin")
    l = duplicate(body, "Legs_skin")
    keep_faces(h, head_faces)
    keep_faces(a, arm_faces)
    keep_faces(l, leg_faces)
    bpy.data.objects.remove(body, do_unlink=True)
    return h, a, l


def reslot_shoes(shoes, sock_faces_z=None, slots_src=None):
    """Material slots by geometry + texture after the sock was cut: band (z near collar), sole (bottom), accent (dark texture)."""
    sm = shoes.data
    tex = load_px(os.path.join(MPFB_DATA, "clothes", "shoes05", "shoes05_diffuse.png"))
    th, tw = tex.shape[:2]
    isl = uv_islands(shoes)
    for faces in isl:
        zmax = max(sm.vertices[i].co.z for f in faces for i in sm.polygons[f].vertices)
        zmin = min(sm.vertices[i].co.z for f in faces for i in sm.polygons[f].vertices)
        uvs = [face_uv_center(sm, sm.polygons[f]) for f in faces]
        is_sock = all(u.x > 0.76 and u.y < 0.15 for u in uvs)
        for f, uvc in zip(faces, uvs):
            p = sm.polygons[f]
            if is_sock:
                p.material_index = 3
            elif zmax < 0.03:
                p.material_index = 2
            elif len(faces) <= 8:
                p.material_index = 1
            else:
                px = tex[min(th - 1, int(uvc.y * th)), min(tw - 1, int(uvc.x * tw))]
                lum = px[0] * 0.21 + px[1] * 0.72 + px[2] * 0.07
                low = max(sm.vertices[i].co.z for i in p.vertices) < 0.024
                p.material_index = 2 if low else (1 if lum < 0.16 else 0)


def trim_hair(hair, z_cut):
    """Shorten long hair to just past the shoulders so it clears the swing; card ends keep their UV tips."""
    bm = bm_of(hair)
    kill = [f for f in bm.faces if all(v.co.z < z_cut for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm_write(bm, hair)


def clean_groups(ob, rig):
    names = {b.name for b in rig.data.bones}
    for g in list(ob.vertex_groups):
        if g.name not in names:
            ob.vertex_groups.remove(g)


def report(name, parts):
    tris = 0
    for o in parts:
        t = sum(len(p.vertices) - 2 for p in o.data.polygons)
        tris += t if not o.name.startswith("Club_") else 0
        print("[build] %-14s tris %6d mats %s keys %s" % (o.name, t, [m.name for m in o.data.materials],
                                                           [k.name for k in o.data.shape_keys.key_blocks] if o.data.shape_keys else []))
    print("[build] %s total tris (no clubs, all parts) %d" % (name, tris))


def placeholder_idle(rig):
    """Relaxed arms-down 'Idle' take so the FBX always has one (the animation pass replaces it)."""
    act = bpy.data.actions.get("Idle") or bpy.data.actions.new("Idle")
    rig.animation_data_create()
    rig.animation_data.action = act
    G.mode(rig, "POSE")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.matrix_basis.identity()
    for S, sx in (("L", 1.0), ("R", -1.0)):
        G._aim(rig, "UpperArm." + S, (sx * 0.2, 0.02, -1.0))
        G._aim(rig, "LowerArm." + S, (sx * 0.12, -0.25, -1.0))
    for f in (1, 60):
        for pb in rig.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=f)
    bpy.ops.object.mode_set(mode="OBJECT")
    act.use_fake_user = True


def export_fbx(rig, parts, path):
    """FBX for Unity, one mesh per part on the shared rig; every action that fits the rig becomes its own take."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Unity builds the Humanoid avatar from the bind pose, the T-pose rest; every action with a fake user (at least
    # Idle) still goes out as its own take.
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    rig.select_set(True)
    for o in parts:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="FACE", use_tspace=False, use_armature_deform_only=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=True, bake_anim_use_all_bones=True,
                             bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.0, path_mode="STRIP", embed_textures=False)
    print("[build] exported", path)


# ------------------------------------------------------------------ textures

def textures(name, c, body, shoes, shoe_tex):
    """Writes Unity textures; returns paths. Character-specific under <Name>/Textures, garments under Shared/Textures."""
    ctex = os.path.join(UNITY_CHAR, name, "Textures")
    os.makedirs(ctex, exist_ok=True)
    os.makedirs(SHARED_TEX, exist_ok=True)
    T = {}
    skin = load_px(os.path.join(MPFB_DATA, "skins", c["skin"].split("/")[0], skin_file(c["skin"])))[..., :3]
    if name == "Maya":
        afr = load_px(os.path.join(MPFB_DATA, "skins", "young_african_female", skin_file("young_african_female/x")))[..., :3]
        skin = skin * 0.6 + resize(afr, skin.shape[0]) * 0.4
    skin = skin * np.array(c["skin_tone"], np.float32)
    T["skin_src"] = save_px(skin, os.path.join(ctex, "%s_Skin_Albedo.png" % name))
    T["skin"] = T["skin_src"]
    T["face"] = save_px(skin, os.path.join(ctex, "%s_Face_Albedo.png" % name))
    eye_src = os.path.join(MPFB_DATA, "eyes", "materials", "%s_eye.png" % c["eyes"])
    T["eye"] = save_px(eye_tone(load_px(eye_src)[..., :3], c.get("iris", (0.5, 0.33, 0.2))), os.path.join(ctex, "%s_Eye_Albedo.png" % name))
    for key, rel in (("brow", c["eyebrows"]), ("lash", c["eyelashes"]), ("hair", c["hair"])):
        kind = {"brow": "eyebrows", "lash": "eyelashes", "hair": "hair"}[key]
        folder = os.path.join(MPFB_DATA, kind, rel.split("/")[0])
        pngs = [f for f in os.listdir(folder) if f.endswith(".png") and "normal" not in f and "thumb" not in f]
        src = load_px(os.path.join(folder, sorted(pngs)[0]))
        T[key] = save_px(src, os.path.join(ctex, "%s_%s.png" % (name, key.capitalize())))
        nrm = [f for f in os.listdir(folder) if "normal" in f]
        if nrm:
            T[key + "_n"] = save_px(load_px(os.path.join(folder, nrm[0]))[..., :3], os.path.join(ctex, "%s_%s_Normal.png" % (name, key.capitalize())), srgb=False)
    # shared garments (only written once)
    polo_dir = os.path.join(MPFB_DATA, "clothes", "namuhekam_male_polo_shirt")
    p = os.path.join(SHARED_TEX, "Polo_Albedo.png")
    if not os.path.exists(p):
        save_px(detail_albedo(load_px(os.path.join(polo_dir, "Polo_Base_Color.png"))[..., :3], radius=20), p)
        save_px(load_px(os.path.join(polo_dir, "Polo_Normal_OpenGL.png"))[..., :3], os.path.join(SHARED_TEX, "Polo_Normal.png"), srgb=False)
    T["polo"] = p
    T["polo_n"] = os.path.join(SHARED_TEX, "Polo_Normal.png")
    tee_dir = os.path.join(MPFB_DATA, "clothes", "toigo_basic_tucked_t-shirt")
    p = os.path.join(SHARED_TEX, "Tee_Albedo.png")
    if not os.path.exists(p):
        save_px(detail_albedo(load_px(os.path.join(tee_dir, "T-shirt_basic.png"))[..., :3], radius=20), p)
        bump = luminance(load_px(os.path.join(tee_dir, "T-shirt_basic-bump.png"))[..., :3])
        save_px(height_to_normal(bump, 3.0), os.path.join(SHARED_TEX, "Tee_Normal.png"), srgb=False)
    T["tee"] = p
    pants_dir = os.path.join(MPFB_DATA, "clothes", "toigo_wool_pants")
    p = os.path.join(SHARED_TEX, "Pants_Albedo.png")
    if not os.path.exists(p):
        src = load_px(os.path.join(pants_dir, "Pants_wool.png"))[..., :3]
        save_px(detail_albedo(src, radius=24), p)
        save_px(height_to_normal(luminance(src), 4.0), os.path.join(SHARED_TEX, "Pants_Normal.png"), srgb=False)
    T["pants"] = p
    p = os.path.join(SHARED_TEX, "Shoes_Albedo.png")
    if not os.path.exists(p):
        save_px(detail_albedo(shoe_tex[..., :3], radius=10, lo=0.7, hi=1.1), p)
        save_px(height_to_normal(luminance(shoe_tex[..., :3]), 2.5), os.path.join(SHARED_TEX, "Shoes_Normal.png"), srgb=False)
    T["shoes"] = p
    p = os.path.join(SHARED_TEX, "Socks_Albedo.png")
    if not os.path.exists(p):
        knit = knit_tile(512)
        save_px(np.repeat(knit[..., None], 3, 2) * 0.12 + 0.86, p)
        save_px(height_to_normal(knit, 6.0), os.path.join(SHARED_TEX, "Socks_Normal.png"), srgb=False)
    T["socks"] = p
    p = os.path.join(SHARED_TEX, "Belt_Albedo.png")
    if not os.path.exists(p):
        n = noise_tile(512, 24, 7, 5)
        leather = 0.82 + 0.12 * n
        rows = np.arange(512)[:, None] / 512.0
        stitch = ((np.abs(rows - 0.12) < 0.012) | (np.abs(rows - 0.88) < 0.012)) & ((np.arange(512)[None, :] // 10) % 2 == 0)
        leather = np.where(stitch, leather * 1.12, leather)
        save_px(np.repeat(leather[..., None], 3, 2), p)
        save_px(height_to_normal(n * 0.6 + stitch * 0.5, 3.0), os.path.join(SHARED_TEX, "Belt_Normal.png"), srgb=False)
    T["belt"] = p
    p = os.path.join(SHARED_TEX, "Logo_Mask.png")
    if not os.path.exists(p):
        save_px(logo_mask(256), p)
    T["logo"] = p
    return T


def eye_tone(rgb, iris):
    """MakeHuman's brown iris reads orange-red: remap iris pixels (saturated, darker than sclera) to a natural tone."""
    L = luminance(rgb)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    m = np.clip((sat - 0.12) / 0.15, 0, 1) * np.clip((0.75 - L) / 0.2, 0, 1)
    m = blur(m[..., None], 2)[..., 0][..., None]
    tone = np.array(iris, np.float32) * (L[..., None] / max(1e-3, float(np.mean(L[m[..., 0] > 0.5])) if np.any(m > 0.5) else 1.0))
    return rgb * (1 - m) + np.clip(tone, 0, 1) * m


def skin_file(rel):
    folder = os.path.join(MPFB_DATA, "skins", rel.split("/")[0])
    pngs = sorted(f for f in os.listdir(folder) if f.endswith(".png") and "diffuse" in f)
    return pngs[0] if pngs else sorted(f for f in os.listdir(folder) if f.endswith(".png"))[0]


def knit_tile(size):
    y, x = np.mgrid[0:size, 0:size] / size
    rib = 0.5 + 0.5 * np.cos(x * 2 * np.pi * 48)
    loops = 0.5 + 0.5 * np.cos(y * 2 * np.pi * 96 + np.sin(x * 2 * np.pi * 48) * 1.2)
    return (rib * 0.6 + loops * 0.4).astype(np.float32)


def logo_mask(size):
    """White crest on transparent: ring + pennant flag (the Puttshack Pro mark at chest size)."""
    y, x = (np.mgrid[0:size, 0:size] + 0.5) / size * 2 - 1
    r = np.sqrt(x * x + y * y)
    ring = (r < 0.92) & (r > 0.74)
    pole = (np.abs(x + 0.12) < 0.06) & (y > -0.55) & (y < 0.6)
    flag = (x > -0.12) & (x < 0.45) & (y < 0.6) & (y > 0.6 - 0.5 * (1 - (x + 0.12) / 0.57))
    ball = (np.sqrt((x - 0.22) ** 2 + (y + 0.42) ** 2) < 0.13)
    a = (ring | pole | flag | ball).astype(np.float32)
    a = blur(a[..., None], 1)[..., 0]
    out = np.ones((size, size, 4), np.float32)
    out[..., 3] = np.clip(a * 1.4, 0, 1)
    return out


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = [a for a in argv if not a.startswith("--")] or ["Caleb"]
    for n in names:
        build(n, export="--no-export" not in argv, review="--review" in argv)
