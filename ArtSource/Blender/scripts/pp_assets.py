"""Procedural builders for the Puttshack Pro review set. Each build_* returns the collection name."""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

from pp_lib import (B, CREAM, CYAN, INK, T, _tb, asset_coll, displace, mat, p_box, p_cyl, p_ico, p_path_tube,
                    p_sphere, p_tube, palette, rad, srgb)


def _split_by_index(bm_src, idx):
    t = bm_src.copy()
    bmesh.ops.delete(t, geom=[f for f in t.faces if f.material_index != idx], context="FACES")
    return t


def cloth(width, height, nx, ny, shape, thickness=0.004, band=None):
    """Grid in the XZ plane displaced by shape(u, v) -> (x, y, z); solidified. band(u, v) -> material index."""
    bm = _tb()
    grid = [[bm.verts.new(shape(i / nx, j / ny)) for j in range(ny + 1)] for i in range(nx + 1)]
    faces = []
    for i in range(nx):
        for j in range(ny):
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            f.material_index = band((i + 0.5) / nx, (j + 0.5) / ny) if band else 0
            faces.append(f)
    bm.normal_update()
    res = bmesh.ops.solidify(bm, geom=faces, thickness=thickness)
    bm.normal_update()
    return bm


# ------------------------------------------------------------------ 2. flagstick
def build_flagstick():
    P = palette()
    flagcloth = mat("FlagCyan", CYAN, 0.7)
    c = asset_coll("PP_Flagstick")
    b = B("PP_Flagstick")
    H, r = 2.10, 0.0095
    b.add(p_tube([(0.004, 0.0), (0.0085, 0.03), (r, 0.075)], 12), P["chrome"])
    for z0, z1, k in [(0.075, 1.62, "cream"), (1.62, 1.66, "ink"), (1.66, 1.70, "cyan"), (1.70, H - 0.02, "cream")]:
        b.add(p_cyl(r, r, z1 - z0, 12), P[k], T((0, 0, z0)))
    b.add(p_tube([(r, H - 0.02), (r * 0.9, H - 0.005), (0.004, H), (0.0, H + 0.004)], 12), P["ink"])
    W, Hf = 0.50, 0.36
    ztop = H - 0.03
    x0 = r + 0.004

    def shape(u, v):
        x = x0 + u * W * 0.96
        y = 0.075 * u * math.sin(u * math.pi * 2.4 + 0.3) + 0.018 * u * math.sin(v * 2.6 + u * 4.0)
        z = ztop - Hf + v * Hf - 0.04 * u * u + 0.012 * u * math.sin(u * 7.0)
        return (x, y, z)

    fb = cloth(W, Hf, 18, 6, shape, 0.004, band=lambda u, v: 1 if 0.40 < v < 0.60 else 0)
    b.add(_split_by_index(fb, 0), flagcloth)
    b.add(_split_by_index(fb, 1), P["cream"])
    fb.free()
    b.add(p_cyl(r + 0.004, r + 0.004, Hf + 0.01, 12), P["ink"], T((0, 0, ztop - Hf - 0.005)))
    b.finish(c, angle=40, ground="z")
    return c.name


