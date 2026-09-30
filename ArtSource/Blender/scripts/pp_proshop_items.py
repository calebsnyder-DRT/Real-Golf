"""Pro Shop display items built from the real golfer clothing meshes, plus display clubs.

Every item faces -Y. Hanging tops: origin at the outer top of the hanger hook. Folded stacks, shoes, socks, belt:
origin at the bottom centre. Mannequin head: origin at the neck base (= golfer Neck bone head). Clubs: origin at
the bottom centre of the head.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

from pp_lib import T, _tb, p_box, p_cyl, p_path_tube, p_sphere, p_tube, rad
from pp_proshop import *  # noqa: F401,F403
import pp_proshop as P
from pp_proshop_pipeline import bake, pose_arms_down, pose_rest


def add_split(b, tb, pick, M=None):
    """Adds a bmesh whose faces use several materials; pick(face) -> material."""
    tb.faces.ensure_lookup_table()
    groups = {}
    for f in tb.faces:
        groups.setdefault(pick(f), []).append(f.index)
    for m, idxs in groups.items():
        t = tb.copy()
        t.faces.ensure_lookup_table()
        keep = set(idxs)
        bmesh.ops.delete(t, geom=[f for f in t.faces if f.index not in keep], context="FACES")
        b.add(t, m, M.copy() if M is not None else None)
    tb.free()


def mesh_to_mb(b, me, mats, M=None):
    off = len(b.co)
    for v in me.vertices:
        b.co.append(tuple(M @ v.co) if M is not None else tuple(v.co))
    for p in me.polygons:
        b.faces.append(tuple(off + i for i in p.vertices))
        b.fm.append(b._mi(mats[p.material_index]))
        b.fs.append(p.use_smooth)


def baked_world(src, name, coll):
    """Evaluated (posed) copy of src with its world transform applied."""
    src.hide_viewport = False
    bpy.context.view_layer.update()
    o = bake(src, name, coll)
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    return o


def drop(o):
    me = o.data
    bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.meshes.remove(me)


# ---------------------------------------------------------------- clothing
def hanging_garment(src, prefix, coll, depth_k=0.36):
    """Posed top flattened onto a hanger. Origin = outer top of the hanger; the rail centre sits 0.020 below it."""
    work = baked_world(src, prefix + "_Bake", coll)
    me = work.data
    zs = [v.co.z for v in me.vertices]
    zmax = max(zs)
    ys = [v.co.y for v in me.vertices if 1.0 < v.co.z < 1.4]
    yc = (min(ys) + max(ys)) / 2
    for v in me.vertices:
        v.co.y = yc + (v.co.y - yc) * depth_k
    body_mi = next((i for i, s in enumerate(work.material_slots)
                    if s.material and s.material.name == "Mat_Top_Blue"), 0)
    body_v = {i for p in me.polygons if p.material_index == body_mi for i in p.vertices}
    xs = [-0.17 + i * 0.0425 for i in range(9)]
    top = []
    for x in xs:
        near = [me.vertices[i].co.z for i in body_v if abs(me.vertices[i].co.x - x) < 0.02]
        top.append(max(near) if near else zmax - 0.08)
    zb = [t - 0.03 for t in top]
    zb = [zb[0]] + [min(zb[i], (zb[i - 1] + zb[i] + zb[i + 1]) / 3) for i in range(1, 8)] + [zb[8]]
    width = max(v.co.x for v in me.vertices) - min(v.co.x for v in me.vertices)
    g = MB(prefix + "_Garment")
    mesh_to_mb(g, me, [s.material for s in work.material_slots])
    drop(work)
    h = MB(prefix + "_Hanger")
    tb = p_path_tube([Vector((x, yc, z)) for x, z in zip(xs, zb)], 0.012, 8)
    bmesh.ops.scale(tb, vec=(1.0, 0.5, 1.0), space=Matrix.Translation((0, -yc, 0)), verts=tb.verts)
    h.add(tb, bpy.data.materials["PS_Hanger"])
    z_top = zmax + 0.055
    Ch = bpy.data.materials["PS_Chrome"]
    h.add(rod((0, yc, zb[4]), (0, yc, z_top), 0.003, 8), Ch)
    arc = [Vector((0.0, yc + 0.019 - 0.019 * math.cos(t), z_top + 0.019 * math.sin(t)))
           for t in [math.pi * k / 10 for k in range(11)]]
    arc.append(Vector((0.0, yc + 0.036, z_top - 0.012)))
    h.add(p_path_tube(arc, 0.003, 8), Ch)
    origin = (0.0, yc + 0.019, z_top + 0.022)
    objs = [g.finish(coll, angle=40, origin=origin), h.finish(coll, angle=40, origin=origin)]
    for o in objs:
        o.location = (0, 0, 0)
    MANIFEST.setdefault("items", {})[prefix] = dict(hem_below_origin=round(origin[2] - min(zs), 4),
                                                    width=round(width, 4))
    return objs


def display_shoes(src, coll):
    work = baked_world(src, "Shoes_Bake", coll)
    mats = [s.material for s in work.material_slots]
    bm = bmesh.new()
    bm.from_mesh(work.data)
    drop(work)
    sides = {1: [v for v in bm.verts if v.co.x > 0], -1: [v for v in bm.verts if v.co.x <= 0]}
    for s, vs in sides.items():
        cx = sum(v.co.x for v in vs) / len(vs)
        cy = sum(v.co.y for v in vs) / len(vs)
        R = Matrix.Translation((cx, cy, 0)) @ Matrix.Rotation(rad(3.5) * s, 4, "Z") @ Matrix.Translation((-cx, -cy, 0))
        bmesh.ops.transform(bm, matrix=R, verts=vs)
    for s, vs in sides.items():
        inner = min(v.co.x for v in vs) if s > 0 else max(v.co.x for v in vs)
        dx = s * 0.0045 - inner
        for v in vs:
            v.co.x += dx
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    bmesh.ops.translate(bm, vec=-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)), verts=bm.verts)
    me2 = bpy.data.meshes.new("Display_Shoes_Pair_Bake")
    bm.to_mesh(me2)
    bm.free()
    b = MB("Display_Shoes_Pair")
    mesh_to_mb(b, me2, mats)
    bpy.data.meshes.remove(me2)
    return [b.finish(coll, angle=40)]


def folded_stack(name, mat, width, depth, lh, n=3, seed=1):
    """Stack of folded trousers/shorts: fold roll at the front, waistband with loops + button on alternating sides,
    centre seam and a back-pocket patch on each layer."""
    b = MB(name)
    rnd = random.Random(seed)
    z = 0.0
    for k in range(n):
        Mk = T((rnd.uniform(-0.008, 0.008), rnd.uniform(-0.006, 0.006), z), (0, 0, rad(rnd.uniform(-2.0, 2.0))))
        w, d = width * (1.0 - 0.02 * k), depth
        b.add(bx(-w / 2, w / 2, -d / 2 + lh / 2, d / 2, 0.0, lh, lh * 0.3, 2), mat, Mk)
        b.add(p_cyl(lh / 2, lh / 2, w - 0.004, 12), mat, Mk @ T((-w / 2 + 0.002, -d / 2 + lh / 2, lh / 2), (0, rad(90), 0)))
        side = 1 if k % 2 == 0 else -1
        xw = side * (w / 2 - 0.018)
        b.add(bx(xw - 0.02, xw + 0.02, -d / 2 + 0.01, d / 2 + 0.004, lh * 0.08, lh * 1.04, 0.004, 1), mat, Mk)
        for yl in (-d * 0.25, d * 0.2):
            b.add(bx(xw - 0.024, xw + 0.024, yl - 0.006, yl + 0.006, lh * 0.2, lh * 1.08, 0.002), mat, Mk)
        b.add(p_cyl(0.0065, 0.0065, 0.004, 10), mat, Mk @ T((xw, -d / 2 + 0.03, lh * 1.04)))
        b.add(bx(-w / 2 + 0.03, w / 2 - 0.03, -0.0015, 0.0015, lh - 0.001, lh + 0.0015), mat, Mk)
        px = -side * w * 0.2
        b.add(bx(px - 0.055, px + 0.055, d * 0.05, d * 0.36, lh - 0.001, lh + 0.003, 0.0015), mat, Mk)
        b.add(bx(px - 0.05, px + 0.05, d * 0.3, d * 0.31, lh + 0.003, lh + 0.0045), mat, Mk)
        z += lh * 0.97
    return b


def socks_pair(mat, coll):
    """Two crew socks standing side-on (L silhouettes toward the camera), mirrored so the feet cross in the middle."""
    b = MB("Display_Socks_Pair")
    pts = [(0.0, 0.0), (0.12, 0.0), (0.145, 0.012), (0.15, 0.03), (0.14, 0.05), (0.07, 0.058), (0.064, 0.19),
           (0.0, 0.19), (-0.008, 0.06), (-0.004, 0.02)]
    th = 0.022
    for M in (T((-0.075, -0.013, 0.0)), T((0.075, 0.013, 0.0), (0, 0, math.pi))):
        tb = _tb()
        fr = [tb.verts.new((x, -th / 2, z)) for x, z in pts]
        bk = [tb.verts.new((x, th / 2, z)) for x, z in pts]
        tb.faces.new(fr)
        tb.faces.new(list(reversed(bk)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            tb.faces.new((fr[i], fr[j], bk[j], bk[i]))
        bmesh.ops.recalc_face_normals(tb, faces=tb.faces)
        bmesh.ops.bevel(tb, geom=list(tb.edges) + list(tb.verts), offset=0.006, offset_type="OFFSET", segments=2,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
        b.add(tb, mat, M.copy())
        b.add(bx(-0.004, 0.068, -0.0125, 0.0125, 0.165, 0.195, 0.004, 2), mat, M.copy())
        for k in range(4):
            x = 0.008 + k * 0.016
            b.add(bx(x - 0.002, x + 0.002, -0.0128, -0.011, 0.07, 0.162), mat, M.copy())
    return [b.finish(coll, angle=35)]


def belt_coil(coll):
    b = MB("Display_Belt_Coiled")
    L, Bk = bpy.data.materials["Mat_Belt_Brown"], bpy.data.materials["Mat_Buckle_Silver"]
    turns, r0, pitch, w, th = 2.35, 0.04, 0.0068, 0.034, 0.0045
    tb = _tb()
    n = int(turns * 40)
    inner, outer = [], []
    for i in range(n + 1):
        t = i / n * turns * 2 * math.pi
        r = r0 + pitch * t / (2 * math.pi)
        c, s = math.cos(t), math.sin(t)
        inner.append([tb.verts.new((r * c, r * s, 0.0)), tb.verts.new((r * c, r * s, w))])
        outer.append([tb.verts.new(((r + th) * c, (r + th) * s, 0.0)), tb.verts.new(((r + th) * c, (r + th) * s, w))])
    for a in range(n):
        bb = a + 1
        tb.faces.new((outer[a][0], outer[bb][0], outer[bb][1], outer[a][1]))
        tb.faces.new((inner[a][1], inner[bb][1], inner[bb][0], inner[a][0]))
        tb.faces.new((inner[a][1], outer[a][1], outer[bb][1], inner[bb][1]))
        tb.faces.new((inner[a][0], inner[bb][0], outer[bb][0], outer[a][0]))
    tb.faces.new((inner[0][0], outer[0][0], outer[0][1], inner[0][1]))
    tb.faces.new((inner[n][1], outer[n][1], outer[n][0], inner[n][0]))
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces)
    b.add(tb, L)
    t_end = turns * 2 * math.pi
    r_end = r0 + pitch * turns
    pe = Vector((r_end * math.cos(t_end), r_end * math.sin(t_end), 0))
    tan = Vector((-math.sin(t_end), math.cos(t_end), 0))
    nrm = Vector((math.cos(t_end), math.sin(t_end), 0))
    Mb = Matrix(((tan.x, nrm.x, 0, pe.x), (tan.y, nrm.y, 0, pe.y), (0, 0, 1, 0), (0, 0, 0, 1)))
    yb = th + 0.004
    frame = [(0.0, -0.004), (0.032, -0.004), (0.036, 0.0), (0.036, w), (0.032, w + 0.004), (0.0, w + 0.004),
             (0.0, -0.004)]
    for (x0, z0), (x1, z1) in zip(frame, frame[1:]):
        b.add(rod((x0, yb, z0), (x1, yb, z1), 0.0028, 8), Bk, Mb.copy())
    b.add(rod((0.0, yb, w / 2), (0.03, yb + 0.002, w / 2), 0.0018, 6), Bk, Mb.copy())
    ob = b.finish(coll, angle=35)
    lo, hi = world_bbox([ob])
    ob.data.transform(Matrix.Translation(-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))))
    return [ob]


def mannequin_head(rig, head_ob, coll):
    neck = rig.data.bones["Neck"].head_local
    co = [head_ob.matrix_world @ v.co for v in head_ob.data.vertices]
    wx = (max(c.x for c in co) - min(c.x for c in co)) * 0.9
    wy = (max(c.y for c in co) - min(c.y for c in co)) * 0.9
    hh = max(c.z for c in co) - neck.z
    prof = [(0.062, 0.0), (0.058, 0.012), (0.05, 0.03), (0.048, 0.1), (0.05, 0.13), (0.07, 0.16), (0.088, 0.19),
            (0.097, 0.23), (0.098, 0.27), (0.092, 0.305), (0.078, 0.33), (0.056, 0.35), (0.028, 0.362), (0.0, 0.366)]
    tb = p_tube(prof, 28)
    bmesh.ops.scale(tb, vec=((wx / 2) / 0.098, (wy / 2) / 0.098, hh / 0.366), verts=tb.verts)
    for v in tb.verts:
        if v.co.z > hh * 0.35:
            v.co.y -= 0.012 * math.sin((v.co.z - hh * 0.35) / (hh * 0.65) * math.pi)
    b = MB("Display_MannequinHead")
    b.add(tb, bpy.data.materials["PS_Mannequin"])
    ob = b.finish(coll, angle=40)
    MANIFEST.setdefault("items", {})["MannequinHead"] = dict(
        neck_base=[round(neck.x, 4), round(neck.y, 4), round(neck.z, 4)], bone="Neck", height=round(hh, 4))
    return [ob]


# ---------------------------------------------------------------- clubs
def _prism_xz(pts, depth):
    bm = _tb()
    front = [bm.verts.new((x, 0.0, z)) for x, z in pts]
    back = [bm.verts.new((x, depth, z)) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def wood_head(b, CM, s=1.0, sz=1.0, seg=(24, 12)):
    tb = p_sphere(1.0, *seg)
    for v in tb.verts:
        x, y, z = v.co
        v.co = Vector((s * (0.062 + x * 0.058), s * (0.05 + y * 0.056), sz * (0.032 + z * 0.031)))
        if v.co.y < -0.002:
            v.co.y = -0.002 + (v.co.y + 0.002) * 0.08
        if v.co.z < 0.004:
            v.co.z = 0.004 + (v.co.z - 0.004) * 0.15
    tb.normal_update()
    add_split(b, tb, lambda f: CM["Club_Head"] if f.normal.z > 0.35 and f.normal.y > -0.8 else CM["Club_Steel"])
    b.add(p_cyl(0.004 * s, 0.004 * s, 0.0015, 10), CM["Club_Accent"], T((0.062 * s, 0.02 * s, sz * 0.0632)))
    return Vector((0.012 * s, 0.01 * s, 0.022 * sz))


def iron_head(b, CM, loft, sole_w=0.02, toe_h=0.048, length=0.078, grooves=6):
    pts = [(0.0, 0.006), (0.006, 0.0), (length - 0.012, 0.0), (length - 0.002, 0.008), (length + 0.002, toe_h * 0.55),
           (length - 0.006, toe_h * 0.92), (length - 0.02, toe_h), (0.01, toe_h * 0.62), (0.0, toe_h * 0.5)]
    tb = _prism_xz(pts, sole_w)
    for v in tb.verts:
        t = min(max(v.co.z / toe_h, 0), 1)
        v.co.y *= (1.0 - 0.62 * t)
    R = Matrix.Rotation(-rad(loft), 4, "X")
    b.add(tb, CM["Club_Steel"], R.copy(), smooth=False)
    for k in range(grooves):
        b.add(bx(length * 0.19, length * 0.81, -0.0009, 0.0003, 0.0075 + k * 0.0055, 0.0086 + k * 0.0055),
              CM["Club_Head"], R.copy(), smooth=False)
    if grooves:
        b.add(bx(length * 0.32, length * 0.72, sole_w * 0.72, sole_w * 0.72 + 0.002, 0.0095, 0.0185),
              CM["Club_Accent"], R.copy(), smooth=False)
    return Vector((0.004, 0.006, 0.012))


def shaft_grip(b, CM, start, lie, length, r_tip=0.0045, r_butt=0.0075, seg=10, grip=True):
    d = Vector((-math.cos(rad(lie)), 0, math.sin(rad(lie))))
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    gl = 0.27
    sl = length - gl if grip else length
    b.add(p_tube([(r_tip, 0.0), (r_tip * 1.05, sl * 0.35), (r_butt, sl * 0.8), (r_butt, sl)], seg, caps=not grip),
          CM["Club_Shaft"], Matrix.Translation(start) @ rot)
    if grip:
        b.add(p_cyl(r_butt + 0.0012, r_butt + 0.0012, 0.018, seg), CM["Club_Accent"],
              Matrix.Translation(start + d * (sl - 0.03)) @ rot)
        b.add(p_tube([(0.0102, 0.0), (0.0112, 0.02), (0.0126, gl * 0.7), (0.0132, gl - 0.012), (0.0115, gl),
                      (0.0, gl + 0.002)], seg + 2), CM["Club_Grip"], Matrix.Translation(start + d * sl) @ rot)
        b.add(p_cyl(0.0114, 0.0114, 0.02, seg + 2), CM["Club_Accent"], Matrix.Translation(start + d * (sl + 0.012)) @ rot)
    return d


CLUB_SPECS = {  # kind: (head type, loft, lie, length, extra)
    "Driver": ("wood", 10, 58, 1.15, dict(s=1.0, sz=1.0)),
    "Wood": ("wood", 15, 58, 1.07, dict(s=0.8, sz=0.78)),
    "Iron": ("iron", 33, 62, 0.94, dict(toe_h=0.048, sole_w=0.02)),
    "Wedge": ("iron", 56, 64, 0.90, dict(toe_h=0.056, sole_w=0.024)),
    "Putter": ("putter", 3, 70, 0.88, {}),
}


def club_mb(kind, CM, name, lod=0, shaft_len=None):
    """Club with the head at the origin, face toward -Y, shaft leaning toward -X at its lie angle.
    lod 1 = in-bag version (lower segments, no grip, shaft cut to shaft_len). Returns (mb, head vertex count, d)."""
    head, loft, lie, length, ex = CLUB_SPECS[kind]
    b = MB(name)
    seg = 10 if lod == 0 else 6
    grip = lod == 0
    d = Vector((-math.cos(rad(lie)), 0, math.sin(rad(lie))))
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    if head == "wood":
        hosel = wood_head(b, CM, ex["s"], ex["sz"], (24, 12) if lod == 0 else (12, 7))
        nh = b.nverts()
        b.add(p_tube([(0.0085, -0.02), (0.0075, 0.03), (0.0068, 0.05)], seg + 2), CM["Club_Head"],
              Matrix.Translation(hosel) @ rot)
        shaft_grip(b, CM, hosel + d * 0.05, lie, shaft_len or length - 0.07, seg=seg, grip=grip)
    elif head == "iron":
        hosel = iron_head(b, CM, loft, ex["sole_w"], ex["toe_h"], grooves=6 if lod == 0 else 0)
        nh = b.nverts()
        b.add(p_tube([(0.0078, -0.012), (0.0072, 0.03), (0.0062, 0.065)], seg + 2), CM["Club_Steel"],
              Matrix.Translation(hosel) @ rot)
        b.add(p_cyl(0.0068, 0.0064, 0.012, seg), CM["Club_Grip"], Matrix.Translation(hosel + d * 0.062) @ rot)
        shaft_grip(b, CM, hosel + d * 0.065, lie, shaft_len or length - 0.075, 0.0045, 0.0072, seg=seg, grip=grip)
    else:
        b.add(p_box(0.105, 0.026, 0.022, bevel=0.003, seg=2 if lod == 0 else 1), CM["Club_Head"], T((0.052, 0.013, 0.0)),
              smooth=False)
        b.add(p_box(0.08, 0.0012, 0.012, base=False), CM["Club_Steel"], T((0.052, -0.0004, 0.011)), smooth=False)
        b.add(p_box(0.003, 0.012, 0.0008, base=False), CM["Club_Accent"], T((0.052, 0.012, 0.0224)), smooth=False)
        nh = b.nverts()
        neck = [Vector((0.014, 0.012, 0.02)), Vector((0.014, 0.012, 0.045)), Vector((0.006, 0.006, 0.055)),
                Vector((0.0, 0.004, 0.062))]
        b.add(p_path_tube(neck, 0.0055, seg), CM["Club_Steel"])
        b.add(p_cyl(0.0066, 0.0062, 0.012, seg), CM["Club_Grip"], Matrix.Translation(neck[-1]) @ rot)
        shaft_grip(b, CM, neck[-1], lie, shaft_len or length - 0.065, 0.0055, 0.0072, seg=seg, grip=grip)
    return b, nh, d


def build_display_club(kind, CM, coll):
    b, nh, _ = club_mb(kind, CM, "Display_Club_" + kind)
    hv = b.co[:nh]
    lo = [min(c[i] for c in hv) for i in range(3)]
    hi = [max(c[i] for c in hv) for i in range(3)]
    origin = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2])
    ob = b.finish(coll, angle=40, origin=origin, order=[CM[n] for n in CLUB_MATS])
    ob.location = (0, 0, 0)
    return [ob]


# ---------------------------------------------------------------- assembly
def build_items(M, gcoll, rig):
    IC = new_coll("PS_Items")
    SRC = new_coll("PS_Preview_Src")
    parts = {o.name: o for o in gcoll.all_objects}
    items = {}
    pose_arms_down(rig, 80.0)
    items["Display_Polo_Hanger"] = hanging_garment(parts["Top_Polo"], "Polo", IC)
    items["Display_Tee_Hanger"] = hanging_garment(parts["Top_Tee"], "Tee", IC)
    P.PREVIEW["mannequin_body"] = [baked_world(parts[n], "PVM_" + n, SRC)
                                   for n in ("Body_Arms", "Top_Polo", "Bottom_Chinos", "Belt", "Shoes_Golf")]
    pose_rest(rig)
    for n in ("Top_Tee", "Bottom_Shorts", "Socks_Ankle", "Socks_Crew"):
        if n in parts:
            parts[n].hide_viewport = True
    items["Display_Shoes_Pair"] = display_shoes(parts["Shoes_Golf"], IC)
    khaki = bpy.data.materials["Mat_Bottom_Khaki"]
    items["Display_Chinos_Folded"] = [folded_stack("Display_Chinos_Folded", khaki, 0.34, 0.30, 0.042, 3, 1).finish(
        IC, angle=35)]
    items["Display_Shorts_Folded"] = [folded_stack("Display_Shorts_Folded", khaki, 0.31, 0.25, 0.038, 3, 2).finish(
        IC, angle=35)]
    items["Display_Socks_Pair"] = socks_pair(bpy.data.materials["Mat_Socks_White"], IC)
    items["Display_Belt_Coiled"] = belt_coil(IC)
    items["Display_MannequinHead"] = mannequin_head(rig, parts["Head_Caleb"], IC)
    for kind in CLUB_SPECS:
        items["Display_Club_" + kind] = build_display_club(kind, M, IC)
    for o in SRC.objects:
        o.hide_render = True
        o.hide_set(True)
    return items
