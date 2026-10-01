"""Puttshack Pro - Pebble Beach (Stillwater Cove) scenery for the par-3 "Stillwater Cliffs".

Builds everything around the gameplay hole that the analytic ground mesh does not cover: the surround terrain
(continuing cliffs, beach and sea floor of the cove, the far shore with the 18th hole and the Lodge), rocks and sea
stacks, the pier and boats, houses, Monterey cypress (three hero variants used for the gameplay trees plus scenery
instances), distant forest and the hazy headlands across Carmel Bay, and the ocean plane.

Coordinates: everything is authored in Unity world coordinates (x right, y up, z toward the green) and converted with
U2B. FBX export uses axis_forward=-Z / axis_up=Y with bake_space_transform (like pp_lib); Unity then imports Blender
(x, y, z) as (x, z, y) with identity transforms (verified with a test cube), so U2B(x, y, z) = (x, z, y).

The surround terrain is a set of concentric rectangular rings around the analytic ground mesh rectangle. Ring 0 uses
exactly the analytic grid vertices (0.8 m) so the seam is shared vertex-for-vertex; heights blend from the gameplay
TerrainModel (pp_pebble_terrain) into the free-form coast over the first 40 m, and normals use the same central
differences as TerrainModel.Normal so shading is continuous across the seam.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise

ROOT = "/Users/calebsnyder/Real-Golf"
sys.path.insert(0, os.path.join(ROOT, "ArtSource/Blender/scripts"))
import pp_pebble_terrain as T  # noqa: E402

HOLE = json.load(open(os.path.join(ROOT, "ArtSource/Blender/pebble_hole.json")))
FEATS = T.features(HOLE)
SEA = -10.0
GX0, GZ0, GX1, GZ1, GNX, GNZ = T.ground_grid(HOLE)
STEP = 0.8
SCENERY_DIR = os.path.join(ROOT, "Assets/_Game/Art/Course/PebbleBeach")
CYPRESS_DIR = os.path.join(ROOT, "Assets/_Game/Resources/Models/Course/Cypress")
TEE_MARKER_DIR = os.path.join(ROOT, "Assets/_Game/Resources/Models/Course/TeeMarker")
TEX_DIR = os.path.join(ROOT, "Assets/_Game/Art/Course/PebbleBeach/Textures")
COURSE_TEX_DIR = os.path.join(ROOT, "Assets/_Game/Resources/Models/Course/Textures")


def U2B(x, y, z):
    return (x, z, y)


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# ================================================================ coastline (Unity xz, sea on the left of travel)
CLIFF_CTRL = [(-160, -520), (-110, -430), (-75, -330), (-55, -220)] + [
    (-42, -110), (-38, -60), (-34, -25), (-32, 5), (-31.5, 30), (-33, 52), (-37, 70), (-34, 88), (-28.5, 104),
    (-24.5, 120), (-23.5, 136), (-25, 152), (-29, 166), (-35, 178), (-41, 192), (-45, 210), (-44, 235), (-38, 260)] + [
    (-31, 284), (-30, 306), (-40, 326), (-62, 342), (-96, 352), (-132, 360), (-168, 366), (-205, 370), (-242, 368),
    (-280, 362), (-320, 358), (-360, 360), (-400, 358), (-440, 348), (-478, 330), (-508, 304), (-530, 272),
    (-552, 262), (-575, 285), (-605, 345), (-640, 430), (-675, 540), (-710, 680), (-750, 860), (-790, 1060)]

# the shore line sits further out where the cove has a beach (north shore by the Beach Club and pier)
SHORE_OFFSET = [10, 10, 10, 10] + [10, 9, 9, 9, 8.5, 8.5, 8, 8, 6.5, 5.5, 5, 5, 6, 8, 11, 15, 18, 20] + [
    18, 16, 18, 24, 30, 34, 30, 22, 14, 10, 9, 9, 9, 10, 10, 9, 8, 8, 9, 10, 10, 10, 10, 10, 10]
assert len(SHORE_OFFSET) == len(CLIFF_CTRL)


def offset_left(ctrl, offsets):
    out = []
    n = len(ctrl)
    for i, (x, z) in enumerate(ctrl):
        a = ctrl[max(0, i - 1)]
        b = ctrl[min(n - 1, i + 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dz)
        nx, nz = -dz / L, dx / L
        out.append((x + nx * offsets[i], z + nz * offsets[i]))
    return out


HOLE_SHORE = [(-52, -110), (-47, -60), (-43, -25), (-41, 5), (-40, 30), (-41.5, 52), (-45, 70), (-42, 88), (-35, 104),
              (-30, 120), (-28.5, 136), (-30, 152), (-35, 166), (-43, 178), (-52, 192), (-60, 210), (-62, 235),
              (-58, 260)]
_shore_ext = offset_left(CLIFF_CTRL, SHORE_OFFSET)
SHORE_CTRL = _shore_ext[:4] + HOLE_SHORE + _shore_ext[22:]
CLIFF_PATH = T.catmull_rom(CLIFF_CTRL, 4)
SHORE_PATH = T.catmull_rom(SHORE_CTRL, 4)


# ================================================================ free-form height field
def land_np(x, z):
    """Free-form inland heights: gentle links undulation, the Lodge bluff, Del Monte Forest hills to the east."""
    h = np.zeros(x.shape)
    for i in range(x.size):
        p = Vector((x.flat[i] / 95.0, z.flat[i] / 95.0, 0.37))
        q = Vector((x.flat[i] / 33.0, z.flat[i] / 33.0, 4.1))
        h.flat[i] = 1.3 * noise.noise(p) + 0.45 * noise.noise(q)
    h += 0.04 * np.maximum(0, x - 90) * (1 - smooth(500, 900, x)) + 18 * smooth(500, 900, x)
    h += 0.02 * np.maximum(0, z - 470)
    r = np.hypot(x + 215, z - 425)
    h += 3.5 * (1 - smooth(50, 150, r))
    return h


def free_height_np(x, z):
    base_feats = [f for f in FEATS if f["kind"] in ("Plateau", "Tilt", "Bowl")]
    h = T.height_np(base_feats, x, z) + land_np(x, z)
    sd_cliff = T.signed_distance_left_np(CLIFF_PATH, x, z)
    w = T.ramp_np(sd_cliff, 0, 7)
    h = h + (-8.6 - h) * w
    sd_shore = T.signed_distance_left_np(SHORE_PATH, x, z)
    w = T.ramp_np(sd_shore, 0, 24)
    h = h + (-12.5 - h) * w
    h = h - np.clip(sd_shore - 24, 0, 600) * 0.045
    # Carmel Point / Point Lobos across the bay: a hazy wooded ridge on the far side of the water
    # (crest ~130 m inland, falling away behind it so the mesh edge never forms the skyline)
    edge = -1060 + 70 * np.sin(z / 260.0) + 35 * np.sin(z / 95.0 + 1.3)
    crest = 62 + 28 * np.sin(z / 170.0 + 0.4) + 14 * np.sin(z / 61.0)
    u = edge - x
    far = np.where(u > 0, -40 + (crest + 40) * (smooth(0, 130, u) - 0.75 * smooth(150, 240, u)), -1e9)
    return np.maximum(h, far)


def rect_distance(x, z):
    dx = np.maximum(np.maximum(GX0 - x, x - GX1), 0)
    dz = np.maximum(np.maximum(GZ0 - z, z - GZ1), 0)
    return np.hypot(dx, dz)


def height_np(x, z):
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    w = smooth(0, 40, rect_distance(x, z))
    ha = T.height_np(FEATS, x, z)
    out = ha.copy()
    idx = np.nonzero(w.ravel() > 0)[0]
    fx, fz, fw, fo, fa = x.ravel(), z.ravel(), w.ravel(), out.reshape(-1), ha.ravel()
    for s in range(0, idx.size, 6000):
        c = idx[s:s + 6000]
        fo[c] = fa[c] * (1 - fw[c]) + free_height_np(fx[c], fz[c]) * fw[c]
    return out


def height_at(x, z):
    return float(height_np(np.array([x]), np.array([z]))[0])


def normals_np(x, z, e=0.02):
    hx = height_np(x + e, z) - height_np(x - e, z)
    hz = height_np(x, z + e) - height_np(x, z - e)
    n = np.stack([-hx, np.full(x.shape, 2 * e), -hz], -1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


# ================================================================ materials
def material(name, rgb, rough=0.7, metal=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Alpha"].default_value = alpha
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    return m


MATS = {}


def make_materials():
    spec = {
        "PB_Terrain": ((0.25, 0.32, 0.14), 0.9),
        "PB_Ocean": ((0.03, 0.25, 0.33), 0.05),
        "PB_Rock": ((0.36, 0.33, 0.29), 0.85),
        "PB_RockDark": ((0.22, 0.2, 0.18), 0.85),
        "PB_Bark": ((0.035, 0.032, 0.03), 0.9),
        # Unity swaps this for Puttshack/Foliage, which reads the vertex colours (R occlusion, G tip tint).
        "PB_Foliage": ((0.045, 0.082, 0.03), 0.9),
        "PB_MarkerBody": ((0.03, 0.06, 0.042), 0.45),
        "PB_Brass": ((0.62, 0.45, 0.2), 0.35),
        "PB_Stucco": ((0.84, 0.81, 0.75), 0.75),
        "PB_StuccoWarm": ((0.86, 0.78, 0.66), 0.75),
        "PB_Roof": ((0.3, 0.33, 0.36), 0.6),
        "PB_RoofRed": ((0.42, 0.22, 0.16), 0.7),
        "PB_Window": ((0.06, 0.08, 0.1), 0.15),
        "PB_Trim": ((0.92, 0.92, 0.9), 0.6),
        "PB_Wood": ((0.36, 0.31, 0.26), 0.85),
        "PB_Hull": ((0.92, 0.92, 0.9), 0.3),
        "PB_HullNavy": ((0.05, 0.09, 0.2), 0.35),
        "PB_Mast": ((0.8, 0.8, 0.78), 0.4),
        "PB_Concrete": ((0.62, 0.6, 0.56), 0.9),
        "PB_FarLand": ((0.3, 0.33, 0.24), 0.95),
        "PB_Flag": ((0.95, 0.85, 0.2), 0.6),
    }
    for k, (rgb, r) in spec.items():
        MATS[k] = material(k, rgb, r)


# ================================================================ mesh helpers
def M_at(ux, uy, uz, yaw=0.0, scale=1.0):
    """Blender-space placement matrix for a Unity position; yaw in degrees about the vertical axis."""
    S = Matrix.Diagonal((scale, scale, scale, 1.0)) if not isinstance(scale, tuple) else \
        Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    return Matrix.Translation(Vector(U2B(ux, uy, uz))) @ Matrix.Rotation(math.radians(yaw), 4, "Z") @ S


class Builder:
    """Accumulates geometry in Blender coordinates (local pieces are authored Z-up with outward normals) and emits
    one object with per-face materials. Unity-space vertex lists (terrain-like sheets) go through add_unity_sheet,
    whose Unity clockwise-from-above windings are reversed (U2B keeps the ground plane and swaps y/z)."""

    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []
        self.fmat = []
        self.mats = []
        self.vnormals = []
        self.vcols = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def add_mesh(self, verts, faces, mat, normals=None, colors=None):
        base = len(self.verts)
        self.verts.extend(verts)
        self.vnormals.extend(normals if normals is not None else [None] * len(verts))
        self.vcols.extend(colors if colors is not None else [None] * len(verts))
        m = self.mi(mat) if isinstance(mat, str) else None
        for fi, f in enumerate(faces):
            self.faces.append(tuple(base + i for i in f))
            self.fmat.append(m if m is not None else self.mi(mat[fi]))
        return base

    def add_unity_sheet(self, verts, faces, mat):
        return self.add_mesh([U2B(*v) for v in verts], [tuple(reversed(f)) for f in faces], mat)

    def add_bm(self, bm, mat, M=None, normals_from=None, color=None):
        """Append a bmesh. mat is a material name or a callable(face) -> name. normals_from=(center) gives
        spherical normals from that local point (soft foliage shading). color is a callable(local co) -> RGBA
        vertex colour."""
        bm.verts.index_update()
        vs, ns, cs = [], [], []
        for v in bm.verts:
            co = v.co if M is None else M @ v.co
            vs.append((co.x, co.y, co.z))
            if normals_from is not None:
                n = (v.co - Vector(normals_from)).normalized()
                if M is not None:
                    n = (M.to_3x3() @ n).normalized()
                ns.append(tuple(n))
            if color is not None:
                cs.append(color(v.co))
        fs = [tuple(v.index for v in f.verts) for f in bm.faces]
        mats = mat if isinstance(mat, str) else [mat(f) for f in bm.faces]
        return self.add_mesh(vs, fs, mats, ns if normals_from is not None else None, cs if color is not None else None)

    def finish(self, coll, smooth_shade=True, box_uv=0.25):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata(self.verts, [], self.faces)
        for m in self.mats:
            me.materials.append(MATS[m])
        me.polygons.foreach_set("material_index", self.fmat)
        me.polygons.foreach_set("use_smooth", [smooth_shade] * len(self.faces))
        if box_uv:
            uvl = me.uv_layers.new(name="UVMap")
            for poly in me.polygons:
                n = poly.normal
                ax = max(range(3), key=lambda a: abs(n[a]))
                u, v = [(1, 2), (0, 2), (0, 1)][ax]
                for li in poly.loop_indices:
                    co = me.vertices[me.loops[li].vertex_index].co
                    uvl.data[li].uv = (co[u] * box_uv, co[v] * box_uv)
        if any(n is not None for n in self.vnormals):
            me.update()
            auto = [tuple(v.normal) for v in me.vertices]
            me.normals_split_custom_set_from_vertices(
                [n if n is not None else auto[i] for i, n in enumerate(self.vnormals)])
        if any(c is not None for c in self.vcols):
            ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
            ca.data.foreach_set("color", np.array([c if c is not None else (1.0, 0.5, 0.0, 1.0) for c in self.vcols],
                                                  dtype=np.float32).ravel())
        me.update()
        ob = bpy.data.objects.new(self.name, me)
        coll.objects.link(ob)
        return ob


def new_coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def clear_coll(c):
    for o in list(c.objects):
        me = o.data if o.type == "MESH" else None
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)


# ================================================================ surround terrain
def ring_offsets():
    ds = [0.0]
    ss = []
    d = 0.0
    k = 0
    while d < 1150:
        if d < 450:
            cap = 6.0
        else:
            cap = min(60.0, 6.0 + 0.12 * (d - 450))
        s = min(STEP * 1.12 ** k, cap)
        ss.append(s)
        d += s
        ds.append(d)
        k += 1
    return ds, ss


def ring_sides(d, s, k):
    """Four sides of ring k (bottom, right, top, left), each a list of (x, z) including both corners, CCW in xz."""
    x0, z0, x1, z1 = GX0 - d, GZ0 - d, GX1 + d, GZ1 + d
    if k == 0:
        bottom = [(GX0 + i * STEP, GZ0) for i in range(GNX)]
        right = [(GX1, GZ0 + j * STEP) for j in range(GNZ)]
        top = [(GX0 + i * STEP, GZ1) for i in range(GNX)][::-1]
        left = [(GX0, GZ0 + j * STEP) for j in range(GNZ)][::-1]
        bottom[-1] = (GX1, GZ0)
        right[-1] = (GX1, GZ1)
        top[0] = (GX1, GZ1)
        left[0] = (GX0, GZ1)
        return [bottom, right, top, left]

    def side(ax, az, bx, bz):
        L = math.hypot(bx - ax, bz - az)
        m = max(1, int(round(L / s)))
        return [(ax + (bx - ax) * i / m, az + (bz - az) * i / m) for i in range(m + 1)]

    return [side(x0, z0, x1, z0), side(x1, z0, x1, z1), side(x1, z1, x0, z1), side(x0, z1, x0, z0)]


def zipper(A, B):
    """Triangles between inner polyline A and outer polyline B (index lists) sharing start and end alignment."""
    tris = []
    i = j = 0
    ma, mb = len(A) - 1, len(B) - 1
    while i < ma or j < mb:
        if j == mb or (i < ma and (i + 1) / ma <= (j + 1) / mb):
            tris.append((A[i], A[i + 1], B[j]))
            i += 1
        else:
            tris.append((A[i], B[j + 1], B[j]))
            j += 1
    return tris


def surround_regions(x, z, d):
    """Vertex colours (R fairway, G green, B bunker, A deep rough) and stripe UVs for the scenery terrain."""
    n = x.size
    col = np.zeros((n, 4))
    # native coastal grasses near the gameplay mesh match its out-of-bounds deep rough
    col[:, 3] = 1 - smooth(30, 60, d)
    # broad native patches further out
    for i in range(n):
        v = noise.noise(Vector((x[i] / 70.0, z[i] / 70.0, 9.3)))
        if d[i] > 60:
            col[i, 3] = max(col[i, 3], float(np.clip((v + 0.1) * 3, 0, 1)))
    uv = np.stack([z, x], -1)

    def poly_mask(poly, soft=1.5):
        inside = T.contains_np(poly, x, z)
        de = T.distance_to_edge_np(poly, x, z)
        return np.where(inside, smooth(-soft, soft, de), 0.0)

    # 18th fairway along the far shore, its green beside the Lodge
    f18 = [(-505, 340), (-470, 352), (-420, 366), (-360, 378), (-310, 386), (-275, 392), (-262, 410), (-300, 418),
           (-360, 412), (-420, 400), (-470, 384), (-500, 366)]
    g18 = [(-262 + 15 * math.cos(a) * math.cos(0.3) - 11 * math.sin(a) * math.sin(0.3),
            401 + 15 * math.cos(a) * math.sin(0.3) + 11 * math.sin(a) * math.cos(0.3))
           for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)]
    b18 = [(-276 + 5 * math.cos(a), 388 + 3 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 16, endpoint=False)]
    # previous hole's green and fairway behind our tee; a parallel hole to the right
    g6 = [(-14 + 13 * math.cos(a), -96 + 10 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)]
    f6 = [(-30, -112), (2, -112), (8, -170), (6, -260), (-18, -300), (-40, -260), (-44, -170)]
    f8 = [(112, -120), (150, -120), (156, 40), (152, 230), (128, 260), (106, 230), (100, 40)]
    g8 = [(130 + 13 * math.cos(a), 282 + 11 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)]
    for poly in (f18, f6, f8):
        w = poly_mask(poly)
        col[:, 0] = np.maximum(col[:, 0], w)
        col[:, 3] *= 1 - w
    for poly in (g18, g6, g8):
        w = poly_mask(poly, 0.6)
        col[:, 1] = np.maximum(col[:, 1], w)
        col[:, 0] *= 1 - w
        col[:, 3] *= 1 - w
    w = poly_mask(b18, 0.5)
    col[:, 2] = np.maximum(col[:, 2], w)
    col[:, 0] *= 1 - w
    col[:, 1] *= 1 - w
    return col, uv


def build_terrain(coll):
    ds, ss = ring_offsets()
    pts = []
    rings = []  # rings[k] = 4 lists of vertex indices (each side incl. corners)
    for k, d in enumerate(ds):
        sides = ring_sides(d, ss[k] if k < len(ss) else ss[-1], k)
        idx_sides = []
        corner_idx = {}
        for sd in sides:
            ids = []
            for p in sd:
                key = (round(p[0], 4), round(p[1], 4))
                if key in corner_idx:
                    ids.append(corner_idx[key])
                    continue
                pts.append(p)
                ids.append(len(pts) - 1)
                corner_idx[key] = ids[-1]
            idx_sides.append(ids)
        rings.append(idx_sides)
    tris = []
    for k in range(len(rings) - 1):
        for s in range(4):
            tris.extend((a, c, b) for a, b, c in zipper(rings[k][s], rings[k + 1][s]))
    P = np.array(pts)
    x, z = P[:, 0], P[:, 1]
    h = height_np(x, z)
    nrm = normals_np(x, z)
    d = rect_distance(x, z)
    col, uv = surround_regions(x, z, d)

    me = bpy.data.meshes.new("PB_Terrain")
    me.from_pydata([U2B(x[i], h[i], z[i]) for i in range(len(x))], [], tris)
    me.materials.append(MATS["PB_Terrain"])
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    ca.data.foreach_set("color", col.astype(np.float32).ravel())
    uvl = me.uv_layers.new(name="UVMap")
    loop_v = np.zeros(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", loop_v)
    uvl.data.foreach_set("uv", uv[loop_v].astype(np.float32).ravel())
    bn = np.stack([nrm[:, 0], nrm[:, 2], nrm[:, 1]], -1)
    me.normals_split_custom_set_from_vertices([tuple(v) for v in bn])
    me.update()
    ob = bpy.data.objects.new("PB_Terrain", me)
    coll.objects.link(ob)
    print("[terrain] rings %d verts %d tris %d" % (len(rings), len(pts), len(tris)))
    return ob


# ================================================================ ocean textures (tileable, generated)
def _periodic_field(n, slope, seed, direction=None, spread=0.0, cutoff=2.0):
    rng = np.random.default_rng(seed)
    kx = np.fft.fftfreq(n) * n
    KX, KY = np.meshgrid(kx, kx)
    k = np.hypot(KX, KY)
    amp = np.where(k > cutoff, k ** -slope, 0.0)
    if direction is not None:
        c = (KX * direction[0] + KY * direction[1]) / np.maximum(k, 1e-9)
        amp *= (1 - spread) + spread * c * c
    spec = amp * (rng.normal(size=(n, n)) + 1j * rng.normal(size=(n, n)))
    f = np.real(np.fft.ifft2(spec))
    return (f - f.mean()) / f.std()


def _save_png(path, rgb):
    n = rgb.shape[0]
    img = bpy.data.images.new(os.path.basename(path), n, n, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    px = np.ones((n, n, 4), dtype=np.float32)
    px[..., :3] = rgb
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def make_textures():
    os.makedirs(TEX_DIR, exist_ok=True)
    n = 512
    h = _periodic_field(n, 2.75, 7, direction=(0.8, 0.6), spread=0.6, cutoff=2.5)
    h += 0.12 * _periodic_field(n, 2.0, 11, cutoff=10.0)
    scale = 0.9
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * scale
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * scale
    nrm = np.stack([-dx, -dy, np.ones_like(h)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    _save_png(os.path.join(TEX_DIR, "ocean_normal.png"), nrm * 0.5 + 0.5)
    f = _periodic_field(n, 1.75, 23, cutoff=3.0)
    ridge = 1 - np.abs(np.tanh(f * 0.9))
    cells = _periodic_field(n, 2.2, 31, cutoff=2.0)
    foam = np.clip(ridge * 0.8 + 0.25 * cells, 0, 1)
    foam = (foam - foam.min()) / (foam.max() - foam.min())
    _save_png(os.path.join(TEX_DIR, "foam_noise.png"), np.repeat(foam[..., None], 3, -1))
    print("[textures] ocean_normal.png, foam_noise.png")


def _worley(n, cells, seed):
    """Distance (in cell units) to the nearest jittered feature point, periodic over the tile."""
    rng = np.random.default_rng(seed)
    jit = rng.random((cells, cells, 2))
    y, x = np.mgrid[0:n, 0:n] / n * cells
    ix, iy = np.floor(x).astype(int), np.floor(y).astype(int)
    best = np.full((n, n), 9.0)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cx, cy = (ix + dx) % cells, (iy + dy) % cells
            best = np.minimum(best, np.hypot(x - (ix + dx + jit[cy, cx, 0]), y - (iy + dy + jit[cy, cx, 1])))
    return best


def make_foliage_textures():
    """Tileable cypress leaf-cluster relief (~1.2 m per tile at the shader's default scale): domed clumps at two
    scales plus fine scale-leaf grain. foliage_noise.png holds the height, foliage_normal.png its OpenGL normal."""
    os.makedirs(COURSE_TEX_DIR, exist_ok=True)
    n = 512
    dome = lambda d, r: np.sqrt(np.clip(1 - (d / r) ** 2, 0, 1))  # noqa: E731
    h = 0.6 * dome(_worley(n, 7, 41), 0.85) + 0.4 * dome(_worley(n, 19, 43), 0.8)
    h += 0.06 * _periodic_field(n, 1.6, 47, cutoff=24.0)
    h = (h - h.min()) / (h.max() - h.min())
    scale = 14.0
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * scale
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * scale
    nrm = np.stack([-dx, -dy, np.ones_like(h)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    _save_png(os.path.join(COURSE_TEX_DIR, "foliage_normal.png"), nrm * 0.5 + 0.5)
    _save_png(os.path.join(COURSE_TEX_DIR, "foliage_noise.png"), np.repeat(h[..., None], 3, -1))
    print("[textures] foliage_normal.png, foliage_noise.png")


# ================================================================ ocean
def build_ocean(coll, cx=-250.0, cz=250.0, half=1900.0, n=24):
    b = Builder("PB_Ocean")
    verts = []
    for j in range(n + 1):
        for i in range(n + 1):
            verts.append((cx - half + 2 * half * i / n, SEA, cz - half + 2 * half * j / n))
    faces = []
    for j in range(n):
        for i in range(n):
            a = j * (n + 1) + i
            faces.append((a, a + n + 1, a + n + 2, a + 1))
    b.add_unity_sheet(verts, faces, "PB_Ocean")
    return b.finish(coll, smooth_shade=False, box_uv=0)


# ================================================================ primitives (Blender local space, Z up, outward normals)
def bm_box(sx, sy, sz, cx=0.0, cy=0.0, cz=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx + cx, v.co.y * sy + cy, v.co.z * sz + cz))
    return bm


def bm_hip_roof(w, d, h, z0, overhang=0.5, cx=0.0, cy=0.0):
    W, D = w / 2 + overhang, d / 2 + overhang
    r = max(0.02, W - D)
    bm = bmesh.new()
    v = [bm.verts.new((x + cx, y + cy, z)) for x, y, z in
         [(-W, -D, z0), (W, -D, z0), (W, D, z0), (-W, D, z0), (-r, 0, z0 + h), (r, 0, z0 + h)]]
    for f in [(0, 1, 5, 4), (1, 2, 5), (2, 3, 4, 5), (3, 0, 4), (3, 2, 1, 0)]:
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bm_gable_roof(w, d, h, z0, overhang=0.5, cx=0.0, cy=0.0):
    W, D = w / 2 + overhang, d / 2 + overhang
    bm = bmesh.new()
    v = [bm.verts.new((x + cx, y + cy, z)) for x, y, z in
         [(-W, -D, z0), (W, -D, z0), (W, D, z0), (-W, D, z0), (-W, 0, z0 + h), (W, 0, z0 + h)]]
    for f in [(0, 1, 5, 4), (1, 2, 5), (2, 3, 4, 5), (3, 0, 4), (3, 2, 1, 0)]:
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def add_tube(bm, p0, p1, r0, r1, sides=6):
    d = p1 - p0
    if d.length < 1e-6:
        return
    d.normalize()
    a = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
    u = d.cross(a).normalized()
    w = d.cross(u)
    ts = [2 * math.pi * i / sides for i in range(sides)]
    ring0 = [bm.verts.new(p0 + (u * math.cos(t) + w * math.sin(t)) * r0) for t in ts]
    ring1 = [bm.verts.new(p1 + (u * math.cos(t) + w * math.sin(t)) * r1) for t in ts]
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new([ring0[i], ring0[j], ring1[j], ring1[i]])


def bm_blob(rx, ry, rz, seed, subdiv=2, rough=0.2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off = Vector((seed * 1.37, seed * 0.71, seed * 2.13))
    for v in bm.verts:
        n = v.co.normalized()
        k = 1 + rough * noise.noise(n * 1.9 + off) + rough * 0.5 * noise.noise(n * 4.3 + off)
        v.co = Vector((n.x * rx * k, n.y * ry * k, n.z * rz * k))
    return bm


def bm_rock(sx, sy, sz, seed, subdiv=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in bm.verts:
        n = v.co.normalized()
        k = 1 + 0.3 * noise.noise(n * 1.2 + off) + 0.12 * noise.noise(n * 3.4 + off)
        p = Vector((n.x * sx * k, n.y * sy * k, n.z * sz * k))
        p.z += 0.05 * sz * math.sin(p.z / max(sz, 0.1) * 9 + seed)
        if p.z < -0.4 * sz:
            p.z = -0.4 * sz + (p.z + 0.4 * sz) * 0.25
        v.co = p
    return bm


def face_yaw(dx, dz):
    """Yaw (deg) that turns a piece whose front faces local -Y toward Unity direction (dx, dz)."""
    return math.degrees(math.atan2(dx, -dz))


def path_frames(path, step):
    """(x, z, left_nx, left_nz) every `step` metres along a polyline."""
    out = []
    acc = 0.0
    for i in range(len(path) - 1):
        ax, az = path[i]
        bx, bz = path[i + 1]
        L = math.hypot(bx - ax, bz - az)
        if L < 1e-9:
            continue
        nx, nz = -(bz - az) / L, (bx - ax) / L
        t = acc
        while t < L:
            out.append((ax + (bx - ax) * t / L, az + (bz - az) * t / L, nx, nz))
            t += step
        acc = t - L
    return out


# ================================================================ rocks and sea stacks
SEA_STACKS = [(-72, 58, 5, 4, 7), (-64, 122, 3.5, 3, 4.5), (-90, 182, 6, 5, 9), (-98, 22, 4, 4, 5.5),
              (-60, 222, 3, 2.5, 3.5), (-150, 255, 7, 6, 8), (-212, 300, 4, 3.5, 5), (-320, 315, 6, 5, 7),
              (-548, 236, 9, 7, 13), (-596, 252, 5, 4, 6), (-80, -160, 6, 5, 8), (-118, -300, 8, 6, 11),
              (-128, -232, 3.5, 3, 4), (-470, 300, 3, 2.5, 3.5)]


def build_rocks(coll, seed=3):
    rnd = random.Random(seed)
    b = Builder("PB_Rocks")
    n = 0
    for (x, z, nx, nz) in path_frames(CLIFF_PATH, 4.5):
        if z < -700 or z > 1000 or x < -760:
            continue
        far_shore = z > 300 and x < -60
        if rnd.random() < 0.85:
            o = rnd.uniform(6.5, 13.0) if not far_shore else rnd.uniform(2.5, 9.0)
            s = rnd.uniform(0.6, 2.4)
            for k in range(rnd.choice((1, 1, 2, 3))):
                px = x + nx * (o + rnd.uniform(-1.5, 1.5)) + rnd.uniform(-2, 2)
                pz = z + nz * (o + rnd.uniform(-1.5, 1.5)) + rnd.uniform(-2, 2)
                sk = s * rnd.uniform(0.5, 1.0)
                py = height_at(px, pz) - 0.35 * sk
                mat = "PB_RockDark" if py < SEA + 0.6 else "PB_Rock"
                b.add_bm(bm_rock(sk * rnd.uniform(0.9, 1.5), sk * rnd.uniform(0.8, 1.2), sk * rnd.uniform(0.55, 0.9),
                                 rnd.random() * 100, 2), mat, M_at(px, py, pz, rnd.uniform(0, 360)))
                n += 1
        if not far_shore and rnd.random() < 0.55:
            o = rnd.uniform(2.6, 5.0)
            px, pz = x + nx * o, z + nz * o
            s = rnd.uniform(1.4, 3.2)
            py = height_at(px, pz) - 0.2 * s
            yaw = face_yaw(nx, nz) + rnd.uniform(-20, 20)
            b.add_bm(bm_rock(s * 1.4, s * 0.7, s * rnd.uniform(0.9, 1.4), rnd.random() * 100, 2), "PB_Rock",
                     M_at(px, py, pz, yaw))
            n += 1
    for i, (x, z, sx, sy, sz) in enumerate(SEA_STACKS):
        py = SEA - 1.5 + sz * 0.45
        b.add_bm(bm_rock(sx, sy, sz, 50 + i * 7.3, 3), "PB_Rock", M_at(x, py, z, rnd.uniform(0, 360)))
        for k in range(rnd.randint(2, 5)):
            a = rnd.uniform(0, 2 * math.pi)
            r = max(sx, sy) * rnd.uniform(1.0, 1.8)
            s = rnd.uniform(0.8, 2.2)
            b.add_bm(bm_rock(s * 1.3, s, s * 0.7, rnd.random() * 100, 2), "PB_RockDark",
                     M_at(x + math.cos(a) * r, SEA - 0.4, z + math.sin(a) * r, rnd.uniform(0, 360)))
            n += 1
    print("[rocks] %d boulders, %d stacks" % (n, len(SEA_STACKS)))
    return b.finish(coll, smooth_shade=True, box_uv=0.22)


# ================================================================ Monterey cypress
def foliage_color(c, r, rz, H, tint):
    """Vertex colour for a canopy pad centred at c: R = occlusion (pad underside, inner core, lower canopy),
    G = tip tint (sun-bleached upper surfaces)."""
    def fn(co):
        u = (co.z - c.z) / rz
        radial = math.hypot(co.x - c.x, co.y - c.y) / max(r, 1e-3)
        ao = (0.28 + 0.72 * float(smooth(-1.0, 0.75, u))) * (0.78 + 0.22 * float(smooth(0.15, 0.9, radial)))
        ao *= 0.62 + 0.38 * float(smooth(H * 0.3, H * 0.85, co.z))
        g = min(1.0, tint + 0.3 * float(smooth(0.1, 1.0, u)))
        return (ao, g, 0.0, 1.0)
    return fn


def cypress_into(b, M, seed, H=10.0, hero=False):
    """Windswept Monterey cypress, authored ~H tall with its base at the origin, leaning toward local +X
    (Unity +X, inland, when placed with yaw 0). hero trees (the gameplay models seen close up) get more and
    rounder leaf clumps per pad."""
    rnd = random.Random(seed)
    s = H / 10.0
    bark = bmesh.new()
    lean = Vector((1.0, 0.0, 0.0))
    hs = H * rnd.uniform(0.16, 0.26)
    root = Vector((0, 0, -0.8 * s))
    fork = Vector((0, 0, hs)) + lean * hs * rnd.uniform(0.15, 0.35)
    add_tube(bark, root, fork, 0.62 * s, 0.48 * s, 8)
    pads = []
    nl = rnd.randint(4, 6)
    a0 = rnd.uniform(0, 2 * math.pi)
    for i in range(nl):
        ang = a0 + 2 * math.pi * i / nl + rnd.uniform(-0.3, 0.3)
        elev = math.radians(rnd.uniform(22, 48))
        L = H * rnd.uniform(0.45, 0.68)
        d = Vector((math.cos(ang) * math.cos(elev), math.sin(ang) * math.cos(elev), math.sin(elev))) + lean * 0.4
        d.normalize()
        mid = fork + d * L * 0.5 + Vector((0, 0, L * 0.1))
        end = fork + d * L
        end.z = min(max(end.z, H * 0.48), H * 0.74)
        add_tube(bark, fork, mid, 0.32 * s, 0.22 * s, 6)
        add_tube(bark, mid, end, 0.22 * s, 0.1 * s, 5)
        pr = H * rnd.uniform(0.16, 0.22)
        pads.append((end + Vector((0, 0, pr * 0.2)), pr, pr * rnd.uniform(0.36, 0.46)))
        for k in range(rnd.randint(1, 3)):
            off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized() * pr * rnd.uniform(0.7, 1.1)
            pads.append((end + off + Vector((0, 0, rnd.uniform(-0.12, 0.08) * H)), pr * rnd.uniform(0.6, 0.85), pr * 0.38))
        if rnd.random() < 0.7:
            pads.append((mid + Vector((0, 0, 0.1 * H)), pr * 0.7, pr * 0.32))
    top = fork + lean * H * 0.12 + Vector((0, 0, H * 0.6))
    add_tube(bark, fork, top, 0.28 * s, 0.1 * s, 5)
    for k in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        off = Vector((math.cos(a), math.sin(a), 0)) * H * rnd.uniform(0.05, 0.2)
        pads.append((top + off + lean * H * 0.08 + Vector((0, 0, H * rnd.uniform(0.04, 0.12))), H * rnd.uniform(0.2, 0.27),
                     H * 0.1))
    b.add_bm(bark, "PB_Bark", M)
    bark.free()
    for i, (c, r, rz) in enumerate(pads):
        tint = rnd.uniform(0.45, 0.7) if c.z > H * 0.74 else rnd.uniform(0.08, 0.4)
        col = foliage_color(c, r, rz, H, tint)
        centre = c - Vector((0, 0, rz * 1.1))
        # each pad is a clump: one core blob and lumpy satellites over its top and rim sharing its soft normals
        core = bm_blob(r * rnd.uniform(0.85, 1.05), r * rnd.uniform(0.8, 1.0), rz, seed * 13 + i, 2, 0.28)
        bmesh.ops.translate(core, verts=core.verts, vec=c)
        b.add_bm(core, "PB_Foliage", M, normals_from=centre, color=col)
        core.free()
        for k in range(rnd.randint(5, 7) if hero else rnd.randint(2, 4)):
            a = rnd.uniform(0, 2 * math.pi)
            off = Vector((math.cos(a), math.sin(a), 0)) * r * rnd.uniform(0.45, 0.85) + Vector((0, 0, rz * rnd.uniform(-0.2, 0.55)))
            ss = rnd.uniform(0.28, 0.5) if hero else rnd.uniform(0.35, 0.55)
            sat = bm_blob(r * ss, r * ss * rnd.uniform(0.85, 1.1), rz * rnd.uniform(0.55, 0.85),
                          seed * 31 + i * 7 + k, 2 if hero else 1, 0.3)
            bmesh.ops.translate(sat, verts=sat.verts, vec=c + off)
            b.add_bm(sat, "PB_Foliage", M, normals_from=centre, color=col)
            sat.free()


CYPRESS_VARIANTS = [("Cypress_A", 101, 10.0), ("Cypress_B", 202, 10.0), ("Cypress_C", 303, 10.0)]


def build_cypress_models(coll):
    obs = []
    for name, seed, H in CYPRESS_VARIANTS:
        b = Builder(name)
        cypress_into(b, Matrix.Identity(4), seed, H, hero=True)
        obs.append(b.finish(coll, smooth_shade=True, box_uv=0.5))
    return obs


SCENERY_CYPRESS = [
    # right of the hole, beyond the white stakes
    (58, -30), (66, 4), (55, 28), (72, 46), (60, 88), (78, 112), (63, 140), (70, 176), (58, 204), (84, 226),
    (92, 60), (96, 150),
    # behind the green and along the cliff top past it
    (-22, 238), (-6, 252), (14, 244), (30, 232), (-30, 268), (40, 262),
    # behind the tee
    (-30, -62), (-8, -70), (22, -58), (40, -84), (-40, -86),
    # far shore by the Lodge and the 18th
    (-120, 400), (-150, 410), (-190, 440), (-240, 445), (-292, 420), (-330, 405), (-380, 398), (-430, 392),
    (-480, 376), (-520, 350), (-96, 380), (-70, 360), (-50, 330),
]


def build_scenery_trees(coll, seed=9):
    rnd = random.Random(seed)
    b = Builder("PB_Trees")
    for i, (x, z) in enumerate(SCENERY_CYPRESS):
        H = rnd.uniform(8.5, 13.0)
        y = height_at(x, z)
        cypress_into(b, M_at(x, y, z, rnd.uniform(-35, 35)), 1000 + i * 17, H)
    print("[trees] %d scenery cypress" % len(SCENERY_CYPRESS))
    return b.finish(coll, smooth_shade=True, box_uv=0.5)


# ================================================================ buildings
def building(b, x, z, facing, w, d, floors, wall="PB_Stucco", roof="PB_Roof", roof_kind="hip", fh=3.4,
             windows=True, chimney=False):
    yaw = face_yaw(*facing)
    a = math.radians(yaw)
    corners = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    hs = []
    for lx, ly in corners:
        bx, by = math.cos(a) * lx - math.sin(a) * ly, math.sin(a) * lx + math.cos(a) * ly
        hs.append(height_at(x + bx, z + by))
    y0 = min(hs) - 1.0
    floor = max(hs) + 0.25
    top = floor + floors * fh
    M = M_at(x, 0.0, z, yaw)
    b.add_bm(bm_box(w, d, top - y0, 0, 0, (top + y0) / 2), wall, M)
    for f in range(1, floors + 1):
        b.add_bm(bm_box(w + 0.16, d + 0.16, 0.22, 0, 0, floor + f * fh - 0.1), "PB_Trim", M)
    if windows:
        for f in range(floors):
            zc = floor + f * fh + fh * 0.5
            n = max(1, int((w - 2) / 3.3))
            for i in range(n):
                xc = -w / 2 + 1 + (i + 0.5) * (w - 2) / n
                for side in (-1, 1):
                    b.add_bm(bm_box(1.3, 0.08, 1.75, xc, side * (d / 2 + 0.02), zc), "PB_Window", M)
            m = max(1, int((d - 2) / 3.3))
            for i in range(m):
                yc = -d / 2 + 1 + (i + 0.5) * (d - 2) / m
                for side in (-1, 1):
                    b.add_bm(bm_box(0.08, 1.3, 1.75, side * (w / 2 + 0.02), yc, zc), "PB_Window", M)
    rh = min(w, d) * 0.34
    rb = bm_hip_roof(w, d, rh, top) if roof_kind == "hip" else bm_gable_roof(w, d, rh, top)
    b.add_bm(rb, roof, M)
    if chimney:
        b.add_bm(bm_box(1.2, 1.2, rh + 1.6, w * 0.3, 0, top + (rh + 1.6) / 2), wall, M)
    return top


LODGE = [
    # (x, z, facing, w, d, floors, wall, roof, kind)
    (-215, 430, (0, -1), 78, 16, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-215, 420, (0, -1), 16, 6, 2, "PB_Stucco", "PB_Roof", "gable"),
    (-262, 413, (0.3, -1), 30, 14, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-168, 413, (-0.3, -1), 30, 14, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-128, 386, (0, -1), 38, 12, 1, "PB_Stucco", "PB_Roof", "hip"),
    (-150, 454, (0, -1), 18, 11, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-182, 466, (0, -1), 16, 10, 2, "PB_StuccoWarm", "PB_Roof", "hip"),
    (-250, 460, (0, -1), 20, 11, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-288, 448, (0.2, -1), 16, 12, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-306, 428, (0.4, -1), 14, 10, 2, "PB_StuccoWarm", "PB_Roof", "gable"),
    (-112, 432, (0, -1), 14, 10, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-100, 458, (0, -1), 18, 10, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-326, 458, (0, -1), 16, 10, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-228, 490, (0, -1), 22, 12, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-192, 500, (0, -1), 16, 10, 2, "PB_StuccoWarm", "PB_Roof", "hip"),
]

HOUSES = [
    (-365, 434, (0.1, -1), 20, 13, 2, "PB_StuccoWarm", "PB_RoofRed", "hip"),
    (-410, 428, (0.15, -1), 24, 14, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-455, 414, (0.3, -1), 18, 12, 2, "PB_StuccoWarm", "PB_RoofRed", "gable"),
    (-498, 396, (0.5, -1), 22, 14, 2, "PB_Stucco", "PB_Roof", "hip"),
    (-540, 370, (0.8, -1), 18, 12, 2, "PB_StuccoWarm", "PB_RoofRed", "hip"),
    (-600, 420, (1, -0.3), 20, 12, 2, "PB_Stucco", "PB_Roof", "hip"),
    (210, 150, (-1, 0), 18, 12, 2, "PB_StuccoWarm", "PB_RoofRed", "hip"),
    (252, 40, (-1, 0.2), 20, 13, 2, "PB_Stucco", "PB_Roof", "hip"),
    (236, 300, (-1, -0.2), 16, 11, 2, "PB_StuccoWarm", "PB_Roof", "gable"),
    (300, 220, (-1, 0), 22, 14, 2, "PB_Stucco", "PB_RoofRed", "hip"),
    (330, 390, (-1, -0.4), 18, 12, 2, "PB_StuccoWarm", "PB_Roof", "hip"),
    (130, -210, (-0.5, 1), 18, 12, 2, "PB_Stucco", "PB_Roof", "hip"),
    (170, -320, (-0.6, 1), 20, 12, 2, "PB_StuccoWarm", "PB_RoofRed", "hip"),
    (60, -380, (-1, 1), 16, 11, 2, "PB_Stucco", "PB_Roof", "gable"),
]


def build_buildings(coll):
    b = Builder("PB_Buildings")
    for (x, z, facing, w, d, fl, wall, roof, kind) in LODGE + HOUSES:
        building(b, x, z, facing, w, d, fl, wall, roof, kind, chimney=(w < 25))
    # 18th green flag
    gy = height_at(-262, 401)
    pole = bmesh.new()
    add_tube(pole, Vector((0, 0, 0)), Vector((0, 0, 2.4)), 0.025, 0.025, 6)
    b.add_bm(pole, "PB_Mast", M_at(-262, gy, 401))
    b.add_bm(bm_box(0.02, 0.55, 0.38, 0, 0.28, 2.18), "PB_Flag", M_at(-262, gy, 401))
    print("[buildings] %d" % (len(LODGE) + len(HOUSES)))
    return b.finish(coll, smooth_shade=False, box_uv=0.25)


# ================================================================ Stillwater Cove pier and boats
PIER_ROOT = (-124.0, 376.0)
PIER_END = (-106.0, 290.0)
DECK_Y = SEA + 2.8


def build_pier(coll):
    b = Builder("PB_Pier")
    ax, az = PIER_ROOT
    bx, bz = PIER_END
    L = math.hypot(bx - ax, bz - az)
    dx, dz = (bx - ax) / L, (bz - az) / L
    t0 = 0.0
    while t0 < L and height_at(ax + dx * t0, az + dz * t0) > DECK_Y - 0.1:
        t0 += 0.5
    t0 = max(0.0, t0 - 3.0)
    sx, sz = ax + dx * t0, az + dz * t0
    length = L - t0
    yaw = face_yaw(-dx, -dz)
    M = M_at(sx, 0.0, sz, yaw)
    # local +Y runs from the shore out along the pier (front faces local -Y = toward shore)
    b.add_bm(bm_box(3.8, length, 0.32, 0, length / 2, DECK_Y), "PB_Wood", M)
    b.add_bm(bm_box(16, 9, 0.32, 0, length + 4.0, DECK_Y), "PB_Wood", M)
    y = 2.0
    while y < length + 8:
        for px in ((-1.7, 1.7) if y < length else (-7.5, -2.5, 2.5, 7.5)):
            pile = bmesh.new()
            add_tube(pile, Vector((px, y, SEA - 5.0)), Vector((px, y, DECK_Y)), 0.17, 0.17, 6)
            b.add_bm(pile, "PB_Wood", M)
        y += 5.0
    y = 0.0
    while y < length:
        for px in (-1.85, 1.85):
            b.add_bm(bm_box(0.1, 0.1, 1.0, px, y, DECK_Y + 0.66), "PB_Trim", M)
        y += 2.5
    for px in (-1.85, 1.85):
        b.add_bm(bm_box(0.08, length, 0.08, px, length / 2, DECK_Y + 1.15), "PB_Trim", M)
    b.add_bm(bm_box(6.0, 4.2, 3.0, 3.0, length + 5.5, DECK_Y + 1.66), "PB_Stucco", M)
    b.add_bm(bm_gable_roof(6.0, 4.2, 1.3, DECK_Y + 3.16, 0.3, 3.0, length + 5.5), "PB_Roof", M)
    print("[pier] %.0f m" % length)
    return b.finish(coll, smooth_shade=False, box_uv=0.3)


def bm_hull(L, W, H, seed):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.y - e.verts[1].co.y) > 0.5],
                              cuts=5, use_grid_fill=True)
    for v in bm.verts:
        y = v.co.y * L
        x = v.co.x * W
        z = v.co.z * H
        t = (y / L + 0.5)
        if t > 0.62:
            x *= max(0.02, ((1 - t) / 0.38) ** 0.7)
            z += (t - 0.62) * H * 0.6
        if t < 0.08:
            x *= 0.9
        if v.co.z < 0:
            x *= 0.45
        v.co = Vector((x, y, z))
    return bm


BOATS = [(-95, 318, 0, False), (-112, 304, 1, True), (-133, 312, 0, False), (-151, 324, 1, False),
         (-86, 296, 0, True), (-142, 296, 1, False), (-166, 336, 0, False), (-118, 332, 0, True),
         (-172, 305, 1, False)]


def build_boats(coll, seed=21):
    rnd = random.Random(seed)
    b = Builder("PB_Boats")
    for i, (x, z, navy, sail) in enumerate(BOATS):
        L = rnd.uniform(7, 11) if sail else rnd.uniform(5, 8)
        W = L * 0.32
        yaw = 200 + rnd.uniform(-18, 18)
        M = M_at(x, SEA + 0.35, z, yaw)
        b.add_bm(bm_hull(L, W, 1.0, i), "PB_HullNavy" if navy else "PB_Hull", M)
        b.add_bm(bm_box(W * 0.55, L * 0.3, 0.8, 0, -L * 0.05, 0.85), "PB_Trim", M)
        if sail:
            mast = bmesh.new()
            add_tube(mast, Vector((0, L * 0.12, 0.4)), Vector((0, L * 0.12, 0.4 + L * 1.25)), 0.06, 0.04, 5)
            add_tube(mast, Vector((0, L * 0.12, 1.6)), Vector((0, -L * 0.3, 1.6)), 0.04, 0.04, 4)
            b.add_bm(mast, "PB_Mast", M)
    # distant sailboats out on Carmel Bay with their sails up
    for (x, z, yaw) in [(-760, -120, 40), (-930, 310, 120), (-640, 120, 70)]:
        M = M_at(x, SEA + 0.3, z, yaw)
        b.add_bm(bm_hull(10, 3.2, 1.0, 0), "PB_Hull", M)
        sail = bmesh.new()
        v = [sail.verts.new(p) for p in [(0, 1.5, 1.2), (0, -3.0, 1.4), (0, 1.5, 13.0)]]
        sail.faces.new(v)
        sail.faces.new(list(reversed([sail.verts.new(p.co.copy()) for p in v])))
        b.add_bm(sail, "PB_Trim", M)
    print("[boats] %d" % (len(BOATS) + 3))
    return b.finish(coll, smooth_shade=True, box_uv=0.3)


# ================================================================ Del Monte Forest and the far headlands
def forest_density(x, z, h, d):
    if h < SEA + 2.5 or d < 14:
        return 0.0
    dens = 0.0
    if x > 168:
        dens = 0.7
    if z > 470:
        dens = max(dens, 0.55)
    if 360 < z <= 470 and x < -90:
        dens = max(dens, 0.18)
    if x > 40 and z < -70:
        dens = max(dens, 0.45)
    if z < -320:
        dens = max(dens, 0.35)
    if x < -1000:
        dens = max(dens, 0.4)
    return dens


def build_forest(coll, seed=17, cap=1450.0):
    rnd = random.Random(seed)
    b = Builder("PB_Forest")
    blocked = [(x, z, max(w, d) * 0.75 + 6) for (x, z, _, w, d, *_r) in LODGE + HOUSES]
    fairways = [
        [(-505, 340), (-470, 352), (-420, 366), (-360, 378), (-310, 386), (-275, 392), (-262, 410), (-300, 418),
         (-360, 412), (-420, 400), (-470, 384), (-500, 366)],
        [(-30, -112), (2, -112), (8, -170), (6, -260), (-18, -300), (-40, -260), (-44, -170)],
        [(112, -120), (150, -120), (156, 40), (152, 230), (128, 260), (106, 230), (100, 40)],
    ]
    cand = []
    for gx in np.arange(-1290, 1225, 16.0):
        for gz in np.arange(-1190, 1370, 16.0):
            dist = math.hypot(gx, gz - 80)
            if dist > cap:
                continue
            step = 16.0 if dist < 500 else 26.0
            if step > 16 and (int(gx / 16) % 2 or int(gz / 16) % 2):
                continue
            cand.append((gx + rnd.uniform(-6, 6), gz + rnd.uniform(-6, 6)))
    C = np.array(cand)
    H = height_np(C[:, 0], C[:, 1])
    D = rect_distance(C[:, 0], C[:, 1])
    n = 0
    for (x, z), h, d in zip(cand, H, D):
        p = forest_density(x, z, h, d)
        if p <= 0 or rnd.random() > p:
            continue
        if any((x - bx) ** 2 + (z - bz) ** 2 < r * r for bx, bz, r in blocked):
            continue
        if any(T.contains(f, x, z) for f in fairways):
            continue
        th = rnd.uniform(9, 17)
        if rnd.random() < 0.55:
            parts = [(0, 0, th * 0.55, th * 0.42, th * 0.42, th * 0.22),
                     (rnd.uniform(-1, 1), rnd.uniform(-1, 1), th * 0.8, th * 0.3, th * 0.3, th * 0.17)]
        else:
            parts = [(0, 0, th * 0.42, th * 0.24, th * 0.24, th * 0.38), (0, 0, th * 0.75, th * 0.15, th * 0.15, th * 0.24)]
        M = M_at(x, h - 0.5, z, rnd.uniform(0, 360))
        tint = rnd.uniform(0.0, 0.3)
        for (ox, oy, oz, rx, ry, rz) in parts:
            blob = bm_blob(rx, ry, rz, rnd.random() * 50, 1, 0.22)
            bmesh.ops.translate(blob, verts=blob.verts, vec=Vector((ox, oy, oz)))
            b.add_bm(blob, "PB_Foliage", M, normals_from=(ox, oy, oz - rz * 0.8),
                     color=foliage_color(Vector((ox, oy, oz)), max(rx, ry), rz, th, tint))
            blob.free()
        n += 1
    print("[forest] %d trees" % n)
    return b.finish(coll, smooth_shade=True, box_uv=0)


# ================================================================ tee marker
def lathe_into(b, parts, sides=24):
    """Revolve (radius, height) profiles about local Z. Each part is shaded smooth on its own; parts do not share
    vertices, so the turned steps between them stay crisp. A part ending at radius 0 closes with a flat cap."""
    for mat, profile in parts:
        verts, faces = [], []
        for r, z in profile:
            verts.extend((r * math.cos(2 * math.pi * k / sides), r * math.sin(2 * math.pi * k / sides), z)
                         for k in range(sides))
        for j in range(len(profile) - 1):
            for k in range(sides):
                a, c = j * sides + k, j * sides + (k + 1) % sides
                faces.append((a, c, c + sides, a + sides))
        if profile[-1][0] < 1e-4:
            verts = verts[:-sides] + [(0.0, 0.0, profile[-1][1])]
            apex = len(verts) - 1
            j = len(profile) - 2
            faces = faces[:-sides] + [(j * sides + k, j * sides + (k + 1) % sides, apex) for k in range(sides)]
        b.add_mesh(verts, faces, mat)


def build_tee_marker(coll):
    """Pebble-style tee marker: a turned peg ~10 cm tall in dark green lacquer with a brass band and domed cap.
    Base at the origin; the bottom 1 cm sits in the turf."""
    b = Builder("TeeMarker")
    dome = [(0.03 * math.cos(t), 0.082 + 0.016 * math.sin(t)) for t in np.linspace(0, math.pi / 2, 7)]
    lathe_into(b, [
        ("PB_MarkerBody", [(0.037, -0.01), (0.037, 0.008)]),
        ("PB_MarkerBody", [(0.037, 0.008), (0.034, 0.013), (0.031, 0.02), (0.0295, 0.07)]),
        ("PB_Brass", [(0.0315, 0.07), (0.0315, 0.08)]),
        ("PB_Brass", [(0.0315, 0.08), (0.03, 0.082)]),
        ("PB_MarkerBody", dome),
    ])
    return b.finish(coll, smooth_shade=True, box_uv=8.0)


# ================================================================ export
def export_fbx(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    vl = bpy.context.view_layer
    vl.update()
    for o in bpy.context.scene.objects:
        if o is not None:
            o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    vl.objects.active = objs[0]
    kw = dict(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
              use_mesh_modifiers=True,
              mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    props = bpy.ops.export_scene.fbx.get_rna_type().properties
    if "colors_type" in props.keys():
        kw["colors_type"] = "LINEAR"
    bpy.ops.export_scene.fbx(**kw)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)
    print("[export] %s  %d objects  %d tris" % (os.path.relpath(path, ROOT), len(objs), tris))
    for o in objs:
        o.select_set(False)


SCENERY_PARTS = [("Terrain", build_terrain), ("Ocean", build_ocean), ("Rocks", build_rocks),
                 ("Trees", build_scenery_trees), ("Forest", build_forest), ("Buildings", build_buildings),
                 ("Pier", build_pier), ("Boats", build_boats)]


def build_all(export=True):
    """Rebuild every collection from scratch and export the scenery, cypress and tee-marker FBX files."""
    make_materials()
    make_textures()
    make_foliage_textures()
    scenery = []
    for name, fn in SCENERY_PARTS:
        c = new_coll(name)
        clear_coll(c)
        scenery.append(fn(c))
    c = new_coll("CypressModels")
    clear_coll(c)
    cypress = build_cypress_models(c)
    c.hide_render = True
    c = new_coll("TeeMarker")
    clear_coll(c)
    marker = build_tee_marker(c)
    c.hide_render = True
    if export:
        export_fbx(scenery, os.path.join(SCENERY_DIR, "PebbleBeach_Scenery.fbx"))
        for o in cypress:
            export_fbx([o], os.path.join(CYPRESS_DIR, o.name + ".fbx"))
        export_fbx([marker], os.path.join(TEE_MARKER_DIR, "TeeMarker.fbx"))
    return scenery, cypress