# ------------------------------------------------------------------ helpers
def text_bm(body, size, extrude=0.002, align="CENTER", font_bold=True):
    """Flat text in XY plane (reads from +Z) converted to a bmesh, baseline centered at origin."""
    cu = bpy.data.curves.new("_pp_txt", "FONT")
    cu.body = body
    cu.size = size
    cu.extrude = extrude
    cu.resolution_u = 4
    try:
        cu.align_x = align
        cu.align_y = "CENTER"
    except TypeError:
        pass
    ob = bpy.data.objects.new("_pp_txt", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    bm = _tb()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    return bm


def poly_prism(pts2d, depth):
    """Extrude a 2D polygon (XY) by depth along +Z."""
    bm = _tb()
    bot = [bm.verts.new((x, y, 0)) for x, y in pts2d]
    top = [bm.verts.new((x, y, depth)) for x, y in pts2d]
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    n = len(pts2d)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


# ------------------------------------------------------------------ 3. tee markers + tee sign
def _tee_marker(name, coll, col_mat, P, x):
    b = B(name)
    b.add(p_tube([(0.0, 0.0), (0.072, 0.0), (0.078, 0.006), (0.078, 0.024), (0.070, 0.032), (0.0, 0.032)], 24),
          P["ink"])
    b.add(p_tube([(0.052, 0.030), (0.056, 0.040), (0.050, 0.044)], 24, caps=False), P["cream"])
    ball = p_sphere(0.062, 24, 12)
    b.add(ball, col_mat, T((0, 0, 0.032 + 0.062 - 0.012)))
    b.add(p_tube([(0.0626, -0.006), (0.0632, 0.0), (0.0626, 0.006)], 24, caps=False), P["cream"],
          T((0, 0, 0.032 + 0.062 - 0.012)))
    ob = b.finish(coll, angle=40)
    ob.location.x = x
    return ob


def build_tee_markers():
    P = palette()
    c = asset_coll("PP_TeeMarkers")
    _tee_marker("PP_TeeMarker_L", c, P["cyan"], P, -0.35)
    _tee_marker("PP_TeeMarker_R", c, P["cyan"], P, 0.35)
    return c.name


def build_tee_sign():
    P = palette()
    c = asset_coll("PP_TeeSign")
    b = B("PP_TeeSign")
    post, span = 0.09, 0.95
    for sx in (-1, 1):
        b.add(p_box(post, post, 1.12, bevel=0.008), P["darkwood"], T((sx * span / 2, 0, 0)), smooth=False)
        b.add(p_box(post + 0.02, post + 0.02, 0.025, bevel=0.006), P["darkwood"], T((sx * span / 2, 0, 1.12)),
              smooth=False)
    pw, ph, pt, pz = 1.08, 0.62, 0.04, 0.42
    b.add(p_box(pw + 0.04, pt, ph + 0.04, bevel=0.01), P["ink"], T((0, -0.06, pz - 0.02)), smooth=False)
    b.add(p_box(pw, pt, ph, bevel=0.006), P["cream"], T((0, -0.075, pz)), smooth=False)
    hdr = 0.15
    b.add(p_box(pw, 0.006, hdr, bevel=0.002), P["cyan"], T((0, -0.096, pz + ph - hdr)), smooth=False)
    face_y = -0.0955
    # text: rotate XY text to face -Y
    R = Matrix.Rotation(rad(90), 4, "X")
    b.add(text_bm("HOLE 1", 0.11, 0.003), P["cream"], T((0, face_y - 0.006, pz + ph - hdr / 2 - 0.035)) @ R,
          smooth=False)
    b.add(text_bm("PAR 4", 0.10, 0.003, "LEFT"), P["ink"], T((-pw / 2 + 0.07, face_y + 0.001, pz + 0.27)) @ R,
          smooth=False)
    b.add(text_bm("352 m", 0.075, 0.003, "LEFT"), P["ink"], T((-pw / 2 + 0.07, face_y + 0.001, pz + 0.12)) @ R,
          smooth=False)
    # hole map: fairway blob + green + flag dot
    fair = [(0.0, 0.0), (0.05, -0.02), (0.11, 0.02), (0.15, 0.12), (0.16, 0.24), (0.12, 0.34), (0.07, 0.36),
            (0.05, 0.28), (0.08, 0.18), (0.05, 0.08), (-0.02, 0.04)]
    fx, fz = 0.16, pz + 0.07
    b.add(poly_prism(fair, 0.004), P.get("grass"), T((fx, face_y + 0.001, fz)) @ R, smooth=False)
    b.add(p_cyl(0.035, 0.035, 0.004, 16), mat("GreenLight", "#79B447", 0.8),
          T((fx + 0.10, face_y - 0.002, fz + 0.36)) @ R, smooth=False)
    b.add(p_cyl(0.011, 0.011, 0.004, 10), P["cyan"], T((fx + 0.10, face_y - 0.006, fz + 0.37)) @ R, smooth=False)
    b.add(p_cyl(0.018, 0.018, 0.004, 10), P["cream"], T((fx + 0.01, face_y - 0.002, fz + 0.01)) @ R,
          smooth=False)
    b.finish(c, angle=35)
    return c.name


# ------------------------------------------------------------------ 4. vegetation + rocks
def _xform(tb, M):
    bmesh.ops.transform(tb, matrix=M, verts=tb.verts)
    return tb


def _clamp_floor(tb, z=0.0):
    for v in tb.verts:
        if v.co.z < z:
            v.co.z = z
    return tb


def _lump(radius, seed, sub=2, amp=0.18, freq=1.6, squash=(1, 1, 0.8)):
    tb = p_ico(1.0, sub)
    displace(tb, amp, freq, seed)
    bmesh.ops.scale(tb, vec=(radius * squash[0], radius * squash[1], radius * squash[2]), verts=tb.verts)
    return tb


def build_oak():
    P = palette()
    rnd = random.Random(7)
    c = asset_coll("PP_TreeOak")
    b = B("PP_TreeOak")
    trunk_pts = [(0, 0, 0), (0.05, 0.02, 1.2), (0.0, 0.06, 2.4), (-0.08, 0.02, 3.4), (-0.05, -0.02, 4.4)]
    b.add(p_path_tube(trunk_pts, 0.3, 10, radii=[0.52, 0.4, 0.34, 0.29, 0.22]), P["bark"])
    # roots flare
    for k in range(5):
        a = k * 2 * math.pi / 5 + 0.3
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = [d * 0.15 + Vector((0, 0, 0.55)), d * 0.45 + Vector((0, 0, 0.15)), d * 0.75 + Vector((0, 0, -0.02))]
        b.add(p_path_tube(pts, 0.1, 6, radii=[0.16, 0.12, 0.05]), P["bark"])
    centers = [Vector((0, 0, 5.6))]
    for k in range(13):
        a = k * 2 * math.pi / 13 + rnd.uniform(-0.2, 0.2)
        rr = rnd.uniform(2.0, 2.8)
        z = 5.2 + rnd.uniform(-0.5, 0.9) + (1.0 if k % 2 else 0.0)
        centers.append(Vector((math.cos(a) * rr, math.sin(a) * rr, z)))
    centers.append(Vector((0.3, -0.2, 7.0)))
    # branches from trunk top toward some clumps
    top = Vector(trunk_pts[-1])
    for i, cc in enumerate(centers[1:13:2]):
        start = Vector((0, 0, 3.0 + 0.3 * i)) + Vector((trunk_pts[3][0], trunk_pts[3][1], 0))
        mid = start.lerp(cc, 0.5) + Vector((0, 0, 0.3))
        end = start.lerp(cc, 0.8)
        b.add(p_path_tube([start, mid, end], 0.1, 6, radii=[0.14, 0.09, 0.05]), P["bark"])
    for i, cc in enumerate(centers):
        r = 2.1 if i == 0 else rnd.uniform(1.05, 1.45)
        tb = _lump(r, i * 3.7 + 1, 3, amp=0.24, freq=1.9, squash=(1, 1, 0.8))
        _xform(tb, T(cc, (0, 0, rnd.uniform(0, 6.28))))
        b.add(tb, P["leaf_oak2"] if (cc.z > 5.8 and i % 2) else P["leaf_oak"])
    b.finish(c, angle=80, ground="z")
    return c.name


def _pine_tier(r, h, z, n=14, droop=0.35, jag=0.72, seed=0):
    bm = _tb()
    rnd = random.Random(seed)
    apex = bm.verts.new((0, 0, z + h))
    rim, mid, inner = [], [], []
    for i in range(2 * n):
        a = i * math.pi / n + rnd.uniform(-0.05, 0.05)
        rr = r * (1.0 if i % 2 == 0 else jag) * rnd.uniform(0.92, 1.05)
        zz = z - droop * (1.0 if i % 2 == 0 else 0.6)
        rim.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), zz)))
        mid.append(bm.verts.new((rr * 0.5 * math.cos(a), rr * 0.5 * math.sin(a), z + h * 0.42)))
        inner.append(bm.verts.new((0.18 * r * math.cos(a), 0.18 * r * math.sin(a), z + 0.08)))
    m = len(rim)
    for i in range(m):
        j = (i + 1) % m
        bm.faces.new((mid[i], mid[j], apex))
        bm.faces.new((rim[i], rim[j], mid[j], mid[i]))
        bm.faces.new((inner[i], inner[j], rim[j], rim[i]))
    bm.faces.new(list(reversed(inner)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build_pine():
    P = palette()
    c = asset_coll("PP_TreePine")
    b = B("PP_TreePine")
    H = 11.0
    b.add(p_tube([(0.34, 0.0), (0.26, 0.4), (0.2, 3.0), (0.12, 7.0), (0.04, H - 0.5)], 10), P["bark"])
    tiers = 8
    for k in range(tiers):
        t = k / (tiers - 1)
        r = 2.5 * (1 - t) + 0.55 * t
        z = 2.5 + t * (H - 4.3)
        h = 2.4 - 0.9 * t
        tb = _pine_tier(r, h, z, n=13 - k // 2, droop=0.45 * (1 - t) + 0.15, seed=k + 11)
        _xform(tb, T((0, 0, 0), (0, 0, k * 0.61)))
        b.add(tb, P["leaf_pine"], smooth=False)
    b.add(_pine_tier(0.45, 1.3, H - 1.3, n=7, droop=0.1, seed=99), P["leaf_pine"], smooth=False)
    b.finish(c, angle=60, ground="z")
    return c.name


def build_shrub():
    P = palette()
    rnd = random.Random(21)
    c = asset_coll("PP_Shrub")
    b = B("PP_Shrub")
    spots = [((0, 0, 0.42), 0.55), ((0.45, 0.1, 0.32), 0.42), ((-0.42, 0.12, 0.3), 0.4), ((0.1, 0.42, 0.3), 0.38),
             ((-0.05, -0.4, 0.28), 0.38), ((0.15, 0.0, 0.72), 0.36)]
    for i, (p, r) in enumerate(spots):
        tb = _lump(r, i * 5.1 + 2, 3, amp=0.2, freq=2.2, squash=(1, 1, 0.85))
        _xform(tb, T(p, (0, 0, rnd.uniform(0, 6.28))))
        _clamp_floor(tb, 0.0)
        b.add(tb, P["leaf_shrub"] if i % 2 == 0 else P["leaf_oak"])
    b.finish(c, angle=80)
    return c.name


def build_rocks():
    P = palette()
    rnd = random.Random(5)
    c = asset_coll("PP_RockCluster")
    b = B("PP_RockCluster")
    rocks = [((0, 0), (0.9, 0.72, 0.7), 0), ((1.05, 0.25), (0.55, 0.48, 0.45), 1),
             ((-0.95, 0.35), (0.52, 0.45, 0.38), 1), ((0.45, -0.85), (0.38, 0.33, 0.3), 0),
             ((-0.45, -0.75), (0.26, 0.22, 0.2), 1), ((1.55, -0.45), (0.18, 0.16, 0.14), 0)]
    for i, (p, s, m) in enumerate(rocks):
        tb = p_ico(1.0, 3)
        displace(tb, 0.16, 0.9, i * 9.3 + 4)
        # planar cuts for a chiselled, stylised-real silhouette
        cuts = [Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 1))).normalized() for _ in range(6)]
        for v in tb.verts:
            for n in cuts:
                d = v.co.dot(n) - 0.78
                if d > 0:
                    v.co -= n * d
        bmesh.ops.scale(tb, vec=s, verts=tb.verts)
        _xform(tb, T((p[0], p[1], s[2] * 0.3), (rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(0, 6.28))))
        _clamp_floor(tb, 0.0)
        b.add(tb, P["rock"] if m == 0 else P["rock2"], smooth=True)
    b.finish(c, angle=28)
    return c.name


