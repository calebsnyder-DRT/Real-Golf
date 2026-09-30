"""Puttshack Pro - Pro Shop / Player Loadout art: room interior, display items, golf bags.

Rebuilds everything from scratch, exports the FBX files, renders the previews and saves the .blend:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python pp_proshop.py
Options after `--`:
  --no-export  --no-render  --no-save  --no-verify  --fast (half-res previews)  --room-only (skip items/bags)
  --verify (only re-import every exported FBX into a fresh scene and print names/materials/tris/bounds)

Coordinates: Blender meters, Z up, the golfer mark at the origin facing -Y (Unity +Z).
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

try:
    HERE = os.path.dirname(os.path.abspath(__file__))
except NameError:
    HERE = "/Users/calebsnyder/Real-Golf/ArtSource/Blender/scripts"
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from pp_lib import T, _tb, displace, p_box, p_cyl, p_ico, p_path_tube, p_sphere, p_tube, rad, srgb  # noqa: E402

REAL = "/Users/calebsnyder/Real-Golf"
ART = REAL + "/ArtSource"
GOLFER_BLEND = ART + "/Blender/PuttshackPro_Caleb_Modular.blend"
BLEND_OUT = ART + "/Blender/PuttshackPro_ProShop.blend"
FBX_DIR = REAL + "/Assets/_Game/Art/ProShop/Models"
REVIEW = ART + "/Review/ProShop/blender"
HDRI = ART + "/Blender/textures/kloofendal_48d_partly_cloudy_puresky_2k.hdr"

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OPT = {a.lstrip("-") for a in ARGV}

# ---------------------------------------------------------------- layout constants
RX0, RX1, RY0, RY1, RH = -6.0, 4.3, -8.6, 4.0, 3.7
WA, WB, WC = (1.9, 4.0), (4.3, 1.6), (4.3, -3.6)  # angled window wall A-B (x + y = 5.9), side windows B-C
DIAG = 5.9
ROOM_POLY = [(RX0, RY0), (RX1, RY0), (RX1, WB[1]), WA, (RX0, RY1)]
PLINTH = (1.35, 0.45, 0.42, 0.45)  # x, y, radius, height
CAM_PORTRAIT = ((0.0, -4.65, 1.20), (0.0, 0.0, 0.78), 32.0)
CAM_OVERVIEW = ((-4.1, -7.7, 2.45), (0.3, 1.9, 0.9), 44.0)

UPRIGHTS = [-5.40, -3.00, -0.26, 1.13]
RAIL_Y, HOOK_Z = 3.62, 1.85
RAIL_Z = HOOK_Z - 0.020
SHELF_Y = RY1 - 0.20
FOLD_Z, LOW_Z, TOP_Z = 2.22, 0.42, 2.64
HANG = ([(-5.05 + 0.34 * k, 30.0) for k in range(6)] + [(-2.65 + 0.34 * k, 30.0) for k in range(7)]
        + [(0.09 + 0.34 * k, -30.0) for k in range(3)])
FOLD = [-5.025, -4.475, -3.925, -3.375, -2.67, -2.15, -1.63, -1.11, -0.59, 0.015, 0.435, 0.855]
LOW_SEQ = [(-4.20, "SSKKSS"), (-1.63, "SSKKKSS"), (0.435, "SKS")]  # S = shoe pair, K = sock pair
RACK = dict(p1=(-1.72, -0.95), yaw=12.0, n=6, pitch=0.45, z=0.10)
MANNEQUINS = [((-4.25, 2.80), 30.0), ((3.55, 0.60), -45.0)]
BASE_H = 0.045

ROSTER = {"blue": "#2F6FD6", "sky": "#7FB3E6", "orange": "#E8872A", "green": "#3E9B4F", "gold": "#D9B021",
          "purple": "#8A4FC7", "teal": "#1FA59A", "coral": "#E86A58", "cream": "#EFE6CF", "navy": "#28324A",
          "white": "#F4F2EC"}

PS_MATS = {
    "PS_Wood_Floor_A": ("#7E5536", 0.38), "PS_Wood_Floor_B": ("#654129", 0.4), "PS_Wood_Floor_C": ("#916443", 0.38),
    "PS_Wood_Dark": ("#3A2517", 0.55), "PS_Timber": ("#7D5233", 0.6), "PS_Wood_Panel": ("#6A4630", 0.65),
    "PS_Wood_Ceiling": ("#9C7552", 0.7), "PS_Plaster_Cream": ("#E6DAC4", 0.92),
    "PS_Stone_A": ("#C4B8A2", 0.85), "PS_Stone_B": ("#AA9C85", 0.85), "PS_Stone_C": ("#8D806B", 0.9),
    "PS_Stone_Grout": ("#6A6256", 0.95), "PS_Stone_Top": ("#E4DDCF", 0.3),
    "PS_Brass": ("#C9A45B", 0.28, 1.0), "PS_Chrome": ("#D0D4D8", 0.15, 1.0), "PS_Metal_Dark": ("#2B2E31", 0.4, 0.8),
    "PS_Glass": ("#A9CBDB", 0.04), "PS_Light_Emissive": ("#FFE7C2", 0.4, 0.0, 6.0),
    "PS_Leaf_A": ("#3E6D2B", 0.7), "PS_Leaf_B": ("#5E9036", 0.7), "PS_Leaf_C": ("#2B5130", 0.8),
    "PS_Bark": ("#5A4331", 0.9),
    "PS_Grass": ("#4E8A34", 0.95), "PS_Grass_Light": ("#66A344", 0.95), "PS_Green": ("#7DBA4E", 0.8),
    "PS_Water": ("#2F7DBE", 0.22), "PS_Sand": ("#E6D7AA", 0.95), "PS_Hill": ("#5B8C4B", 0.95),
    "PS_Hill_Far": ("#8AAE9E", 1.0),
    "PS_Rug_Ink": ("#192020", 0.95), "PS_Rug_Trim": ("#C9B07A", 0.75),
    "PS_Mannequin": ("#EFE8DC", 0.55), "PS_Hanger": ("#B9844F", 0.5),
    "PS_Pot": ("#EAE3D5", 0.5), "PS_Pot_Dark": ("#3B3631", 0.6), "PS_Soil": ("#3B2E24", 0.95),
    "PS_Golfball": ("#F7F7F2", 0.35),
    "PS_Box_White": ("#F2F0E8", 0.6), "PS_Box_Ink": ("#1E2526", 0.6), "PS_Box_Cyan": ("#00CBE7", 0.5),
    "PS_Box_Kraft": ("#C7A57A", 0.8),
    "PS_Flag": ("#00CBE7", 0.7), "PS_Flagstick": ("#F4F2EA", 0.5),
    "PS_Lamp_Shade": ("#1F2527", 0.45, 0.7), "PS_Screen": ("#10161A", 0.2),
}
BAG_MATS = {"Bag_Body": ("#F2EDDF", 0.6), "Bag_Accent": ("#192020", 0.55), "Bag_Trim": ("#00CBE7", 0.45),
            "Bag_Strap": ("#1E2526", 0.65), "Bag_Metal": ("#C9CDD2", 0.25, 1.0), "Bag_Panel": ("#FBF9F3", 0.55),
            "HeadCover": ("#192020", 0.9), "HeadCover_Accent": ("#00CBE7", 0.85)}
CLUB_MATS = {"Club_Steel": ("#CDD1D6", 0.18, 1.0), "Club_Grip": ("#1C1E1F", 0.8), "Club_Shaft": ("#9CA2A8", 0.3, 1.0),
             "Club_Head": ("#23282B", 0.35, 0.5), "Club_Accent": ("#00CBE7", 0.45)}
BAG_SLOTS = ["Bag_Body", "Bag_Accent", "Bag_Trim", "Bag_Strap", "Bag_Metal", "Bag_Panel", "HeadCover",
             "HeadCover_Accent", "Club_Steel", "Club_Grip", "Club_Shaft"]

MANIFEST = {"markers": {}, "files": {}}


# ================================================================ generic helpers
def clamp01(t):
    return max(0.0, min(1.0, t))


def smooth01(a, b, x):
    t = clamp01((x - a) / (b - a))
    return t * t * (3 - 2 * t)


def material(name, hexcol, rough=0.6, metal=0.0, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        nt.nodes.clear()
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(bsdf.outputs[0], out.inputs[0])
    col = srgb(hexcol)
    bsdf.inputs["Base Color"].default_value = col
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit > 0:
        bsdf.inputs["Emission Color"].default_value = col
        bsdf.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = col
    m.roughness = rough
    m.metallic = metal
    return m


def make_materials():
    M = {}
    for table in (PS_MATS, BAG_MATS, CLUB_MATS):
        for name, spec in table.items():
            M[name] = material(name, *spec)
    return M


_PV = {}


def pv(hexcol, rough=0.7, metal=0.0):
    key = (hexcol, rough, metal)
    if key not in _PV:
        _PV[key] = material("PV_%s_%02d" % (hexcol.lstrip("#"), int(rough * 100)), hexcol, rough, metal)
    return _PV[key]


class MB:
    """Accumulates bmesh parts (one material each) into a single mesh object."""

    def __init__(self, name):
        self.name = name
        self.co, self.faces, self.fm, self.fs, self.mats = [], [], [], [], []

    def _mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def add(self, tb, m, M=None, smooth=True):
        if M is not None:
            bmesh.ops.transform(tb, matrix=M, verts=tb.verts)
        mi = self._mi(m)
        off = len(self.co)
        tb.verts.index_update()
        self.co.extend(tuple(v.co) for v in tb.verts)
        for f in tb.faces:
            self.faces.append(tuple(off + v.index for v in f.verts))
            self.fm.append(mi)
            self.fs.append(smooth)
        tb.free()
        return self

    def merge(self, other, M=None):
        off = len(self.co)
        for c in other.co:
            self.co.append(tuple(M @ Vector(c)) if M is not None else c)
        for f, mi, s in zip(other.faces, other.fm, other.fs):
            self.faces.append(tuple(off + i for i in f))
            self.fm.append(self._mi(other.mats[mi]))
            self.fs.append(s)
        return self

    def nverts(self):
        return len(self.co)

    def tris(self):
        return sum(len(f) - 2 for f in self.faces)

    def finish(self, coll, angle=32.0, origin=(0.0, 0.0, 0.0), order=None):
        import numpy as np
        me = bpy.data.meshes.new(self.name)
        o = Vector(origin)
        co = [(c[0] - o.x, c[1] - o.y, c[2] - o.z) for c in self.co]
        mats = self.mats
        fm = self.fm
        if order:  # fixed material slot order (unused slots still created)
            mats = list(order)
            fm = [mats.index(self.mats[i]) for i in self.fm]
        me.from_pydata(co, [], self.faces)
        for m in mats:
            me.materials.append(m)
        me.polygons.foreach_set("material_index", fm)
        me.polygons.foreach_set("use_smooth", self.fs)
        me.update()
        # box-projected UVs (flat colours need none, but Unity's lightmapper and tangents like having them)
        nl = len(me.loops)
        if nl:
            vidx = np.empty(nl, np.int32)
            me.loops.foreach_get("vertex_index", vidx)
            cov = np.empty(len(me.vertices) * 3, np.float32)
            me.vertices.foreach_get("co", cov)
            cov = cov.reshape(-1, 3)
            pn = np.empty(len(me.polygons) * 3, np.float32)
            me.polygons.foreach_get("normal", pn)
            pn = pn.reshape(-1, 3)
            lt = np.empty(len(me.polygons), np.int32)
            me.polygons.foreach_get("loop_total", lt)
            pol = np.repeat(np.arange(len(me.polygons)), lt)
            ax = np.argmax(np.abs(pn), axis=1)[pol]
            c = cov[vidx]
            u = np.where(ax == 0, c[:, 1], c[:, 0])
            v = np.where(ax == 2, c[:, 1], c[:, 2])
            uvl = me.uv_layers.new(name="UVMap")
            uvl.data.foreach_set("uv", np.stack([u, v], 1).ravel())
        me.set_sharp_from_angle(angle=rad(angle))
        me.validate(clean_customdata=False)
        ob = bpy.data.objects.new(self.name, me)
        coll.objects.link(ob)
        ob.location = o
        return ob


def bx(x0, x1, y0, y1, z0, z1, bevel=0.0, seg=1):
    tb = p_box(x1 - x0, y1 - y0, z1 - z0, bevel=bevel, seg=seg, base=True)
    bmesh.ops.translate(tb, vec=((x0 + x1) / 2, (y0 + y1) / 2, z0), verts=tb.verts)
    return tb


def prism(pts, z0, z1, top_bevel=0.0, bottom=True, top=True):
    area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
    if area < 0:
        pts = list(reversed(pts))
    bm = _tb()
    bot = [bm.verts.new((x, y, z0)) for x, y in pts]
    tp = [bm.verts.new((x, y, z1)) for x, y in pts]
    ft = bm.faces.new(tp) if top else None
    if bottom:
        bm.faces.new(list(reversed(bot)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bot[i], bot[j], tp[j], tp[i]))
    if top_bevel > 0 and ft is not None:
        edges = list(ft.edges)
        verts = list({v for e in edges for v in e.verts})
        bmesh.ops.bevel(bm, geom=edges + verts, offset=top_bevel, offset_type="OFFSET", segments=1, profile=0.5,
                        affect="EDGES", clamp_overlap=True)
    bm.normal_update()
    return bm


def rod(p0, p1, r, seg=10, caps=True):
    return p_path_tube([Vector(p0), Vector(p1)], r, seg, caps)


def rod_x(x0, x1, y, z, r, seg=12):
    return rod((x0, y, z), (x1, y, z), r, seg)


def wall_frame(o, n):
    """Local frame on a vertical wall: x along the wall, -y toward the room (n = room-facing normal), z up."""
    n = Vector(n).normalized()
    u = Vector((0, 0, 1)).cross(n)
    y = -n
    return Matrix(((u.x, y.x, 0, o[0]), (u.y, y.y, 0, o[1]), (u.z, y.z, 1, o[2]), (0, 0, 0, 1)))


def seg_frame(P, Q):
    """Frame for a wall segment P->Q: x along P->Q, +y = outward (room is -y), origin at P."""
    P, Q = Vector((P[0], P[1], 0)), Vector((Q[0], Q[1], 0))
    d = (Q - P)
    L = d.length
    d.normalize()
    out = Vector((0, 0, 1)).cross(d)
    return Matrix(((d.x, out.x, 0, P.x), (d.y, out.y, 0, P.y), (0, 0, 1, 0), (0, 0, 0, 1))), L


def Mz(x, y, z, deg):
    return T((x, y, z), (0, 0, rad(deg)))


def ymax(x):
    return RY1 if x <= WA[0] else max(WB[1], DIAG - x)


def xmax(y):
    return RX1 if y <= WB[1] else DIAG - y


def new_coll(name, parent=None):
    c = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(c)
    return c


def empty(coll, name, loc, rz=0.0, size=0.12, kind="ARROWS"):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = kind
    e.empty_display_size = size
    e.location = loc
    e.rotation_euler = (0.0, 0.0, rad(rz))
    coll.objects.link(e)
    MANIFEST["markers"][name] = dict(pos=[round(v, 4) for v in loc], rotZ=round(rz, 2))
    return e


def lathe(profile, seg=24, caps=True, oy=1.0):
    tb = p_tube(profile, seg, caps)
    if oy != 1.0:
        bmesh.ops.scale(tb, vec=(1, oy, 1), verts=tb.verts)
    return tb


def obj_tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def world_bbox(objs):
    pts = []
    for o in objs:
        if o.type == "MESH":
            mw = o.matrix_world
            pts += [mw @ v.co for v in o.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    return lo, hi


# ================================================================ room
def build_floor(C, M):
    b = MB("Floor")
    b.add(prism(ROOM_POLY, -0.12, -0.012), M["PS_Wood_Dark"])
    rnd = random.Random(11)
    W, G = 0.19, 0.004
    tones = [M["PS_Wood_Floor_A"], M["PS_Wood_Floor_B"], M["PS_Wood_Floor_C"]]
    x = RX0
    while x < RX1 - 1e-4:
        xa, xb = x + G / 2, min(x + W - G / 2, RX1)
        ta, tbm = ymax(xa), ymax(xb)
        y = RY0 - rnd.uniform(0.0, 1.8)
        while y < max(ta, tbm):
            L = rnd.uniform(1.0, 2.6)
            ya, yb = max(y + G / 2, RY0), y + L - G / 2
            y += L
            if yb <= ya + 0.05:
                continue
            ea = max(min(yb, ta), ya + 0.01)
            eb = max(min(yb, tbm), ya + 0.01)
            b.add(prism([(xa, ya), (xb, ya), (xb, eb), (xa, ea)], -0.02, 0.0, top_bevel=0.0028, bottom=False),
                  tones[rnd.choices((0, 1, 2), weights=(5, 3, 2))[0]])
        x += W
    return b.finish(C, angle=30)


def build_walls(C, M):
    b = MB("Walls_Plaster")
    P = M["PS_Plaster_Cream"]
    t, H = 0.25, RH + 0.05
    b.add(bx(RX0 - t, WA[0], RY1, RY1 + t, 0, H), P)
    b.add(bx(RX0 - t, RX0, RY0 - t, RY1, 0, H), P)
    b.add(bx(RX0, RX1 + t, RY0 - t, RY0, 0, H), P)
    b.add(bx(RX1, RX1 + t, RY0, WC[1], 0, H), P)
    return b.finish(C)


def stone_face(b, M, Mw, width, height, seed, depth=0.07, course=(0.17, 0.26), blk=(0.26, 0.52), gap=0.012,
               z0=0.0, grout=True):
    rnd = random.Random(seed)
    tones = [M["PS_Stone_A"], M["PS_Stone_B"], M["PS_Stone_C"]]
    if grout:
        b.add(bx(0.0, width, -depth * 0.55, -0.001, z0, z0 + height), M["PS_Stone_Grout"], Mw)
    z, row = z0, 0
    top = z0 + height
    while z < top - 0.02:
        h = rnd.uniform(*course)
        if top - (z + h) < 0.09:
            h = top - z
        x, first = 0.0, True
        while x < width - 0.01:
            w = rnd.uniform(*blk)
            if first and row % 2:
                w *= 0.55
            first = False
            if width - (x + w) < 0.13:
                w = width - x
            pro = rnd.uniform(0.0, 0.014)
            b.add(bx(x + gap / 2, x + w - gap / 2, -depth - pro, 0.0, z + gap / 2, z + h - gap / 2, bevel=0.012),
                  tones[rnd.choices((0, 1, 2), weights=(4, 4, 2))[0]], Mw)
            x += w
        z += h
        row += 1


def build_stone(C, M):
    b = MB("Stone")
    stone_face(b, M, wall_frame((RX0, RY1, 0), (0, -1, 0)), UPRIGHTS[0] - 0.04 - RX0, RH, 3, depth=0.16)
    stone_face(b, M, wall_frame((UPRIGHTS[-1] + 0.04, RY1, 0), (0, -1, 0)), WA[0] - UPRIGHTS[-1] - 0.04, RH, 5,
               depth=0.16)
    stone_face(b, M, wall_frame((RX0, -0.7, 0), (1, 0, 0)), 3.6, RH, 9, depth=0.07)
    return b.finish(C, angle=30)


def build_trim(C, M):
    b = MB("Trim")
    D, Tm = M["PS_Wood_Dark"], M["PS_Timber"]
    sk = 0.12
    b.add(bx(RX0, RX0 + 0.022, RY0, -0.7, 0, sk, 0.004), D)
    b.add(bx(RX0, RX0 + 0.022, 2.9, RY1 - 0.16, 0, sk, 0.004), D)
    b.add(bx(RX0, RX1, RY0, RY0 + 0.022, 0, sk, 0.004), D)
    b.add(bx(RX1 - 0.022, RX1, RY0, WC[1] - 0.06, 0, sk, 0.004), D)
    cz = RH - 0.16
    b.add(bx(RX0, RX0 + 0.14, RY0, RY1, cz, RH, 0.01, 2), Tm)
    b.add(bx(RX0 + 0.14, RX1, RY0, RY0 + 0.14, cz, RH, 0.01, 2), Tm)
    b.add(bx(RX1 - 0.14, RX1, RY0 + 0.14, WC[1] - 0.06, cz, RH, 0.01, 2), Tm)
    b.add(bx(RX0 + 0.14, WA[0], RY1 - 0.14, RY1, cz, RH, 0.01, 2), Tm)
    b.add(bx(UPRIGHTS[0] - 0.04, UPRIGHTS[-1] + 0.04, RY1 - 0.03, RY1, 3.24, 3.3, 0.006), D)
    return b.finish(C, angle=30)


def build_ceiling(C, M):
    b = MB("Ceiling")
    rnd = random.Random(21)
    tones = [M["PS_Wood_Ceiling"], M["PS_Wood_Floor_C"], M["PS_Wood_Ceiling"]]
    b.add(prism(ROOM_POLY, RH + 0.025, RH + 0.06), M["PS_Wood_Dark"])
    y, W = RY0, 0.16
    while y < RY1 - 1e-4:
        ya, yb = y + 0.002, min(y + W - 0.002, RY1)
        b.add(prism([(RX0, ya), (xmax(ya), ya), (xmax(yb), yb), (RX0, yb)], RH, RH + 0.025, top=False),
              rnd.choice(tones))
        y += W
    beams = MB("Ceiling_Beams")
    Tm = M["PS_Timber"]
    for yb in (3.0, 1.0, -1.0, -3.0, -5.0, -7.0):
        x1 = xmax(yb + 0.12)
        beams.add(bx(RX0, x1, yb - 0.12, yb + 0.12, RH - 0.36, RH, 0.012, 2), Tm)
    x = -5.0
    while x < RX1 - 0.2:
        y1 = ymax(x + 0.05)
        beams.add(bx(x - 0.05, x + 0.05, RY0, y1, RH - 0.13, RH, 0.006, 1), M["PS_Wood_Dark"])
        x += 1.2
    return b.finish(C), beams.finish(C, angle=30)


def build_windows(C, M):
    fr = MB("Window_Frames")
    D, S = M["PS_Wood_Dark"], [M["PS_Stone_B"], M["PS_Stone_C"], M["PS_Stone_A"]]
    glass = []
    rnd = random.Random(4)
    for tag, P, Q, n, first_post in (("Bay", WA, WB, 3, True), ("Side", WB, WC, 4, False)):
        Mw, L = seg_frame(P, Q)
        for i in range(n + 1):
            if i == 0 and not first_post:
                continue
            x = i * L / n
            fr.add(bx(x - 0.07, x + 0.07, -0.11, 0.11, 0.14, RH, 0.008, 2), D, Mw)
        x = -0.06
        while x < L + 0.06:
            w = min(rnd.uniform(0.35, 0.6), L + 0.06 - x)
            fr.add(bx(x + 0.005, x + w - 0.005, -0.17, 0.17, 0.0, 0.14, 0.01), rnd.choice(S), Mw)
            x += w
        fr.add(bx(0, L, -0.08, 0.08, 0.14, 0.24, 0.006), D, Mw)
        fr.add(bx(0, L, -0.07, 0.07, 2.72, 2.84, 0.006), D, Mw)
        fr.add(bx(-0.07, L + 0.07, -0.13, 0.13, 3.42, RH, 0.01, 2), D, Mw)
        for i in range(n):
            xa, xb = i * L / n + 0.07, (i + 1) * L / n - 0.07
            g = MB("Glass_%s_%02d" % (tag, i + 1))
            g.add(bx(xa, xb, -0.006, 0.006, 0.24, 2.72), M["PS_Glass"], Mw.copy())
            g.add(bx(xa, xb, -0.006, 0.006, 2.84, 3.42), M["PS_Glass"], Mw.copy())
            glass.append(g.finish(C))
    ea = MB("Outside_Eave")
    o1, o2 = Vector((0.7071, 0.7071)), Vector((1.0, 0.0))
    mit = (o1 + o2).normalized() * (1.3 / math.cos(rad(22.5)))
    pts = [WA, WB, WC, (WC[0] + 1.3, WC[1]), (WB[0] + mit.x, WB[1] + mit.y),
           (WA[0] + o1.x * 1.3, WA[1] + o1.y * 1.3)]
    ea.add(prism(pts, RH, RH + 0.3), D)
    return fr.finish(C, angle=30), glass, ea.finish(C)


def build_clothing_wall(C, M, CM):
    b = MB("Shelving")
    D, Tm, Br = M["PS_Wood_Dark"], M["PS_Timber"], M["PS_Brass"]
    W0, W1 = UPRIGHTS[0], UPRIGHTS[-1]
    for x in UPRIGHTS:
        b.add(bx(x - 0.035, x + 0.035, RY1 - 0.44, RY1, 0.0, 2.86, 0.006, 2), D)
    b.add(bx(W0 - 0.05, W1 + 0.05, RY1 - 0.47, RY1, 2.86, 3.02, 0.012, 2), Tm)
    b.add(bx(W0 - 0.04, W1 + 0.04, RY1 - 0.48, RY1 - 0.44, 2.83, 2.87, 0.005), D)
    b.add(bx(W0, W1, RY1 - 0.40, RY1, 0.0, 0.08), D)
    for i in range(len(UPRIGHTS) - 1):
        xa, xb = UPRIGHTS[i] + 0.035, UPRIGHTS[i + 1] - 0.035
        for z in (0.08, LOW_Z - 0.04, FOLD_Z - 0.04, TOP_Z - 0.04):
            b.add(bx(xa, xb, RY1 - 0.41, RY1, z, z + 0.04, 0.004), Tm)
            b.add(bx(xa, xb, RY1 - 0.43, RY1 - 0.40, z - 0.012, z + 0.042, 0.004), D)
        x, k = xa, 0
        while x < xb - 0.01:
            w = min(0.14, xb - x)
            b.add(bx(x + 0.002, x + w - 0.002, RY1 - 0.018, RY1, 0.08, 2.86),
                  M["PS_Wood_Panel"] if k % 3 else M["PS_Timber"])
            x += w
            k += 1
    rail = MB("Hanging_Rail")
    rail.add(rod_x(W0, W1, RAIL_Y, RAIL_Z, 0.014, 16), Br)
    for x in UPRIGHTS:
        for s in (-1, 1):
            x0 = x + 0.035 if s > 0 else x - 0.035 - 0.012
            rail.add(p_cyl(0.028, 0.028, 0.012, 16), Br, T((x0, RAIL_Y, RAIL_Z), (0, rad(90), 0)))
    dec = MB("Shelf_Decor")
    rnd = random.Random(8)
    for i in range(len(UPRIGHTS) - 1):
        xa, xb = UPRIGHTS[i] + 0.06, UPRIGHTS[i + 1] - 0.06
        x = xa
        while x < xb - 0.3:
            w = rnd.uniform(0.30, 0.34)
            col = rnd.choice(["PS_Box_Ink", "PS_Box_White", "PS_Box_Kraft", "PS_Box_Ink"])
            dec.add(bx(x, x + w, RY1 - 0.37, RY1 - 0.04, 0.12, 0.23, 0.004), M[col])
            dec.add(bx(x - 0.004, x + w + 0.004, RY1 - 0.374, RY1 - 0.036, 0.23, 0.265, 0.004),
                    M["PS_Box_Cyan" if col == "PS_Box_Ink" else "PS_Box_Ink"])
            if rnd.random() < 0.6:
                dec.add(bx(x + 0.01, x + w - 0.01, RY1 - 0.36, RY1 - 0.05, 0.27, 0.36, 0.004), M[col])
            x += w + 0.025
        x = xa + 0.05
        k = 0
        while x < xb - 0.2:
            kind = (i * 3 + k) % 4
            if kind in (0, 2):
                for r in range(2):
                    for c in range(3 - r):
                        bxx = x + c * 0.075 + r * 0.037
                        z0 = TOP_Z + r * 0.075
                        dec.add(bx(bxx, bxx + 0.07, RY1 - 0.3, RY1 - 0.23, z0, z0 + 0.07, 0.003), M["PS_Box_White"])
                        dec.add(bx(bxx - 0.001, bxx + 0.071, RY1 - 0.301, RY1 - 0.229, z0 + 0.028, z0 + 0.042),
                                M["PS_Box_Cyan"])
                x += 0.3
            elif kind == 1:
                cx = x + 0.08
                dec.add(lathe([(0.05, 0), (0.05, 0.04), (0.02, 0.05), (0.012, 0.09), (0.02, 0.1), (0.055, 0.13),
                               (0.065, 0.2), (0.06, 0.2), (0.0, 0.12)], 20), Br, T((cx, RY1 - 0.22, TOP_Z + 0.04)))
                dec.add(bx(cx - 0.06, cx + 0.06, RY1 - 0.28, RY1 - 0.16, TOP_Z, TOP_Z + 0.04, 0.004), D)
                x += 0.24
            else:
                cx = x + 0.08
                dec.add(lathe([(0.05, 0), (0.065, 0.12), (0.06, 0.12), (0.0, 0.11)], 16), M["PS_Pot"],
                        T((cx, RY1 - 0.2, TOP_Z)))
                for j in range(5):
                    tb = p_ico(1.0, 2)
                    displace(tb, 0.2, 2.0, j + 3 * i)
                    a = j * 2.4
                    dec.add(tb, M["PS_Leaf_B" if j % 2 else "PS_Leaf_A"],
                            T((cx + 0.04 * math.cos(a), RY1 - 0.2 + 0.03 * math.sin(a), TOP_Z + 0.15 + 0.02 * j),
                              (0, 0, a), (0.07, 0.06, 0.06)))
                x += 0.22
            x += 0.06
            k += 1
    for k, (x, yaw) in enumerate(HANG):
        empty(CM, "Slot_Hang_%03d" % (k + 1), (round(x, 4), RAIL_Y, HOOK_Z), yaw)
    for k, x in enumerate(FOLD):
        empty(CM, "Slot_Fold_%03d" % (k + 1), (x, SHELF_Y, FOLD_Z), 0.0)
    ns = nk = 0
    pitch = {"S": 0.38, "K": 0.20}
    for cx, seq in LOW_SEQ:
        x = cx - sum(pitch[c] for c in seq) / 2
        for c in seq:
            xc = round(x + pitch[c] / 2, 4)
            if c == "S":
                ns += 1
                empty(CM, "Slot_Shoe_%03d" % ns, (xc, SHELF_Y, LOW_Z), 0.0)
            else:
                nk += 1
                empty(CM, "Slot_Sock_%03d" % nk, (xc, SHELF_Y, LOW_Z), 0.0)
            x += pitch[c]
    return b.finish(C, angle=30), rail.finish(C), dec.finish(C, angle=30)


def build_rug(C, M):
    b = MB("Rug")
    seg = 96
    b.add(p_tube([(1.35, 0.0), (1.35, 0.006), (1.343, 0.01), (1.21, 0.01)], seg, caps=False), M["PS_Rug_Ink"])
    b.add(p_tube([(1.21, 0.01), (1.18, 0.01)], seg, caps=False), M["PS_Rug_Trim"])
    b.add(p_tube([(1.18, 0.01), (0.0, 0.01)], seg, caps=False), M["PS_Rug_Ink"])
    return b.finish(C, angle=40)


def build_plinth(C, M, CM):
    px, py, R, H = PLINTH
    b = MB("Plinth_BagStand")
    D, Tm = M["PS_Wood_Dark"], M["PS_Timber"]
    b.add(p_tube([(R + 0.02, 0.0), (R + 0.02, 0.03), (R + 0.005, 0.045), (R - 0.03, 0.045)], 64), D, T((px, py, 0)))
    b.add(p_cyl(R - 0.02, R - 0.02, H - 0.08, 48, caps=False), D, T((px, py, 0.04)))
    n = 30
    for i in range(n):
        a = 2 * math.pi * i / n
        w = 2 * math.pi * (R - 0.015) / n - 0.006
        tone = [Tm, M["PS_Wood_Floor_B"], M["PS_Wood_Floor_A"]][i % 3]
        b.add(bx(-w / 2, w / 2, 0.0, 0.03, 0.045, H - 0.075, 0.004), tone,
              T((px + R * math.sin(a), py - R * math.cos(a), 0), (0, 0, a)))
    b.add(p_tube([(R + 0.012, H - 0.075), (R + 0.012, H - 0.068)], 64, caps=False), M["PS_Brass"], T((px, py, 0)))
    b.add(p_tube([(R + 0.008, H - 0.068), (R + 0.012, H - 0.02), (R + 0.02, H - 0.018), (R + 0.022, H - 0.008),
                  (R + 0.012, H), (0.0, H)], 64, caps=False), D, T((px, py, 0)))
    empty(CM, "Mark_BagStand", (px, py, H), 0.0)
    return b.finish(C, angle=35)


def build_counter(C, M):
    b = MB("Counter")
    L, Dp, H = 2.4, 0.65, 1.02
    Mw = Mz(-4.6, 1.2, 0.0, 90.0)
    D, Tm = M["PS_Wood_Dark"], M["PS_Timber"]
    b.add(bx(-L / 2, L / 2, 0.0, Dp, 0.1, H - 0.05), D, Mw)
    b.add(bx(-L / 2 + 0.03, L / 2 - 0.03, 0.05, Dp - 0.02, 0.0, 0.1), D, Mw)
    n = int(L / 0.1)
    tones = [Tm, M["PS_Wood_Floor_B"], M["PS_Wood_Floor_C"]]
    for i in range(n):
        x = -L / 2 + (i + 0.5) * L / n
        b.add(bx(x - L / n / 2 + 0.004, x + L / n / 2 - 0.004, -0.028, 0.0, 0.13, H - 0.07, 0.006, 2), tones[i % 3], Mw)
    for sx in (-1, 1):
        b.add(bx(sx * L / 2 - 0.03, sx * L / 2 + 0.03, -0.035, Dp, 0.1, H - 0.05, 0.008, 2), D, Mw)
    b.add(bx(-L / 2, L / 2, -0.034, 0.0, 0.1, 0.13), M["PS_Brass"], Mw)
    b.add(bx(-L / 2 - 0.05, L / 2 + 0.05, -0.07, Dp + 0.02, H - 0.05, H, 0.01, 2), M["PS_Stone_Top"], Mw)
    b.add(bx(0.35, 0.7, 0.25, 0.55, H, H + 0.06, 0.01, 2), M["PS_Metal_Dark"], Mw)
    Ms = Mw @ T((0.525, 0.46, H + 0.06), (rad(-12), 0, 0))
    b.add(bx(-0.11, 0.11, -0.015, 0.015, 0.0, 0.2, 0.006), M["PS_Metal_Dark"], Ms)
    b.add(bx(-0.095, 0.095, -0.02, -0.014, 0.02, 0.185), M["PS_Screen"], Ms)
    b.add(lathe([(0.06, 0), (0.1, 0.03), (0.13, 0.07), (0.12, 0.07), (0.0, 0.03)], 24), M["PS_Pot_Dark"],
          Mw @ T((-0.55, 0.3, H)))
    for k in range(9):
        a = k * 2.39996
        rr = 0.075 * math.sqrt((k + 0.5) / 9)
        b.add(p_sphere(0.0214, 12, 6), M["PS_Golfball"],
              Mw @ T((-0.55 + rr * math.cos(a), 0.3 + rr * math.sin(a), H + 0.055 + 0.01 * (k % 3))))
    b.add(lathe([(0.035, 0), (0.035, 0.09), (0.032, 0.09), (0.0, 0.01)], 16), M["PS_Chrome"], Mw @ T((-0.2, 0.35, H)))
    b.add(lathe([(0.055, 0), (0.07, 0.1), (0.066, 0.1), (0.0, 0.09)], 16), M["PS_Pot"], Mw @ T((0.95, 0.35, H)))
    for j in range(6):
        a = j * 1.05
        b.add(bx(-0.012, 0.012, -0.004, 0.004, 0.0, 0.26), M["PS_Leaf_C"],
              Mw @ T((0.95 + 0.02 * math.cos(a), 0.35 + 0.02 * math.sin(a), H + 0.09),
                     (rad(8) * math.cos(a), rad(8) * math.sin(a), a)))
    return b.finish(C, angle=30)


def build_cabinet(C, M):
    """Low cabinet under the bay windows; kept under 0.62 m so the portrait camera still sees the course."""
    b = MB("Ball_Cabinet")
    L, Dp, H = 2.1, 0.46, 0.62
    mid = ((WA[0] + WB[0]) / 2, (WA[1] + WB[1]) / 2)
    f = Vector(mid) + Vector((-0.7071, -0.7071)) * (0.16 + Dp)
    Mw = Mz(f.x, f.y, 0.0, -45.0)
    D, Tm = M["PS_Wood_Dark"], M["PS_Timber"]
    b.add(bx(-L / 2 + 0.03, L / 2 - 0.03, 0.03, Dp - 0.02, 0.0, 0.08), D, Mw)
    b.add(bx(-L / 2 - 0.02, L / 2 + 0.02, -0.02, Dp, H - 0.045, H, 0.008, 2), Tm, Mw)
    b.add(bx(-L / 2, L / 2, Dp - 0.02, Dp, 0.08, H - 0.045), Tm, Mw)
    b.add(bx(-L / 2, L / 2, 0.0, Dp, 0.08, 0.11, 0.004), Tm, Mw)
    b.add(bx(-L / 2, L / 2, 0.0, Dp, 0.335, 0.36, 0.004), Tm, Mw)
    ncol = 3
    for i in range(ncol + 1):
        x = -L / 2 + i * L / ncol
        b.add(bx(x - 0.02, x + 0.02, 0.0, Dp, 0.08, H - 0.045, 0.004, 2), D, Mw)
    b.add(bx(-L / 2, L / 2, -0.004, 0.012, 0.08, 0.11), M["PS_Brass"], Mw)
    for i in range(ncol):
        cx = -L / 2 + (i + 0.5) * L / ncol
        tw = L / ncol - 0.14
        for zt in (0.11, 0.36):
            b.add(bx(cx - tw / 2, cx + tw / 2, 0.06, 0.34, zt, zt + 0.035, 0.004), D, Mw)
            nx = int(tw / 0.046)
            for ix in range(nx):
                for iy in range(2):
                    bxx = cx - tw / 2 + 0.03 + ix * (tw - 0.06) / max(nx - 1, 1)
                    b.add(p_sphere(0.0214, 12, 6), M["PS_Golfball"], Mw @ T((bxx, 0.13 + iy * 0.08, zt + 0.045)))
    for k, x in enumerate((L / 2 - 0.34, -L / 2 + 0.12)):
        for r in range(3 - k):
            for c in range(3 - k - r):
                bxx = x + c * 0.075 + r * 0.037
                col = M["PS_Box_White"] if (k + c) % 3 else M["PS_Box_Ink"]
                z0 = H + r * 0.075
                b.add(bx(bxx, bxx + 0.07, 0.15, 0.25, z0, z0 + 0.07, 0.003), col, Mw)
                b.add(bx(bxx - 0.001, bxx + 0.071, 0.149, 0.251, z0 + 0.03, z0 + 0.042), M["PS_Box_Cyan"], Mw)
    return b.finish(C, angle=30)


def rack_frame():
    x1, y1 = RACK["p1"]
    return Mz(x1, y1, 0.0, RACK["yaw"])


def build_bag_rack(C, M, CM):
    b = MB("Bag_Rack")
    Mw = rack_frame()
    n, p = RACK["n"], RACK["pitch"]
    xl = -(n - 1) * p - 0.3
    D, Tm, Br = M["PS_Wood_Dark"], M["PS_Timber"], M["PS_Brass"]
    b.add(bx(xl + 0.02, 0.28, -0.26, 0.36, 0.0, 0.035), D, Mw)
    b.add(bx(xl, 0.3, -0.3, 0.38, 0.035, RACK["z"], 0.012, 2), Tm, Mw)
    for x in (0.27, xl + 0.03):
        b.add(bx(x - 0.03, x + 0.03, 0.31, 0.37, RACK["z"], 1.02, 0.006, 2), D, Mw)
    b.add(rod_x(xl + 0.03, 0.27, 0.34, 0.97, 0.016), Br, Mw)
    b.add(rod_x(xl + 0.03, 0.27, 0.34, 0.52, 0.014), Br, Mw)
    for k in range(n):
        x = -k * p
        for s in (-1, 1):
            b.add(rod((x + s * 0.13, 0.34, 0.97), (x + s * 0.12, 0.17, 0.97), 0.008, 8), Br, Mw)
            b.add(p_sphere(0.012, 10, 6), Br, Mw @ T((x + s * 0.12, 0.17, 0.97)))
    for k in range(n):
        w = Mw @ Vector((-k * p, 0.0, RACK["z"]))
        empty(CM, "Slot_Bag_%03d" % (k + 1), (round(w.x, 4), round(w.y, 4), RACK["z"]), RACK["yaw"])
    return b.finish(C, angle=30)


TABLE = ((2.3, -2.7), -18.0)


def build_table(C, M, CM):
    """Nesting display tables on the open floor (overview only); two extra fold slots on the main top."""
    b = MB("Display_Tables")
    (x, y), yaw = TABLE
    Mw = Mz(x, y, 0.0, yaw)
    D, Tm = M["PS_Wood_Dark"], M["PS_Timber"]
    for (cx, cy, L, W, H) in ((0.0, 0.0, 1.3, 0.7, 0.8), (0.62, -0.72, 0.9, 0.5, 0.52)):
        b.add(bx(cx - L / 2, cx + L / 2, cy - W / 2, cy + W / 2, H - 0.045, H, 0.01, 2), Tm, Mw)
        b.add(bx(cx - L / 2 + 0.05, cx + L / 2 - 0.05, cy - W / 2 + 0.05, cy + W / 2 - 0.05, H - 0.12, H - 0.045),
              D, Mw)
        for sx in (-1, 1):
            for sy in (-1, 1):
                lx, ly = cx + sx * (L / 2 - 0.07), cy + sy * (W / 2 - 0.07)
                b.add(bx(lx - 0.03, lx + 0.03, ly - 0.03, ly + 0.03, 0.0, H - 0.045, 0.004, 1), D, Mw)
        b.add(bx(cx - L / 2 + 0.04, cx + L / 2 - 0.04, cy - 0.012, cy + 0.012, 0.1, 0.13), M["PS_Brass"], Mw)
    for r in range(3):
        for c in range(3 - r):
            bxx = 0.3 + c * 0.075 + r * 0.037
            z0 = 0.52 + r * 0.075
            b.add(bx(bxx, bxx + 0.07, -0.8, -0.73, z0, z0 + 0.07, 0.003), M["PS_Box_White"], Mw)
            b.add(bx(bxx - 0.001, bxx + 0.071, -0.801, -0.729, z0 + 0.028, z0 + 0.042), M["PS_Box_Cyan"], Mw)
    b.add(lathe([(0.05, 0), (0.065, 0.12), (0.06, 0.12), (0.0, 0.11)], 16), M["PS_Pot"], Mw @ T((0.9, -0.7, 0.52)))
    for j in range(5):
        tb = p_ico(1.0, 2)
        displace(tb, 0.2, 2.0, 40 + j)
        a = j * 2.4
        b.add(tb, M["PS_Leaf_B" if j % 2 else "PS_Leaf_A"],
              Mw @ T((0.9 + 0.04 * math.cos(a), -0.7 + 0.03 * math.sin(a), 0.67 + 0.02 * j), (0, 0, a), (0.07, 0.06, 0.06)))
    for k, lx in enumerate((-0.3, 0.3)):
        w = Mw @ Vector((lx, 0.02, 0.8))
        empty(CM, "Slot_Fold_%03d" % (len(FOLD) + k + 1), (round(w.x, 4), round(w.y, 4), 0.8), yaw)
    return b.finish(C, angle=30)


def build_mannequin_bases(C, M, CM):
    b = MB("Mannequin_Bases")
    for k, ((x, y), yaw) in enumerate(MANNEQUINS):
        b.add(p_tube([(0.32, 0.0), (0.32, 0.03), (0.31, BASE_H), (0.0, BASE_H)], 48, caps=False), M["PS_Wood_Dark"],
              T((x, y, 0)))
        b.add(p_tube([(0.322, 0.03), (0.322, 0.036)], 48, caps=False), M["PS_Brass"], T((x, y, 0)))
        empty(CM, "Slot_Mannequin_%03d" % (k + 1), (x, y, BASE_H), yaw)
    return b.finish(C, angle=40)


# ---------------------------------------------------------------- plants & lamps
def pot(b, M, x, y, r, h, mat):
    b.add(p_tube([(r * 0.72, 0.0), (r * 0.8, 0.02), (r, h * 0.9), (r * 1.05, h), (r * 0.97, h), (r * 0.94, h * 0.93)],
                 32), mat, T((x, y, 0)))
    b.add(p_tube([(r * 0.94, h * 0.9), (0.0, h * 0.9)], 32, caps=False), M["PS_Soil"], T((x, y, 0)))
    return h * 0.9


def ficus(b, M, x, y, z0, height, seed, stems=3):
    rnd = random.Random(seed)
    for s in range(stems):
        a = s * 2 * math.pi / stems + rnd.uniform(-0.4, 0.4)
        lean = rnd.uniform(0.05, 0.16)
        top = height * rnd.uniform(0.75, 1.0)
        pts = [Vector((x, y, z0)),
               Vector((x + lean * 0.3 * math.cos(a), y + lean * 0.3 * math.sin(a), z0 + top * 0.35)),
               Vector((x + lean * 0.8 * math.cos(a), y + lean * 0.8 * math.sin(a), z0 + top * 0.7)),
               Vector((x + lean * math.cos(a), y + lean * math.sin(a), z0 + top))]
        b.add(p_path_tube(pts, 0.014, 6, radii=[0.018, 0.014, 0.01, 0.006]), M["PS_Bark"])
        nl = int(top / 0.075)
        for k in range(nl):
            t = 0.3 + 0.7 * k / max(nl - 1, 1)
            i = min(int(t * 3), 2)
            p = pts[i].lerp(pts[i + 1], t * 3 - i)
            yaw = k * 2.39996 + s
            pitch = rnd.uniform(-15, 35) + 25 * (1 - t)
            sc = rnd.uniform(0.85, 1.15) * (0.8 + 0.3 * t)
            leaf = p_sphere(1.0, 8, 4)
            bmesh.ops.scale(leaf, vec=(0.12 * sc, 0.065 * sc, 0.01), verts=leaf.verts)
            bmesh.ops.translate(leaf, vec=(0.11 * sc, 0, 0), verts=leaf.verts)
            b.add(leaf, M["PS_Leaf_A"] if (k + s) % 3 else M["PS_Leaf_B"],
                  Matrix.Translation(p) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(rad(-pitch), 4, "Y"))


def bush(b, M, x, y, z0, r, seed, n=6):
    rnd = random.Random(seed)
    for i in range(n):
        a = i * 2.39996
        rr = r * (0.0 if i == 0 else rnd.uniform(0.35, 0.6))
        tb = p_ico(1.0, 2)
        displace(tb, 0.18, 2.2, seed + i)
        s = r * rnd.uniform(0.55, 0.75)
        b.add(tb, M["PS_Leaf_A"] if i % 2 else M["PS_Leaf_B"],
              T((x + rr * math.cos(a), y + rr * math.sin(a), z0 + s * 0.8 + (r * 0.5 if i == 0 else 0)),
                (0, 0, a), (s, s, s * 0.9)))


def snake_plant(b, M, x, y, z0, h, seed):
    rnd = random.Random(seed)
    for i in range(11):
        a = i * 2.39996
        rr = rnd.uniform(0.0, 0.07)
        hh = h * rnd.uniform(0.55, 1.0)
        tilt = rnd.uniform(3, 14)
        blade = prism([(-0.028, -0.004), (0.028, -0.004), (0.028, 0.004), (-0.028, 0.004)], 0.0, hh)
        for v in blade.verts:
            t = v.co.z / hh
            v.co.x *= 1.0 - 0.85 * t * t
            v.co.y *= 1.0 - 0.5 * t
        b.add(blade, M["PS_Leaf_C"] if i % 3 else M["PS_Leaf_B"],
              T((x + rr * math.cos(a), y + rr * math.sin(a), z0), (rad(tilt) * math.cos(a), rad(tilt) * math.sin(a), a)))


def build_plants(C, M):
    b = MB("Plants")
    z = pot(b, M, 1.55, 3.42, 0.2, 0.46, M["PS_Pot"])
    ficus(b, M, 1.55, 3.42, z, 1.55, 1)
    z = pot(b, M, -5.45, -0.6, 0.22, 0.52, M["PS_Pot_Dark"])
    snake_plant(b, M, -5.45, -0.6, z, 0.95, 2)
    z = pot(b, M, 3.8, -3.2, 0.26, 0.5, M["PS_Pot"])
    ficus(b, M, 3.8, -3.2, z, 1.7, 3, stems=4)
    z = pot(b, M, -5.5, 3.1, 0.19, 0.44, M["PS_Pot_Dark"])
    bush(b, M, -5.5, 3.1, z, 0.34, 4)
    return b.finish(C, angle=50)


def pendant(b, e, M, x, y, zb, r=0.2):
    b.add(rod((x, y, zb + 0.18), (x, y, RH), 0.005, 6), M["PS_Metal_Dark"])
    b.add(p_cyl(0.06, 0.06, 0.02, 16), M["PS_Metal_Dark"], T((x, y, RH - 0.02)))
    b.add(p_tube([(r, 0.0), (r, 0.02), (r * 0.8, 0.1), (r * 0.35, 0.17), (0.02, 0.19)], 24, caps=False),
          M["PS_Lamp_Shade"], T((x, y, zb)))
    b.add(p_tube([(r * 0.33, 0.165), (r * 0.78, 0.095), (r * 0.98, 0.02), (r * 0.98, 0.0)], 24, caps=False),
          M["PS_Brass"], T((x, y, zb)))
    e.add(p_sphere(0.045, 12, 8), M["PS_Light_Emissive"], T((x, y, zb + 0.07)))


def build_lamps(C, M):
    b = MB("Lamps")
    e = MB("Lamp_Emissive")
    for y in (0.4, 1.2, 2.0):
        pendant(b, e, M, -5.0, y, 2.35, 0.16)
    for x in (-1.0, 1.0):
        pendant(b, e, M, x, -0.1, 2.75, 0.26)
    for x in (-4.4, -2.0, 0.4, 2.6):
        for y in (2.0, 0.0, -2.0, -4.0, -6.0):
            if x > xmax(y) - 0.4:
                continue
            b.add(p_tube([(0.08, RH - 0.012), (0.08, RH)], 20, caps=False), M["PS_Metal_Dark"], T((x, y, 0)))
            b.add(p_tube([(0.062, RH - 0.012), (0.08, RH - 0.012)], 20, caps=False), M["PS_Metal_Dark"], T((x, y, 0)))
            lens = p_tube([(0.0, RH - 0.011), (0.062, RH - 0.011)], 20, caps=False)
            e.add(lens, M["PS_Light_Emissive"], T((x, y, 0)))
    for x in (-4.7, -3.5, -2.3, -1.1, 0.1, 0.9):
        zb = RH - 0.36
        b.add(p_cyl(0.035, 0.035, 0.012, 12), M["PS_Metal_Dark"], T((x, 3.0, zb - 0.012)))
        b.add(rod((x, 3.0, zb - 0.012), (x, 3.0, zb - 0.09), 0.006, 6), M["PS_Metal_Dark"])
        Mc = T((x, 3.0, zb - 0.11), (rad(-125), 0, 0))
        b.add(p_cyl(0.04, 0.045, 0.13, 16), M["PS_Metal_Dark"], Mc @ T((0, 0, -0.03)))
        e.add(p_tube([(0.036, 0.101), (0.0, 0.101)], 16, caps=False), M["PS_Light_Emissive"], Mc)
    return b.finish(C, angle=40), e.finish(C, angle=40)


# ---------------------------------------------------------------- outside vignette
LAKE = ((9.0, 18.5), (9.5, 4.8), 12.0)
GREEN = ((5.0, 9.8), (3.6, 2.4), 18.0)
BUNKER = ((8.2, 12.0), (1.4, 0.7), -25.0)
FLAG_AT = (5.4, 10.1)
FAIRWAY = ((5.0, 9.8), (45.0, -2.0))
TREES = [  # x, y, height, kind (R = round, C = conifer); none between the portrait camera and the lake
    (4.0, 27.5, 6.8, "R"), (8.5, 29.0, 7.6, "R"), (12.5, 27.0, 6.4, "C"), (16.5, 29.5, 7.4, "R"),
    (20.5, 26.5, 6.8, "R"), (24.0, 30.0, 7.8, "C"), (0.5, 30.0, 7.2, "R"), (-3.5, 26.0, 6.6, "C"),
    (0.0, 13.8, 6.0, "C"), (-2.5, 18.0, 7.0, "R"), (11.5, -1.5, 6.0, "R"), (19.0, -4.5, 7.2, "C"),
    (13.0, -4.0, 6.0, "R"), (21.0, -8.5, 7.5, "R"), (27.0, 13.5, 8.0, "R"), (30.0, -9.0, 7.0, "C"),
    (23.0, 15.0, 7.6, "R"), (32.0, 14.0, 8.4, "R"), (17.0, -14.0, 7.0, "C"), (35.0, -12.0, 8.0, "R"),
    (41.0, 10.0, 9.0, "R"), (30.0, 21.0, 8.0, "C"), (36.0, 26.0, 8.6, "R"), (12.0, 36.0, 9.0, "R"),
    (28.0, 36.0, 8.0, "C"), (-7.0, 30.0, 8.0, "R"), (44.0, -20.0, 8.6, "C"), (46.0, 16.0, 9.0, "R"),
]


def seg_dist(p, a, b):
    a, b = Vector(a), Vector(b)
    ab = b - a
    t = clamp01((p - a).dot(ab) / ab.length_squared)
    return (p - (a + ab * t)).length


def ell_e(x, y, spec):
    (cx, cy), (rx, ry), rot = spec[0], spec[1], spec[2]
    c, s = math.cos(rad(-rot)), math.sin(rad(-rot))
    dx, dy = x - cx, y - cy
    u, v = dx * c - dy * s, dx * s + dy * c
    return math.sqrt((u / rx) ** 2 + (v / ry) ** 2)


def terrain_base(x, y):
    """Course rises gently away from the clubhouse so the lake and green read above the window furniture."""
    r = math.hypot(x - 2.0, y - 1.0)
    h = -0.14 + 0.45 * smooth01(9.0, 30.0, r)
    h += smooth01(38.0, 80.0, r) * (2.2 + 2.0 * noise.noise(Vector((x * 0.035, y * 0.035, 0.3))))
    h += smooth01(14.0, 40.0, r) * 0.3 * noise.noise(Vector((x * 0.09, y * 0.09, 1.7)))
    return h


_LAKE_Z = []


def lake_z():
    if not _LAKE_Z:
        (cx, cy), (rx, ry), rot = LAKE[0], LAKE[1], LAKE[2]
        c, s = math.cos(rad(rot)), math.sin(rad(rot))
        zs = []
        for k in range(48):
            a = 2 * math.pi * k / 48
            u, v = 1.06 * rx * math.cos(a), 1.06 * ry * math.sin(a)
            zs.append(terrain_base(cx + u * c - v * s, cy + u * s + v * c))
        _LAKE_Z.append(min(zs) - 0.1)
    return _LAKE_Z[0]


def terrain_h(x, y):
    h = terrain_base(x, y)
    e = ell_e(x, y, LAKE)
    if e < 1.25:
        lo = lake_z() - 0.5
        h = lo + (h - lo) * smooth01(0.8, 1.25, e)
    return h


def ellipse_disc(b, m, spec, z, profile, seg=48):
    (cx, cy), (rx, ry), rot = spec[0], spec[1], spec[2]
    tb = p_tube(profile, seg, caps=False)
    b.add(tb, m, T((cx, cy, z), (0, 0, rad(rot)), (rx, ry, 1.0)))


def tree(b, M, x, y, h, kind, seed):
    rnd = random.Random(seed)
    z0 = terrain_h(x, y) - 0.1
    if kind == "C":
        b.add(p_cyl(0.16, 0.1, h * 0.3, 7), M["PS_Bark"], T((x, y, z0)))
        for k in range(3):
            r = h * (0.24 - 0.05 * k)
            zc = z0 + h * (0.18 + 0.24 * k)
            cone = p_cyl(r, 0.0, h * 0.42, 9)
            displace(cone, r * 0.08, 1.3, seed + k)
            b.add(cone, M["PS_Leaf_C"] if k % 2 == 0 else M["PS_Leaf_A"], T((x, y, zc), (0, 0, rnd.uniform(0, 6))))
    else:
        b.add(p_cyl(0.2, 0.13, h * 0.45, 7), M["PS_Bark"], T((x, y, z0)))
        n = rnd.randint(3, 4)
        for k in range(n):
            a = k * 2.39996 + rnd.uniform(0, 1)
            rr = h * (0.0 if k == 0 else 0.13)
            s = h * rnd.uniform(0.22, 0.3)
            blob = p_ico(1.0, 1)
            displace(blob, 0.16, 1.6, seed * 3 + k)
            b.add(blob, [M["PS_Leaf_A"], M["PS_Leaf_B"], M["PS_Leaf_C"]][(k + seed) % 3],
                  T((x + rr * math.cos(a), y + rr * math.sin(a), z0 + h * (0.62 + (0.1 if k == 0 else 0.0))),
                    (0, 0, a), (s, s, s * 0.85)))


def build_outside(C, M):
    out = []
    g = MB("Outside_Ground")
    bm = _tb()
    xs = [-40.0 + 2.5 * i for i in range(69)]
    ys = [-60.0 + 2.5 * j for j in range(89)]
    grid = [[bm.verts.new((x, y, terrain_h(x, y))) for x in xs] for y in ys]
    for j in range(len(ys) - 1):
        for i in range(len(xs) - 1):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    g.add(bm, M["PS_Grass"])
    out.append(g.finish(C, angle=60))

    t = MB("Outside_Terrace")
    rnd = random.Random(31)
    tones = [M["PS_Stone_A"], M["PS_Stone_B"], M["PS_Stone_A"], M["PS_Stone_C"]]
    P = 0.9
    for i in range(-3, 9):
        for j in range(-5, 11):
            x0, y0 = RX1 + 0.2 + i * P, WC[1] - 0.8 + j * P
            x1, y1 = x0 + P - 0.02, y0 + P - 0.02
            corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
            if any(x <= RX1 + 0.18 and x + y <= DIAG + 0.25 for x, y in corners):
                continue
            c = Vector(((x0 + x1) / 2, (y0 + y1) / 2))
            d = min(seg_dist(c, WA, WB), seg_dist(c, WB, WC))
            if d > 3.0 or c.y > RY1 + 3.0:
                continue
            t.add(bx(x0, x1, y0, y1, -0.1, -0.02, 0.01), rnd.choice(tones))
    for (px, py) in ((6.2, -3.9), (5.6, 4.2)):
        t.add(bx(px - 0.45, px + 0.45, py - 0.45, py + 0.45, -0.02, 0.5, 0.02, 2), M["PS_Stone_C"])
        t.add(bx(px - 0.4, px + 0.4, py - 0.4, py + 0.4, 0.49, 0.52), M["PS_Soil"])
        bush(t, M, px, py, 0.52, 0.42, int(px * 10))
    out.append(t.finish(C, angle=40))

    w = MB("Outside_Water")
    wb = next(n for n in M["PS_Water"].node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if wb.inputs.get("IOR Level"):
        wb.inputs["IOR Level"].default_value = 0.15  # grazing-angle sky reflection otherwise hides the blue
    ellipse_disc(w, M["PS_Water"], LAKE, lake_z(), [(1.06, 0.0), (0.0, 0.0)], 64)
    out.append(w.finish(C))

    gr = MB("Outside_Green")
    gz = terrain_h(*GREEN[0])
    ellipse_disc(gr, M["PS_Grass_Light"], GREEN, gz,
                 [(1.34, -0.03), (1.3, 0.04), (1.2, 0.06), (1.06, 0.07)], 64)
    ellipse_disc(gr, M["PS_Green"], GREEN, gz,
                 [(1.06, 0.07), (1.0, 0.09), (0.7, 0.13), (0.35, 0.15), (0.0, 0.155)], 64)
    fx, fy = FLAG_AT
    e = ell_e(fx, fy, GREEN)
    fz = gz + (0.155 - 0.06 * e * e)
    gr.add(p_tube([(0.06, fz + 0.004), (0.0, fz + 0.004)], 16, caps=False), M["PS_Stone_Grout"], T((fx, fy, 0)))
    bz = terrain_h(*BUNKER[0])
    ellipse_disc(gr, M["PS_Grass_Light"], BUNKER, bz, [(1.25, -0.02), (1.18, 0.06), (1.05, 0.07)], 40)
    ellipse_disc(gr, M["PS_Sand"], BUNKER, bz, [(1.05, 0.07), (0.8, 0.03), (0.0, 0.01)], 40)
    out.append(gr.finish(C, angle=50))

    fl = MB("Outside_Flag")
    fl.add(p_cyl(0.016, 0.016, 2.2, 8), M["PS_Flagstick"], T((fx, fy, fz)))
    fbm = _tb()
    nu, nv, fw, fh = 7, 4, 0.62, 0.42
    vv = [[fbm.verts.new((i * fw / (nu - 1), 0.05 * math.sin(i * 1.3) * (i / (nu - 1)), -j * fh / (nv - 1)))
           for i in range(nu)] for j in range(nv)]
    faces = [fbm.faces.new((vv[j][i], vv[j + 1][i], vv[j + 1][i + 1], vv[j][i + 1]))
             for j in range(nv - 1) for i in range(nu - 1)]
    bmesh.ops.solidify(fbm, geom=faces, thickness=0.008)
    fl.add(fbm, M["PS_Flag"], T((fx + 0.015, fy, fz + 2.18), (0, 0, rad(-35))))
    out.append(fl.finish(C, angle=40))

    fw_ = MB("Outside_Fairway")
    fbm = _tb()
    A, Bp = Vector(FAIRWAY[0]), Vector(FAIRWAY[1])
    d = (Bp - A).normalized()
    nrm = Vector((-d.y, d.x))
    rows = []
    N = 22
    for k in range(N + 1):
        tt = k / N
        p = A.lerp(Bp, tt) + nrm * 3.0 * math.sin(tt * 3.2)
        wd = 4.5 + 3.0 * math.sin(tt * math.pi) if tt > 0.05 else 3.0
        row = []
        for s in (-1.0, -0.5, 0.0, 0.5, 1.0):
            q = p + nrm * wd * s
            row.append(fbm.verts.new((q.x, q.y, terrain_h(q.x, q.y) + 0.035)))
        rows.append(row)
    for k in range(N):
        for s in range(4):
            fbm.faces.new((rows[k][s], rows[k][s + 1], rows[k + 1][s + 1], rows[k + 1][s]))
    bmesh.ops.recalc_face_normals(fbm, faces=fbm.faces)
    for f in fbm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    fw_.add(fbm, M["PS_Grass_Light"])
    out.append(fw_.finish(C, angle=60))

    tr = MB("Outside_Trees")
    for k, (x, y, h, kind) in enumerate(TREES):
        tree(tr, M, x, y, h, kind, k + 1)
    out.append(tr.finish(C, angle=40))

    hl = MB("Outside_Hills")
    rnd = random.Random(55)
    for k in range(11):
        a = rad(-25 + k * 11.0 + rnd.uniform(-4, 4))
        dist = rnd.uniform(105, 165)
        x, y = 2.0 + dist * math.cos(a), 1.0 + dist * math.sin(a)
        s = rnd.uniform(28, 48)
        tb = p_ico(1.0, 2)
        displace(tb, 0.12, 1.4, k)
        bmesh.ops.transform(tb, matrix=T((x, y, -s * 0.72), (0, 0, rnd.uniform(0, 6)),
                                         (s * rnd.uniform(1.1, 1.6), s, s)), verts=tb.verts)
        bmesh.ops.bisect_plane(tb, geom=tb.verts[:] + tb.edges[:] + tb.faces[:], plane_co=(0, 0, -2.0),
                               plane_no=(0, 0, 1), clear_inner=True)
        hl.add(tb, M["PS_Hill_Far"] if dist > 140 else M["PS_Hill"])
    out.append(hl.finish(C, angle=50))
    return out


def build_markers(CM):
    empty(CM, "Mark_Golfer", (0.0, 0.0, 0.01), 0.0)
    for tag, (loc, tgt, fov) in (("Portrait", CAM_PORTRAIT), ("Overview", CAM_OVERVIEW)):
        dx, dy = tgt[0] - loc[0], tgt[1] - loc[1]
        yaw = math.degrees(math.atan2(dx, -dy))
        empty(CM, "Cam_" + tag, loc, round(yaw, 2), 0.3)
        empty(CM, "Cam_%s_Target" % tag, tgt, 0.0, 0.15, "SPHERE")
        MANIFEST["markers"]["Cam_" + tag]["vfov"] = fov


def build_room(M):
    C = new_coll("PS_Room")
    CM = new_coll("PS_Room_Markers", C)
    objs = [build_floor(C, M), build_walls(C, M), build_stone(C, M), build_trim(C, M)]
    objs += list(build_ceiling(C, M))
    frames, glass, eave = build_windows(C, M)
    objs += [frames, eave] + glass
    objs += list(build_clothing_wall(C, M, CM))
    objs += [build_rug(C, M), build_plinth(C, M, CM), build_counter(C, M), build_cabinet(C, M),
             build_bag_rack(C, M, CM), build_mannequin_bases(C, M, CM), build_table(C, M, CM), build_plants(C, M)]
    objs += list(build_lamps(C, M))
    objs += build_outside(C, M)
    build_markers(CM)
    empties = list(CM.objects)
    return C, objs, empties


PREVIEW = {}



# ================================================================ entry point
if __name__ == "__main__":
    sys.modules.setdefault("pp_proshop", sys.modules[__name__])
    import pp_proshop_pipeline
    pp_proshop_pipeline.main()
