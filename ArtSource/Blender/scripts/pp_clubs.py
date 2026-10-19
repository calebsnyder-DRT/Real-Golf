"""Puttshack Pro club lineup: AI-generated silhouettes and textures -> game-ready club meshes.

Inputs (made with the image generator, see ArtSource/Clubs/README.md):
  ArtSource/Clubs/Silhouettes/sil_*.jpg   orthographic black-on-white head silhouettes, traced into outlines here
  ArtSource/Clubs/Textures/ai/tex_*.jpg   tileable grip / carbon / milled-face textures (normal maps derived here)
Outputs:
  ArtSource/Clubs/Exports/Club_<model>.fbx   one static mesh per club, materials named by role
  ArtSource/Clubs/Exports/clubs_manifest.json   reference points per club in Unity seat space
  ArtSource/Clubs/Textures/Club_*.png        tinted-neutral albedo + normal maps
  ArtSource/Clubs/PuttshackPro_Clubs.blend    the built lineup
  ArtSource/Review/Clubs/blender_*.png        preview renders

Every club uses the hand-attachment frame of pp_golfer_clubs.py (the ClubGrip_<Kind> seat in the lead hand):
  origin = lead-hand grip point (55 mm below the butt), +Y = down the shaft toward the head, +Z = square face normal.
Shaft line, length and lie per seat kind (Driver / Iron / Wedge / Putter) are the same as the clubs baked into the
golfers, so a new club sits exactly where the old one did and the measured ball contact still holds.
Exported vertices are pre-rotated so Unity's FBX import lands them in seat space: Unity (x, y, z) = (-bx, by, bz).

Run: Blender -b -P pp_clubs.py -- [all|textures|build|render]
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.geometry import delaunay_2d_cdt

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SRC = os.path.join(REPO, "ArtSource", "Clubs")
SIL = os.path.join(SRC, "Silhouettes")
TEX = os.path.join(SRC, "Textures")
AI_TEX = os.path.join(TEX, "ai")
OUT = os.path.join(SRC, "Exports")
REVIEW = os.path.join(REPO, "ArtSource", "Review", "Clubs")
BLEND = os.path.join(SRC, "PuttshackPro_Clubs.blend")

ROLES = ["Head", "Face", "Accent", "Crown", "Shaft", "Grip", "Trim", "Decal"]
RI = {k: i for i, k in enumerate(ROLES)}

# Seat kinds: identical to pp_golfer_clubs.CLUBS / build() so the grip point and shaft line do not move.
KINDS = {
    "Driver": dict(length=1.156, lie=58.0, base=(-0.012, 0.008, 0.045), hosel=0.055),
    "Iron": dict(length=0.940, lie=62.5, base=(-0.008, 0.026, 0.02), hosel=0.055),
    "Wedge": dict(length=0.895, lie=64.0, base=(-0.008, 0.026, 0.02), hosel=0.055),
    "Putter": dict(length=0.864, lie=70.0, base=(-0.012, 0.022, 0.022), hosel=0.07),
}
GRIP_FROM_BUTT = 0.055
GRIP_LEN = 0.27
TRAIL_GRIP_GAP = 0.085  # GolferCharacterBaker.TrailGripGap

IRON_FACE = dict(sil="sil_iron_cavity_face", erase=(0.70, 0.0, 1.0, 0.47))
BLADE_FACE = dict(sil="sil_iron_blade_face", erase=(0.72, 0.0, 1.0, 0.47))
WEDGE_FACE = dict(sil="sil_wedge_face", erase=(0.68, 0.0, 1.0, 0.49))


def _iron(mid, num, loft, height, style, base, **kw):
    d = dict(id=mid, kind="Iron", head="iron", style=style, loft=loft, height=height, length=0.098, heel_y=0.032,
             number=num, shaft="steel", sole_t=0.024, top_t=0.009, plate=0.0045)
    d.update(base)
    d.update(kw)
    return d


MODELS = [
    dict(id="driver", kind="Driver", head="wood", sil="sil_driver_top", erase=(0.80, 0.64, 1.0, 1.0), width=0.124,
         depth=0.116, height=0.062, heel_y=0.012, protrude=0.005, loft=10.5, shaft="graphite", label="DRIVER"),
    dict(id="wood3", kind="Driver", head="wood", sil="sil_wood_top", erase=(0.70, 0.715, 1.0, 1.0), width=0.104,
         depth=0.086, height=0.041, heel_y=0.003, protrude=0.004, loft=15.0, shaft="graphite", label="3 WOOD"),
    dict(id="hybrid", kind="Iron", head="wood", sil="sil_hybrid_top", erase=(0.74, 0.665, 1.0, 1.0), width=0.098,
         depth=0.060, height=0.037, heel_y=0.031, protrude=0.004, loft=22.0, shaft="graphite", label="HYBRID"),
    _iron("iron5_cavity", "5", 26.0, 0.047, "cavity", IRON_FACE, label="5 IRON"),
    _iron("iron7_cavity", "7", 34.0, 0.049, "cavity", IRON_FACE, label="7 IRON"),
    _iron("iron9_cavity", "9", 42.0, 0.051, "cavity", IRON_FACE, label="9 IRON"),
    _iron("iron5_blade", "5", 26.0, 0.045, "blade", BLADE_FACE, length=0.094, sole_t=0.019, label="5 BLADE"),
    _iron("iron7_blade", "7", 34.0, 0.047, "blade", BLADE_FACE, length=0.094, sole_t=0.019, label="7 BLADE"),
    _iron("iron9_blade", "9", 42.0, 0.049, "blade", BLADE_FACE, length=0.094, sole_t=0.019, label="9 BLADE"),
    _iron("wedge_pw", "46", 46.0, 0.053, "wedge", WEDGE_FACE, kind="Wedge", length=0.096, sole_t=0.020, plate=0.0055,
          label="PW"),
    _iron("wedge_sw", "56", 56.0, 0.057, "wedge", WEDGE_FACE, kind="Wedge", length=0.096, sole_t=0.026, plate=0.0055,
          label="SW"),
    _iron("wedge_lw", "60", 60.0, 0.059, "wedge", WEDGE_FACE, kind="Wedge", length=0.096, sole_t=0.023, plate=0.0055,
          label="LW"),
    dict(id="putter_blade", kind="Putter", head="putter", style="blade", sil="sil_putter_blade_top", width=0.100,
         depth=0.030, height=0.023, center_y=-0.0165, shaft="steel", label="BLADE PUTTER"),
    dict(id="putter_mallet", kind="Putter", head="putter", style="mallet", sil="sil_putter_mallet_top", width=0.098,
         depth=0.078, height=0.025, center_y=-0.0165, shaft="steel", label="MALLET PUTTER"),
    dict(id="putter_fang", kind="Putter", head="putter", style="fang", sil="sil_putter_fang_top", width=0.100,
         depth=0.076, height=0.022, center_y=-0.0165, shaft="steel", label="FANG PUTTER", smooth=1),
]

# Preview palette (Tour Chrome); Unity owns the real palettes (ClubPalette assets).
PREVIEW = {
    "Head": ((0.86, 0.87, 0.89), 1.0, 0.14), "Face": ((0.70, 0.71, 0.73), 1.0, 0.35),
    "Accent": ((0.0, 0.62, 0.78), 0.0, 0.35), "Crown": ((0.03, 0.035, 0.04), 0.0, 0.15),
    "Shaft": ((0.85, 0.86, 0.88), 1.0, 0.18), "Grip": ((0.05, 0.05, 0.055), 0.0, 0.7),
    "Trim": ((0.02, 0.02, 0.022), 0.0, 0.4), "Decal": ((0.95, 0.95, 0.95), 0.0, 0.4),
}


# ---------------------------------------------------------------- math helpers

def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def area(p):
    return 0.5 * float(np.sum(p[:, 0] * np.roll(p[:, 1], -1) - np.roll(p[:, 0], -1) * p[:, 1]))


def ccw(p):
    return p if area(p) > 0 else p[::-1].copy()


def inside(pts, poly):
    x, y = pts[:, 0][:, None], pts[:, 1][:, None]
    x1, y1 = poly[:, 0][None], poly[:, 1][None]
    x2, y2 = np.roll(poly[:, 0], -1)[None], np.roll(poly[:, 1], -1)[None]
    cond = (y1 > y) != (y2 > y)
    xint = x1 + (y - y1) * (x2 - x1) / np.where(np.abs(y2 - y1) < 1e-12, 1e-12, y2 - y1)
    return (cond & (x < xint)).sum(1) % 2 == 1


def dist(pts, poly):
    a = poly[None]
    b = np.roll(poly, -1, 0)[None]
    p = pts[:, None]
    ab = b - a
    t = np.clip(((p - a) * ab).sum(-1) / np.maximum((ab * ab).sum(-1), 1e-18), 0, 1)
    q = a + ab * t[..., None]
    return np.sqrt(((p - q) ** 2).sum(-1)).min(1)


def resample(pts, n):
    closed = np.vstack([pts, pts[:1]])
    seg = np.linalg.norm(np.diff(closed, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.stack([np.interp(t, s, closed[:, 0]), np.interp(t, s, closed[:, 1])], 1)


def smooth(pts, passes):
    for _ in range(passes):
        pts = (np.roll(pts, 1, 0) + 2 * pts + np.roll(pts, -1, 0)) / 4
    return pts


def offset(poly, d):
    """Moves a CCW polygon's points inward by d along the averaged edge normals."""
    e = np.roll(poly, -1, 0) - poly
    n = np.stack([e[:, 1], -e[:, 0]], 1)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    vn = n + np.roll(n, 1, 0)
    vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
    return poly - vn * d