# ------------------------------------------------------------------ 5. clubs
def _add_split(b, tb, pick):
    """pick(face) -> material; splits a temp bmesh by material and adds each part."""
    groups = {}
    for f in tb.faces:
        groups.setdefault(pick(f), []).append(f.index)
    tb.faces.ensure_lookup_table()
    for m, idxs in groups.items():
        t = tb.copy()
        t.faces.ensure_lookup_table()
        keep = set(idxs)
        bmesh.ops.delete(t, geom=[f for f in t.faces if f.index not in keep], context="FACES")
        b.add(t, m)
    tb.free()


def _prism_xz(pts, depth):
    """Polygon in the XZ plane (face plane) extruded toward +Y."""
    bm = _tb()
    front = [bm.verts.new((x, 0.0, z)) for x, z in pts]
    back = [bm.verts.new((x, depth, z)) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def _shaft_and_grip(b, P, start, lie, length, shaft_mat, band_mat, r_tip=0.0045, r_butt=0.0075):
    d = Vector((-math.cos(rad(lie)), 0, math.sin(rad(lie))))
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    grip = 0.27
    sl = length - grip
    b.add(p_tube([(r_tip, 0.0), (r_tip * 1.05, sl * 0.35), (r_butt, sl * 0.8), (r_butt, sl)], 10, caps=False),
          shaft_mat, Matrix.Translation(start) @ rot)
    b.add(p_cyl(r_butt + 0.0012, r_butt + 0.0012, 0.018, 10), band_mat, Matrix.Translation(start + d * (sl - 0.03)) @ rot)
    b.add(p_tube([(0.0102, 0.0), (0.0112, 0.02), (0.0126, grip * 0.7), (0.0132, grip - 0.012), (0.0115, grip),
                  (0.0, grip + 0.002)], 12), P["rubber"], Matrix.Translation(start + d * sl) @ rot)
    b.add(p_cyl(0.0114, 0.0114, 0.02, 12), P["cyan"], Matrix.Translation(start + d * (sl + 0.012)) @ rot)
    return d


def build_driver():
    P = palette()
    c = asset_coll("PP_ClubDriver")
    b = B("PP_ClubDriver")
    tb = p_sphere(1.0, 24, 12)
    for v in tb.verts:
        x, y, z = v.co
        v.co = Vector((0.062 + x * 0.058, 0.05 + y * 0.056, 0.032 + z * 0.031))
        if v.co.y < -0.002:
            v.co.y = -0.002 + (v.co.y + 0.002) * 0.08
        if v.co.z < 0.004:
            v.co.z = 0.004 + (v.co.z - 0.004) * 0.15
    tb.normal_update()

    def pick(f):
        n = f.normal
        if n.y < -0.8:
            return P["darksteel"]
        if n.z > 0.35:
            return P["ink"]
        return P["chrome"]

    _add_split(b, tb, pick)
    b.add(p_cyl(0.004, 0.004, 0.0015, 10), P["cyan"], T((0.062, 0.02, 0.0635)))
    hosel = Vector((0.012, 0.01, 0.022))
    d = Vector((-math.cos(rad(58)), 0, math.sin(rad(58))))
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    b.add(p_tube([(0.0085, -0.02), (0.0075, 0.03), (0.0068, 0.05)], 12), P["ink"], Matrix.Translation(hosel) @ rot)
    _shaft_and_grip(b, P, hosel + d * 0.05, 58, 1.15 - 0.07, mat("Graphite", "#262B2E", 0.35, 0.2), P["cyan"])
    b.finish(c, angle=40, ground="z")
    return c.name


def _iron_head(b, P, loft, sole_w=0.02, toe_h=0.048, length=0.078, badge=True):
    pts = [(0.0, 0.006), (0.006, 0.0), (length - 0.012, 0.0), (length - 0.002, 0.008), (length + 0.002, toe_h * 0.55),
           (length - 0.006, toe_h * 0.92), (length - 0.02, toe_h), (0.01, toe_h * 0.62), (0.0, toe_h * 0.5)]
    tb = _prism_xz(pts, sole_w)
    for v in tb.verts:
        t = min(max(v.co.z / toe_h, 0), 1)
        v.co.y *= (1.0 - 0.62 * t)
    for f in tb.faces:
        f.material_index = 0
    # grooves on face
    for k in range(6):
        g = p_box(length * 0.62, 0.0012, 0.0011, base=False)
        bmesh.ops.transform(g, matrix=T((length * 0.5, -0.0003, 0.008 + k * 0.0055)), verts=g.verts)
        me = bpy.data.meshes.new("_g")
        g.to_mesh(me)
        g.free()
        n0 = len(tb.faces)
        tb.from_mesh(me)
        bpy.data.meshes.remove(me)
        tb.faces.ensure_lookup_table()
        for f in tb.faces[n0:]:
            f.material_index = 1
    if badge:
        bb = p_box(length * 0.4, 0.002, 0.009, base=False)
        bmesh.ops.transform(bb, matrix=T((length * 0.52, sole_w * 0.72 + 0.001, 0.014)), verts=bb.verts)
        me = bpy.data.meshes.new("_b")
        bb.to_mesh(me)
        bb.free()
        n0 = len(tb.faces)
        tb.from_mesh(me)
        bpy.data.meshes.remove(me)
        tb.faces.ensure_lookup_table()
        for f in tb.faces[n0:]:
            f.material_index = 2
    bmesh.ops.transform(tb, matrix=Matrix.Rotation(-rad(loft), 4, "X"), verts=tb.verts)
    tb.normal_update()
    mats = [P["chrome"], P["ink"], P["cyan"]]
    _add_split(b, tb, lambda f: mats[f.material_index])


def _build_iron_like(name, loft, lie, length, toe_h, sole_w):
    P = palette()
    c = asset_coll(name)
    b = B(name)
    _iron_head(b, P, loft, sole_w=sole_w, toe_h=toe_h)
    d = Vector((-math.cos(rad(lie)), 0, math.sin(rad(lie))))
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    hosel = Vector((0.004, 0.006, 0.012))
    b.add(p_tube([(0.0078, -0.012), (0.0072, 0.03), (0.0062, 0.065)], 12), P["chrome"], Matrix.Translation(hosel) @ rot)
    _shaft_and_grip(b, P, hosel + d * 0.065, lie, length - 0.075, P["steel"], P["cyan"], 0.0045, 0.0072)
    b.finish(c, angle=40, ground="z")
    return c.name


def build_iron():
    return _build_iron_like("PP_ClubIron", 33, 62, 0.94, 0.048, 0.02)


def build_wedge():
    return _build_iron_like("PP_ClubWedge", 56, 64, 0.90, 0.056, 0.024)


def build_putter():
    P = palette()
    c = asset_coll("PP_ClubPutter")
    b = B("PP_ClubPutter")
    b.add(p_box(0.105, 0.026, 0.022, bevel=0.003, seg=2), P["darksteel"], T((0.052, 0.013, 0.0)), smooth=False)
    b.add(p_box(0.08, 0.0012, 0.012, base=False), P["chrome"], T((0.052, -0.0004, 0.011)), smooth=False)
    b.add(p_box(0.003, 0.012, 0.0008, base=False), P["cyan"], T((0.052, 0.012, 0.0224)), smooth=False)
    b.add(p_box(0.03, 0.004, 0.0008, base=False), P["cream"], T((0.052, 0.022, 0.0224)), smooth=False)
    lie = 70
    d = Vector((-math.cos(rad(lie)), 0, math.sin(rad(lie))))
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    # plumber's neck
    neck = [Vector((0.014, 0.012, 0.02)), Vector((0.014, 0.012, 0.045)), Vector((0.006, 0.006, 0.055)),
            Vector((0.0, 0.004, 0.062))]
    b.add(p_path_tube(neck, 0.0055, 10), P["chrome"])
    _shaft_and_grip(b, P, neck[-1], lie, 0.88 - 0.065, P["steel"], P["ink"], 0.0055, 0.0072)
    b.finish(c, angle=40, ground="z")
    return c.name


# ------------------------------------------------------------------ 6. course props
def build_bench():
    P = palette()
    c = asset_coll("PP_Bench")
    b = B("PP_Bench")
    L = 1.6
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.08)
        # cast side frame (ink): front leg, rear leg/back upright, seat rail, armrest
        b.add(p_path_tube([(x, -0.22, 0.0), (x, -0.21, 0.40), (x, -0.2, 0.62)], 0.022, 8), P["ink"])
        b.add(p_path_tube([(x, 0.24, 0.0), (x, 0.2, 0.42), (x, 0.27, 0.62), (x, 0.33, 0.9)], 0.022, 8), P["ink"])
        b.add(p_path_tube([(x, -0.26, 0.4), (x, 0.24, 0.42)], 0.018, 8), P["ink"])
        b.add(p_path_tube([(x, -0.27, 0.62), (x, -0.05, 0.64), (x, 0.26, 0.63)], 0.02, 8), P["ink"])
        b.add(p_sphere(0.028, 10, 6), P["ink"], T((x, -0.27, 0.62)))
        b.add(p_box(0.07, 0.07, 0.012, bevel=0.003), P["ink"], T((x, -0.22, 0.0)), smooth=False)
        b.add(p_box(0.07, 0.07, 0.012, bevel=0.003), P["ink"], T((x, 0.24, 0.0)), smooth=False)
    for k in range(4):
        y = -0.22 + k * 0.125
        b.add(p_box(L, 0.1, 0.035, bevel=0.008, seg=2), P["wood"], T((0, y, 0.425 + 0.004 * k)), smooth=False)
    for k in range(3):
        z = 0.53 + k * 0.12
        y = 0.215 + (z - 0.42) * 0.25
        b.add(p_box(L, 0.1, 0.03, bevel=0.008, seg=2), P["wood"], T((0, y + 0.02, z), (rad(78), 0, 0)), smooth=False)
    b.add(p_box(0.22, 0.006, 0.05, bevel=0.002), P["cyan"], T((0, 0.215 + (0.78 - 0.42) * 0.25 - 0.02, 0.76), (rad(-12), 0, 0)),
          smooth=False)
    b.finish(c, angle=35)
    return c.name


