"""Puttshack Pro - stylized course props for the Pebble Beach course: golf cart, course furniture and vegetation.

Run headless:
    Blender -b --factory-startup --python pp_course_props.py -- build    # build, save .blend, export FBX
    Blender -b --factory-startup --python pp_course_props.py -- render   # review renders (reads the saved .blend)
    Blender -b --factory-startup --python pp_course_props.py -- verify   # re-import GolfCart.fbx and print transforms

Every asset is authored in Blender space with its front toward +Y (Unity +Z), +X right, +Z up, metres, base at the
origin. The FBX settings match pp_pebble (axis_forward -Z, axis_up Y, bake_space_transform), so Unity imports
Blender (x, y, z) as Unity (x, z, y).

Foliage convention (shared with pp_pebble): leaf geometry uses 'PB_Foliage' with vertex colours R = occlusion,
G = tip tint; Unity swaps any 'PB_Foliage*' material for the stylized foliage shader. Trunks use 'PB_Bark'.
"""
import json
import math
import os
import random
import shutil
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = "/Users/calebsnyder/Real-Golf"
sys.path.insert(0, os.path.join(ROOT, "ArtSource/Blender/scripts"))
import pp_pebble as PB  # noqa: E402

BLEND = os.path.join(ROOT, "ArtSource/Blender/PuttshackPro_CourseProps.blend")
EXPORT_DIR = os.path.join(ROOT, "ArtSource/Exports/Course")
REVIEW_DIR = os.path.join(ROOT, "ArtSource/Review/Course/assets")
V = Vector
Z = V((0.0, 0.0, 1.0))

# name: (linear rgb, roughness, metallic)
MAT_SPEC = {
    "Cart_Paint": ((0.80, 0.73, 0.58), 0.35, 0.0),
    "Cart_Roof": ((0.05, 0.04, 0.09), 0.5, 0.0),
    "Cart_Seat": ((0.055, 0.045, 0.095), 0.6, 0.0),
    "Cart_Accent": ((0.0, 0.53, 0.72), 0.35, 0.0),
    "Cart_Tire": ((0.022, 0.022, 0.025), 0.9, 0.0),
    "Cart_Rim": ((0.66, 0.67, 0.69), 0.28, 0.7),
    "Cart_Metal": ((0.11, 0.11, 0.12), 0.45, 0.5),
    "Cart_Dark": ((0.035, 0.035, 0.04), 0.8, 0.0),
    "Cart_Light": ((1.0, 0.93, 0.78), 0.15, 0.0),
    "Prop_Wood": ((0.30, 0.15, 0.07), 0.65, 0.0),
    "Prop_Iron": ((0.018, 0.034, 0.024), 0.5, 0.3),
    "Prop_Accent": ((0.03, 0.05, 0.14), 0.45, 0.0),
    "Prop_Metal": ((0.45, 0.46, 0.48), 0.35, 0.6),
    "Prop_Plastic": ((0.82, 0.80, 0.74), 0.5, 0.0),
    "Sign_Face": ((0.86, 0.80, 0.64), 0.8, 0.0),
    "Yard_Cap": ((0.90, 0.90, 0.88), 0.5, 0.0),
    "PB_Grass": ((0.20, 0.19, 0.07), 0.85, 0.0),
    "PB_IcePlant": ((0.13, 0.17, 0.05), 0.42, 0.0),
    "PB_Flower": ((0.62, 0.035, 0.30), 0.6, 0.0),
    "PB_Flower2": ((0.17, 0.15, 0.62), 0.6, 0.0),
}


def make_materials():
    PB.make_materials()
    for k, (rgb, rough, metal) in MAT_SPEC.items():
        PB.MATS[k] = PB.material(k, rgb, rough, metal)


# ================================================================ geometry helpers (Blender space, Z up)
def rx(deg):
    return Matrix.Rotation(math.radians(deg), 4, "X")


def ry(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Y")


def rz(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Z")


def xf(bm, M):
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return bm


def rbox(sx, sy, sz, c=(0, 0, 0), r=0.02, segs=2, rot=None):
    """Box with bevelled (soft) edges, centred at c; rot is a 4x4 applied about the centre."""
    bm = PB.bm_box(sx, sy, sz)
    r = min(r, 0.45 * min(sx, sy, sz))
    if r > 1e-5 and segs > 0:
        bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=r, segments=segs, affect="EDGES",
                        profile=0.5)
    M = Matrix.Translation(V(c))
    if rot is not None:
        M = M @ rot
    return xf(bm, M)


def bar(p0, p1, w, h, r=0.01, segs=1):
    """Rounded box running from p0 to p1 (w across, h roughly vertical)."""
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    bm = rbox(w, d.length, h, r=r, segs=segs)
    q = d.normalized().to_track_quat("Y", "Z")
    return xf(bm, Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4())


def circle(n, r=1.0, phase=0.0):
    return [(r * math.cos(2 * math.pi * k / n + phase), r * math.sin(2 * math.pi * k / n + phase)) for k in range(n)]


def rrect(hu, hv, rr, nc=2):
    """Counter-clockwise rounded rectangle section (half sizes hu, hv)."""
    rr = min(rr, hu * 0.99, hv * 0.99)
    pts = []
    for ci, (cx, cy) in enumerate([(hu - rr, hv - rr), (rr - hu, hv - rr), (rr - hu, rr - hv), (hu - rr, rr - hv)]):
        for j in range(nc + 1):
            a = math.radians(90 * ci + 90 * j / nc)
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return pts


def loft(bm, rings, cap0=True, cap1=True, closed=False):
    """Skin consecutive rings (lists of points, counter-clockwise about the travel direction) with outward quads."""
    vr = [[bm.verts.new(V(p)) for p in ring] for ring in rings]
    m = len(vr[0])
    n = len(vr)
    for i in range(n if closed else n - 1):
        A, B = vr[i], vr[(i + 1) % n]
        for k in range(m):
            k1 = (k + 1) % m
            bm.faces.new((A[k], A[k1], B[k1], B[k]))
    if not closed:
        if cap0:
            bm.faces.new(list(reversed(vr[0])))
        if cap1:
            bm.faces.new(vr[-1])
    return vr


def sweep(bm, pts, section, radii=None, normals=None, cap0=True, cap1=True, closed=False):
    """Sweep a 2D section (u along the frame normal, v along the binormal) along a polyline with
    parallel-transported frames. radii scales the section per point; normals optionally fixes the frame."""
    pts = [V(p) for p in pts]
    n = len(pts)
    rings = []
    N = None
    for i in range(n):
        if closed:
            T = (pts[(i + 1) % n] - pts[(i - 1) % n]).normalized()
        else:
            T = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        if normals is not None:
            N = V(normals[i])
        elif N is None:
            a = Z if abs(T.dot(Z)) < 0.95 else V((1, 0, 0))
            N = a
        N = (N - T * N.dot(T)).normalized()
        B = T.cross(N)
        s = 1.0 if radii is None else radii[i]
        su, sv = (s, s) if not isinstance(s, tuple) else s
        rings.append([pts[i] + N * (u * su) + B * (v * sv) for u, v in section])
    return loft(bm, rings, cap0, cap1, closed)


def pipe(bm, pts, r, sides=8, radii=None, **kw):
    return sweep(bm, pts, circle(sides, r), radii=radii, **kw)


def lathe_bm(profile, sides=24, phase=0.0, rfun=None):
    """Revolve (r, z) about Z; profiles listed bottom-to-top along the outside face outward. r == 0 collapses to a pole."""
    bm = bmesh.new()
    rings = []
    for j, (r, zz) in enumerate(profile):
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, zz))])
            continue
        ring = []
        for k in range(sides):
            t = 2 * math.pi * k / sides + phase
            rr = rfun(j, k, r) if rfun else r
            ring.append(bm.verts.new((rr * math.cos(t), rr * math.sin(t), zz)))
        rings.append(ring)
    for j in range(len(rings) - 1):
        A, B = rings[j], rings[j + 1]
        for k in range(sides):
            k1 = (k + 1) % sides
            if len(A) == 1 and len(B) == 1:
                continue
            if len(A) == 1:
                bm.faces.new((A[0], B[k1], B[k]))
            elif len(B) == 1:
                bm.faces.new((A[k], A[k1], B[0]))
            else:
                bm.faces.new((A[k], A[k1], B[k1], B[k]))
    return bm