# ---------------------------------------------------------------- silhouette tracing

def load_mask(path, size=512):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * img.channels, dtype=np.float32)
    img.pixels.foreach_get(px)
    ch = img.channels
    bpy.data.images.remove(img)
    lum = px.reshape(h, w, ch)[..., :3].mean(axis=2)[::-1]
    f = w // size
    lum = lum[:size * f, :size * f].reshape(size, f, size, f).mean(axis=(1, 3))
    return lum < 0.5


def _grow(seed, allowed):
    reach = seed & allowed
    while True:
        g = reach.copy()
        g[1:] |= reach[:-1]
        g[:-1] |= reach[1:]
        g[:, 1:] |= reach[:, :-1]
        g[:, :-1] |= reach[:, 1:]
        g &= allowed
        if (g == reach).all():
            return reach
        reach = g


def largest(mask):
    left = mask.copy()
    best, best_n = None, 0
    while left.any():
        r, c = np.argwhere(left)[0]
        seed = np.zeros_like(mask)
        seed[r, c] = True
        comp = _grow(seed, mask)
        left &= ~comp
        if comp.sum() > best_n:
            best, best_n = comp, comp.sum()
    return best


def hole_mask(mask):
    white = ~mask
    border = np.zeros_like(white)
    border[0, :] = border[-1, :] = border[:, 0] = border[:, -1] = True
    holes = white & ~_grow(border, white)
    if not holes.any():
        return None
    return largest(holes)


NB = [(-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1)]


def trace(mask):
    """Moore-neighbour boundary trace of the (single) foreground region; returns (x, y) pixel coords, y down."""
    m = np.pad(mask, 1)
    r, c = np.argwhere(m)[0]
    start = (int(r), int(c))
    p, b = start, (start[0], start[1] - 1)
    out = [start]
    for _ in range(400000):
        k = NB.index((b[0] - p[0], b[1] - p[1]))
        nxt = None
        for j in range(1, 9):
            kk = (k + j) % 8
            q = (p[0] + NB[kk][0], p[1] + NB[kk][1])
            if m[q]:
                pk = (kk - 1) % 8
                b = (p[0] + NB[pk][0], p[1] + NB[pk][1])
                nxt = q
                break
        if nxt is None or (nxt == start and len(out) > 2):
            break
        p = nxt
        out.append(p)
    a = np.array(out, float) - 1.0
    return a[:, ::-1]