def build_ball_washer():
    P = palette()
    towel = mat("Towel", CREAM, 0.95)
    c = asset_coll("PP_BallWasher")
    b = B("PP_BallWasher")
    b.add(p_box(0.3, 0.3, 0.02, bevel=0.006), P["darksteel"], smooth=False)
    b.add(p_cyl(0.035, 0.03, 0.86, 12), P["ink"], T((0, 0, 0.02)))
    # washer body (cyan) with rounded shell
    b.add(p_box(0.13, 0.26, 0.24, bevel=0.03, seg=3), P["cyan"], T((0, 0, 0.84)), smooth=True)
    b.add(p_box(0.14, 0.27, 0.03, bevel=0.01, seg=2), P["cream"], T((0, 0, 1.07)), smooth=True)
    # side paddle hub
    b.add(p_cyl(0.05, 0.05, 0.02, 16), P["cream"], T((0.065, 0, 0.96), (0, rad(90), 0)))
    # plunger handle
    b.add(p_cyl(0.008, 0.008, 0.16, 8), P["chrome"], T((0, 0, 1.09)))
    b.add(p_path_tube([(0, -0.06, 1.25), (0, 0.06, 1.25)], 0.016, 10), P["ink"])
    # hanging towel on side hook
    b.add(p_cyl(0.006, 0.006, 0.04, 6), P["chrome"], T((-0.064, 0, 0.9), (0, rad(-90), 0)))

    def shape(u, v):
        x = -0.088 - 0.01 * math.sin(v * 5.0 + u * 2.0) - 0.02 * (1 - v)
        y = (u - 0.5) * 0.18 * (1.0 - 0.12 * (1 - v))
        z = 0.905 - (1 - v) * 0.3
        return (x, y, z)

    tw = cloth(0.2, 0.3, 6, 6, shape, 0.006)
    b.add(tw, towel)
    b.finish(c, angle=35)
    return c.name


