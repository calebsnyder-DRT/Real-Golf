"""Puttshack Pro - stylized-realistic course trees with leaf-card canopies, LODs and impostors.

    blender -b --factory-startup --python ArtSource/Blender/scripts/pp_nature.py -- [all|textures|trees|previews]

Trees (Blender Z up, base at the origin, roots sunk 0.6-0.8 m; Unity gets them through the usual U2B FBX settings):
  Cypress_A/B/C  Monterey cypress, windswept: short gnarled trunk, limbs leaning inland (+X) under flat-topped shelves
  Pine_A/B       Monterey pine: straight trunk, whorled branches with needle tufts, broad irregular crown
  Oak_A/B        coast live oak: short trunk, sinuous spreading limbs, dense dark dome

Every tree is exported as one FBX with Name_LOD0..Name_LOD3 meshes, which Unity's model importer turns into a LODGroup
(screen-height thresholds are in the .fbx.meta, see nature_manifest.json):
  LOD0  full skeleton, dark inner canopy shells, leaf cards (2 tris each)            hero, < ~45 m
  LOD1  limbs + sub-limbs, shells, ~40% of the cards at 1.5x size                     < ~110 m
  LOD2  trunk + main limbs, coarse shells, ~10% of the cards at 2.6x size             < ~350 m
  LOD3  impostor: two crossed vertical cards + one horizontal card (6 tris), baked     beyond
        front/side/top views in Impostor_Trees.png

Vertex colours (all meshes): R = occlusion, G = sun-bleached tip tint, B = wind sway weight (by reach from the trunk,
so bark and leaves at a limb end move together), A = leaf flutter weight (cards 1, bark/shells 0).
Materials: PB_Leaf_Cypress / PB_Leaf_Pine / PB_Leaf_Oak (alpha-cut atlas), PB_Bark_Tree, PB_Impostor_Trees; Unity remaps
them to Assets/_Game/Art/Course/Nature/Materials through the FBX .meta externalObjects.

pp_pebble.py uses scenery_tree_mesh() / impostor_mesh() to dress the surround with the same assets (LOD2 meshes near
the course, impostor cards for the distant Del Monte Forest).
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = "/Users/calebsnyder/Real-Golf"
sys.path.insert(0, os.path.join(ROOT, "ArtSource/Blender/scripts"))
import pp_nature_tex as TEX  # noqa: E402

NATURE_DIR = os.path.join(ROOT, "Assets/_Game/Art/Course/Nature")
TEX_DIR = os.path.join(NATURE_DIR, "Textures")
CYPRESS_DIR = os.path.join(ROOT, "Assets/_Game/Resources/Models/Course/Cypress")
VEG_DIR = os.path.join(ROOT, "Assets/_Game/Resources/Models/Course/Vegetation")
REVIEW_DIR = os.path.join(ROOT, "ArtSource/Review/Nature")
MANIFEST = os.path.join(NATURE_DIR, "nature_manifest.json")
BLEND = os.path.join(ROOT, "ArtSource/Blender/PuttshackPro_Nature.blend")
if os.environ.get("PP_NATURE_TEST"):  # dry runs write everything under this directory instead of the project
    _T = os.environ["PP_NATURE_TEST"]
    NATURE_DIR, TEX_DIR, CYPRESS_DIR, VEG_DIR, REVIEW_DIR = _T, _T + "/Textures", _T + "/Cypress", _T + "/Veg", _T + "/Review"
    MANIFEST, BLEND = _T + "/nature_manifest.json", _T + "/Nature.blend"

# (name, species, seed, height m, crown width m, export dir)
TREES = [
    ("Cypress_A", "cypress", 101, 10.0, 0, CYPRESS_DIR),
    ("Cypress_B", "cypress", 202, 10.0, 0, CYPRESS_DIR),
    ("Cypress_C", "cypress", 303, 10.0, 0, CYPRESS_DIR),
    ("Pine_A", "pine", 41, 14.0, 9.0, VEG_DIR),
    ("Pine_B", "pine", 77, 15.5, 10.5, VEG_DIR),
    ("Oak_A", "oak", 13, 8.0, 11.0, VEG_DIR),
    ("Oak_B", "oak", 29, 9.2, 12.0, VEG_DIR),
]
TREE_INDEX = {t[0]: i for i, t in enumerate(TREES)}
LEAF_MAT = {"cypress": "PB_Leaf_Cypress", "pine": "PB_Leaf_Pine", "oak": "PB_Leaf_Oak"}
BARK_MAT = "PB_Bark_Tree"
IMPOSTOR_MAT = "PB_Impostor_Trees"
IMPOSTOR_ATLAS = 2048
IMPOSTOR_CELL = 512
# Unity LODGroup screen-relative heights (LOD0..LOD3 end), written into the .fbx.meta lODScreenPercentages.
LOD_SCREEN = [0.25, 0.1, 0.03, 0.004]
BUDGET = {"cypress": (10000, 4000, 1500, 6), "pine": (10000, 4000, 1500, 6), "oak": (10000, 4000, 1500, 6)}
# per species: card coverage factor, card size factor, card size clamp
CARD = {"cypress": (1.7, 0.5, (0.75, 1.8)), "pine": (1.2, 0.62, (0.6, 1.5)), "oak": (1.3, 0.5, (0.75, 1.6))}
LOD_CARDS = [(1.0, 1.0), (0.4, 1.5), (0.1, 2.6)]


def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def clamp01(x):
    return min(max(x, 0.0), 1.0)


# ================================================================ mesh accumulator
class Acc:
    """Vertices with custom normals and RGBA colours, faces with per-corner UVs and a material name."""

    def __init__(self):
        self.v, self.n, self.c = [], [], []
        self.f, self.uv, self.m = [], [], []

    def vert(self, p, n, c):
        self.v.append((p[0], p[1], p[2]))
        self.n.append((n[0], n[1], n[2]))
        self.c.append(tuple(c))
        return len(self.v) - 1

    def face(self, idx, uvs, mat):
        self.f.append(tuple(idx))
        self.uv.append(tuple(uvs))
        self.m.append(mat)

    def extend(self, o, M=None, color=None):
        base = len(self.v)
        if M is None:
            self.v.extend(o.v)
            self.n.extend(o.n)
        else:
            R = M.to_3x3()
            for p, n in zip(o.v, o.n):
                q = M @ Vector(p)
                m = (R @ Vector(n)).normalized()
                self.v.append((q.x, q.y, q.z))
                self.n.append((m.x, m.y, m.z))
        self.c.extend(o.c if color is None else [color(c) for c in o.c])
        self.f.extend(tuple(base + i for i in f) for f in o.f)
        self.uv.extend(o.uv)
        self.m.extend(o.m)

    @property
    def tris(self):
        return sum(len(f) - 2 for f in self.f)

    def bounds(self):
        xs, ys, zs = zip(*self.v)
        return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))

    def to_object(self, name, coll):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        mats = []
        for m in self.m:
            if m not in mats:
                mats.append(m)
        for m in mats:
            me.materials.append(material(m))
        me.polygons.foreach_set("material_index", [mats.index(m) for m in self.m])
        me.polygons.foreach_set("use_smooth", [True] * len(self.f))
        uvl = me.uv_layers.new(name="UVMap")
        flat = [c for face in self.uv for uv in face for c in uv]
        uvl.data.foreach_set("uv", flat)
        ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        ca.data.foreach_set("color", [x for c in self.c for x in c])
        me.update()
        me.normals_split_custom_set_from_vertices(self.n)
        ob = bpy.data.objects.new(name, me)
        coll.objects.link(ob)
        return ob


# ================================================================ materials (preview-capable; Unity remaps by name)
TIP_TINT = (1.22, 1.16, 0.8)
AO_STRENGTH = 0.8


def _tex_node(nt, path, non_color=False):
    img = bpy.data.images.get(os.path.basename(path))
    if img is None or img.filepath != path:
        img = bpy.data.images.load(path, check_existing=True)
    img.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
    img.alpha_mode = "STRAIGHT"
    n = nt.nodes.new("ShaderNodeTexImage")
    n.image = img
    return n


def _albedo_chain(nt, tex_color, tint=True):
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(vc.outputs["Color"], sep.inputs[0])
    ao = nt.nodes.new("ShaderNodeVectorMath")
    ao.operation = "MULTIPLY_ADD"
    nt.links.new(sep.outputs[0], ao.inputs[0])
    ao.inputs[1].default_value = (AO_STRENGTH,) * 3
    ao.inputs[2].default_value = (1 - AO_STRENGTH,) * 3
    mul = nt.nodes.new("ShaderNodeVectorMath")
    mul.operation = "MULTIPLY"
    nt.links.new(tex_color, mul.inputs[0])
    nt.links.new(ao.outputs[0], mul.inputs[1])
    if not tint:
        return mul.outputs[0]
    tc = nt.nodes.new("ShaderNodeVectorMath")
    tc.operation = "MULTIPLY_ADD"
    nt.links.new(sep.outputs[1], tc.inputs[0])
    tc.inputs[1].default_value = tuple(t - 1 for t in TIP_TINT)
    tc.inputs[2].default_value = (1.0, 1.0, 1.0)
    mul2 = nt.nodes.new("ShaderNodeVectorMath")
    mul2.operation = "MULTIPLY"
    nt.links.new(mul.outputs[0], mul2.inputs[0])
    nt.links.new(tc.outputs[0], mul2.inputs[1])
    return mul2.outputs[0]


def _build_material(m, kind, tex, bake):
    """kind: leaf | bark | impostor. bake=True gives an unlit albedo (emission) version for the impostor renders."""
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    t = _tex_node(nt, tex)
    if kind == "impostor":
        albedo = t.outputs["Color"]
    else:
        albedo = _albedo_chain(nt, t.outputs["Color"], tint=(kind == "leaf"))
    cut = None
    if kind in ("leaf", "impostor"):
        cut = nt.nodes.new("ShaderNodeMath")
        cut.operation = "GREATER_THAN"
        nt.links.new(t.outputs["Alpha"], cut.inputs[0])
        cut.inputs[1].default_value = 0.5
    if bake:
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(albedo, em.inputs["Color"])
        shader = em.outputs[0]
    else:
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(albedo, bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.75 if kind != "bark" else 0.9
        if kind == "bark":
            nm = _tex_node(nt, os.path.join(TEX_DIR, "Bark_Tree_Normal.png"), non_color=True)
            nmap = nt.nodes.new("ShaderNodeNormalMap")
            nmap.inputs["Strength"].default_value = 0.8
            nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
            nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        shader = bsdf.outputs[0]
        if kind == "leaf":
            tr = nt.nodes.new("ShaderNodeBsdfTranslucent")
            nt.links.new(albedo, tr.inputs["Color"])
            mix = nt.nodes.new("ShaderNodeMixShader")
            mix.inputs[0].default_value = 0.22
            nt.links.new(shader, mix.inputs[1])
            nt.links.new(tr.outputs[0], mix.inputs[2])
            shader = mix.outputs[0]
    if cut is not None:
        tp = nt.nodes.new("ShaderNodeBsdfTransparent")
        mix = nt.nodes.new("ShaderNodeMixShader")
        nt.links.new(cut.outputs[0], mix.inputs[0])
        nt.links.new(tp.outputs[0], mix.inputs[1])
        nt.links.new(shader, mix.inputs[2])
        shader = mix.outputs[0]
    nt.links.new(shader, out.inputs["Surface"])


MAT_SPEC = {
    "PB_Leaf_Cypress": ("leaf", "Leaf_Cypress.png", (0.15, 0.24, 0.1)),
    "PB_Leaf_Pine": ("leaf", "Leaf_Pine.png", (0.16, 0.29, 0.13)),
    "PB_Leaf_Oak": ("leaf", "Leaf_Oak.png", (0.13, 0.21, 0.08)),
    "PB_Bark_Tree": ("bark", "Bark_Tree.png", (0.3, 0.26, 0.22)),
    "PB_Impostor_Trees": ("impostor", "Impostor_Trees.png", (0.14, 0.22, 0.1)),
}


def material(name, bake=False):
    key = name + ("__bake" if bake else "")
    m = bpy.data.materials.get(key)
    if m is not None:
        return m
    m = bpy.data.materials.new(key)
    kind, tex, rgb = MAT_SPEC[name]
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    path = os.path.join(TEX_DIR, tex)
    if os.path.exists(path):
        _build_material(m, kind, path, bake)
    return m


def refresh_material(name):
    """Rebuild a material's nodes (after its texture was regenerated)."""
    for key in (name, name + "__bake"):
        m = bpy.data.materials.get(key)
        if m is not None:
            kind, tex, _ = MAT_SPEC[name]
            img = bpy.data.images.get(tex)
            if img is not None:
                img.reload()
            _build_material(m, kind, os.path.join(TEX_DIR, tex), key.endswith("__bake"))