def se_ring(y, hw, zb, zt, n=4.0, m=28):
    """Superellipse cross-section in the XZ plane at y, counter-clockwise about +Y."""
    cz, hh = (zb + zt) / 2, (zt - zb) / 2
    pts = []
    for k in range(m):
        t = 2 * math.pi * k / m
        c, s = math.cos(t), math.sin(t)
        u = math.copysign(abs(c) ** (2 / n), c)
        v = math.copysign(abs(s) ** (2 / n), s)
        pts.append(V((-hw * u, y, cz + hh * v)))
    return pts


def se_x(hw, zb, zt, z, n=4.0):
    cz, hh = (zb + zt) / 2, (zt - zb) / 2
    q = abs((z - cz) / hh)
    return hw * (1 - q ** n) ** (1 / n) if q < 1 else 0.0


def tris_of(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == "MESH" else 0


def finish(b, coll, sharp=None, loc=None, uv=0.5, smooth=True):
    ob = b.finish(coll, smooth_shade=smooth, box_uv=uv)
    if sharp is not None:
        ob.data.set_sharp_from_angle(angle=math.radians(sharp))
    if loc is not None:
        ob.location = V(loc)
    return ob


def empty(coll, name, loc, size=0.05):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "PLAIN_AXES"
    e.empty_display_size = size
    e.location = V(loc)
    coll.objects.link(e)
    return e


def new_coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    return c


# ================================================================ golf cart
WHEEL_R = 0.23
TRACK_X = 0.5
AXLE_Y = 0.82
STEER_HUB = V((-0.28, 0.12, 1.02))
STEER_ELEV = 55.0       # column elevation above horizontal
STEER_RIM_R = 0.19
STEER_DISH = 0.03       # rim plane sits this far from the hub toward the driver
SEAT_X = 0.29
HIP = (-0.28, 0.81)     # (y, z) of the seated hip joint
COWL = [(0.54, 0.60, 0.46, 0.93), (0.70, 0.60, 0.46, 0.91), (0.88, 0.595, 0.46, 0.85), (1.02, 0.585, 0.46, 0.79),
        (1.11, 0.565, 0.465, 0.745), (1.17, 0.53, 0.475, 0.705), (1.205, 0.47, 0.49, 0.67),
        (1.225, 0.38, 0.51, 0.64)]
STRIPE_Z = 0.535


def steer_frame():
    """Column axis d (from hub toward the driver, up and back) and the in-plane 12 o'clock direction v."""
    a = math.radians(STEER_ELEV)
    d = V((0.0, -math.cos(a), math.sin(a)))
    v = V((0.0, math.sin(a), math.cos(a)))
    return d, v


def cart_anchors():
    d, v = steer_frame()
    X = V((1, 0, 0))
    rim_c = STEER_HUB + d * STEER_DISH
    pedal = {}
    for name, x in (("Foot_L", -0.37), ("Foot_R", -0.18)):
        c = V((x, 0.47, 0.39))
        n = V((0.0, -math.sin(math.radians(35)), math.cos(math.radians(35))))
        pedal[name] = c + n * 0.012
    return {
        "Seat_Driver": V((-SEAT_X, HIP[0], HIP[1])),
        "Seat_Passenger": V((SEAT_X, HIP[0], HIP[1])),
        "Hand_L": rim_c + (v * 0.5 - X * 0.866) * STEER_RIM_R,
        "Hand_R": rim_c + (v * 0.5 + X * 0.866) * STEER_RIM_R,
        "Foot_L": pedal["Foot_L"],
        "Foot_R": pedal["Foot_R"],
        "Steering_Up": STEER_HUB + d * 0.1,
        "Bag_Mount": V((0.0, -0.90, 0.65)),
    }


def build_cart_body(coll):
    b = PB.Builder("Cart_Body")
    P, S, A, MET, D, L = "Cart_Paint", "Cart_Seat", "Cart_Accent", "Cart_Metal", "Cart_Dark", "Cart_Light"
    # chassis: dark frame rail underneath, narrow lower noses between the wheels
    b.add_bm(rbox(0.76, 2.24, 0.07, (0, 0, 0.255), 0.02), MET)
    b.add_bm(rbox(0.76, 0.64, 0.30, (0, 0.88, 0.37), 0.07, 3), P)
    b.add_bm(rbox(0.76, 0.66, 0.30, (0, -0.87, 0.37), 0.07, 3), P)
    # front cowl: lofted superellipse sections, rounded nose
    bm = bmesh.new()
    loft(bm, [se_ring(*s) for s in COWL])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bvh = BVHTree.FromBMesh(bm)
    b.add_bm(bm, P)
    # aqua stripe hugging the cowl at STRIPE_Z, wrapping the nose
    left = [V((-(se_x(hw, zb, zt, STRIPE_Z) + 0.002), y, STRIPE_Z)) for (y, hw, zb, zt) in COWL]
    nose_y = COWL[-1][0] + 0.004
    xn = abs(left[-1].x)
    front = [V((xn * (-1 + 2 * i / 6), nose_y, STRIPE_Z)) for i in range(1, 6)]
    right = [V((-p.x, p.y, p.z)) for p in reversed(left)]
    path = left + front + right
    nrm = []
    for i, p in enumerate(path):
        a, c = path[max(0, i - 1)], path[min(len(path) - 1, i + 1)]
        t = (c - a).normalized()
        h = V((t.y, -t.x, 0.0))   # horizontal, to the left of travel = outward (path runs clockwise from above)
        if h.dot(V((p.x, p.y - 0.85, 0))) < 0:
            h = -h
        nrm.append(h)
    bm = bmesh.new()
    sweep(bm, path, rrect(0.005, 0.026, 0.004, 1), normals=nrm)
    b.add_bm(bm, A)
    # headlights: metal bezel + pale lens dome, seated on the nose surface and facing out along its normal
    for sx in (-1, 1):
        hit, hn, _i, _d = bvh.ray_cast(V((sx * 0.30, 2.0, 0.625)), V((0, -1, 0)))
        axis = (hn.normalized() * 0.6 + V((0, 1, 0)) * 0.4).normalized()
        M = Matrix.Translation(hit) @ axis.to_track_quat("Z", "Y").to_matrix().to_4x4()
        b.add_bm(xf(lathe_bm([(0.058, -0.04), (0.064, 0.0), (0.06, 0.013), (0.05, 0.015)], 20), M), MET)
        dome = [(0.05 * math.cos(t), 0.009 + 0.02 * math.sin(t)) for t in [i * math.pi / 8 for i in range(5)]]
        b.add_bm(xf(lathe_bm(dome[:-1] + [(0.0, 0.029)], 20), M), L)
    # bumpers
    b.add_bm(rbox(1.10, 0.08, 0.10, (0, 1.205, 0.30), 0.04, 2), D)
    b.add_bm(rbox(1.10, 0.08, 0.10, (0, -1.205, 0.30), 0.04, 2), D)
    # fenders (wheel-well lips) over each wheel
    for wy in (AXLE_Y, -AXLE_Y):
        for sx in (-1, 1):
            angs = [math.radians(8 + 164 * i / 12) for i in range(13)]
            pts = [V((sx * 0.51, wy + 0.30 * math.cos(a), WHEEL_R + 0.30 * math.sin(a))) for a in angs]
            nr = [V((0, math.cos(a), math.sin(a))) for a in angs]
            bm = bmesh.new()
            sweep(bm, pts, rrect(0.03, 0.115, 0.012, 2), normals=nr)
            b.add_bm(bm, P)
    # floor pan + mat, dash
    b.add_bm(rbox(1.16, 0.64, 0.10, (0, 0.27, 0.29), 0.03), P)
    b.add_bm(rbox(1.04, 0.54, 0.02, (0, 0.26, 0.347), 0.008, 1), D)
    b.add_bm(rbox(1.04, 0.05, 0.52, (0, 0.545, 0.62), 0.02), D)
    b.add_bm(rbox(1.08, 0.14, 0.04, (0, 0.515, 0.87), 0.018, 2), D)
    for sx in (-0.3, 0.3):      # cup holders / cubbies on the dash shelf
        b.add_bm(rbox(0.16, 0.07, 0.012, (sx + 0.08, 0.50, 0.893), 0.005, 1), MET)
    # pedals
    for x in (-0.37, -0.18):
        b.add_bm(rbox(0.09, 0.14, 0.022, (x, 0.47, 0.39), 0.007, 1, rot=rx(35)), MET)
        b.add_bm(bar((x, 0.53, 0.36), (x, 0.50, 0.40), 0.02, 0.02, 0.005), MET)
    # steering column with a rubber boot at the dash
    d, _v = steer_frame()
    bm = bmesh.new()
    pipe(bm, [STEER_HUB - d * 0.80, STEER_HUB - d * 0.04], 0.024, 10)
    b.add_bm(bm, MET)
    boot = lathe_bm([(0.06, 0.0), (0.05, 0.05), (0.032, 0.12), (0.028, 0.14)], 12)
    q = (-d).to_track_quat("-Z", "Y").to_matrix().to_4x4()
    b.add_bm(xf(boot, Matrix.Translation(STEER_HUB - d * 0.78) @ q), D)
    # seat base, rear deck, side stripes, seat piping
    b.add_bm(rbox(1.20, 0.52, 0.32, (0, -0.24, 0.46), 0.05, 2), P)
    b.add_bm(rbox(1.20, 0.70, 0.15, (0, -0.85, 0.545), 0.04, 2), P)
    for sx in (-1, 1):
        b.add_bm(rbox(0.012, 1.17, 0.045, (sx * 0.601, -0.595, STRIPE_Z), 0.005, 1), A)
    bm = bmesh.new()
    pipe(bm, [(-0.585, 0.024, 0.600), (0.585, 0.024, 0.600)], 0.013, 8)
    b.add_bm(bm, A)
    # bench seat: two cushions and two backrest pads, tilted back 12 degrees
    for sx in (-1, 1):
        b.add_bm(rbox(0.56, 0.52, 0.11, (sx * SEAT_X, -0.235, 0.655), 0.045, 3), S)
        b.add_bm(rbox(0.55, 0.09, 0.44, (sx * SEAT_X, -0.50, 0.93), 0.04, 3, rot=rx(12)), S)
        # hip restraint grab handles
        bm = bmesh.new()
        pipe(bm, [(sx * 0.585, -0.43, 0.60), (sx * 0.62, -0.42, 0.74), (sx * 0.625, -0.36, 0.80),
                  (sx * 0.625, -0.16, 0.80), (sx * 0.62, -0.10, 0.74), (sx * 0.585, -0.09, 0.60)], 0.016, 8)
        b.add_bm(bm, MET)
    # roof struts
    for sx in (-1, 1):
        bm = bmesh.new()
        pipe(bm, [(sx * 0.53, 0.70, 0.80), (sx * 0.53, 0.745, 1.73)], 0.025, 10)
        pipe(bm, [(sx * 0.53, -0.62, 0.58), (sx * 0.53, -0.70, 1.73)], 0.025, 10)
        b.add_bm(bm, MET)
        b.add_bm(rbox(0.08, 0.08, 0.025, (sx * 0.53, -0.62, 0.62), 0.008, 1), MET)
    # bag frame between the rear struts, backrest brackets
    for zc in (1.0, 1.2):
        yc = -0.62 - 0.08 * (zc - 0.58) / 1.13
        bm = bmesh.new()
        pipe(bm, [(-0.53, yc, zc), (0.53, yc, zc)], 0.017, 8)
        b.add_bm(bm, MET)
    b.add_bm(rbox(0.14, 0.03, 0.07, (0, -0.70, 1.2), 0.01, 1), D)   # bag strap buckle
    b.add_bm(rbox(0.14, 0.03, 0.07, (0, -0.69, 1.0), 0.01, 1), D)
    for sx in (-0.3, 0.3):
        b.add_bm(bar((sx, -0.55, 0.98), (sx, -0.65, 0.99), 0.04, 0.04, 0.008), MET)
    # bag rack: tray with a low wire basket rail
    b.add_bm(rbox(0.80, 0.48, 0.03, (0, -0.93, 0.635), 0.01, 1), MET)
    bm = bmesh.new()
    loop = [(-0.40, -0.70, 0.76), (-0.40, -1.16, 0.76), (0.40, -1.16, 0.76), (0.40, -0.70, 0.76)]
    pipe(bm, loop, 0.012, 6, closed=True)
    for (x, y) in [(-0.4, -0.7), (-0.4, -1.16), (0.4, -1.16), (0.4, -0.7), (-0.4, -0.93), (0.4, -0.93),
                   (0.0, -1.16), (-0.2, -1.16), (0.2, -1.16)]:
        pipe(bm, [(x, y, 0.64), (x, y, 0.76)], 0.01, 6)
    b.add_bm(bm, MET)
    # roof: navy crowned slab over an aqua trim band
    roof = rbox(1.24, 2.02, 0.08, (0, -0.10, 1.762), 0.035, 3)
    for v in roof.verts:
        if v.co.z > 1.763:
            v.co.z += 0.022 * (1 - (v.co.x / 0.62) ** 2) * (1 - ((v.co.y + 0.10) / 1.01) ** 2)
    b.add_bm(roof, "Cart_Roof")
    b.add_bm(rbox(1.26, 2.04, 0.032, (0, -0.10, 1.717), 0.012, 2), A)
    return finish(b, coll, sharp=38)


def wheel_parts(outward):
    """(bmesh, material) parts of a wheel centred at its hub, axle along X, hubcap facing outward (+1/-1 X)."""
    M = (rz(180) if outward < 0 else Matrix.Identity(4)) @ ry(90)

    def tread(j, k, r):
        if j == 4 and k % 4 in (0, 1):
            return r + 0.008
        if j == 5 and k % 4 in (2, 3):
            return r + 0.008
        return r

    tyre = [(0.150, -0.085), (0.175, -0.098), (0.205, -0.100), (0.218, -0.088), (0.222, -0.06), (0.222, 0.06),
            (0.218, 0.088), (0.205, 0.100), (0.175, 0.098), (0.150, 0.085)]
    parts = [(lathe_bm(tyre, 40, rfun=tread), "Cart_Tire"),
             (lathe_bm([(0.152, -0.08), (0.152, 0.07), (0.160, 0.080), (0.157, 0.093), (0.145, 0.097),
                        (0.132, 0.086), (0.124, 0.058)], 32), "Cart_Rim"),
             (lathe_bm([(0.126, 0.058), (0.10, 0.045), (0.0, 0.042)], 32), "Cart_Metal"),
             (lathe_bm([(0.0, -0.075), (0.152, -0.075)], 24), "Cart_Metal"),
             (lathe_bm([(0.058, 0.045), (0.058, 0.07), (0.05, 0.085), (0.032, 0.093), (0.0, 0.095)], 20), "Cart_Rim"),
             (lathe_bm([(0.025, 0.0925), (0.023, 0.097), (0.0, 0.0985)], 16), "Cart_Accent")]
    for i in range(5):
        a = 2 * math.pi * i / 5 + 0.3
        c, s = math.cos(a), math.sin(a)
        parts.append((bar((0.05 * c, 0.05 * s, 0.062), (0.128 * c, 0.128 * s, 0.066), 0.034, 0.026, 0.008),
                      "Cart_Rim"))
    for bm, _m in parts:
        xf(bm, M)
    return parts


def build_wheels(coll):
    obs = []
    for name, sx, sy in (("Wheel_FL", -1, 1), ("Wheel_FR", 1, 1), ("Wheel_RL", -1, -1), ("Wheel_RR", 1, -1)):
        b = PB.Builder(name)
        for bm, m in wheel_parts(sx):
            b.add_bm(bm, m)
        obs.append(finish(b, coll, sharp=40, loc=(sx * TRACK_X, sy * AXLE_Y, WHEEL_R)))
    return obs


def build_steering(coll):
    b = PB.Builder("Steering_Wheel")
    d, v = steer_frame()
    X = V((1, 0, 0))
    R = Matrix((X, v, d)).transposed().to_4x4()   # local X, Y (12 o'clock), Z (toward the driver)
    bm = bmesh.new()
    pts = [V((STEER_RIM_R * math.cos(t), STEER_RIM_R * math.sin(t), STEER_DISH)) for t in
           [2 * math.pi * i / 24 for i in range(24)]]
    sweep(bm, pts, circle(8, 0.019), closed=True)
    b.add_bm(xf(bm, R), "Cart_Dark")
    for a in (0.0, math.pi, 1.5 * math.pi):
        c, s = math.cos(a), math.sin(a)
        b.add_bm(xf(bar((0.04 * c, 0.04 * s, 0.012), (0.18 * c, 0.18 * s, STEER_DISH), 0.04, 0.014, 0.006), R),
                 "Cart_Metal")
    b.add_bm(xf(lathe_bm([(0.0, -0.04), (0.045, -0.04), (0.052, 0.0), (0.046, 0.024), (0.03, 0.034)], 16), R),
             "Cart_Dark")
    b.add_bm(xf(lathe_bm([(0.03, 0.034), (0.026, 0.038), (0.0, 0.041)], 16), R), "Cart_Accent")
    return finish(b, coll, sharp=40, loc=STEER_HUB)


def build_cart(coll):
    objs = [build_cart_body(coll)] + build_wheels(coll) + [build_steering(coll)]
    for name, p in cart_anchors().items():
        objs.append(empty(coll, name, p))
    return objs


# ================================================================ props
def build_bench(coll):
    b = PB.Builder("Bench")
    W, I = "Prop_Wood", "Prop_Iron"
    for y in (0.17, 0.065, -0.04, -0.145):
        b.add_bm(rbox(1.60, 0.088, 0.034, (0, y, 0.44), 0.01, 2), W)
    back_dir = V((0, -math.sin(math.radians(15)), math.cos(math.radians(15))))
    base = V((0, -0.205, 0.50))
    for s in (0.09, 0.21, 0.33):
        c = base + back_dir * s
        b.add_bm(rbox(1.60, 0.03, 0.09, tuple(c), 0.01, 2, rot=rx(15)), W)
    for x in (-0.70, 0.70, 0.0):
        if x == 0.0:
            b.add_bm(bar((0, 0.20, 0.405), (0, -0.19, 0.405), 0.04, 0.035), I)
            continue
        bt = base + back_dir * 0.42 - V((-x, 0.035, 0))
        for p0, p1, w, h in [((x, 0.20, 0.0), (x, 0.18, 0.66), 0.05, 0.05),         # front leg up to the arm
                             ((x, -0.215, 0.0), (x, -0.185, 0.42), 0.05, 0.05),     # back leg
                             ((x, -0.185, 0.42), tuple(bt), 0.05, 0.045),           # back support
                             ((x, 0.215, 0.405), (x, -0.21, 0.405), 0.05, 0.04),    # seat rail
                             ((x, 0.26, 0.665), (x, -0.215, 0.69), 0.065, 0.035),   # arm rest
                             ((x, 0.20, 0.12), (x, -0.205, 0.12), 0.035, 0.03)]:    # stretcher
            b.add_bm(bar(p0, p1, w, h, 0.012), I)
        for y in (0.20, -0.215):
            b.add_bm(rbox(0.08, 0.09, 0.03, (x, y, 0.015), 0.01, 1), I)
        bm = bmesh.new()     # scroll curl under the arm front
        angs = [math.radians(-90 + 270 * i / 8) for i in range(9)]
        pipe(bm, [(x, 0.235 + 0.05 * math.cos(a) * (1 - i / 14), 0.60 + 0.05 * math.sin(a) * (1 - i / 14))
                  for i, a in enumerate(angs)], 0.014, 6)
        b.add_bm(bm, I)
    return [finish(b, coll, sharp=40)]


def build_ball_washer(coll):
    b = PB.Builder("BallWasher")
    b.add_bm(rbox(0.34, 0.34, 0.04, (0, 0, 0.02), 0.014, 2), "Prop_Iron")
    b.add_bm(rbox(0.09, 0.09, 0.88, (0, 0, 0.46), 0.014, 2), "Prop_Wood")
    b.add_bm(rbox(0.13, 0.13, 0.05, (0, 0, 0.87), 0.012, 1), "Prop_Metal")
    b.add_bm(rbox(0.16, 0.27, 0.32, (0, 0, 1.06), 0.035, 3), "Prop_Accent")
    b.add_bm(rbox(0.172, 0.282, 0.03, (0, 0, 1.205), 0.012, 2), "Prop_Metal")
    b.add_bm(rbox(0.07, 0.014, 0.07, (0, 0.135, 1.08), 0.012, 2), "Prop_Iron")         # ball slot
    b.add_bm(rbox(0.012, 0.09, 0.09, (0.081, 0, 1.06), 0.01, 1), "Prop_Plastic")        # badge plate
    bm = bmesh.new()
    pipe(bm, [(0, 0, 1.21), (0, 0, 1.33)], 0.012, 8)
    b.add_bm(bm, "Prop_Metal")
    b.add_bm(bar((0, -0.08, 1.345), (0, 0.08, 1.345), 0.034, 0.034, 0.015, 2), "Prop_Plastic")
    return [finish(b, coll, sharp=40)]


def build_trash_bin(coll):
    b = PB.Builder("TrashBin")
    b.add_bm(lathe_bm([(0.0, 0.03), (0.24, 0.03), (0.24, 0.70), (0.0, 0.70)], 20), "Prop_Iron")
    b.add_bm(lathe_bm([(0.0, 0.0), (0.275, 0.0), (0.28, 0.02), (0.27, 0.035), (0.0, 0.035)], 28), "Prop_Iron")
    n = 14
    for k in range(n):
        a = 2 * math.pi * k / n
        b.add_bm(rbox(0.082, 0.03, 0.66, (0.258 * math.cos(a), 0.258 * math.sin(a), 0.355), 0.009, 1,
                      rot=rz(math.degrees(a) + 90)), "Prop_Wood")
    for zc in (0.13, 0.58):
        b.add_bm(lathe_bm([(0.27, zc - 0.022), (0.278, zc - 0.015), (0.278, zc + 0.015), (0.27, zc + 0.022)], 28),
                 "Prop_Iron")
    b.add_bm(lathe_bm([(0.0, 0.68), (0.25, 0.68), (0.292, 0.69), (0.297, 0.712), (0.28, 0.732), (0.22, 0.765),
                       (0.12, 0.787), (0.0, 0.792)], 28), "Prop_Accent")
    b.add_bm(lathe_bm([(0.032, 0.785), (0.036, 0.808), (0.026, 0.826), (0.0, 0.83)], 14), "Prop_Metal")
    return [finish(b, coll, sharp=40)]


def build_tee_sign(coll):
    b = PB.Builder("TeeSign")
    W = "Prop_Wood"
    for sx in (-1, 1):
        b.add_bm(rbox(0.085, 0.085, 0.97, (sx * 0.24, -0.077, 0.485), 0.012, 2), W)
    b.add_bm(rbox(0.68, 0.07, 0.53, (0, 0, 0.735), 0.02, 2), W)
    b.add_bm(rbox(0.60, 0.012, 0.45, (0, 0.036, 0.735), 0.004, 1), "Sign_Face")
    b.add_bm(rbox(0.74, 0.12, 0.035, (0, -0.02, 1.012), 0.014, 2), W)
    b.add_bm(rbox(0.66, 0.014, 0.02, (0, 0.03, 0.488), 0.006, 1), "Prop_Accent")      # navy kick strip
    ob = finish(b, coll, sharp=40)
    # 0..1 UVs on the sign face front so a label texture can be mapped directly
    me = ob.data
    uvl = me.uv_layers.active
    fi = list(me.materials).index(PB.MATS["Sign_Face"])
    for poly in me.polygons:
        if poly.material_index == fi and poly.normal.y > 0.9:
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uvl.data[li].uv = ((co.x + 0.30) / 0.60, (co.z - 0.51) / 0.45)
    lab = empty(coll, "Sign_Label", (0, 0.042 + 0.005, 0.735))
    return [ob, lab]


def build_yardage_post(coll):
    b = PB.Builder("YardagePost")
    b.add_bm(rbox(0.10, 0.10, 0.41, (0, 0, 0.205), 0.012, 2), "Prop_Wood")
    b.add_bm(rbox(0.112, 0.112, 0.05, (0, 0, 0.425), 0.016, 2), "Yard_Cap")
    return [finish(b, coll, sharp=40)]


def build_bunker_rake(coll):
    b = PB.Builder("BunkerRake")
    hy = 0.70
    bm = bmesh.new()
    pipe(bm, [(0, -0.76, 0.017), (0, hy - 0.06, 0.05)], 0.016, 8)
    b.add_bm(bm, "Prop_Metal")
    bm = bmesh.new()
    pipe(bm, [(0, -0.765, 0.0175), (0, -0.60, 0.0215)], 0.019, 8)
    b.add_bm(bm, "Prop_Accent")
    b.add_bm(xf(lathe_bm([(0.02, 0.0), (0.03, 0.05), (0.03, 0.07)], 8), Matrix.Translation((0, hy - 0.08, 0.052))
                @ rx(-90)), "Prop_Plastic")
    b.add_bm(rbox(0.56, 0.04, 0.035, (0, hy, 0.062), 0.01, 2), "Prop_Plastic")
    for i in range(14):
        x = -0.26 + 0.52 * i / 13
        b.add_bm(rbox(0.014, 0.014, 0.05, (x, hy + 0.004, 0.025), 0.0, 0), "Prop_Plastic")
    ob = finish(b, coll, sharp=40)
    me = ob.data
    ys = [v.co.y for v in me.vertices]
    cy = (min(ys) + max(ys)) / 2
    for v in me.vertices:
        v.co.y -= cy
    me.update()
    return [ob]


# ================================================================ vegetation
def tuft(b, c, r, rz_, seed, tint, H, nc, subdiv=2, rough=0.3, sat=(0, 0)):
    """A foliage clump: one core blob plus satellites over its top/rim, sharing soft normals (like cypress pads)."""
    rnd = random.Random(seed)
    col = PB.foliage_color(c, r, rz_, H, tint)
    centre = c - V((0, 0, rz_ * 1.1))
    core = PB.bm_blob(r * rnd.uniform(0.85, 1.05), r * rnd.uniform(0.85, 1.0), rz_, seed, subdiv, rough)
    bmesh.ops.translate(core, verts=core.verts, vec=c)
    b.add_bm(core, "PB_Foliage", None, normals_from=centre, color=col)
    core.free()
    for k in range(rnd.randint(*sat) if sat[1] else 0):
        a = rnd.uniform(0, 2 * math.pi)
        off = V((math.cos(a), math.sin(a), 0)) * r * rnd.uniform(0.45, 0.8) + V((0, 0, rz_ * rnd.uniform(-0.1, 0.55)))
        ss = rnd.uniform(0.4, 0.58)
        s = PB.bm_blob(r * ss, r * ss, rz_ * rnd.uniform(0.6, 0.85), seed * 7 + k, 1, rough)
        bmesh.ops.translate(s, verts=s.verts, vec=c + off)
        b.add_bm(s, "PB_Foliage", None, normals_from=centre, color=col)
        s.free()


def pine_into(b, seed, H):
    """Monterey pine: tall, slightly leaning bare trunk, a few upturned limbs, irregular rounded tufts concentrated
    in the upper half, darker and bluer (low tip tint) than the cypress."""
    rnd = random.Random(seed)
    s = H / 14.0
    la = rnd.uniform(0, 2 * math.pi)
    lean = V((math.cos(la), math.sin(la), 0))
    lean_amt = rnd.uniform(0.06, 0.1) * H
    ph = rnd.uniform(0, 6)

    def trunk_at(t):
        return (V((0, 0, t * H * 0.86)) + lean * lean_amt * t ** 1.5 +
                V((math.sin(t * 6 + ph), math.cos(t * 5 + ph), 0)) * 0.18 * s * t)

    bark = bmesh.new()
    tp = [trunk_at(i / 9) for i in range(10)]
    tp[0] = tp[0] - V((0, 0, 0.4))
    radii = [0.6 * s] + [0.5 * s * (1 - 0.72 * (i / 9)) for i in range(1, 10)]
    radii[1] *= 1.12
    sweep(bark, tp, circle(9), radii=radii, cap0=False)
    tufts = []
    n = rnd.randint(9, 11)
    a0 = rnd.uniform(0, 2 * math.pi)
    for i in range(n):
        t = 0.5 + 0.38 * (i / (n - 1)) + rnd.uniform(-0.03, 0.03)
        start = trunk_at(t)
        ang = a0 + i * 2.39996 + rnd.uniform(-0.3, 0.3)
        elev = math.radians(rnd.uniform(24, 44))
        L = H * rnd.uniform(0.2, 0.3) * (1.2 - 0.6 * (t - 0.5) / 0.38)
        dh = (V((math.cos(ang), math.sin(ang), 0)) + lean * 0.35).normalized()
        mid = start + dh * L * 0.55 * math.cos(elev) + Z * L * 0.4 * math.sin(elev)
        end = start + dh * L * math.cos(elev) + Z * (L * math.sin(elev) + L * 0.3)
        rr = 0.16 * s * (1.15 - 0.6 * t)
        pipe(bark, [start, mid, end], 1.0, 6, radii=[rr, rr * 0.7, rr * 0.35])
        cr = H * rnd.uniform(0.095, 0.12)
        tufts.append((end + Z * cr * 0.2, cr, cr * rnd.uniform(0.55, 0.65)))
        for k in range(rnd.randint(3, 4)):
            off = V((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized() * cr * rnd.uniform(0.7, 1.1)
            tufts.append((end + off + Z * cr * rnd.uniform(-0.3, 0.25), cr * rnd.uniform(0.6, 0.85),
                          cr * rnd.uniform(0.42, 0.55)))
        if rnd.random() < 0.8:
            tufts.append((mid + Z * cr * 0.25, cr * 0.7, cr * 0.42))
    top = trunk_at(1.0)
    pipe(bark, [trunk_at(0.95), top + Z * 0.8 * s], 1.0, 5, radii=[0.12 * s, 0.05 * s])
    for k in range(6):
        a = rnd.uniform(0, 2 * math.pi)
        off = V((math.cos(a), math.sin(a), 0)) * H * rnd.uniform(0.03, 0.1)
        cr = H * rnd.uniform(0.09, 0.115)
        tufts.append((top + off + Z * H * rnd.uniform(0.0, 0.05), cr, cr * 0.6))
    b.add_bm(bark, "PB_Bark")
    bark.free()
    for i, (c, r, rz_) in enumerate(tufts):
        tint = rnd.uniform(0.08, 0.2) if c.z > H * 0.85 else rnd.uniform(0.0, 0.08)
        tuft(b, c, r, rz_, seed * 17 + i, tint, H, 2, 2, 0.34, sat=(1, 2))


def oak_into(b, seed, H=8.0, W=11.0):
    """Coast live oak: short gnarly 2-3 stem trunk, crooked spreading limbs, dense lumpy dome wider than tall."""
    rnd = random.Random(seed)
    bark = bmesh.new()
    pipe(bark, [(0, 0, -0.3), (0, 0, 0.6)], 1.0, 9, radii=[0.62, 0.5], cap0=False)
    ns = rnd.choice((2, 3, 3))
    a0 = rnd.uniform(0, 2 * math.pi)
    zc, Rx, Ry = H * 0.62, W * 0.47, W * 0.43
    Rz = H - zc - 0.2
    for i in range(ns):
        a = a0 + 2 * math.pi * i / ns + rnd.uniform(-0.4, 0.4)
        d = V((math.cos(a), math.sin(a), 0))
        side = V((-d.y, d.x, 0))
        pts = [V((0, 0, 0.3))]
        for j in range(1, 6):
            t = j / 5
            p = d * (Rx * 0.42 * t ** 1.1) + Z * (0.6 + (zc - 0.4) * math.sqrt(t))
            p += side * rnd.uniform(-0.35, 0.35) + Z * rnd.uniform(-0.15, 0.15)
            pts.append(p)
        pipe(bark, pts, 1.0, 7, radii=[0.4, 0.34, 0.27, 0.21, 0.16, 0.11])
        for k in (-1, 1):                     # each stem forks into two crooked limbs
            fork = pts[3]
            dd = (d + side * k * rnd.uniform(0.5, 0.9)).normalized()
            q = [fork, fork + dd * Rx * 0.25 + Z * 0.7 + side * rnd.uniform(-0.3, 0.3),
                 fork + dd * Rx * 0.5 + Z * 1.1]
            pipe(bark, q, 1.0, 5, radii=[0.17, 0.11, 0.06])
    b.add_bm(bark, "PB_Bark")
    bark.free()
    # dome: flattened core plus a skin of lumps, denser and larger at the rim
    core = PB.bm_blob(Rx * 0.86, Ry * 0.86, Rz * 0.8, seed, 3, 0.18)
    bottom = -0.42 * Rz
    for v in core.verts:
        if v.co.z < bottom:
            v.co.z = bottom + (v.co.z - bottom) * 0.3
    c0 = V((0, 0, zc))
    bmesh.ops.translate(core, verts=core.verts, vec=c0)
    b.add_bm(core, "PB_Foliage", None, normals_from=c0 - Z * Rz * 0.5,
             color=PB.foliage_color(c0, Rx, Rz * 0.8, H, 0.15))
    core.free()
    n = 44
    for i in range(n):
        u = (i + 0.5) / n
        elev = math.asin(max(-0.25, min(1.0, 1.0 - 1.15 * u)))   # roughly area-uniform over the dome
        az = i * 2.39996 + rnd.uniform(-0.2, 0.2)
        p = V((Rx * math.cos(elev) * math.cos(az), Ry * math.cos(elev) * math.sin(az), Rz * math.sin(elev))) * 0.86
        c = c0 + p
        r = rnd.uniform(1.0, 1.8) * (1.0 + 0.25 * (1 - abs(math.sin(elev))))
        tint = rnd.uniform(0.2, 0.42) if elev > 0.5 else rnd.uniform(0.08, 0.26)
        tuft(b, c, r, r * rnd.uniform(0.62, 0.78), seed * 23 + i, tint, H, 0, 2, 0.28)


def shrub_into(b, seed, shape):
    rnd = random.Random(seed)
    lumps = []
    if shape == "round":         # ~1.1 m dome
        H = 1.15
        lumps.append((V((0, 0, 0.5)), 0.55, 0.48))
        for i in range(10):
            a = i * 2.39996
            e = math.asin(1 - 1.4 * (i + 0.5) / 10)
            lumps.append((V((0.5 * math.cos(e) * math.cos(a), 0.5 * math.cos(e) * math.sin(a), 0.5 + 0.45 * math.sin(e))),
                          rnd.uniform(0.22, 0.3), rnd.uniform(0.2, 0.26)))
    elif shape == "wide":        # ~0.6 m tall, ~2 m across
        H = 0.65
        for i in range(14):
            a = i * 2.39996
            rr = 0.95 * math.sqrt((i + 0.5) / 14)
            p = V((rr * math.cos(a), rr * math.sin(a) * 0.72, 0.0))
            r = rnd.uniform(0.28, 0.42) * (1.15 - 0.4 * rr)
            p.z = 0.42 * (1 - (rr / 1.1) ** 2) - r * 0.55
            lumps.append((p, r, r * rnd.uniform(0.6, 0.8)))
    else:                        # tall and upright, ~1.8 m
        H = 1.8
        for i in range(15):
            t = (i + 0.5) / 15
            a = i * 2.39996
            w = 0.34 * (1 - 0.65 * t) + 0.06
            p = V((w * math.cos(a), w * math.sin(a), 0.28 + 1.3 * t))
            r = 0.28 * (1 - 0.5 * t) + 0.08
            lumps.append((p, r, r * 1.15))
        bark = bmesh.new()
        for k in range(3):
            a = 2 * math.pi * k / 3
            pipe(bark, [(0.04 * math.cos(a), 0.04 * math.sin(a), -0.05),
                        (0.12 * math.cos(a), 0.12 * math.sin(a), 0.45)], 1.0, 5, radii=[0.035, 0.022])
        b.add_bm(bark, "PB_Bark")
        bark.free()
    for i, (c, r, rz_) in enumerate(lumps):
        tint = rnd.uniform(0.25, 0.5) if c.z > H * 0.55 else rnd.uniform(0.08, 0.28)
        tuft(b, c, r, rz_, seed * 11 + i, tint, H, 0, 2, 0.26)


def strap_into(b, seed, n, H, w0, bend, mat="PB_Grass", taper=0.8, spread=0.045, tint=(0.75, 0.95), segs=3):
    """Double-sided tapered curved strips (grass blades or strap leaves) springing from the origin."""
    rnd = random.Random(seed)
    for i in range(n):
        a = 2 * math.pi * i / n + rnd.uniform(-0.25, 0.25)
        rr = spread * math.sqrt(rnd.random())
        root = V((math.cos(a) * rr, math.sin(a) * rr, -0.01))
        out = V((math.cos(a + rnd.uniform(-0.4, 0.4)), math.sin(a + rnd.uniform(-0.4, 0.4)), 0)).normalized()
        L = H * rnd.uniform(0.75, 1.1)
        tilt = math.radians(rnd.uniform(4, 22)) * (0.4 + 0.6 * rr / spread)
        bnd = math.radians(rnd.uniform(*bend))
        twist = rnd.uniform(-0.5, 0.5)
        w = w0 * rnd.uniform(0.8, 1.2)
        g_tip = rnd.uniform(*tint)
        pts, ws, ts = [root], [w], [0.0]
        p = root.copy()
        for j in range(1, segs + 2):
            t = j / (segs + 1)
            ang = tilt + bnd * t ** 1.6
            p = p + (out * math.sin(ang) + Z * math.cos(ang)) * (L / (segs + 1))
            pts.append(p.copy())
            ws.append(w * (1 - t) ** taper if j <= segs else 0.0)
            ts.append(t)
        front, back, fn, bn, cols = [], [], [], [], []
        verts = []
        for j, (q, ww, t) in enumerate(zip(pts, ws, ts)):
            tdir = (pts[min(j + 1, len(pts) - 1)] - pts[max(j - 1, 0)]).normalized()
            sd = Z.cross(out)
            sd = (sd * math.cos(twist * t) + tdir.cross(sd) * math.sin(twist * t)).normalized()
            nrm = sd.cross(tdir).normalized()
            if nrm.dot(Z) + nrm.dot(out) < 0:
                nrm = -nrm
            col = (0.32 + 0.68 * t ** 0.8, 0.12 + (g_tip - 0.12) * t ** 1.4, 0.0, 1.0)
            ring = [q - sd * ww, q + sd * ww] if ww > 0 else [q]
            for v in ring:
                verts.append((v, nrm, col))
        # verts: rows of two, last row single (tip)
        vs = [tuple(v) for v, _n, _c in verts]
        cs = [c for _v, _n, c in verts]
        nf = [tuple((n * 0.45 + Z).normalized()) for _v, n, _c in verts]
        nb = [tuple((-n * 0.45 + Z).normalized()) for _v, n, _c in verts]
        rows = segs + 1
        f = []
        for j in range(rows - 1):
            a0, a1, b0, b1 = 2 * j, 2 * j + 1, 2 * j + 2, 2 * j + 3
            f.append((a0, a1, b1, b0))
        tip = 2 * rows
        f.append((2 * (rows - 1), 2 * (rows - 1) + 1, tip))
        # orient front faces along nrm
        p0, p1, p2 = V(vs[f[0][0]]), V(vs[f[0][1]]), V(vs[f[0][2]])
        if (p1 - p0).cross(p2 - p1).dot(verts[0][1]) < 0:
            f = [tuple(reversed(x)) for x in f]
        b.add_mesh(vs, f, mat, nf, cs)
        b.add_mesh(vs, [tuple(reversed(x)) for x in f], mat, nb, cs)


def grass_into(b, seed, n, H):
    strap_into(b, seed, n, H, 0.0105, (25, 70), "PB_Grass", taper=0.7, spread=0.05)


def ice_plant_into(b, seed):
    rnd = random.Random(seed)
    blobs = []
    for i in range(22):
        a = i * 2.39996 + rnd.uniform(-0.3, 0.3)
        rr = 1.05 * math.sqrt((i + 0.5) / 22) * (0.85 + 0.25 * math.sin(3 * a + seed))
        c = V((rr * math.cos(a), rr * math.sin(a) * 0.85, 0.0))
        r = rnd.uniform(0.26, 0.4)
        hz = rnd.uniform(0.11, 0.17) * (1.1 - 0.35 * rr)
        c.z = hz * 0.25
        blobs.append((c, r, r * rnd.uniform(0.8, 1.1), hz))
        bm = PB.bm_blob(r, blobs[-1][2], hz, seed * 5 + i, 2, 0.32)
        bmesh.ops.translate(bm, verts=bm.verts, vec=c)
        b.add_bm(bm, "PB_IcePlant", None, normals_from=c - Z * hz)
        bm.free()

    def top(x, y):
        best = 0.0
        for c, r1, r2, hz in blobs:
            q = 1 - ((x - c.x) / r1) ** 2 - ((y - c.y) / r2) ** 2
            if q > 0:
                best = max(best, c.z + hz * math.sqrt(q))
        return best

    placed = 0
    while placed < 28:
        a = rnd.uniform(0, 2 * math.pi)
        rr = 1.0 * math.sqrt(rnd.random())
        x, y = rr * math.cos(a), rr * math.sin(a) * 0.85
        zt = top(x, y)
        if zt < 0.06:
            continue
        fr = rnd.uniform(0.045, 0.065)
        bm = PB.bm_blob(fr, fr, fr * 0.45, seed + placed * 3, 1, 0.15)
        bmesh.ops.translate(bm, verts=bm.verts, vec=V((x, y, zt * 0.93 + fr * 0.2)))
        b.add_bm(bm, "PB_Flower")
        bm.free()
        placed += 1


def flowers_into(b, seed):
    rnd = random.Random(seed)
    strap_into(b, seed, 14, 0.36, 0.022, (50, 95), "PB_Foliage", taper=0.35, spread=0.06, tint=(0.35, 0.55), segs=3)
    heads = []
    for i in range(5):
        a = 2 * math.pi * i / 5 + rnd.uniform(-0.3, 0.3)
        lean = V((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.05, 0.13)
        h = rnd.uniform(0.47, 0.54)
        p0 = V((math.cos(a) * 0.02, math.sin(a) * 0.02, -0.01))
        p1 = p0 + lean * 0.4 + Z * h * 0.5
        p2 = p0 + lean + Z * h
        sw = bmesh.new()
        pipe(sw, [p0, p1, p2], 1.0, 5, radii=[0.008, 0.007, 0.005])
        b.add_bm(sw, "PB_Foliage", None, color=lambda co, h=h: (0.5 + 0.5 * min(1, co.z / h), 0.3, 0.0, 1.0))
        sw.free()
        heads.append(p2)
    for i, c in enumerate(heads):
        r = rnd.uniform(0.055, 0.065)
        cc = c + Z * r * 0.8
        bm = PB.bm_blob(r, r, r * 0.9, seed + i, 2, 0.18)
        bmesh.ops.translate(bm, verts=bm.verts, vec=cc)
        b.add_bm(bm, "PB_Flower2", None, normals_from=cc)
        bm.free()
        for k in range(6):
            a = k * 2.39996 + i
            e = math.asin(1 - 1.6 * (k + 0.5) / 6)
            d = V((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
            f = PB.bm_blob(0.022, 0.022, 0.022, seed * 3 + i * 7 + k, 1, 0.1)
            bmesh.ops.translate(f, verts=f.verts, vec=cc + d * r * 0.95)
            b.add_bm(f, "PB_Flower2", None, normals_from=cc)
            f.free()


VEG = [("Pine_A", lambda b: pine_into(b, 41, 14.0)),
       ("Pine_B", lambda b: pine_into(b, 77, 15.5)),
       ("Oak_A", lambda b: oak_into(b, 13, 8.0, 11.0)),
       ("Oak_B", lambda b: oak_into(b, 29, 9.2, 12.0)),
       ("Shrub_A", lambda b: shrub_into(b, 5, "round")),
       ("Shrub_B", lambda b: shrub_into(b, 6, "wide")),
       ("Shrub_C", lambda b: shrub_into(b, 7, "tall")),
       ("Grass_A", lambda b: grass_into(b, 3, 18, 0.42)),
       ("Grass_B", lambda b: grass_into(b, 8, 24, 0.6)),
       ("IcePlant_A", lambda b: ice_plant_into(b, 4)),
       ("Flowers_A", lambda b: flowers_into(b, 9))]

# The course trees (leaf-card canopies with LODs and impostors) are built and exported by pp_nature.py; the blob
# versions above stay for reference but are no longer written over its Vegetation/Pine_* and Oak_* exports.
NATURE_TREES = {"Pine_A", "Pine_B", "Oak_A", "Oak_B"}

PROPS = [("Bench", build_bench), ("BallWasher", build_ball_washer), ("TrashBin", build_trash_bin),
         ("TeeSign", build_tee_sign), ("YardagePost", build_yardage_post), ("BunkerRake", build_bunker_rake)]

BUDGET = {"Cart": 15000, "Bench": 2500, "BallWasher": 2500, "TrashBin": 2500, "TeeSign": 2500, "YardagePost": 2500,
          "BunkerRake": 2500, "Pine_A": 10000, "Pine_B": 10000, "Oak_A": 10000, "Oak_B": 10000, "Shrub_A": 2000,
          "Shrub_B": 2000, "Shrub_C": 2000, "Grass_A": 400, "Grass_B": 400, "IcePlant_A": 2500, "Flowers_A": 1500}


# ================================================================ export / build
def export_fbx(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    vl = bpy.context.view_layer
    vl.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    vl.objects.active = next(o for o in objs if o.type == "MESH")
    kw = dict(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"}, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
              use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
              path_mode="STRIP")
    if "colors_type" in bpy.ops.export_scene.fbx.get_rna_type().properties.keys():
        kw["colors_type"] = "LINEAR"
    bpy.ops.export_scene.fbx(**kw)
    for o in objs:
        o.select_set(False)
    tris = sum(tris_of(o) for o in objs)
    print("[export] %-48s %6d tris  %s" % (os.path.relpath(path, ROOT), tris,
                                            ", ".join("%s(%s)" % (o.name, o.type[0]) for o in objs)))
    return tris


def object_materials(objs):
    return sorted({m.name for o in objs if o.type == "MESH" for m in o.data.materials})


def u3(v):
    return [round(v[0], 4), round(v[2], 4), round(v[1], 4)]


def build_all(export=True):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    make_materials()
    report = {}
    groups = []
    c = new_coll("Cart")
    cart = build_cart(c)
    groups.append(("Cart", cart, os.path.join(EXPORT_DIR, "Cart", "GolfCart.fbx")))
    for name, fn in PROPS:
        c = new_coll("Prop_" + name)
        groups.append((name, fn(c), os.path.join(EXPORT_DIR, "Props", name + ".fbx")))
    for name, fn in VEG:
        if name in NATURE_TREES:
            continue
        c = new_coll("Veg_" + name)
        b = PB.Builder(name)
        fn(b)
        groups.append((name, [finish(b, c, uv=0.5)], os.path.join(EXPORT_DIR, "Vegetation", name + ".fbx")))
    for name, objs, path in groups:
        tris = export_fbx(objs, path) if export else sum(tris_of(o) for o in objs)
        meshes = [o for o in objs if o.type == "MESH"]
        lo = [min((o.matrix_world @ V(cn))[i] for o in meshes for cn in o.bound_box) for i in range(3)]
        hi = [max((o.matrix_world @ V(cn))[i] for o in meshes for cn in o.bound_box) for i in range(3)]
        size = [round(hi[i] - lo[i], 3) for i in range(3)]
        flag = "" if tris <= BUDGET[name] else "  OVER BUDGET %d" % BUDGET[name]
        print("[asset] %-12s tris %6d  size x %.2f y %.2f z %.2f  mats %s%s" % (
            name, tris, size[0], size[1], size[2], object_materials(objs), flag))
        report[name] = {"file": os.path.relpath(path, ROOT), "tris": tris, "size_blender_xyz": size,
                        "objects": {o.name: {"type": o.type, "tris": tris_of(o), "unity_pos": u3(o.location)}
                                    for o in objs},
                        "materials": object_materials(objs)}
    if export:
        with open(os.path.join(EXPORT_DIR, "course_props_manifest.json"), "w") as f:
            json.dump(report, f, indent=1)
        save_blend()
    return report


def save_blend():
    if os.path.exists(BLEND):
        shutil.copy2(BLEND, BLEND.replace(".blend", "_backup.blend"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
    print("[save]", os.path.relpath(BLEND, ROOT))


# ================================================================ review renders
def preview_materials():
    """Approximate the Unity foliage shader (shade->tip green by G, darkened by R) for review renders only."""
    def vc_mat(m, shade, tip, ao=0.85):
        nt = m.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        at = nt.nodes.new("ShaderNodeAttribute")
        at.attribute_name = "Col"
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(at.outputs["Color"], sep.inputs[0])
        t = nt.nodes.new("ShaderNodeMath")
        t.operation = "MULTIPLY_ADD"
        nt.links.new(sep.outputs[1], t.inputs[0])
        t.inputs[1].default_value = 0.85
        t.inputs[2].default_value = 0.08
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        nt.links.new(t.outputs[0], mix.inputs[0])
        mix.inputs[6].default_value = (*shade, 1)
        mix.inputs[7].default_value = (*tip, 1)
        a = nt.nodes.new("ShaderNodeMath")
        a.operation = "MULTIPLY_ADD"
        nt.links.new(sep.outputs[0], a.inputs[0])
        a.inputs[1].default_value = ao
        a.inputs[2].default_value = 1 - ao
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1.0
        nt.links.new(mix.outputs[2], mul.inputs[6])
        nt.links.new(a.outputs[0], mul.inputs[7])
        nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])

    vc_mat(bpy.data.materials["PB_Foliage"], (0.085, 0.13, 0.065), (0.21, 0.29, 0.12))
    g = MAT_SPEC["PB_Grass"][0]
    vc_mat(bpy.data.materials["PB_Grass"], tuple(c * 0.55 for c in g), (0.42, 0.36, 0.14), 0.7)


def setup_render():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 960, 540
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    try:
        sc.eevee.taa_render_samples = 32
    except Exception:
        pass
    for vt in ("AgX", "Khronos PBR Neutral", "Standard"):
        try:
            sc.view_settings.view_transform = vt
            break
        except TypeError:
            continue
    try:
        sc.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    w = bpy.data.worlds.new("Review_World")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.42, 0.55, 0.78, 1)
    bg.inputs[1].default_value = 0.9
    sc.world = w
    coll = new_coll("Review")
    sun = bpy.data.objects.new("Review_Sun", bpy.data.lights.new("Review_Sun", "SUN"))
    sun.data.energy = 4.2
    sun.data.color = (1.0, 0.84, 0.64)
    sun.data.angle = math.radians(3)
    coll.objects.link(sun)
    gm = PB.material("Review_Ground", (0.12, 0.17, 0.06), 0.95)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=500)
    me = bpy.data.meshes.new("Review_Ground")
    bm.to_mesh(me)
    me.materials.append(gm)
    g = bpy.data.objects.new("Review_Ground", me)
    coll.objects.link(g)
    hb = PB.Builder("Review_Human")
    PB.MATS["Review_Human"] = PB.material("Review_Human", (0.5, 0.5, 0.52), 0.6)
    hb.add_bm(rbox(0.45, 0.28, 1.8, (0, 0, 0.9), 0.04), "Review_Human")
    human = hb.finish(coll, smooth_shade=False, box_uv=0)
    cam = bpy.data.objects.new("Review_Cam", bpy.data.cameras.new("Review_Cam"))
    coll.objects.link(cam)
    sc.camera = cam
    return sc, sun, cam, human, g


def aim(cam, sun, objs, az, el, lens=60, margin=1.08, sun_az=None, sun_el=24, target=None):
    bpy.context.view_layer.update()
    pts = []
    for o in objs:
        if o.type == "MESH":
            pts += [o.matrix_world @ V(c) for c in o.bound_box]
    lo = V((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = V((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (lo + hi) / 2 if target is None else V(target)
    cam.data.lens = lens
    sw = cam.data.sensor_width
    aspect = 960 / 540
    fw = 2 * math.atan(sw / 2 / lens)
    fh = 2 * math.atan(math.tan(fw / 2) / aspect)
    a, e = math.radians(az), math.radians(el)
    d = V((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))   # az 0 = in front (+Y)
    fwd = -d
    right = fwd.cross(Z).normalized()
    up = right.cross(fwd).normalized()
    need = 0.0
    for c in [V((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]:
        v = c - ctr
        depth = v.dot(fwd)
        need = max(need, abs(v.dot(right)) / math.tan(fw / 2) * margin - depth,
                   abs(v.dot(up)) / math.tan(fh / 2) * margin - depth)
    cam.location = ctr + d * need
    cam.rotation_euler = fwd.to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = 0.05
    cam.data.clip_end = need * 10 + 100
    sa = math.radians(az + 50 if sun_az is None else sun_az)
    se = math.radians(sun_el)
    sd = V((math.sin(sa) * math.cos(se), math.cos(sa) * math.cos(se), math.sin(se)))
    sun.rotation_euler = (-sd).to_track_quat("-Z", "Y").to_euler()


def render_reviews():
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    for k in MAT_SPEC:
        PB.MATS[k] = bpy.data.materials[k]
    PB.MATS["PB_Foliage"] = bpy.data.materials["PB_Foliage"]
    PB.MATS["PB_Bark"] = bpy.data.materials["PB_Bark"]
    preview_materials()
    sc, sun, cam, human, ground = setup_render()
    os.makedirs(REVIEW_DIR, exist_ok=True)
    allobs = [o for o in bpy.data.objects if o.type in ("MESH", "EMPTY") and not o.name.startswith("Review")]

    def show(objs, with_human=False):
        keep = set(objs) | {ground, sun, cam} | ({human} if with_human else set())
        for o in bpy.data.objects:
            o.hide_render = o not in keep

    def shot(fname):
        sc.render.filepath = os.path.join(REVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)
        print("[render]", os.path.relpath(sc.render.filepath, ROOT))

    cart = list(bpy.data.collections["Cart"].objects)
    show(cart)
    aim(cam, sun, cart, 38, 14, 55, sun_az=80, sun_el=30)
    shot("cart_front34.png")
    aim(cam, sun, cart, 90, 6, 55, sun_az=40, sun_el=30)
    shot("cart_side.png")
    aim(cam, sun, cart, 215, 22, 55, sun_az=170, sun_el=35)
    shot("cart_rear34.png")
    # props lined up along X
    x = 0.0
    props = []
    for name, _fn in PROPS:
        objs = list(bpy.data.collections["Prop_" + name].objects)
        meshes = [o for o in objs if o.type == "MESH"]
        w = max(abs(V(c).x) for o in meshes for c in o.bound_box)
        x += w
        for o in objs:
            o.location.x += x
        x += w + 0.45
        props += objs
    human.location = (x + 0.1, 0, 0)
    show(props, True)
    aim(cam, sun, props + [human], 18, 14, 55, sun_az=60, sun_el=28)
    shot("props_lineup.png")
    # vegetation: trees, then small plants, each with the 1.8 m reference box
    for group, names, gap in (("trees", ["Pine_A", "Pine_B", "Oak_A", "Oak_B"], 1.0),
                              ("plants", ["Shrub_A", "Shrub_B", "Shrub_C", "Grass_A", "Grass_B", "IcePlant_A",
                                          "Flowers_A"], 0.35),
                              ("groundcover", ["Grass_A", "Grass_B", "Flowers_A", "IcePlant_A"], 0.25)):
        x = 0.0
        obs = []
        names = [n for n in names if n in bpy.data.objects]
        if not names:
            continue
        for n in names:
            o = bpy.data.objects[n]
            w = max(abs(V(c).x) for c in o.bound_box)
            x += w
            o.location.x = x
            x += w + gap
            obs.append(o)
        human.location = (x + 0.3, 0, 0)
        show(obs, True)
        aim(cam, sun, obs + [human], 10, {"trees": 10, "plants": 16, "groundcover": 20}[group], 50, sun_az=60, sun_el=26)
        shot("veg_%s.png" % group)
    # close-up of the oak + pine canopy shading
    for n in ("Pine_A", "Oak_A"):
        if n not in bpy.data.objects:
            continue
        o = bpy.data.objects[n]
        o.location.x = 0
        show([o], True)
        human.location = (max(abs(V(c).x) for c in o.bound_box) + 0.8, 0, 0)
        aim(cam, sun, [o, human], 25, 12, 50, sun_az=70, sun_el=26)
        shot("veg_%s_close.png" % n.lower())


# ================================================================ verification
def verify_cart():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = os.path.join(EXPORT_DIR, "Cart", "GolfCart.fbx")
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    print("[verify] %s" % os.path.relpath(path, ROOT))
    exp = cart_anchors()
    exp.update({"Wheel_FL": V((-TRACK_X, AXLE_Y, WHEEL_R)), "Wheel_FR": V((TRACK_X, AXLE_Y, WHEEL_R)),
                "Wheel_RL": V((-TRACK_X, -AXLE_Y, WHEEL_R)), "Wheel_RR": V((TRACK_X, -AXLE_Y, WHEEL_R)),
                "Steering_Wheel": STEER_HUB, "Cart_Body": V((0, 0, 0))})
    worst = 0.0
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        loc = o.matrix_world.translation
        rot = [round(math.degrees(a), 2) for a in o.matrix_world.to_euler()]
        sc = [round(s, 4) for s in o.matrix_world.to_scale()]
        err = (loc - exp[o.name]).length if o.name in exp else float("nan")
        worst = max(worst, err) if err == err else worst
        extra = ""
        if o.type == "MESH":
            pts = [o.matrix_world @ V(c) for c in o.bound_box]
            extra = " zmin %.4f tris %d" % (min(p.z for p in pts), tris_of(o))
        print("  %-15s %-6s loc (%.4f, %.4f, %.4f) rot %s scale %s err %.5f%s" % (
            o.name, o.type, loc.x, loc.y, loc.z, rot, sc, err, extra))
    print("[verify] max position error vs authored: %.6f m" % worst)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    mode = argv[0] if argv else "build"
    if mode == "build":
        build_all()
    elif mode == "render":
        render_reviews()
    elif mode == "verify":
        verify_cart()