def build_bunker_rake():
    P = palette()
    c = asset_coll("PP_BunkerRake")
    b = B("PP_BunkerRake")
    W = 0.6
    tine_h = 0.055
    b.add(p_box(0.035, W, 0.03, bevel=0.006, seg=2), P["cyan"], T((0, 0, tine_h)), smooth=False)
    n = 15
    for k in range(n):
        y = -W / 2 + 0.02 + k * (W - 0.04) / (n - 1)
        b.add(p_tube([(0.003, 0.0), (0.0065, 0.012), (0.0075, tine_h + 0.005)], 6), P["cyan"], T((0, y, 0)))
    # socket + handle lying back along +X, sloping to the ground
    start = Vector((0.02, 0, tine_h + 0.02))
    end = Vector((1.55, 0, 0.016))
    b.add(p_path_tube([start, start.lerp(end, 0.08)], 0.018, 10), P["cyan"])
    b.add(p_path_tube([start.lerp(end, 0.08), end], 0.0145, 10), P["cream"])
    b.add(p_path_tube([end - (end - start).normalized() * 0.16, end + (end - start).normalized() * 0.005], 0.0165, 10),
          P["ink"])
    b.finish(c, angle=35)
    return c.name


def _tire(P, x, y, z, r=0.23, w=0.18):
    ri = r * 0.6
    prof = [(ri, -w / 2), (r - 0.03, -w / 2), (r, -w / 2 + 0.03), (r, w / 2 - 0.03), (r - 0.03, w / 2), (ri, w / 2)]
    M = T((x, y, z), (rad(90), 0, 0))
    return [(p_tube(prof, 20, caps=False), P["rubber"], M),
            (p_tube([(ri, -w / 2 + 0.02), (ri, w / 2 - 0.02)], 16), mat("Hub", "#B9BCBC", 0.35, 0.8), M),
            (p_cyl(0.05, 0.05, 0.02, 12), P["ink"], M @ T((0, 0, (w / 2 - 0.02) * (1 if y < 0 else -1) - (0.0 if y < 0 else 0.02))))]


def build_golf_cart():
    P = palette()
    body_m = mat("CartBody", CREAM, 0.35)
    seat_m = mat("CartSeat", INK, 0.55)
    light_m = mat("Headlight", "#FFF8E8", 0.2, 0.0, **{"Emission Strength": 0.0})
    c = asset_coll("PP_GolfCart")
    b = B("PP_GolfCart")
    Wd = 1.12
    prof = [(-1.15, 0.26), (1.1, 0.26), (1.2, 0.38), (1.19, 0.58), (1.02, 0.74), (0.6, 0.8), (0.5, 0.8),
            (0.44, 0.38), (-0.3, 0.38), (-0.36, 0.6), (-1.15, 0.6)]
    body = _prism_xz(prof, Wd)
    bmesh.ops.translate(body, vec=(0, -Wd / 2, 0), verts=body.verts)
    try:
        bmesh.ops.bevel(body, geom=list(body.edges), offset=0.025, offset_type="OFFSET", segments=2, profile=0.5,
                        affect="EDGES", clamp_overlap=True)
    except Exception as e:
        print("cart bevel", e)
    bmesh.ops.triangulate(body, faces=[f for f in body.faces if len(f.verts) > 4])
    b.add(body, body_m, smooth=True)
    # cyan side stripe + bumpers
    for sy in (-1, 1):
        b.add(p_box(2.05, 0.01, 0.05, bevel=0.003), P["cyan"], T((-0.02, sy * (Wd / 2 + 0.004), 0.45)), smooth=False)
    b.add(p_box(0.06, Wd + 0.04, 0.1, bevel=0.02, seg=2), P["ink"], T((1.2, 0, 0.25)), smooth=False)
    b.add(p_box(0.06, Wd + 0.04, 0.1, bevel=0.02, seg=2), P["ink"], T((-1.17, 0, 0.25)), smooth=False)
    # headlights
    for sy in (-1, 1):
        b.add(p_cyl(0.05, 0.05, 0.02, 12), light_m, T((1.19, sy * 0.34, 0.52), (0, rad(80), 0)))
    # seat
    b.add(p_box(0.7, 1.0, 0.1, bevel=0.03, seg=2), seat_m, T((-0.72, 0, 0.6)), smooth=True)
    b.add(p_box(0.1, 1.0, 0.38, bevel=0.03, seg=2), seat_m, T((-1.08, 0, 0.72), (0, rad(-10), 0)), smooth=True)
    # roof struts + roof
    for sy in (-1, 1):
        b.add(p_path_tube([(0.56, sy * 0.5, 0.78), (0.4, sy * 0.5, 1.72)], 0.018, 8), P["ink"])
        b.add(p_path_tube([(-1.12, sy * 0.5, 0.6), (-1.1, sy * 0.5, 1.72)], 0.018, 8), P["ink"])
    b.add(p_box(1.95, 1.2, 0.06, bevel=0.025, seg=2), body_m, T((-0.35, 0, 1.72)), smooth=True)
    b.add(p_box(1.97, 1.22, 0.02, bevel=0.006), P["cyan"], T((-0.35, 0, 1.715)), smooth=False)
    # steering
    col_base = Vector((0.5, 0.25, 0.62))
    col_top = Vector((0.3, 0.25, 1.0))
    b.add(p_path_tube([col_base, col_top], 0.018, 8), P["ink"])
    wheel = p_tube([(0.15, -0.012), (0.17, 0.0), (0.15, 0.012)], 20, caps=False)
    b.add(wheel, P["ink"], T(col_top, (0, rad(-35), 0)))
    b.add(p_path_tube([col_top + Vector((0.0, -0.15, 0.0)), col_top + Vector((0.0, 0.15, 0.0))], 0.01, 6), P["ink"],
          Matrix.Translation(col_top) @ Matrix.Rotation(rad(-35), 4, "Y") @ Matrix.Translation(-col_top))
    # rear bag well + golf bag
    b.add(p_box(0.3, 0.8, 0.05, bevel=0.01), P["ink"], T((-1.3, 0, 0.35)), smooth=False)
    bag_M = T((-1.32, 0.15, 0.38), (0, rad(-14), 0))
    b.add(p_tube([(0.0, 0.0), (0.12, 0.0), (0.13, 0.05), (0.13, 0.75), (0.14, 0.8), (0.14, 0.86)], 16), P["cyan"], bag_M)
    b.add(p_cyl(0.143, 0.143, 0.05, 16, caps=False), P["cream"], bag_M @ T((0, 0, 0.82)))
    b.add(p_box(0.05, 0.03, 0.55, bevel=0.01), P["ink"], bag_M @ T((0, 0.13, 0.15)), smooth=False)
    for k, (dx, dy) in enumerate([(-0.05, 0.04), (0.05, -0.02), (0.0, -0.06)]):
        b.add(p_cyl(0.03, 0.03, 0.12, 10), P["rubber"], bag_M @ T((dx, dy, 0.86)))
        b.add(p_sphere(0.034, 10, 6), [P["ink"], P["cream"], P["cyan"]][k], bag_M @ T((dx, dy, 0.99)))
    # wheels
    for x in (0.78, -0.78):
        for y in (-0.6, 0.6):
            for tb, m, M in _tire(P, x, y, 0.23):
                b.add(tb, m, M)
    b.finish(c, angle=40)
    return c.name