# ================================================================ skeletons
class Branch:
    def __init__(self, pts, radii, depth):
        self.pts, self.radii, self.depth = pts, radii, depth


class Pad:
    def __init__(self, c, rx, ry, rz):
        self.c, self.rx, self.ry, self.rz = Vector(c), rx, ry, rz


def rdir(rnd):
    while True:
        v = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)))
        if 0.05 < v.length <= 1:
            return v.normalized()


def gnarl(rnd, start, d, L, segs, kink, rise=0.0, droop=0.0):
    """Polyline from start along d with random kinks; rise > 0 bends it upward toward the tip, droop sags the middle."""
    pts = [Vector(start)]
    d = Vector(d).normalized()
    for i in range(segs):
        t = (i + 1) / segs
        d = (d + rdir(rnd) * kink + Vector((0, 0, rise * t))).normalized()
        p = pts[-1] + d * (L / segs)
        p.z -= droop * math.sin(math.pi * t) * L / segs
        pts.append(p)
    return pts


def taper(n, r0, r1, power=1.0):
    return [r0 + (r1 - r0) * (i / max(1, n - 1)) ** power for i in range(n)]


def cypress_skeleton(seed, H, W):
    rnd = random.Random(seed)
    s = H / 10.0
    lean = Vector((1, 0, 0))
    br, pads = [], []
    hs = H * rnd.uniform(0.14, 0.22)
    root = Vector((0, 0, -0.8 * s))
    fork = Vector((0, 0, hs)) + lean * hs * rnd.uniform(0.25, 0.45)
    mid = Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.15, 0.15), hs * 0.45)) * 1.0
    trunk = [root, Vector((0, 0, 0)), mid, (mid + fork) / 2 + Vector((0, rnd.uniform(-0.1, 0.1), 0)), fork]
    br.append(Branch(trunk, [0.95 * s, 0.82 * s, 0.7 * s, 0.6 * s, 0.52 * s], 0))
    nl = rnd.randint(4, 6)
    a0 = rnd.uniform(0, 2 * math.pi)
    for i in range(nl):
        ang = a0 + 2 * math.pi * i / nl + rnd.uniform(-0.35, 0.35)
        elev = math.radians(rnd.uniform(16, 42))
        L = H * rnd.uniform(0.5, 0.74)
        d = (Vector((math.cos(ang) * math.cos(elev), math.sin(ang) * math.cos(elev), math.sin(elev))) + lean * 0.5)
        pts = gnarl(rnd, fork, d, L, 6, 0.28, rise=-0.15)
        end = pts[-1]
        zt = min(max(end.z, H * 0.46), H * 0.76)
        for k, p in enumerate(pts):
            p.z += (zt - end.z) * (k / (len(pts) - 1)) ** 1.5
        br.append(Branch(pts, taper(len(pts), 0.4 * s, 0.1 * s, 0.8), 0))
        pr = H * rnd.uniform(0.17, 0.24)
        pads.append(Pad(pts[-1] + Vector((0, 0, pr * 0.12)), pr * rnd.uniform(0.95, 1.15), pr * rnd.uniform(0.8, 1.0),
                        pr * rnd.uniform(0.3, 0.36)))
        for k in range(rnd.randint(1, 3)):
            j = rnd.randint(2, len(pts) - 2)
            h = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized()
            sd = (d.normalized() + h * 0.9 + Vector((0, 0, 0.25))).normalized()
            sub = gnarl(rnd, pts[j], sd, L * rnd.uniform(0.25, 0.42), 4, 0.3)
            if sub[-1].z < H * 0.36:
                sub[-1].z = H * 0.36
            br.append(Branch(sub, taper(len(sub), 0.17 * s, 0.05 * s), 1))
            pr2 = pr * rnd.uniform(0.55, 0.8)
            pads.append(Pad(sub[-1] + Vector((0, 0, pr2 * 0.1)), pr2, pr2 * rnd.uniform(0.8, 1.0), pr2 * 0.36))
    top = fork + lean * H * 0.12 + Vector((0, 0, H * 0.6))
    leader = gnarl(rnd, fork, top - fork, (top - fork).length, 5, 0.12)
    br.append(Branch(leader, taper(len(leader), 0.34 * s, 0.1 * s), 0))
    for k in range(rnd.randint(2, 4)):
        a = rnd.uniform(0, 2 * math.pi)
        off = Vector((math.cos(a), math.sin(a), 0)) * H * rnd.uniform(0.05, 0.2)
        c = leader[-1] + off + lean * H * 0.06 + Vector((0, 0, H * rnd.uniform(0.02, 0.1)))
        pr = H * rnd.uniform(0.19, 0.26)
        pads.append(Pad(c, pr, pr * rnd.uniform(0.8, 1.0), H * 0.095))
        twig = gnarl(rnd, leader[-2], c - leader[-2], (c - leader[-2]).length, 3, 0.15)
        br.append(Branch(twig, taper(len(twig), 0.12 * s, 0.05 * s), 1))
    return br, pads