def contour(mask, n, passes=3):
    c = trace(mask)
    c = resample(c, max(600, len(c) // 2))
    c = smooth(c, passes * 3)
    return resample(c, n) / mask.shape[0]


_SIL_CACHE = {}


def silhouette(name, erase=None, n=180, passes=3):
    """Outer outline and cavity window (if the AI drew one) of a silhouette, normalised image coords (y down)."""
    key = (name, erase, n, passes)
    if key in _SIL_CACHE:
        return _SIL_CACHE[key]
    mask = load_mask(os.path.join(SIL, name + ".jpg"))
    s = mask.shape[0]
    hole = hole_mask(mask)
    if erase:
        x0, y0, x1, y1 = erase
        mask[int(y0 * s):int(y1 * s), int(x0 * s):int(x1 * s)] = False
    mask = largest(mask)
    outline = contour(mask, n, passes)
    window = None
    if hole is not None and hole.sum() > 0.01 * mask.sum():
        window = contour(hole, n // 2, passes)
    _SIL_CACHE[key] = (outline, window)
    return outline, window


# ---------------------------------------------------------------- mesh helpers

def slab(outline, holes=(), loops=(), spacing=0.003):
    """Constrained Delaunay fill of a CCW polygon with an interior lattice (for height/thickness fields)."""
    loops_all = [outline] + list(holes) + list(loops)
    lo, hi = outline.min(0), outline.max(0)
    gx, gy = np.meshgrid(np.arange(lo[0] + spacing * 0.5, hi[0], spacing),
                         np.arange(lo[1] + spacing * 0.5, hi[1], spacing))
    g = np.stack([gx.ravel(), gy.ravel()], 1)
    keep = inside(g, outline)
    for h in holes:
        keep &= ~inside(g, h)
    g = g[keep]
    if len(g):
        dmin = np.min(np.stack([dist(g, l) for l in loops_all]), 0)
        g = g[dmin > spacing * 0.55]
    verts, edges, starts = [], [], []
    for l in loops_all:
        s0 = len(verts)
        starts.append((s0, len(l)))
        verts += [Vector((float(x), float(y))) for x, y in l]
        edges += [(s0 + i, s0 + (i + 1) % len(l)) for i in range(len(l))]
    verts += [Vector((float(x), float(y))) for x, y in g]
    res = delaunay_2d_cdt(verts, edges, [list(range(len(outline)))], 1, 1e-9, True)
    V = np.array([[v.x, v.y] for v in res[0]])
    remap = {}
    for oi, ids in enumerate(res[3]):
        for i in ids:
            remap[i] = oi
    tris = []
    for f in res[2]:
        if len(f) != 3:
            continue
        a, b, c = f
        cen = (V[a] + V[b] + V[c]) / 3
        if not inside(cen[None], outline)[0] or any(inside(cen[None], h)[0] for h in holes):
            continue
        cross = (V[b] - V[a])[0] * (V[c] - V[a])[1] - (V[b] - V[a])[1] * (V[c] - V[a])[0]
        if abs(cross) < 1e-14:
            continue
        tris.append((a, b, c) if cross > 0 else (a, c, b))
    walls = [[remap[s0 + i] for i in range(n)] for s0, n in starts[:1 + len(holes)]]
    return V, tris, walls


def solid(bm, V, tris, walls, front, back, mat_front, mat_back, mat_side=RI["Head"]):
    """Closed solid from a 2D fill: front/back map a 2D point to 3D; returns the created faces."""
    F = [bm.verts.new(front(p)) for p in V]
    B = [bm.verts.new(back(p)) for p in V]
    faces = []
    for a, b, c in tris:
        cen = (V[a] + V[b] + V[c]) / 3
        f = bm.faces.new((F[a], F[b], F[c]))
        f.material_index = mat_front(cen)
        g = bm.faces.new((B[c], B[b], B[a]))
        g.material_index = mat_back(cen)
        faces += [f, g]
    for loop in walls:
        n = len(loop)
        for i in range(n):
            a, b = loop[i], loop[(i + 1) % n]
            if a == b:
                continue
            try:
                f = bm.faces.new((F[b], F[a], B[a], B[b]))
            except ValueError:
                continue
            f.material_index = mat_side
            faces.append(f)
    return faces


def ring(center, axis, radius, seg, ref=None):
    axis = axis.normalized()
    ref = ref or (Vector((1, 0, 0)) if abs(axis.x) < 0.9 else Vector((0, 1, 0)))
    a = axis.cross(ref).normalized()
    b = axis.cross(a).normalized()
    return [center + (a * math.cos(2 * math.pi * i / seg) + b * math.sin(2 * math.pi * i / seg)) * radius
            for i in range(seg)]


def tube(bm, uvl, pts, radii, seg, mats, caps=(True, True), v_scale=None, u_repeat=1.0, cap_mat=None):
    """Swept tube with cylindrical UVs; mats is one role index or one per segment."""
    rings, vs = [], [0.0]
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        rings.append([bm.verts.new(c) for c in ring(p, d, radii[i], seg, ref=Vector((1, 0, 0)))])
        if i:
            vs.append(vs[-1] + (pts[i] - pts[i - 1]).length / (v_scale or (2 * math.pi * max(radii[i], 1e-4))))
    faces = []
    for k, (r0, r1) in enumerate(zip(rings, rings[1:])):
        mi = mats[k] if isinstance(mats, (list, tuple)) else mats
        for i in range(seg):
            f = bm.faces.new((r0[i], r0[(i + 1) % seg], r1[(i + 1) % seg], r1[i]))
            f.material_index = mi
            f.smooth = True
            u0, u1 = i / seg * u_repeat, (i + 1) / seg * u_repeat
            for loop, uv in zip(f.loops, ((u0, vs[k]), (u1, vs[k]), (u1, vs[k + 1]), (u0, vs[k + 1]))):
                loop[uvl].uv = uv
            faces.append(f)
    cm = cap_mat if cap_mat is not None else (mats[0] if isinstance(mats, (list, tuple)) else mats)
    for end, on in ((0, caps[0]), (-1, caps[1])):
        if not on:
            continue
        r = rings[end]
        f = bm.faces.new(list(reversed(r)) if end == 0 else r)
        f.material_index = cm if end == 0 else (cap_mat if cap_mat is not None else cm)
        for loop in f.loops:
            loop[uvl].uv = (0.5, 0.5)
        faces.append(f)
    return faces


def box_uv(faces, uvl, scale=0.1):
    for f in faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        u, v = [(1, 2), (0, 2), (0, 1)][ax]
        for loop in f.loops:
            co = loop.vert.co
            loop[uvl].uv = (co[u] / scale, co[v] / scale)


def planar_uv(faces, uvl, fn):
    for f in faces:
        for loop in f.loops:
            loop[uvl].uv = fn(loop.vert.co)


def sharpen(faces, angle=38.0):
    lim = math.radians(angle)
    edges = set()
    for f in faces:
        f.smooth = True
        edges.update(f.edges)
    for e in edges:
        if len(e.link_faces) == 2:
            e.smooth = e.calc_face_angle(0.0) < lim and e.link_faces[0].material_index == e.link_faces[1].material_index
        else:
            e.smooth = False


def text_mesh(body, size):
    cu = bpy.data.curves.new("txt", "FONT")
    cu.body = body
    cu.size = size
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.extrude = 0.00025
    cu.resolution_u = 3
    ob = bpy.data.objects.new("txt", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    return me


def add_mesh(bm, me, M, mat):
    verts = {}
    for v in me.vertices:
        verts[v.index] = bm.verts.new(M @ v.co)
    out = []
    for p in me.polygons:
        try:
            f = bm.faces.new([verts[i] for i in p.vertices])
        except ValueError:
            continue
        f.material_index = mat
        out.append(f)
    bpy.data.meshes.remove(me)
    return out


# ---------------------------------------------------------------- heads

def face_outline(spec):
    """Face-on silhouette -> (Y, Z) outline: image x -> +Y (heel right), image up -> +Z, sole on z = 0."""
    out, win = silhouette(spec["sil"], spec.get("erase"), n=spec.get("n", 180))
    lo, hi = out.min(0), out.max(0)
    sy = spec["length"] / (hi[0] - lo[0])
    sz = spec["height"] / (hi[1] - lo[1])

    def m(p):
        return np.stack([spec["heel_y"] - (hi[0] - p[:, 0]) * sy, (hi[1] - p[:, 1]) * sz], 1)

    return ccw(m(out)), (ccw(m(win)) if win is not None else None)


def wedge_window(outline, iron_spec):
    """Cavity for wedges: the AI iron's cavity window, fitted into the wedge's own outline."""
    iron_out, iron_win = face_outline(dict(iron_spec, length=0.098, height=0.05, heel_y=0.032))
    ilo, ihi = iron_out.min(0), iron_out.max(0)
    lo, hi = outline.min(0), outline.max(0)
    w = lo + (iron_win - ilo) / (ihi - ilo) * (hi - lo)
    c = w.mean(0)
    for _ in range(12):
        if dist(w, outline).min() > 0.0045 and inside(w, outline).all():
            break
        w = c + (w - c) * 0.94
    return ccw(w)


def iron_head(bm, uvl, spec):
    outline, win = face_outline(spec)
    style = spec["style"]
    if style == "wedge":
        win = wedge_window(outline, IRON_FACE)
    if style == "blade":
        win = None
    zmax = outline[:, 1].max()
    heel = spec["heel_y"]
    ramp = 0.0018
    inner = offset(win, ramp) if win is not None else None
    loops = [win, inner] if win is not None else []
    V, tris, walls = slab(outline, loops=loops, spacing=0.003)

    neck_w = smoothstep(heel - 0.024, heel - 0.010, V[:, 0])
    zn = np.clip(V[:, 1] / zmax, 0, 1)
    if style == "blade":
        yn = (V[:, 0] - outline[:, 0].min()) / (heel - outline[:, 0].min())
        t = 0.0062 + (spec["sole_t"] - 0.0062) * (1 - zn) ** 2.2
        t += 0.0055 * np.exp(-((zn - 0.32) / 0.24) ** 2) * smoothstep(0.02, 0.2, yn) * (1 - smoothstep(0.62, 0.82, yn))
    else:
        t = spec["top_t"] + (spec["sole_t"] - spec["top_t"]) * (1 - zn) ** 1.6
        if win is not None:
            ins = inside(V, win)
            d = dist(V, win)
            w = np.where(ins, smoothstep(0.0, ramp * 1.02, d), 0.0)
            t = t * (1 - w) + spec["plate"] * w
    t = np.maximum(t, 0.0115 * neck_w)
    T = {i: float(t[i]) for i in range(len(V))}
    index = {(round(V[i, 0], 9), round(V[i, 1], 9)): i for i in range(len(V))}

    def thick(p):
        return T[index[(round(p[0], 9), round(p[1], 9))]]

    in_win = (lambda c: RI["Accent"] if inside(c[None], inner)[0] else RI["Head"]) if win is not None else (lambda c: RI["Head"])
    faces = solid(bm, V, tris, walls, lambda p: Vector((0.0, p[0], p[1])), lambda p: Vector((-thick(p), p[0], p[1])),
                  lambda c: RI["Face"], in_win)
    planar_uv([f for f in faces if f.material_index == RI["Face"]], uvl, lambda co: ((heel - co.y) / 0.1, co.z / 0.1))
    box_uv([f for f in faces if f.material_index != RI["Face"]], uvl)
    if win is not None and spec.get("number"):
        c = inner.mean(0)
        size = 0.0125 if len(spec["number"]) == 1 else 0.0105
        me = text_mesh(spec["number"], size)
        # text +X -> -Y (reads from behind), text +Y -> +Z, text +Z -> -X
        M = Matrix(((0, 0, -1, -spec["plate"] - 0.00005), (-1, 0, 0, float(c[0]) - 0.003), (0, 1, 0, float(c[1])),
                    (0, 0, 0, 1)))
        tf = add_mesh(bm, me, M, RI["Decal"])
        box_uv(tf, uvl)
        faces += tf
    # Loft: lay the face back about the leading edge; the neck stays on the hosel.
    loft = math.radians(spec["loft"])
    R = Matrix.Rotation(-loft, 4, "Y")
    verts = {v for f in faces for v in f.verts}
    for v in verts:
        x0, y = v.co.x, v.co.y
        v.co = R @ v.co
        w = float(smoothstep(heel - 0.020, heel - 0.006, y))
        v.co.x += w * 0.85 * (x0 - v.co.x)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    sharpen(faces)
    lead_y = float((outline[:, 0].min() + heel - 0.012) / 2)
    face_c = R @ Vector((0.0, lead_y, min(0.022, zmax * 0.45)))
    return faces, dict(face=face_c, sole=Vector((0.0, lead_y, 0.0)), face_n=R @ Vector((1, 0, 0)))


def plan_outline(spec, X0):
    """Top-view silhouette -> (X, Y): image x -> +Y (heel right), image up -> -X (back), face edge at X = X0."""
    out, _ = silhouette(spec["sil"], spec.get("erase"), n=spec.get("n", 160), passes=spec.get("smooth", 3))
    lo, hi = out.min(0), out.max(0)
    sy = spec["width"] / (hi[0] - lo[0])
    sx = spec["depth"] / (hi[1] - lo[1])
    if "center_y" in spec:
        y = spec["center_y"] + (out[:, 0] - (lo[0] + hi[0]) / 2) * sy
    else:
        y = spec["heel_y"] - (hi[0] - out[:, 0]) * sy
    return ccw(np.stack([X0 + (out[:, 1] - hi[1]) * sx, y], 1))


def wood_head(bm, uvl, spec):
    plan = plan_outline(dict(spec, n=110), spec["protrude"])
    n = len(plan)
    c = np.array([plan[:, 0].min() * 0.5 + plan[:, 0].max() * 0.5, plan[:, 1].mean()])
    depth = plan[:, 0].min()
    H = spec["height"]
    z_eq = H * 0.4
    loft = math.radians(spec["loft"])
    z_lead = H * 0.06
    J = 26
    rows = []
    for j in range(1, J):
        phi = -math.pi / 2 + math.pi * j / J
        row = []
        for i in range(n):
            p = plan[i]
            back = float(np.clip(p[0] / depth, 0, 1))
            # Boxy toward the face (tall, near-vertical face wall), round toward the back.
            if phi < 0:
                r = math.cos(phi) ** (0.14 + 0.24 * back)
                z = z_eq * (1 + math.sin(phi))
            else:
                r = math.cos(phi) ** (0.2 + 0.55 * back ** 0.7)
                top = (H - z_eq) * (1.0 - 0.42 * back ** 1.4)
                z = z_eq + top * math.sin(phi)
            xy = c + (p - c) * r
            row.append([xy[0], xy[1], z])
        rows.append(row)
    rows = np.array(rows)
    bottom = np.append(rows[0, :, :2].mean(0), rows[0, :, 2].min() - 0.0002)
    top = np.append(rows[-1, :, :2].mean(0), rows[-1, :, 2].mean() + 0.0008)
    # Smooth dome first; the flat lofted face is an exact boolean cut, so its edge is a clean curve.
    hb = bmesh.new()
    huv = hb.loops.layers.uv.verify()
    vr = [[hb.verts.new(Vector(rows[j, i])) for i in range(n)] for j in range(len(rows))]
    vb, vt = hb.verts.new(Vector(bottom)), hb.verts.new(Vector(top))
    band = (J // 2 - 1, J // 2)
    for j in range(len(rows) - 1):
        for i in range(n):
            i2 = (i + 1) % n
            f = hb.faces.new((vr[j][i], vr[j][i2], vr[j + 1][i2], vr[j + 1][i]))
            f.material_index = RI["Accent"] if j in band else RI["Crown"] if j >= band[1] else RI["Head"]
            f.smooth = True
    for i in range(n):
        i2 = (i + 1) % n
        hb.faces.new((vr[0][i2], vr[0][i], vb)).material_index = RI["Head"]
        hb.faces.new((vr[-1][i], vr[-1][i2], vt)).material_index = RI["Crown"]
    bmesh.ops.recalc_face_normals(hb, faces=hb.faces)
    for f in hb.faces:
        f.smooth = True
        if f.material_index == RI["Crown"] and f.normal.z < 0.25:
            f.material_index = RI["Head"]
    planar_uv([f for f in hb.faces if f.material_index == RI["Crown"]], huv, lambda co: (co.y / 0.06, co.x / 0.06))
    box_uv([f for f in hb.faces if f.material_index != RI["Crown"]], huv)
    me = bpy.data.meshes.new("dome")
    hb.to_mesh(me)
    hb.free()
    for r in ROLES:
        me.materials.append(preview_material(r))
    dome = bpy.data.objects.new("dome", me)
    cut_me = bpy.data.meshes.new("cut")
    cb = bmesh.new()
    bmesh.ops.create_cube(cb, size=1.0)
    for f in cb.faces:
        f.material_index = RI["Face"]
    cb.to_mesh(cut_me)
    cb.free()
    for r in ROLES:
        cut_me.materials.append(preview_material(r))
    cutter = bpy.data.objects.new("cutter", cut_me)
    # Cube's back face on the face plane x = -(z - z_lead) tan(loft).
    cutter.matrix_world = (Matrix.Translation((0.0, float(c[1]), z_lead)) @ Matrix.Rotation(-loft, 4, "Y")
                           @ Matrix.Translation((0.5, 0.0, 0.0)))
    for o in (dome, cutter):
        bpy.context.scene.collection.objects.link(o)
    mod = dome.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    mod.material_mode = "INDEX"
    bev = dome.modifiers.new("bevel", "BEVEL")
    bev.limit_method = "ANGLE"
    bev.angle_limit = math.radians(35)
    bev.width = 0.0009
    bev.segments = 2
    dg = bpy.context.evaluated_depsgraph_get()
    res = bpy.data.meshes.new_from_object(dome.evaluated_get(dg))
    bpy.data.objects.remove(dome)
    bpy.data.objects.remove(cutter)
    bpy.data.meshes.remove(me)
    bpy.data.meshes.remove(cut_me)
    before = set(bm.faces)
    bm.from_mesh(res)
    bpy.data.meshes.remove(res)
    uvl = bm.loops.layers.uv.verify()
    faces = [f for f in bm.faces if f not in before]
    fn = Matrix.Rotation(-loft, 4, "Y") @ Vector((1, 0, 0))
    for f in faces:
        if f.normal.dot(fn) > 0.995 and f.calc_center_median().x > -(f.calc_center_median().z - z_lead) * math.tan(loft) - 0.0015:
            f.material_index = RI["Face"]
    fverts = {v for f in faces if f.material_index == RI["Face"] for v in f.verts}
    fy = float(np.mean([v.co.y for v in fverts])) if fverts else float(c[1])
    fz = float(np.mean([v.co.z for v in fverts])) if fverts else z_eq
    # Sole weight port.
    port = Vector((float(depth * 0.72), float(c[1] + (plan[:, 1].max() - c[1]) * 0.35), 0.0))
    pf = []
    pts = [port + Vector((0, 0, -0.0004)), port + Vector((0, 0, 0.0035))]
    pf += tube(bm, uvl, pts, [H * 0.12, H * 0.12], 16, RI["Trim"], cap_mat=RI["Accent"])
    planar_uv([f for f in faces if f.material_index == RI["Face"]], uvl, lambda co: ((co.y - fy) / 0.12 + 0.5, co.z / 0.07))
    sharpen(faces, 50.0)
    faces += pf
    fx = float(-(fz - z_lead) * math.tan(loft))
    return faces, dict(face=Vector((fx, float(fy), float(fz))), sole=Vector((fx - 0.01, float(fy), 0.0)),
                       face_n=Matrix.Rotation(-loft, 4, "Y") @ Vector((1, 0, 0)))


def putter_head(bm, uvl, spec):
    plan = plan_outline(spec, 0.0)
    style = spec["style"]
    H = spec["height"]
    D = -plan[:, 0].min()
    yc = spec["center_y"]
    half = (plan[:, 1].max() - plan[:, 1].min()) / 2
    if style == "fang":
        line = np.array([[-0.003, yc - 0.0016], [-0.003, yc + 0.0016], [-0.017, yc + 0.0016], [-0.017, yc - 0.0016]])
    elif style == "mallet":
        line = np.array([[-0.006, yc - 0.0018], [-0.006, yc + 0.0018], [-D + 0.012, yc + 0.0018], [-D + 0.012, yc - 0.0018]])
    else:
        line = np.array([[-0.0035, yc - 0.0012], [-0.0035, yc + 0.0012], [-0.009, yc + 0.0012], [-0.009, yc - 0.0012]])
    line = ccw(line)
    V, tris, walls = slab(plan, loops=[line], spacing=0.0022)
    X, Y = V[:, 0], V[:, 1]
    if style == "blade":
        cav = smoothstep(-0.009, -0.0125, X) * (1 - smoothstep(-D + 0.0065, -D + 0.0035, X))
        cav *= 1 - smoothstep(half - 0.016, half - 0.012, np.abs(Y - yc))
        h = H - (H - 0.0115) * cav
        h = np.where(X < -D + 0.0035, H * 0.68, h)
    elif style == "mallet":
        h = H - 0.006 * (np.clip(-X / D, 0, 1) ** 2)
    else:
        h = H - 0.004 * smoothstep(0.02, D, -X)
    Hm = {(round(V[i, 0], 9), round(V[i, 1], 9)): float(h[i]) for i in range(len(V))}

    def top(p):
        return Vector((p[0], p[1], Hm[(round(p[0], 9), round(p[1], 9))]))

    def sole(p):
        return Vector((p[0], p[1], 0.0006 * ((p[1] - yc) / half) ** 2))

    faces = solid(bm, V, tris, walls, top, sole,
                  lambda cc: RI["Decal"] if inside(cc[None], line)[0] else RI["Head"], lambda cc: RI["Head"])
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    for f in faces:
        if f.normal.x > 0.92 and f.calc_center_median().z > 0.0012:
            f.material_index = RI["Face"]
    planar_uv([f for f in faces if f.material_index == RI["Face"]], uvl, lambda co: (co.y / 0.05, co.z / 0.05))
    box_uv([f for f in faces if f.material_index != RI["Face"]], uvl)
    sharpen(faces, 40.0)
    return faces, dict(face=Vector((0.0, yc, H * 0.5)), sole=Vector((-0.004, yc, 0.0)), face_n=Vector((1, 0, 0)))


# ---------------------------------------------------------------- club assembly

def chaikin(pts, passes=2):
    for _ in range(passes):
        out = [pts[0]]
        for a, b in zip(pts, pts[1:]):
            out += [a.lerp(b, 0.25), a.lerp(b, 0.75)]
        out.append(pts[-1])
        pts = out
    return pts


def build_club(spec):
    kind = KINDS[spec["kind"]]
    lie = math.radians(kind["lie"])
    u = Vector((0.0, math.cos(lie), math.sin(lie)))
    base = Vector(kind["base"])
    hosel_top = base + u * kind["hosel"]
    butt = base + u * (kind["length"] - 0.02)
    grip = butt - u * GRIP_FROM_BUTT
    grip_start = butt - u * GRIP_LEN

    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    head = spec["head"]
    if head == "iron":
        hf, ref = iron_head(bm, uvl, spec)
    elif head == "wood":
        hf, ref = wood_head(bm, uvl, spec)
    else:
        hf, ref = putter_head(bm, uvl, spec)

    # Hosel / neck.
    if head == "putter":
        st = spec["style"]
        if st == "blade":
            z = 0.040
            t = (z - base.z) / u.z
            pts = [Vector((-0.011, 0.004, 0.012)), Vector((-0.011, 0.004, 0.036)), Vector((-0.011, 0.010, z)),
                   base + u * t, hosel_top]
            pts = chaikin(pts, 2)
            tube(bm, uvl, pts, [0.0052] * len(pts), 14, RI["Head"])
        else:
            start = Vector((-0.018 if st == "mallet" else -0.010, 0.012, 0.014))
            mid = base + u * 0.018
            pts = chaikin([start, start + Vector((0, 0, 0.012)), mid, hosel_top], 2)
            tube(bm, uvl, pts, [0.0055] * len(pts), 14, RI["Head"])
    else:
        dip = {"Driver": 0.032 if spec["id"] == "wood3" else 0.016, "Iron": 0.010, "Wedge": 0.010}[spec["kind"]]
        r0 = 0.0072 if head == "wood" else 0.0068
        pts = [base - u * dip, base + u * (kind["hosel"] * 0.55), hosel_top]
        tube(bm, uvl, pts, [r0, r0 * 0.92, 0.0058], 16, RI["Head"], caps=(True, False))
    # Ferrule, shaft, grip.
    tube(bm, uvl, [hosel_top - u * 0.002, hosel_top + u * 0.016], [0.0064, 0.0052], 16, RI["Trim"], caps=(False, False))
    s0 = hosel_top + u * 0.016
    if spec["shaft"] == "steel":
        steps = 9
        pts, radii = [], []
        for k in range(steps + 1):
            t0 = k / steps
            r = 0.0046 + (0.0072 - 0.0046) * t0
            p = s0.lerp(grip_start, (t0 ** 1.25) * 0.7)
            pts += [p, p + u * 0.0012]
            radii += [r - 0.00025, r]
        pts.append(grip_start)
        radii.append(0.0072)
        tube(bm, uvl, pts, radii, 16, RI["Shaft"], caps=(False, False), v_scale=0.05)
    else:
        band0 = s0.lerp(grip_start, 0.34)
        band1 = s0.lerp(grip_start, 0.42)
        pts = [s0, band0, band0, band1, band1, grip_start]
        radii = [0.0046, 0.0058, 0.0059, 0.0062, 0.0061, 0.0074]
        mats = [RI["Shaft"], RI["Shaft"], RI["Accent"], RI["Shaft"], RI["Shaft"]]
        tube(bm, uvl, pts, radii, 16, mats, caps=(False, False), v_scale=0.05)
    gp = [grip_start + (butt - grip_start) * t for t in (0.0, 0.035, 0.07, 0.075, 0.105, 0.11, 0.45, 0.8, 0.95, 0.99, 1.0)]
    fat = 1.18 if spec["kind"] == "Putter" else 1.0
    gr = [0.0076, 0.0094, 0.0101, 0.0104, 0.0104, 0.0101, 0.0117, 0.0124, 0.0128, 0.0126, 0.0104]
    gr = [r * (fat if i > 4 else 1.0) for i, r in enumerate(gr)]
    gm = [RI["Grip"], RI["Grip"], RI["Accent"], RI["Accent"], RI["Accent"], RI["Grip"], RI["Grip"], RI["Grip"], RI["Grip"], RI["Trim"]]
    tube(bm, uvl, gp, gr, 20, gm, caps=(True, True), v_scale=0.03, u_repeat=3.0, cap_mat=RI["Trim"])

    # Address frame -> ClubGrip bone frame (pp_golfer_clubs.build) -> export frame (Unity seat after FBX import).
    yb = (-u).normalized()
    zb = Vector((1.0, 0.0, 0.0))
    xb = yb.cross(zb).normalized()
    B = Matrix((xb, yb, zb)).transposed().to_4x4()
    B.translation = grip
    Binv = B.inverted()
    E = Matrix.Rotation(math.radians(90), 4, "X") @ Binv
    bmesh.ops.transform(bm, matrix=E, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    me = bpy.data.meshes.new("Club_" + spec["id"])
    bm.to_mesh(me)
    bm.free()
    for r in ROLES:
        me.materials.append(preview_material(r))
    ob = bpy.data.objects.new("Club_" + spec["id"], me)

    def seat(p):
        b = Binv @ p
        return [-b.x, b.y, b.z]

    def seat_dir(d):
        b = Binv.to_3x3() @ d
        return [-b.x, b.y, b.z]

    info = dict(id=spec["id"], kind=spec["kind"], label=spec.get("label", spec["id"]), tris=tris,
                length_m=kind["length"], lie_deg=kind["lie"], loft_deg=spec.get("loft", 3.0), shaft=spec["shaft"],
                grip=[0, 0, 0], grip_trail=[0, TRAIL_GRIP_GAP, 0], butt=[0, -GRIP_FROM_BUTT, 0], hosel=seat(hosel_top),
                face=seat(ref["face"]), face_normal=seat_dir(ref["face_n"]), sole=seat(ref["sole"]))
    return ob, info


_MATS = {}


def preview_material(role):
    if role in _MATS:
        return _MATS[role]
    m = bpy.data.materials.new(role)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    col, metal, rough = PREVIEW[role]
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    tex = {"Grip": "Club_Grip", "Crown": "Club_Carbon", "Face": None}.get(role)
    if tex and os.path.exists(os.path.join(TEX, tex + "_Albedo.png")):
        nt = m.node_tree
        img = nt.nodes.new("ShaderNodeTexImage")
        img.image = bpy.data.images.load(os.path.join(TEX, tex + "_Albedo.png"), check_existing=True)
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        mix.inputs[6].default_value = (*col, 1)
        nt.links.new(img.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    m.diffuse_color = (*col, 1)
    _MATS[role] = m
    return m


# ---------------------------------------------------------------- textures

def _img_array(path, size):
    img = bpy.data.images.load(path, check_existing=False)
    img.scale(size, size)
    px = np.empty(size * size * img.channels, dtype=np.float32)
    img.pixels.foreach_get(px)
    ch = img.channels
    bpy.data.images.remove(img)
    return px.reshape(size, size, ch)[..., :3]


def _save(arr, name):
    h, w = arr.shape[:2]
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., :3] = np.clip(arr, 0, 1)
    img = bpy.data.images.new(name, w, h, alpha=False, float_buffer=False)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(TEX, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def _normal(height, strength):
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5 * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5 * strength
    n = np.stack([-dx, -dy, np.ones_like(height)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def _neutral(rgb):
    """Albedo for tinting: luminance normalised around 1 so the palette colour sets the base."""
    lum = rgb.mean(-1)
    lum = 0.82 * lum / max(lum.mean(), 1e-4)
    return np.repeat(np.clip(lum, 0, 1)[..., None], 3, -1)


def textures():
    os.makedirs(TEX, exist_ok=True)
    for src, name, strength in (("tex_grip_rubber", "Club_Grip", 6.0), ("tex_carbon_weave", "Club_Carbon", 3.0),
                                ("tex_putter_milled", "Club_PutterFace", 5.0)):
        rgb = _img_array(os.path.join(AI_TEX, src + ".jpg"), 512)
        lum = rgb.mean(-1)
        h = (lum - lum.min()) / max(np.ptp(lum), 1e-4)
        _save(_neutral(rgb), name + "_Albedo")
        _save(_normal(h, strength), name + "_Normal")
    # Iron face: scorelines across the hitting area (UV: u heel->toe per 10 cm, v up the face per 10 cm).
    S = 512
    v, uu = np.mgrid[0:S, 0:S] / S
    v = 1.0 - v
    area_ = smoothstep(0.13, 0.17, uu) * (1 - smoothstep(0.74, 0.78, uu)) * smoothstep(0.03, 0.05, v) * (1 - smoothstep(0.40, 0.42, v))
    groove = (np.abs(((v - 0.06) / 0.034) % 1.0 - 0.5) > 0.42).astype(np.float32) * area_
    brushed = 0.03 * np.sin(uu * 900.0 + np.sin(v * 37.0) * 3.0)
    alb = 0.92 - 0.45 * groove + brushed
    _save(np.repeat(alb[..., None], 3, -1), "Club_IronFace_Albedo")
    _save(_normal(1.0 - groove * 0.8 + brushed * 0.5, 4.0), "Club_IronFace_Normal")
    # Wood face: fine scorelines in the centre band (u across the face centred at 0.5, v per 7 cm).
    band = smoothstep(0.18, 0.24, uu) * (1 - smoothstep(0.76, 0.82, uu)) * smoothstep(0.2, 0.26, v) * (1 - smoothstep(0.78, 0.84, v))
    lines = (np.abs(((v - 0.2) / 0.06) % 1.0 - 0.5) > 0.44).astype(np.float32) * band
    alb = 0.9 - 0.35 * lines + 0.03 * np.sin(uu * 700.0)
    _save(np.repeat(alb[..., None], 3, -1), "Club_WoodFace_Albedo")
    _save(_normal(1.0 - lines * 0.7, 4.0), "Club_WoodFace_Normal")
    print("[pp_clubs] textures written to", TEX)


# ---------------------------------------------------------------- build / export / render

def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)


def build(only=None):
    clear()
    os.makedirs(OUT, exist_ok=True)
    coll = bpy.context.scene.collection
    manifest = {"frame": "Unity seat space of ClubGrip_<Kind>: origin = lead-hand grip, +Y down the shaft, +Z face normal",
                "clubs": []}
    obs = []
    for spec in MODELS:
        if only and spec["id"] not in only:
            continue
        ob, info = build_club(spec)
        coll.objects.link(ob)
        obs.append(ob)
        manifest["clubs"].append(info)
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, ob.name + ".fbx"), use_selection=True,
                                 object_types={"MESH"}, axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="OFF",
                                 use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
        print("[pp_clubs] %-14s %5d tris" % (spec["id"], info["tris"]))
    if not only:
        with open(os.path.join(OUT, "clubs_manifest.json"), "w") as f:
            json.dump(manifest, f, indent=1)
    return obs


def to_address(ob, x):
    """Places an exported club upright for previews: shaft vertical (head down), face toward -Y (camera)."""
    # export frame -> seat (bone) frame: undo Rx(90); seat +Y down the shaft -> world -Z.
    Rinv = Matrix.Rotation(math.radians(-90), 4, "X")
    S = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
    ob.matrix_world = Matrix.Translation((x, 0, 1.25)) @ S @ Rinv


def address_matrix(kind_name):
    """Export frame -> address frame (sole on z = 0, face toward +X, toe toward -Y)."""
    kind = KINDS[kind_name]
    lie = math.radians(kind["lie"])
    u = Vector((0.0, math.cos(lie), math.sin(lie)))
    grip = Vector(kind["base"]) + u * (kind["length"] - 0.02 - GRIP_FROM_BUTT)
    yb = (-u).normalized()
    zb = Vector((1.0, 0.0, 0.0))
    B = Matrix((yb.cross(zb).normalized(), yb, zb)).transposed().to_4x4()
    B.translation = grip
    return B @ Matrix.Rotation(math.radians(-90), 4, "X")


def studio(scn):
    try:
        scn.render.engine = "BLENDER_EEVEE"
    except TypeError:
        scn.render.engine = "BLENDER_EEVEE_NEXT"
    world = scn.world or bpy.data.worlds.new("W")
    scn.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes.get("Background")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.45
    ramp.color_ramp.elements[0].color = (0.05, 0.055, 0.06, 1)
    ramp.color_ramp.elements[1].position = 0.62
    ramp.color_ramp.elements[1].color = (0.9, 0.93, 0.97, 1)
    mapr = nt.nodes.new("ShaderNodeMapRange")
    mapr.inputs["From Min"].default_value = -1.0
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], mapr.inputs["Value"])
    nt.links.new(mapr.outputs["Result"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bg.inputs[0])
    bg.inputs[1].default_value = 1.0
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(45), 0, math.radians(120))
    scn.collection.objects.link(sun)
    scn.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in scn.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"


def render(obs):
    scn = bpy.context.scene
    os.makedirs(REVIEW, exist_ok=True)
    studio(scn)
    by_id = {o.name[len("Club_"):]: o for o in obs}
    specs = [s for s in MODELS if s["id"] in by_id]
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scn.collection.objects.link(cam)
    scn.camera = cam
    # Lineup: standing upright, soles level.
    for i, s in enumerate(specs):
        to_address(by_id[s["id"]], (i - (len(specs) - 1) / 2) * 0.16)
        bpy.context.view_layer.update()
        ob = by_id[s["id"]]
        zmin = min((ob.matrix_world @ v.co).z for v in ob.data.vertices)
        ob.matrix_world = Matrix.Translation((0, 0, -zmin)) @ ob.matrix_world
    scn.render.resolution_x, scn.render.resolution_y = 2400, 1100
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = len(specs) * 0.16 + 0.1
    cam.location = (0, -4, 0.6)
    cam.rotation_euler = (math.radians(90), 0, 0)
    scn.render.filepath = os.path.join(REVIEW, "blender_lineup.png")
    bpy.ops.render.render(write_still=True)
    # Heads at address, one tile each, then a contact sheet.
    cam.data.type = "PERSP"
    cam.data.lens = 85
    TW, TH = 480, 360
    scn.render.resolution_x, scn.render.resolution_y = TW, TH
    views = [Vector((0.75, -0.55, 0.42)), Vector((-0.35, 0.55, 0.85)), Vector((-0.85, -0.35, 0.35))]
    tiles = []
    for s in specs:
        for o in obs:
            o.hide_render = o is not by_id[s["id"]]
        ob = by_id[s["id"]]
        ob.matrix_world = address_matrix(s["kind"])
        target = Vector((-0.03 if s["head"] != "putter" else -0.03, -0.025, 0.02))
        dist_ = 0.7 if s["head"] == "wood" else 0.55
        for vi, dv in enumerate(views):
            dirv = dv.normalized()
            cam.location = target + dirv * dist_
            cam.rotation_euler = (-dirv).to_track_quat("-Z", "Y").to_euler()
            path = os.path.join(REVIEW, "_tile_%s_%d.png" % (s["id"], vi))
            scn.render.filepath = path
            bpy.ops.render.render(write_still=True)
            tiles.append(path)
    cols = len(views) * (2 if len(specs) > 4 else 1)
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.ones((rows * TH, cols * TW, 4), np.float32)
    for k, p in enumerate(tiles):
        img = bpy.data.images.load(p)
        px = np.empty(TW * TH * 4, np.float32)
        img.pixels.foreach_get(px)
        bpy.data.images.remove(img)
        r, c = rows - 1 - k // cols, k % cols
        sheet[r * TH:(r + 1) * TH, c * TW:(c + 1) * TW] = px.reshape(TH, TW, 4)
        os.remove(p)
    img = bpy.data.images.new("sheet", cols * TW, rows * TH, alpha=True)
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = os.path.join(REVIEW, "blender_heads.png")
    img.file_format = "PNG"
    img.save()
    for o in obs:
        o.hide_render = False
    print("[pp_clubs] renders in", REVIEW)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stage = argv[0] if argv else "all"
    only = argv[1].split(",") if len(argv) > 1 else None
    if stage in ("all", "textures"):
        textures()
    if stage in ("all", "build", "render"):
        obs = build(only)
        if stage in ("all", "render"):
            render(obs)
        if not only:
            if os.path.exists(BLEND):
                os.replace(BLEND, BLEND.replace(".blend", "_backup.blend"))
            bpy.ops.wm.save_as_mainfile(filepath=BLEND)


if __name__ == "__main__":
    main()