# ------------------------------------------------------------------ 7. clubhouse
def _window(b, P, x, y, z0, w, h, side):
    """side: '-Y', '+Y', '-X', '+X' wall the window sits in; (x, y) is the point on the wall plane."""
    rotz = {"-Y": 0.0, "+Y": math.pi, "-X": -math.pi / 2, "+X": math.pi / 2}[side]
    R = T((x, y, 0), (0, 0, rotz))
    fw = 0.1
    b.add(p_box(w + 2 * fw, 0.18, fw, bevel=0.01), P["darkwood"], R @ T((0, -0.04, z0 - fw)), smooth=False)
    b.add(p_box(w + 2 * fw, 0.18, fw, bevel=0.01), P["darkwood"], R @ T((0, -0.04, z0 + h)), smooth=False)
    for sx in (-1, 1):
        b.add(p_box(fw, 0.18, h, bevel=0.01), P["darkwood"], R @ T((sx * (w + fw) / 2, -0.04, z0)), smooth=False)
    b.add(p_box(w, 0.1, h, bevel=0.0), P["glass"], R @ T((0, -0.0, z0)), smooth=False)
    b.add(p_box(0.07, 0.14, h, bevel=0.0), P["darkwood"], R @ T((0, -0.06, z0)), smooth=False)
    b.add(p_box(w, 0.14, 0.06, bevel=0.0), P["darkwood"], R @ T((0, -0.06, z0 + h * 0.62)), smooth=False)
    b.add(p_box(w + 0.34, 0.24, 0.08, bevel=0.015), P["stone"], R @ T((0, -0.09, z0 - 0.17)), smooth=False)


def _roof_slab(b, m, ridge_y, ridge_z, theta, L, length, thick, sign, axis="X", cx=0.0):
    """Sloped slab hanging from a ridge. axis='X' ridge runs along X (slopes toward +/-Y)."""
    dy, dz = -sign * math.cos(theta), -math.sin(theta)
    ny, nz = -sign * math.sin(theta), math.cos(theta)
    cy = ridge_y + dy * L / 2 + ny * thick / 2
    cz = ridge_z + dz * L / 2 + nz * thick / 2
    rot = (sign * theta, 0, 0)
    M = T((cx, cy, cz), rot)
    if axis == "Y":
        # ridge along Y: build as if along X then swap by rotating about Z
        M = T((0, 0, 0), (0, 0, math.pi / 2)) @ M
    b.add(p_box(length, L, thick, bevel=0.02, base=False), m, M, smooth=False)