def pine_skeleton(seed, H, W):
    rnd = random.Random(seed)
    s = H / 14.0
    br, pads = [], []
    trunk = [Vector((0, 0, -0.6 * s))]
    n = 9
    off = Vector((0, 0, 0))
    for i in range(1, n + 1):
        t = i / n
        off += Vector((rnd.uniform(-0.12, 0.16), rnd.uniform(-0.12, 0.12), 0)) * s
        trunk.append(Vector((off.x, off.y, 0.97 * H * t)))
    br.append(Branch(trunk, taper(len(trunk), 0.46 * s, 0.07 * s, 0.85), 0))

    def trunk_at(z):
        for a, b in zip(trunk, trunk[1:]):
            if a.z <= z <= b.z:
                return a.lerp(b, (z - a.z) / max(b.z - a.z, 1e-6))
        return trunk[-1].copy()

    z = H * rnd.uniform(0.3, 0.36)
    ga = rnd.uniform(0, 2 * math.pi)
    while z < H * 0.9:
        t = (z - 0.3 * H) / (0.6 * H)
        nb = rnd.randint(3, 5)
        for k in range(nb):
            ga += 2.39996
            if rnd.random() > 0.8:
                continue
            Lmax = W * 0.5 * (0.6 + 0.4 * math.sin(math.pi * (0.25 + 0.7 * t))) * (1 - 0.5 * t * t)
            L = Lmax * rnd.uniform(0.7, 1.05)
            elev = math.radians(rnd.uniform(-4, 16) + 14 * t)
            d = Vector((math.cos(ga) * math.cos(elev), math.sin(ga) * math.cos(elev), math.sin(elev)))
            pts = gnarl(rnd, trunk_at(z), d, L, 4, 0.12, rise=0.35, droop=0.25)
            br.append(Branch(pts, taper(len(pts), 0.16 * s * (1 - 0.5 * t), 0.035 * s), 1))
            pr = max(1.0, 0.36 * L) * rnd.uniform(0.85, 1.15)
            pads.append(Pad(pts[-1] + Vector((0, 0, pr * 0.15)), pr, pr * rnd.uniform(0.8, 1.0), pr * 0.5))
            if L > 2.6:
                q = pts[2]
                pr2 = pr * rnd.uniform(0.6, 0.8)
                pads.append(Pad(q + Vector((0, 0, pr2 * 0.3)), pr2, pr2, pr2 * 0.45))
        z += H * rnd.uniform(0.05, 0.075)
    for k in range(rnd.randint(2, 3)):
        a = rnd.uniform(0, 2 * math.pi)
        c = trunk[-1] + Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.4, 1.2) * s + Vector((0, 0, 0.3 * s))
        pr = rnd.uniform(1.1, 1.6) * s
        pads.append(Pad(c, pr, pr, pr * 0.55))
    return br, pads


def oak_skeleton(seed, H, W):
    rnd = random.Random(seed)
    s = H / 8.0
    br, pads = [], []
    ht = H * rnd.uniform(0.24, 0.3)
    lean = Vector((rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 0))
    trunk = [Vector((0, 0, -0.5 * s)), Vector((0, 0, 0)), Vector((0, 0, ht * 0.5)) + lean * 0.5, Vector((0, 0, ht)) + lean]
    br.append(Branch(trunk, [0.62 * s, 0.55 * s, 0.47 * s, 0.42 * s], 0))
    top = trunk[-1]
    dome_c = Vector((lean.x, lean.y, H * 0.57))
    dome_r = Vector((W * 0.5, W * 0.46, H * 0.43))
    ends = []
    nl = rnd.randint(4, 6)
    a0 = rnd.uniform(0, 2 * math.pi)
    for i in range(nl):
        ang = a0 + 2 * math.pi * i / nl + rnd.uniform(-0.3, 0.3)
        elev = math.radians(rnd.uniform(30, 58))
        d = Vector((math.cos(ang) * math.cos(elev), math.sin(ang) * math.cos(elev), math.sin(elev)))
        L = (W * 0.5) * rnd.uniform(0.55, 0.75)
        pts = gnarl(rnd, top, d, L, 5, 0.3, rise=-0.1)
        br.append(Branch(pts, taper(len(pts), 0.3 * s, 0.11 * s, 0.8), 0))
        ends.append(pts[-1])
        for k in range(rnd.randint(2, 3)):
            j = rnd.randint(2, len(pts) - 1)
            h = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0, 0.8))).normalized()
            sub = gnarl(rnd, pts[j], (d + h).normalized(), L * rnd.uniform(0.35, 0.55), 4, 0.35)
            br.append(Branch(sub, taper(len(sub), 0.13 * s, 0.04 * s), 1))
            ends.append(sub[-1])
    # pads tile the upper dome (Fibonacci sphere), pulled slightly inward
    n = rnd.randint(24, 30)
    for i in range(n * 2):
        zf = 1 - (i + 0.5) / (n * 2) * 1.6
        if zf < -0.45:
            continue
        r = math.sqrt(max(0.0, 1 - zf * zf))
        a = i * 2.39996 + rnd.uniform(-0.2, 0.2)
        dirv = Vector((math.cos(a) * r, math.sin(a) * r, zf))
        c = dome_c + Vector((dirv.x * dome_r.x, dirv.y * dome_r.y, dirv.z * dome_r.z)) * rnd.uniform(0.72, 0.86)
        pr = rnd.uniform(1.5, 2.1) * s * (W / 11.0)
        pads.append(Pad(c, pr, pr * rnd.uniform(0.85, 1.0), pr * rnd.uniform(0.5, 0.62)))
        e = min(ends, key=lambda q: (q - c).length)
        if (e - c).length > 0.8:
            tw = gnarl(rnd, e, c - e, (c - e).length * 0.85, 2, 0.2)
            br.append(Branch(tw, taper(len(tw), 0.06 * s, 0.03 * s), 2))
    return br, pads


SKELETONS = {"cypress": cypress_skeleton, "pine": pine_skeleton, "oak": oak_skeleton}


# ================================================================ geometry
class Ctx:
    """Tree-wide shading context: canopy centre/extent for normals, reach for the wind weight."""

    def __init__(self, pads, H):
        self.H = H
        w = sum(p.rx * p.ry for p in pads)
        self.cc = sum((p.c * (p.rx * p.ry) for p in pads), Vector()) / w
        self.cr = max((p.c - self.cc).length + max(p.rx, p.ry) for p in pads)
        self.reach = max(math.hypot(p.c.x, p.c.y) + max(p.rx, p.ry) for p in pads)
        self.top = max(p.c.z + p.rz for p in pads)

    def wind(self, p):
        r = math.hypot(p.x, p.y) / max(self.reach, 1e-3)
        return clamp01(0.08 + 0.92 * r ** 1.2) * smooth(0.15 * self.H, 0.45 * self.H, p.z)