def build_clubhouse():
    P = palette()
    trim = mat("TrimCream", CREAM, 0.6)
    c = asset_coll("PP_Clubhouse")
    b = B("PP_Clubhouse")
    LX, WY, base, eave, ridge = 14.0, 9.0, 0.5, 3.7, 6.7
    hy = WY / 2
    # plinth and stone wainscot
    b.add(p_box(LX + 0.5, WY + 0.5, base, bevel=0.03), P["stone"], smooth=False)
    # main mass (gable ends on +/-X): YZ profile extruded along X
    prof = [(-hy, base), (hy, base), (hy, eave), (0.0, ridge), (-hy, eave)]
    mass = _prism_xz(prof, LX)
    bmesh.ops.transform(mass, matrix=T((-LX / 2, 0, 0)) @ Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))) @
                        Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1))), verts=mass.verts)
    bmesh.ops.recalc_face_normals(mass, faces=mass.faces)
    b.add(mass, P["plaster"], smooth=False)
    b.add(p_box(LX + 0.14, WY + 0.14, 1.25, bevel=0.02), P["stone"], T((0, 0, base)), smooth=False)
    # timber frame: corner posts, band beam, intermediate posts
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.add(p_box(0.26, 0.26, eave - base - 1.25), P["darkwood"], T((sx * (LX / 2 - 0.05), sy * (hy - 0.05), base + 1.25)),
                  smooth=False)
    b.add(p_box(LX + 0.2, WY + 0.2, 0.22), P["darkwood"], T((0, 0, eave - 0.22)), smooth=False)
    b.add(p_box(LX + 0.2, WY + 0.2, 0.08), P["darkwood"], T((0, 0, base + 1.25)), smooth=False)
    for x in (-3.7, 3.7):
        b.add(p_box(0.22, 0.08, eave - base - 1.25), P["darkwood"], T((x, -hy - 0.04, base + 1.25)), smooth=False)
    for x in (-3.5, 0.0, 3.5):
        b.add(p_box(0.22, 0.08, eave - base - 1.25), P["darkwood"], T((x, hy + 0.04, base + 1.25)), smooth=False)
    # gable-end timbers (king post + collar) on both X ends
    for sx in (-1, 1):
        xg = sx * (LX / 2 + 0.04)
        b.add(p_box(0.08, 0.22, ridge - eave - 0.3), P["darkwood"], T((xg, 0, eave)), smooth=False)
        b.add(p_box(0.08, WY * 0.55, 0.18), P["darkwood"], T((xg, 0, eave + 1.2)), smooth=False)
        _window(b, P, sx * (LX / 2 + 0.07), 0.0, 1.9, 1.3, 1.3, "+X" if sx > 0 else "-X")
    # roof
    theta = math.atan2(ridge - eave, hy)
    L = math.hypot(hy, ridge - eave) + 0.85
    for sgn in (1, -1):
        _roof_slab(b, P["roof"], 0.0, ridge + 0.02, theta, L, LX + 1.2, 0.24, sgn)
    b.add(p_box(LX + 1.25, 0.34, 0.16, bevel=0.03), P["darkwood"], T((0, 0, ridge + 0.14)), smooth=False)
    # front windows (-Y) and back windows (+Y)
    for x in (-5.4, 5.4):
        _window(b, P, x, -hy - 0.07, 1.9, 2.0, 1.45, "-Y")
    for x in (-5.2, -1.75, 1.75, 5.2):
        _window(b, P, x, hy + 0.07, 1.9, 1.8, 1.45, "+Y")
    # front door
    b.add(p_box(2.1, 0.2, 2.75, bevel=0.02), P["darkwood"], T((0, -hy - 0.02, base)), smooth=False)
    b.add(p_box(0.85, 0.08, 2.4), P["wood"], T((-0.46, -hy - 0.1, base + 0.05)), smooth=False)
    b.add(p_box(0.85, 0.08, 2.4), P["wood"], T((0.46, -hy - 0.1, base + 0.05)), smooth=False)
    for sx in (-1, 1):
        b.add(p_box(0.5, 0.1, 1.2), P["glass"], T((sx * 0.46, -hy - 0.11, base + 1.0)), smooth=False)
        b.add(p_box(0.04, 0.06, 0.3), P["chrome"], T((sx * 0.1, -hy - 0.16, base + 1.05)), smooth=False)
    # porch: deck, columns, beam, cross-gable roof
    py0, py1, pw = -hy, -hy - 2.6, 6.4
    b.add(p_box(pw + 0.4, 2.7, base, bevel=0.03), P["stone"], T((0, (py0 + py1) / 2 - 0.05, 0)), smooth=False)
    b.add(p_box(2.4, 0.5, 0.18, bevel=0.02), P["stone"], T((0, py1 - 0.45, 0)), smooth=False)
    pe = 3.45
    for x in (-pw / 2 + 0.3, -1.0, 1.0, pw / 2 - 0.3):
        b.add(p_box(0.5, 0.5, 0.7, bevel=0.03), P["stone"], T((x, py1 + 0.3, base)), smooth=False)
        b.add(p_box(0.26, 0.26, pe - base - 0.7), P["darkwood"], T((x, py1 + 0.3, base + 0.7)), smooth=False)
    b.add(p_box(pw + 0.2, 0.3, 0.3), P["darkwood"], T((0, py1 + 0.3, pe - 0.02)), smooth=False)
    pr = pe + 2.1
    tri = _prism_xz([(-pw / 2 - 0.1, pe + 0.25), (pw / 2 + 0.1, pe + 0.25), (0, pr)], 4.2)
    bmesh.ops.translate(tri, vec=(0, py1 + 0.15, 0), verts=tri.verts)
    b.add(tri, P["plaster"], smooth=False)
    b.add(p_box(0.12, 0.08, pr - pe - 0.6), P["darkwood"], T((0, py1 + 0.1, pe + 0.3)), smooth=False)
    ptheta = math.atan2(pr - pe, pw / 2)
    pL = math.hypot(pw / 2, pr - pe) + 0.7
    for sgn in (1, -1):
        # ridge along Y: slabs slope toward +/-X
        dx, dz = sgn * math.cos(ptheta), -math.sin(ptheta)
        nx, nz = sgn * math.sin(ptheta), math.cos(ptheta)
        cx = dx * pL / 2 + nx * 0.11
        cz = pr + 0.3 + dz * pL / 2 + nz * 0.11
        b.add(p_box(pL, 6.4, 0.22, bevel=0.02, base=False), P["roof"],
              T((cx, py1 - 0.55 + 3.2, cz), (0, sgn * ptheta, 0)), smooth=False)
    b.add(p_box(0.3, 6.4, 0.14, bevel=0.03), P["darkwood"], T((0, py1 - 0.55 + 3.2, pr + 0.36)), smooth=False)
    # brand sign on porch gable
    b.add(p_box(2.9, 0.08, 0.62, bevel=0.02), P["cyan"], T((0, py1 + 0.08, pe + 0.55)), smooth=False)
    R = Matrix.Rotation(rad(90), 4, "X")
    b.add(text_bm("CLUBHOUSE", 0.36, 0.02), P["cream"], T((0, py1 + 0.02, pe + 0.86)) @ R, smooth=False)
    # chimney
    b.add(p_box(1.1, 0.9, ridge + 1.6 - base, bevel=0.03), P["stone"], T((4.4, 1.9, base)), smooth=False)
    b.add(p_box(1.3, 1.1, 0.14, bevel=0.03), P["roof"], T((4.4, 1.9, ridge + 1.6)), smooth=False)
    # planter boxes either side of steps
    for sx in (-1, 1):
        b.add(p_box(0.9, 0.5, 0.45, bevel=0.03), P["stone"], T((sx * 2.1, py1 - 0.45, 0)), smooth=False)
        b.add(_lump(0.42, 3.0 + sx, 3, amp=0.18, freq=4.5, squash=(1.1, 0.7, 0.8)), P["leaf_shrub"],
              T((sx * 2.1, py1 - 0.45, 0.62)))
    b.finish(c, angle=35)
    return c.name


# ------------------------------------------------------------------ 8. golfer 'Caleb'
def _ell_lathe(profile, sy=0.65, seg=16, caps=True, sx=1.0):
    tb = p_tube(profile, seg, caps)
    bmesh.ops.scale(tb, vec=(sx, sy, 1.0), verts=tb.verts)
    return tb


def _limb(pts, radii, seg=12):
    return p_path_tube(pts, radii[0], seg, caps=True, radii=radii)


def build_caleb():
    P = palette()
    skin = mat("Skin", "#D9A47C", 0.55)
    polo = mat("PoloBlue", "#2D6CB5", 0.7)
    khaki = mat("Khaki", "#C8B48A", 0.8)
    capw = mat("CapWhite", "#F5F5F2", 0.6)
    hair = mat("HairBrown", "#4A3324", 0.7)
    shoe = mat("ShoeWhite", "#F2F2EE", 0.45)
    belt = mat("Belt", "#2A2320", 0.5)
    c = asset_coll("PP_Caleb")
    b = B("PP_Caleb")
    # shoes
    for sx in (-1, 1):
        x = sx * 0.11
        s = p_sphere(1.0, 16, 8)
        bmesh.ops.scale(s, vec=(0.052, 0.135, 0.06), verts=s.verts)
        bmesh.ops.translate(s, vec=(x, -0.035, 0.045), verts=s.verts)
        _clamp_floor(s, 0.012)
        b.add(s, shoe)
        b.add(p_cyl(1.0, 1.0, 0.014, 16), P["ink"], T((x, -0.035, 0.0), (0, 0, 0), (0.047, 0.12, 1.0)))
        b.add(p_box(0.012, 0.06, 0.02, bevel=0.004), P["cyan"], T((x + sx * 0.048, 0.0, 0.035)), smooth=False)
    # trousers (khaki) legs
    for sx in (-1, 1):
        pts = [(sx * 0.115, 0.0, 0.07), (sx * 0.112, 0.0, 0.3), (sx * 0.106, -0.01, 0.52), (sx * 0.1, 0.0, 0.75),
               (sx * 0.095, 0.0, 0.95)]
        b.add(_limb(pts, [0.058, 0.057, 0.064, 0.08, 0.095], 14), khaki)
    # pelvis / seat of trousers
    b.add(_ell_lathe([(0.01, 0.755), (0.08, 0.77), (0.14, 0.81), (0.175, 0.88), (0.18, 0.98), (0.172, 1.03)],
                     sy=0.66, seg=18), khaki)
    b.add(_ell_lathe([(0.176, 0.985), (0.178, 1.02)], sy=0.67, seg=18, caps=False), belt)
    b.add(p_box(0.05, 0.012, 0.035, bevel=0.004), P["chrome"], T((0, -0.12, 0.985)), smooth=False)
    # polo torso
    torso = [(0.17, 1.0), (0.166, 1.08), (0.172, 1.2), (0.188, 1.32), (0.195, 1.4), (0.17, 1.46), (0.1, 1.5),
             (0.06, 1.51)]
    b.add(_ell_lathe(torso, sy=0.62, seg=20), polo)
    # collar + placket + logo
    b.add(_ell_lathe([(0.072, 1.48), (0.078, 1.53), (0.07, 1.545)], sy=0.9, seg=16, caps=False), P["white"])
    b.add(p_box(0.03, 0.01, 0.1, bevel=0.003), polo, T((0, -0.105, 1.38), (rad(-8), 0, 0)), smooth=False)
    for k in range(2):
        b.add(p_cyl(0.005, 0.005, 0.004, 8), P["white"], T((0, -0.113, 1.41 + k * 0.04), (rad(90), 0, 0)))
    b.add(p_box(0.045, 0.006, 0.03, bevel=0.003), P["cyan"], T((-0.09, -0.113, 1.36), (rad(-10), 0, 0)), smooth=False)
    # neck + head
    b.add(p_cyl(0.05, 0.048, 0.12, 12), skin, T((0, 0.005, 1.48)))
    head = p_sphere(1.0, 20, 12)
    bmesh.ops.scale(head, vec=(0.087, 0.1, 0.112), verts=head.verts)
    for v in head.verts:  # jaw taper
        if v.co.z < 0:
            f = 1.0 + v.co.z / 0.112 * 0.25
            v.co.x *= f
    bmesh.ops.translate(head, vec=(0, -0.01, 1.685), verts=head.verts)
    b.add(head, skin)
    b.add(p_sphere(0.017, 10, 6), skin, T((0, -0.112, 1.675), (0, 0, 0), (0.9, 1.0, 1.2)))
    for sx in (-1, 1):
        b.add(p_sphere(0.024, 10, 6), skin, T((sx * 0.087, 0.0, 1.68), (0, 0, 0), (0.45, 0.8, 1.0)))
        b.add(p_sphere(0.011, 10, 6), P["ink"], T((sx * 0.033, -0.098, 1.7), (0, 0, 0), (1.0, 0.6, 1.1)))
        b.add(p_box(0.03, 0.01, 0.007, bevel=0.002), hair, T((sx * 0.034, -0.1, 1.722), (0, 0, sx * rad(-6))), smooth=False)
    b.add(p_box(0.03, 0.008, 0.005, bevel=0.002), mat("Lip", "#A8674E", 0.6), T((0, -0.103, 1.628)), smooth=False)
    # hair (sides/back under the cap)
    hr = p_sphere(1.0, 18, 10)
    bmesh.ops.scale(hr, vec=(0.093, 0.104, 0.1), verts=hr.verts)
    bmesh.ops.translate(hr, vec=(0, 0.008, 1.705), verts=hr.verts)
    bmesh.ops.delete(hr, geom=[v for v in hr.verts if v.co.y < -0.04 or v.co.z < 1.64], context="VERTS")
    b.add(hr, hair)
    # cap: crown + brim + button
    cap = p_sphere(1.0, 20, 10)
    bmesh.ops.delete(cap, geom=[v for v in cap.verts if v.co.z < -0.05], context="VERTS")
    bmesh.ops.scale(cap, vec=(0.097, 0.108, 0.085), verts=cap.verts)
    bmesh.ops.translate(cap, vec=(0, -0.004, 1.728), verts=cap.verts)
    b.add(cap, capw)
    brim = p_sphere(1.0, 20, 6)
    bmesh.ops.scale(brim, vec=(0.09, 0.095, 0.008), verts=brim.verts)
    bmesh.ops.translate(brim, vec=(0, -0.1, 1.73), verts=brim.verts)
    b.add(brim, capw, Matrix.Translation((0, -0.06, 1.73)) @ Matrix.Rotation(rad(10), 4, "X") @
          Matrix.Translation((0, 0.06, -1.73)))
    b.add(p_sphere(0.011, 8, 4), P["cyan"], T((0, -0.004, 1.81)))
    b.add(p_box(0.05, 0.006, 0.028, bevel=0.003), P["cyan"], T((0, -0.1, 1.75), (rad(-25), 0, 0)), smooth=False)
    # arms (A-pose ~40 deg from vertical)
    for sx in (-1, 1):
        sh = Vector((sx * 0.175, 0.005, 1.43))
        d = Vector((sx * math.sin(rad(40)), 0.0, -math.cos(rad(40))))
        elbow = sh + d * 0.3 + Vector((0, -0.01, 0))
        wrist = elbow + (d + Vector((0, -0.08, 0))).normalized() * 0.26
        sleeve_end = sh + d * 0.17
        b.add(_limb([sh - d * 0.06, sh + d * 0.06, sleeve_end], [0.06, 0.068, 0.063], 14), polo)
        b.add(_limb([sh + d * 0.1, elbow, wrist], [0.046, 0.04, 0.031], 12), skin)
        hand_m = P["white"] if sx < 0 else skin  # golf glove on the left hand
        hand = p_sphere(1.0, 12, 8)
        bmesh.ops.scale(hand, vec=(0.028, 0.045, 0.075), verts=hand.verts)
        rot = d.to_track_quat("-Z", "Y").to_matrix().to_4x4()
        b.add(hand, hand_m, Matrix.Translation(wrist + d * 0.07) @ rot)
        th = p_sphere(1.0, 8, 6)
        bmesh.ops.scale(th, vec=(0.013, 0.013, 0.035), verts=th.verts)
        b.add(th, hand_m, Matrix.Translation(wrist + d * 0.04 + Vector((0, -0.035, 0))) @ rot @ Matrix.Rotation(rad(25), 4, "X"))
    b.finish(c, angle=60)
    return c.name