def tube(acc, br, sides, ctx, step=1, uv_around=1.1, uv_along=2.4):
    pts = br.pts[::step] if step > 1 else br.pts
    radii = br.radii[::step] if step > 1 else br.radii
    if step > 1 and pts[-1] is not br.pts[-1]:
        pts = pts + [br.pts[-1]]
        radii = radii + [br.radii[-1]]
    n = len(pts)
    if n < 2:
        return
    T = [(pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(n)]
    a = Vector((1, 0, 0)) if abs(T[0].x) < 0.9 else Vector((0, 1, 0))
    N = T[0].cross(a).normalized()
    rep = max(1, round(2 * math.pi * radii[0] / uv_around))
    rings, vs = [], []
    v = 0.0
    for i in range(n):
        if i > 0:
            axis = T[i - 1].cross(T[i])
            if axis.length > 1e-6:
                N = Matrix.Rotation(T[i - 1].angle(T[i]), 3, axis.normalized()) @ N
            v += (pts[i] - pts[i - 1]).length / uv_along
        B = T[i].cross(N)
        ring = []
        for k in range(sides + 1):
            t = 2 * math.pi * k / sides
            d = N * math.cos(t) + B * math.sin(t)
            p = pts[i] + d * radii[i]
            ao = (0.55 + 0.45 * smooth(-0.4, 0.45 * ctx.H, p.z)) * (1.0 if br.depth == 0 else 0.85)
            ring.append(acc.vert(p, d, (ao, 0.0, ctx.wind(p), 0.0)))
        rings.append(ring)
        vs.append(v)
    for i in range(n - 1):
        for k in range(sides):
            u0, u1 = rep * k / sides, rep * (k + 1) / sides
            acc.face((rings[i][k], rings[i][k + 1], rings[i + 1][k + 1], rings[i + 1][k]),
                     ((u0, vs[i]), (u1, vs[i]), (u1, vs[i + 1]), (u0, vs[i + 1])), BARK_MAT)


def cell_rect(k, inset=0.004):
    u0, v0 = 0.5 * (k % 2), 0.5 * (k // 2)
    return u0 + inset, v0 + inset, u0 + 0.5 - inset, v0 + 0.5 - inset


def pad_shading(pad, ctx, p, rnd_tint=0.0):
    rel = p - pad.c
    zz = max(-1.0, min(1.0, rel.z / pad.rz))
    e = Vector((rel.x / pad.rx, rel.y / pad.ry, rel.z / pad.rz)).length
    ao = (0.36 + 0.64 * smooth(-1.0, 0.7, zz)) * (0.68 + 0.32 * smooth(0.35, 1.0, e))
    ao *= 0.6 + 0.4 * smooth(0.2 * ctx.H, 0.95 * ctx.top, p.z)
    tint = clamp01(0.12 + 0.6 * smooth(-0.1, 1.0, zz) * smooth(0.35 * ctx.top, ctx.top, p.z) + rnd_tint)
    q = Vector((rel.x / pad.rx, rel.y / pad.ry, rel.z / pad.rz))
    n = (q.normalized() if q.length > 1e-6 else Vector((0, 0, 1))) + (p - ctx.cc) / ctx.cr * 0.6 + Vector((0, 0, 0.3))
    return n.normalized(), ao, tint


def pad_cards(acc, pad, count, size, rnd, ctx, mat):
    for _ in range(count):
        d = rdir(rnd)
        if d.z < -0.25 and rnd.random() < 0.65:
            d.z = -d.z
        r = rnd.uniform(0.72, 1.0)
        p = pad.c + Vector((d.x * pad.rx, d.y * pad.ry, d.z * pad.rz)) * r
        on = Vector((d.x / pad.rx, d.y / pad.ry, d.z / pad.rz)).normalized()
        sz = size * rnd.uniform(0.8, 1.25)
        if rnd.random() < 0.55:
            fn = (on + rdir(rnd) * 0.45).normalized()
            vd = Vector((p.x - pad.c.x, p.y - pad.c.y, 0.0))
            vd = vd.normalized() if vd.length > 1e-4 else Vector((1, 0, 0))
            vd = vd + Vector((0, 0, 0.35))
            vd = vd - fn * vd.dot(fn)
            if vd.length < 1e-4:
                vd = fn.orthogonal()
            vd.normalize()
            ud = vd.cross(fn).normalized()
        else:
            vd = (on + rdir(rnd) * 0.3).normalized()
            ud = vd.cross(rdir(rnd))
            if ud.length < 1e-4:
                ud = vd.orthogonal()
            ud.normalize()
        base = p - vd * sz * 0.3
        corners = [base - ud * sz * 0.5, base + ud * sz * 0.5, base + ud * sz * 0.5 + vd * sz,
                   base - ud * sz * 0.5 + vd * sz]
        u0, v0, u1, v1 = cell_rect(rnd.randrange(3))
        if rnd.random() < 0.5:
            u0, u1 = u1, u0
        rt = rnd.uniform(-0.12, 0.12)
        idx = []
        for k, q in enumerate(corners):
            n, ao, tint = pad_shading(pad, ctx, q, rt)
            idx.append(acc.vert(q, n, (ao, tint, ctx.wind(q), 1.0 if k >= 2 else 0.35)))
        acc.face(idx, ((u0, v0), (u1, v0), (u1, v1), (u0, v1)), mat)


def pad_shell(acc, pad, subdiv, ctx, mat):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.index_update()
    u0, v0, u1, v1 = cell_rect(3, 0.02)
    ids = []
    for vtx in bm.verts:
        q = vtx.co.normalized()
        p = pad.c + Vector((q.x * pad.rx * 0.74, q.y * pad.ry * 0.74, q.z * pad.rz * 0.72 - pad.rz * 0.1))
        n, ao, tint = pad_shading(pad, ctx, p)
        ids.append((acc.vert(p, n, (ao * 0.6, tint * 0.3, ctx.wind(p), 0.0)),
                    (u0 + (u1 - u0) * (0.5 + 0.5 * q.x), v0 + (v1 - v0) * (0.5 + 0.5 * (q.y * 0.5 + q.z * 0.5)))))
    for f in bm.faces:
        vv = [ids[v.index] for v in f.verts]
        acc.face([a for a, _ in vv], [b for _, b in vv], mat)
    bm.free()


def build_lod(spec, lod):
    name, species, seed, H, W, _ = spec
    br, pads = SKELETONS[species](seed, H, W)
    ctx = Ctx(pads, H)
    acc = Acc()
    sides = [(10, 7, 5), (7, 5, 0), (5, 0, 0)][lod]
    for b in br:
        sd = sides[min(b.depth, 2)]
        if sd == 0:
            continue
        tube(acc, b, sd, ctx, step=2 if lod == 2 else 1)
    cov, kf, (smin, smax) = CARD[species]
    nf, sf = LOD_CARDS[lod]
    mat = LEAF_MAT[species]
    for i, pad in enumerate(pads):
        rnd = random.Random(seed * 7919 + i * 31 + lod)
        r = (pad.rx + pad.ry) * 0.5
        size = min(max(kf * r, smin), smax)
        area = 4 * math.pi * ((pad.rx * pad.ry + pad.rx * pad.rz + pad.ry * pad.rz) / 3)
        n0 = cov * area / (size * size)
        count = max(2, int(round(n0 * nf)))
        pad_cards(acc, pad, count, size * sf, rnd, ctx, mat)
        pad_shell(acc, pad, 2 if lod == 0 and r > 1.2 else 1, ctx, mat)
    return acc, ctx


# ================================================================ impostors
def impostor_layout(i):
    """Atlas rects (u0, v0, u1, v1) of tree i's front view, side view and top view."""
    c = IMPOSTOR_CELL / IMPOSTOR_ATLAS

    def cell(k):
        col, row = k % 4, k // 4
        return col * c, row * c, (col + 1) * c, (row + 1) * c

    front, side = cell(2 * i), cell(2 * i + 1)
    big = 14 + i // 4
    sub = i % 4
    bu0, bv0, _, _ = cell(big)
    h = c / 2
    top = (bu0 + (sub % 2) * h, bv0 + (sub // 2) * h, bu0 + (sub % 2 + 1) * h, bv0 + (sub // 2 + 1) * h)
    return front, side, top


def impostor_frames(lo, hi):
    """Square ortho frames: (centre-x/y, z0, size) for the front (looking +Y), side (looking -X) and top views."""
    hgt = hi.z - lo.z
    fs = max(hi.x - lo.x, hgt) * 1.03
    ss = max(hi.y - lo.y, hgt) * 1.03
    ts = max(hi.x - lo.x, hi.y - lo.y) * 1.03
    cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2
    return {"front": (cx, lo.z, fs), "side": (cy, lo.z, ss), "top": (cx, cy, ts), "lo": lo, "hi": hi}


def impostor_mesh(i, fr):
    """Two crossed vertical cards and one horizontal card at 70% height, UV'd into the impostor atlas."""
    front, side, top = impostor_layout(i)
    lo, hi = fr["lo"], fr["hi"]
    cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2
    centre = Vector((cx, cy, (lo.z + hi.z) / 2))
    ext = Vector((max(hi.x - lo.x, 1) / 2, max(hi.y - lo.y, 1) / 2, max(hi.z - lo.z, 1) / 2))
    acc = Acc()

    def quad(ps, rect, up=False):
        u0, v0, u1, v1 = rect
        uvs = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))
        idx = []
        for k, p in enumerate(ps):
            q = p - centre
            n = Vector((q.x / ext.x, q.y / ext.y, q.z / ext.z)) + Vector((0, 0, 0.6 if up else 0.25))
            idx.append(acc.vert(p, n.normalized(), (1.0, 0.0, 0.15 if k >= 2 else 0.0, 0.0)))
        acc.face(idx, uvs, IMPOSTOR_MAT)

    fx, z0, fs = fr["front"]
    quad([Vector((fx - fs / 2, cy, z0)), Vector((fx + fs / 2, cy, z0)), Vector((fx + fs / 2, cy, z0 + fs)),
          Vector((fx - fs / 2, cy, z0 + fs))], front)
    sy, z0, ss = fr["side"]
    quad([Vector((cx, sy - ss / 2, z0)), Vector((cx, sy + ss / 2, z0)), Vector((cx, sy + ss / 2, z0 + ss)),
          Vector((cx, sy - ss / 2, z0 + ss))], side)
    tx, ty, ts = fr["top"]
    zt = lo.z + 0.7 * (hi.z - lo.z)
    quad([Vector((tx - ts / 2, ty - ts / 2, zt)), Vector((tx + ts / 2, ty - ts / 2, zt)),
          Vector((tx + ts / 2, ty + ts / 2, zt)), Vector((tx - ts / 2, ty + ts / 2, zt))], top, up=True)
    return acc


def _cycles(scene, samples, transparent):
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.film_transparent = transparent
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        scene.cycles.device = "GPU"
    except Exception as e:  # noqa: BLE001
        print("[render] GPU unavailable (%s); using CPU" % e)
        scene.cycles.device = "CPU"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1


def _load_rgba(path):
    import numpy as np
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return px


def render_impostors(objs):
    """objs: LOD0 object per tree (TREES order). Renders unlit albedo views and assembles Impostor_Trees.png."""
    import numpy as np
    scene = bpy.context.scene
    _cycles(scene, 24, True)
    scene.render.resolution_x = scene.render.resolution_y = IMPOSTOR_CELL
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    world = bpy.data.worlds.new("Impostor_World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[1].default_value = 0.0
    scene.world = world
    cam = bpy.data.objects.new("Impostor_Cam", bpy.data.cameras.new("Impostor_Cam"))
    scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    scene.camera = cam
    atlas = np.zeros((IMPOSTOR_ATLAS, IMPOSTOR_ATLAS, 4), np.float32)
    alpha = np.zeros((IMPOSTOR_ATLAS, IMPOSTOR_ATLAS), np.float32)
    frames = []
    tmp = os.path.join(REVIEW_DIR, "_impostor_tmp.png")
    os.makedirs(REVIEW_DIR, exist_ok=True)
    for i, ob in enumerate(objs):
        saved = [s.material for s in ob.material_slots]
        for s in ob.material_slots:
            base = s.material.name.split("__")[0]
            s.material = material(base, bake=True)
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.hide_render = o is not ob
        acc_lo = Vector(ob.bound_box[0])
        acc_hi = Vector(ob.bound_box[6])
        fr = impostor_frames(acc_lo, acc_hi)
        frames.append(fr)
        rects = impostor_layout(i)
        views = [("front", rects[0]), ("side", rects[1]), ("top", rects[2])]
        for view, rect in views:
            if view == "front":
                x, z0, sz = fr["front"]
                cam.location = (x, fr["lo"].y - 50, z0 + sz / 2)
                cam.rotation_euler = (math.radians(90), 0, 0)
            elif view == "side":
                y, z0, sz = fr["side"]
                cam.location = (fr["hi"].x + 50, y, z0 + sz / 2)
                cam.rotation_euler = (math.radians(90), 0, math.radians(90))
            else:
                x, y, sz = fr["top"]
                cam.location = (x, y, fr["hi"].z + 50)
                cam.rotation_euler = (0, 0, 0)
            cam.data.ortho_scale = sz
            cam.data.clip_start = 0.1
            cam.data.clip_end = 400
            res = IMPOSTOR_CELL if view != "top" else IMPOSTOR_CELL // 2
            scene.render.resolution_x = scene.render.resolution_y = res
            scene.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            px = _load_rgba(tmp)
            u0, v0 = int(round(rect[0] * IMPOSTOR_ATLAS)), int(round(rect[1] * IMPOSTOR_ATLAS))
            atlas[v0:v0 + res, u0:u0 + res] = px
            alpha[v0:v0 + res, u0:u0 + res] = px[..., 3]
        for s, m in zip(ob.material_slots, saved):
            s.material = m
        print("[impostor] %s" % TREES[i][0])
    # rendered colour is straight alpha over a black background (premultiplied in effect); un-premultiply + bleed
    rgb = TEX.dilate(atlas[..., :3] * alpha[..., None], alpha)
    out = np.concatenate([rgb, alpha[..., None]], -1)
    TEX.save_png(os.path.join(TEX_DIR, "Impostor_Trees.png"), out)
    if os.path.exists(tmp):
        os.remove(tmp)
    bpy.data.objects.remove(cam, do_unlink=True)
    return frames


# ================================================================ export
def export_fbx(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    vl = bpy.context.view_layer
    vl.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    vl.objects.active = objs[0]
    kw = dict(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
              use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
              path_mode="STRIP")
    if "colors_type" in bpy.ops.export_scene.fbx.get_rna_type().properties.keys():
        kw["colors_type"] = "LINEAR"
    bpy.ops.export_scene.fbx(**kw)
    for o in objs:
        o.select_set(False)


def new_coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    for o in list(c.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    return c


def check_winding(acc, mat):
    """Fraction of faces of `mat` whose geometric normal agrees with their custom normals (bark must be ~1)."""
    good = tot = 0
    for f, m in zip(acc.f, acc.m):
        if m != mat:
            continue
        a, b, c = (Vector(acc.v[i]) for i in f[:3])
        gn = (b - a).cross(c - a)
        vn = sum((Vector(acc.n[i]) for i in f), Vector())
        tot += 1
        good += gn.dot(vn) > 0
    return good / max(1, tot)


def build_trees(export=True, impostors=True):
    os.makedirs(TEX_DIR, exist_ok=True)
    for name in MAT_SPEC:
        refresh_material(name)
    report = {}
    lod_objs = []
    for spec in TREES:
        name, species = spec[0], spec[1]
        c = new_coll("Tree_" + name)
        objs = []
        tris = []
        for lod in range(3):
            acc, ctx = build_lod(spec, lod)
            if lod == 0:
                wind = check_winding(acc, BARK_MAT)
                lo, hi = acc.bounds()
            objs.append(acc.to_object("%s_LOD%d" % (name, lod), c))
            tris.append(acc.tris)
        lod_objs.append(objs)
        report[name] = {"species": species, "height": spec[3], "tris": tris, "bark_winding_ok": round(wind, 4),
                        "bounds_blender": [list(map(lambda x: round(x, 3), lo)), list(map(lambda x: round(x, 3), hi))]}
        print("[tree] %-10s LOD0 %5d  LOD1 %5d  LOD2 %5d tris  bark winding %.3f  size %.1f x %.1f x %.1f" % (
            name, tris[0], tris[1], tris[2], wind, hi.x - lo.x, hi.y - lo.y, hi.z - lo.z))
    frames = render_impostors([o[0] for o in lod_objs]) if impostors else None
    refresh_material(IMPOSTOR_MAT)
    for i, spec in enumerate(TREES):
        name = spec[0]
        if frames is None:
            o = lod_objs[i][0]
            frames_i = impostor_frames(Vector(o.bound_box[0]), Vector(o.bound_box[6]))
        else:
            frames_i = frames[i]
        imp = impostor_mesh(i, frames_i)
        ob = imp.to_object("%s_LOD3" % name, bpy.data.collections["Tree_" + name])
        lod_objs[i].append(ob)
        report[name]["tris"].append(imp.tris)
        report[name]["impostor_rects"] = [list(map(lambda x: round(x, 5), r)) for r in impostor_layout(i)]
        budget = BUDGET[spec[1]]
        report[name]["over_budget"] = [t > b for t, b in zip(report[name]["tris"], budget)]
        path = os.path.join(spec[5], name + ".fbx")
        report[name]["file"] = os.path.relpath(path, ROOT)
        if export:
            export_fbx(lod_objs[i], path)
            print("[export] %s" % os.path.relpath(path, ROOT))
    manifest = {
        "generator": "ArtSource/Blender/scripts/pp_nature.py",
        "lod_screen_relative_heights": LOD_SCREEN,
        "materials": {k: v[1] for k, v in MAT_SPEC.items()},
        "vertex_color": "R occlusion, G tip tint, B wind sway weight, A leaf flutter weight",
        "budgets_tris": BUDGET,
        "trees": report,
    }
    if export:
        with open(MANIFEST, "w") as f:
            json.dump(manifest, f, indent=1)
        print("[manifest] %s" % os.path.relpath(MANIFEST, ROOT))
    return lod_objs, manifest


# ================================================================ previews
def preview_scene():
    scene = bpy.context.scene
    _cycles(scene, 48, False)
    scene.cycles.use_denoising = True
    scene.view_settings.exposure = 0.5
    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    w = bpy.data.worlds.new("Preview_World")
    w.use_nodes = True
    sky = w.node_tree.nodes.new("ShaderNodeTexSky")
    bg = w.node_tree.nodes.get("Background")
    try:
        sky.sky_type = "NISHITA"
        sky.sun_elevation = math.radians(22)
        sky.sun_rotation = math.radians(130)
        w.node_tree.links.new(sky.outputs[0], bg.inputs[0])
        bg.inputs[1].default_value = 0.35
    except Exception:  # noqa: BLE001
        bg.inputs[0].default_value = (0.42, 0.58, 0.85, 1)
        bg.inputs[1].default_value = 1.1
    scene.world = w
    c = new_coll("Preview")
    sun = bpy.data.objects.new("Preview_Sun", bpy.data.lights.new("Preview_Sun", "SUN"))
    sun.data.energy = 5.5
    sun.data.color = (1.0, 0.88, 0.72)
    sun.data.angle = math.radians(2.5)
    sun.rotation_euler = (math.radians(62), 0, math.radians(130))
    c.objects.link(sun)
    gm = bpy.data.materials.new("Preview_Ground")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.09, 0.14, 0.05, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=300)
    me = bpy.data.meshes.new("Preview_Ground")
    bm.to_mesh(me)
    me.materials.append(gm)
    c.objects.link(bpy.data.objects.new("Preview_Ground", me))
    hm = bpy.data.materials.new("Preview_Human")
    hm.diffuse_color = (0.6, 0.6, 0.62, 1)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 0.45, v.co.y * 0.28, v.co.z * 1.8 + 0.9))
    me = bpy.data.meshes.new("Preview_Human")
    bm.to_mesh(me)
    me.materials.append(hm)
    human = bpy.data.objects.new("Preview_Human", me)
    c.objects.link(human)
    cam = bpy.data.objects.new("Preview_Cam", bpy.data.cameras.new("Preview_Cam"))
    c.objects.link(cam)
    scene.camera = cam
    return scene, cam, human


def frame(cam, objs, az, el, lens, aspect):
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(cn) for o in objs for cn in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (lo + hi) / 2
    cam.data.type = "PERSP"
    cam.data.lens = lens
    fw = 2 * math.atan(cam.data.sensor_width / 2 / lens)
    fh = 2 * math.atan(math.tan(fw / 2) / aspect)
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    fwd = -d
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(fwd).normalized()
    need = 0.0
    for c in [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]:
        v = c - ctr
        need = max(need, abs(v.dot(right)) / math.tan(fw / 2) * 1.08 - v.dot(fwd),
                   abs(v.dot(up)) / math.tan(fh / 2) * 1.08 - v.dot(fwd))
    cam.location = ctr + d * need
    cam.rotation_euler = fwd.to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = 0.1
    cam.data.clip_end = need * 10 + 200


def render_previews(lod_objs):
    scene, cam, human = preview_scene()
    os.makedirs(REVIEW_DIR, exist_ok=True)
    keep = {o for o in bpy.data.collections["Preview"].objects}
    out = []

    def show(objs):
        vis = set(objs) | keep
        for o in bpy.data.objects:
            o.hide_render = o not in vis

    def shot(path, w, h):
        scene.render.resolution_x, scene.render.resolution_y = w, h
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out.append(os.path.relpath(path, ROOT))
        print("[preview] %s" % os.path.relpath(path, ROOT))

    for i, spec in enumerate(TREES):
        objs = lod_objs[i]
        for o in objs:
            o.location = (0, 0, 0)
        hi = Vector(objs[0].bound_box[6])
        human.location = (hi.x + 1.2, 0, 0)
        show([objs[0], human])
        frame(cam, [objs[0], human], 28, 9, 50, 0.8)
        shot(os.path.join(REVIEW_DIR, "%s.png" % spec[0]), 800, 1000)
        # LOD lineup
        x = 0.0
        for o in objs:
            lo_, hi_ = Vector(o.bound_box[0]), Vector(o.bound_box[6])
            x += -lo_.x
            o.location = (x, 0, 0)
            x += hi_.x + 1.5
        human.location = (x + 0.5, 0, 0)
        show(objs + [human])
        frame(cam, objs + [human], 0, 6, 70, 2.4)
        shot(os.path.join(REVIEW_DIR, "%s_lods.png" % spec[0]), 1440, 600)
        for o in objs:
            o.location = (0, 0, 0)
            o.hide_render = True
    # family lineup
    x = 0.0
    lineup = []
    for i, spec in enumerate(TREES):
        o = lod_objs[i][0]
        lo_, hi_ = Vector(o.bound_box[0]), Vector(o.bound_box[6])
        x += -lo_.x
        o.location = (x, 0, 0)
        x += hi_.x + 1.0
        lineup.append(o)
    human.location = (x + 0.5, 0, 0)
    show(lineup + [human])
    frame(cam, lineup + [human], 12, 7, 60, 2.6)
    shot(os.path.join(REVIEW_DIR, "trees_lineup.png"), 1820, 700)
    for o in lineup:
        o.location = (0, 0, 0)
    return out


def main(mode):
    if mode in ("all", "textures"):
        cov = TEX.make_all(TEX_DIR)
        print("[textures] coverage", cov)
    if mode in ("all", "trees", "previews"):
        lod_objs, manifest = build_trees(export=(mode != "previews"), impostors=(mode != "previews"))
        if mode in ("all", "previews"):
            manifest["previews"] = render_previews(lod_objs)
            if mode == "all":
                with open(MANIFEST, "w") as f:
                    json.dump(manifest, f, indent=1)
        if mode != "previews":
            bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
            print("[saved] %s" % os.path.relpath(BLEND, ROOT))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(argv[0] if argv else "all")
