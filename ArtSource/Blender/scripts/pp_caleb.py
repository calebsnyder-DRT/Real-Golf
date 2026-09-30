"""Golfer 'Caleb' v2: continuous skin-modifier body + fitted clothing shells. Front = -Y, Z up, feet at origin."""
import math

import bmesh
import bpy
from mathutils import Vector

from pp_lib import get_scene

WIP = "PP_CalebWIP"
ARM_DEG = 42.0


def _coll():
    sc = get_scene()
    c = bpy.data.collections.get(WIP)
    if c is None:
        c = bpy.data.collections.new(WIP)
        sc.collection.children.link(c)
    return c


def arm_dir(sx):
    a = math.radians(ARM_DEG)
    return Vector((sx * math.sin(a), 0.0, -math.cos(a)))


SHOULDER = (0.2, 0.0, 1.405)


def skeleton():
    """(name, co, (rx, ry), parent_name). Radii: rx across (X), ry depth (Y) for the trunk."""
    J = []

    def add(n, co, r, p=None):
        J.append((n, Vector(co), r, p))

    add("pelvis", (0, 0.0, 0.965), (0.182, 0.125))
    add("waist", (0, 0.0, 1.08), (0.158, 0.112), "pelvis")
    add("chest", (0, -0.008, 1.23), (0.192, 0.135), "waist")
    add("upchest", (0, 0.0, 1.35), (0.205, 0.122), "chest")
    add("neckb", (0, 0.01, 1.44), (0.078, 0.074), "upchest")
    add("neck", (0, 0.014, 1.505), (0.066, 0.066), "neckb")
    add("jaw", (0, -0.004, 1.58), (0.094, 0.104), "neck")
    add("headm", (0, 0.006, 1.665), (0.114, 0.126), "jaw")
    add("headt", (0, 0.012, 1.735), (0.1, 0.113), "headm")
    for sx, s in ((-1, "R"), (1, "L")):
        sh = Vector((sx * SHOULDER[0], SHOULDER[1], SHOULDER[2]))
        d = arm_dir(sx)
        add("clav" + s, (sx * 0.12, 0.0, 1.405), (0.08, 0.075), "upchest")
        add("sh" + s, sh, (0.078, 0.078), "clav" + s)
        add("bic" + s, sh + d * 0.12, (0.062, 0.066), "sh" + s)
        el = sh + d * 0.285 + Vector((0, 0.008, 0))
        add("el" + s, el, (0.048, 0.05), "bic" + s)
        add("fa" + s, el + d * 0.08 + Vector((0, -0.006, 0)), (0.053, 0.049), "el" + s)
        wr = el + d * 0.25 + Vector((0, -0.02, 0))
        add("wr" + s, wr, (0.036, 0.027), "fa" + s)
        hd = (d + Vector((0, -0.05, -0.12))).normalized()
        add("palm" + s, wr + hd * 0.055, (0.027, 0.052), "wr" + s)
        add("fing" + s, wr + hd * 0.13, (0.022, 0.046), "palm" + s)
        add("fingt" + s, wr + hd * 0.172, (0.018, 0.036), "fing" + s)
        add("thb" + s, wr + hd * 0.03 + Vector((0, -0.032, 0)), (0.02, 0.02), "wr" + s)
        add("tht" + s, wr + hd * 0.085 + Vector((0, -0.05, 0)), (0.015, 0.015), "thb" + s)
        add("hip" + s, (sx * 0.1, 0.0, 0.915), (0.108, 0.11), "pelvis")
        add("thigh" + s, (sx * 0.106, -0.008, 0.72), (0.093, 0.096), "hip" + s)
        add("knee" + s, (sx * 0.108, -0.012, 0.5), (0.064, 0.066), "thigh" + s)
        add("calf" + s, (sx * 0.109, 0.01, 0.37), (0.067, 0.073), "knee" + s)
        add("ankle" + s, (sx * 0.11, 0.012, 0.1), (0.041, 0.043), "calf" + s)
        add("heel" + s, (sx * 0.11, 0.02, 0.05), (0.04, 0.043), "ankle" + s)
        add("toe" + s, (sx * 0.116, -0.13, 0.037), (0.042, 0.034), "heel" + s)
    return J


def build_base(levels=2):
    c = _coll()
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    J = skeleton()
    idx = {n: i for i, (n, *_r) in enumerate(J)}
    me = bpy.data.meshes.new("PP_CalebBody")
    verts = [tuple(co) for (_n, co, _r, _p) in J]
    edges = [(idx[p], i) for i, (_n, _co, _r, p) in enumerate(J) if p]
    me.from_pydata(verts, edges, [])
    ob = bpy.data.objects.new("PP_CalebBody", me)
    c.objects.link(ob)
    sk = ob.modifiers.new("Skin", "SKIN")
    sk.branch_smoothing = 0.6
    sk.use_smooth_shade = True
    for i, (_n, _co, r, _p) in enumerate(J):
        sv = me.skin_vertices[0].data[i]
        sv.radius = r
        sv.use_root = (i == 0)
    ss = ob.modifiers.new("Subsurf", "SUBSURF")
    ss.levels = levels
    ss.render_levels = levels
    return ob


def apply_mods(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    me.name = "PP_CalebBody"
    bpy.data.meshes.remove(old)
    return ob


def viewport(target=(0, 0, 0.9), az=0.0, el=5.0, dist=3.2, shading="SOLID", lens=50):
    """Point the first 3D viewport at target; az=0 looks from the front (-Y)."""
    for win in bpy.context.window_manager.windows:
        for area in win.screen.areas:
            if area.type != "VIEW_3D":
                continue
            sp = area.spaces.active
            r3 = sp.region_3d
            r3.view_perspective = "PERSP"
            sp.lens = lens
            sp.overlay.show_overlays = False
            try:
                sp.shading.type = shading
            except TypeError:
                pass
            if shading == "SOLID":
                sp.shading.light = "STUDIO"
                sp.shading.color_type = "MATERIAL"
            a, e = math.radians(az), math.radians(el)
            d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
            r3.view_location = Vector(target)
            r3.view_rotation = (-d).to_track_quat("-Z", "Y")
            r3.view_distance = dist
            area.tag_redraw()
            return True
    return False


# ------------------------------------------------------------------ sculpt helpers
def g1(x, s):
    return math.exp(-(x / s) ** 2)


def smooth01(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def arm_param(p):
    """Return (side, t along arm, dist from arm axis) or None."""
    if abs(p.x) < 0.12:
        return None
    sx = 1 if p.x > 0 else -1
    S = Vector((sx * SHOULDER[0], SHOULDER[1], SHOULDER[2]))
    d = arm_dir(sx)
    q = p - S
    t = q.dot(d)
    dist = (q - d * t).length
    return sx, t, dist, S, d


def is_arm(p):
    a = arm_param(p)
    return a is not None and a[1] > -0.03 and a[2] < 0.13 and p.z > 0.85


def sculpt_body(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for v in bm.verts:
        p = v.co.copy()
        x, y, z = p
        ax = abs(x)
        arm = arm_param(p) if is_arm(p) else None
        if arm:
            sx, t, dist, S, d = arm
            radial = (p - S - d * t)
            rn = radial.normalized() if radial.length > 1e-6 else Vector((0, 0, 1))
            # deltoid cap + bicep + forearm swell
            bulge = 0.014 * g1(t - 0.03, 0.07) + 0.006 * g1(t - 0.13, 0.05) + 0.006 * g1(t - 0.36, 0.05)
            v.co += rn * bulge
            # hands: scale beyond wrist
            tw = 0.285 + 0.25
            if t > tw - 0.01:
                W = S + d * tw
                v.co = W + (v.co - W) * 1.18
            continue
        if z > 0.8 and z < 1.5 and ax < 0.26:
            # trapezius slope
            v.co.z += 0.014 * g1(z - 1.43, 0.03) * smooth01(0.05, 0.1, ax) * (1 - smooth01(0.16, 0.22, ax))
            # chest
            if y < -0.03:
                v.co.y -= 0.014 * g1(ax - 0.075, 0.055) * g1(z - 1.3, 0.05)
                v.co.y -= 0.006 * g1(z - 1.18, 0.06) * g1(ax, 0.1)
            # upper back / shoulder blades
            if y > 0.03:
                v.co.y += 0.008 * g1(ax - 0.08, 0.05) * g1(z - 1.32, 0.06)
                # glutes
                v.co.y += 0.02 * g1(ax - 0.075, 0.06) * g1(z - 0.9, 0.055)
            # waist taper
            v.co.x *= 1.0 - 0.05 * g1(z - 1.09, 0.05)
            # broaden upper chest toward shoulders
            v.co.x *= 1.0 + 0.04 * g1(z - 1.36, 0.05)
        elif z <= 0.8:
            if y < -0.02:
                v.co.y -= 0.009 * g1(z - 0.5, 0.028)  # knee cap
                v.co.y -= 0.007 * g1(z - 0.72, 0.07)  # quads
            if y > 0.0:
                v.co.y += 0.014 * g1(z - 0.4, 0.05)  # calf
            v.co.x += (1 if x > 0 else -1) * 0.005 * g1(z - 0.4, 0.05) * (1 if ax > 0.11 else 0)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


HEAD_C = Vector((0.0, 0.006, 1.662))


def refine_head(ob, bands=((1.5, 9.0, 1.0), (1.545, 1.73, 0.035))):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for z0, z1, ymax in bands:
        faces = [f for f in bm.faces if all(z0 < v.co.z < z1 and v.co.y < ymax for v in f.verts)]
        edges = list({e for f in faces for e in f.edges})
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True, smooth=1.0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def head_radius(u):
    """Stylized head surface radius along unit direction u (from HEAD_C)."""
    a, b = 0.094, 0.108
    c = 0.128 if u.z > 0 else 0.118
    if u.z < 0:
        k = -u.z
        a *= 1.0 - 0.33 * k * k            # jaw taper
        if u.y < 0:
            b *= 1.0 - 0.06 * k             # chin under-plane
    if u.y > 0 and u.z > -0.2:
        b *= 1.04                           # occiput
    if u.y < 0:
        b *= 0.97                           # flatter face plane
    return 1.0 / math.sqrt((u.x / a) ** 2 + (u.y / b) ** 2 + (u.z / c) ** 2)


def sculpt_head(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for v in bm.verts:
        if v.co.z < 1.515:
            continue
        q = v.co - HEAD_C
        if q.length < 1e-6:
            continue
        u = q.normalized()
        target = HEAD_C + u * head_radius(u)
        # blend into the neck below the jaw
        w = smooth01(1.525, 1.575, v.co.z) if u.y > -0.2 else smooth01(1.54, 1.585, v.co.z)
        if u.z < -0.55 and u.y > -0.3:
            w *= smooth01(1.53, 1.6, v.co.z)
        v.co = v.co.lerp(target, w)
    bm.normal_update()
    # facial features: displacement along vertex normal on the front of the head
    for v in bm.verts:
        x, y, z = v.co
        if z < 1.54 or y > 0.02:
            continue
        ax = abs(x)
        n = v.normal
        front = smooth01(-0.2, -0.75, n.y)
        dsp = 0.0
        dsp += 0.019 * g1(ax, 0.011) * g1(z - 1.652, 0.024)          # nose bridge + body
        dsp += 0.008 * g1(ax, 0.013) * g1(z - 1.634, 0.011)          # nose tip
        dsp += 0.004 * g1(ax - 0.013, 0.008) * g1(z - 1.628, 0.008)  # nostrils
        dsp += 0.007 * g1(ax - 0.035, 0.03) * g1(z - 1.696, 0.009)   # brow ridge
        dsp -= 0.011 * g1(ax - 0.036, 0.016) * g1(z - 1.674, 0.013)  # eye sockets
        dsp += 0.006 * g1(ax - 0.062, 0.02) * g1(z - 1.642, 0.018)   # cheekbones
        dsp += 0.009 * g1(ax, 0.02) * g1(z - 1.566, 0.014)           # chin
        dsp += 0.003 * g1(ax, 0.018) * g1(z - 1.603, 0.005)          # upper lip
        dsp += 0.0025 * g1(ax, 0.016) * g1(z - 1.591, 0.005)         # lower lip
        dsp -= 0.0025 * g1(ax, 0.02) * g1(z - 1.597, 0.003)          # mouth line
        v.co += n * dsp * front
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


# ------------------------------------------------------------------ head v2: neck + skull as one analytic surface
HC = Vector((0.0, 0.004, 1.66))
HA, HB, HCU, HCD = 0.099, 0.113, 0.13, 0.124
NECK_C = Vector((0.0, 0.014, 0.0))
NECK_A, NECK_B = 0.057, 0.061


def _smax(a, b, k=0.018):
    h = max(k - abs(a - b), 0.0) / k
    return max(a, b) + h * h * k * 0.25


def _head_plan_r(phi, z):
    """Head cross-section radius at height z (below centre) in direction phi (0=+X, -90deg = front)."""
    dz = (z - HC.z) / HCD
    if dz <= -1.0:
        return 0.0
    s = max(1.0 - dz * dz, 0.0) ** 0.3
    k = max(-dz, 0.0)
    a = HA * s * (1.0 - 0.14 * k * k)
    b = HB * max(1.0 - dz * dz, 0.0) ** 0.42
    cy = math.sin(phi)
    if cy < 0:  # front: gentle V toward the chin
        a *= 1.0 - 0.16 * k * (-cy)
        b *= 1.0 - 0.03 * k
    else:
        b *= 1.04
    return 1.0 / math.sqrt((math.cos(phi) / a) ** 2 + (math.sin(phi) / b) ** 2) if a > 1e-5 and b > 1e-5 else 0.0


def _neck_r(phi):
    return 1.0 / math.sqrt((math.cos(phi) / NECK_A) ** 2 + (math.sin(phi) / NECK_B) ** 2)


def sculpt_head2(ob, features=True):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    head_vs = []
    for v in bm.verts:
        z = v.co.z
        if z < 1.44 or abs(v.co.x) > 0.16:
            continue
        head_vs.append(v)
        if z >= HC.z:
            q = v.co - HC
            u = q.normalized()
            b = HB * (1.04 if u.y > 0 else 0.985)
            r = 1.0 / math.sqrt((u.x / HA) ** 2 + (u.y / b) ** 2 + (u.z / HCU) ** 2)
            v.co = HC + u * r
            continue
        # cylindrical projection around a vertical axis
        axis = Vector((0.0, (HC.y if z > 1.56 else NECK_C.y), z))
        rel = Vector((v.co.x, v.co.y - axis.y, 0.0))
        if rel.length < 1e-6:
            continue
        phi = math.atan2(rel.y, rel.x)
        rh = _head_plan_r(phi, z)
        rn = _neck_r(phi)
        r = _smax(rh, rn, 0.02)
        w = smooth01(1.44, 1.475, z)  # blend into the trapezius below
        newxy = Vector((math.cos(phi), math.sin(phi), 0.0)) * r
        target = Vector((newxy.x, newxy.y + axis.y, z))
        v.co = v.co.lerp(target, w)
    for _ in range(4):
        bmesh.ops.smooth_vert(bm, verts=[v for v in head_vs if v.co.z > 1.46], factor=0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.normal_update()
    if features:
        for v in bm.verts:
            x, y, z = v.co
            if z < 1.54 or y > 0.0 or abs(x) > 0.1:
                continue
            ax = abs(x)
            n = v.normal
            front = smooth01(-0.25, -0.8, n.y)
            d = 0.0
            d += 0.011 * g1(ax, 0.011) * g1(z - 1.656, 0.02)           # nose bridge
            d += 0.012 * g1(ax, 0.012) * g1(z - 1.637, 0.011)          # nose tip
            d += 0.004 * g1(ax - 0.014, 0.008) * g1(z - 1.632, 0.007)  # alae
            d += 0.006 * g1(ax - 0.034, 0.028) * g1(z - 1.694, 0.009)  # brow ridge
            d -= 0.009 * g1(ax - 0.036, 0.015) * g1(z - 1.672, 0.012)  # eye sockets
            d += 0.005 * g1(ax - 0.062, 0.02) * g1(z - 1.645, 0.016)   # cheekbones
            d += 0.007 * g1(ax, 0.02) * g1(z - 1.568, 0.013)           # chin
            d += 0.0028 * g1(ax, 0.017) * g1(z - 1.607, 0.005)         # upper lip
            d += 0.0024 * g1(ax, 0.015) * g1(z - 1.594, 0.005)         # lower lip
            d -= 0.002 * g1(ax, 0.02) * g1(z - 1.6, 0.003)             # mouth line
            v.co += n * d * front
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


# ------------------------------------------------------------------ dressing
from mathutils.bvhtree import BVHTree  # noqa: E402

import pp_lib  # noqa: E402
from pp_lib import B, CREAM, CYAN, INK, T, _tb, mat, p_sphere, palette  # noqa: E402

WRIST_T = 0.285 + 0.25


def _bvh(bm):
    return BVHTree.FromBMesh(bm)


def surf_front(bvh, x, z):
    hit = bvh.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
    return hit[0], hit[1]


def surf_along(bvh, origin, direction):
    hit = bvh.ray_cast(origin, direction)
    return hit[0], hit[1]


def cap_edge_z(fy):
    """Cap bottom edge height; fy = sin(angle around head), -1 = front."""
    return 1.712 + 0.016 * smooth01(0.2, 1.0, -fy) - 0.014 * smooth01(0.1, 0.9, fy)


def region_shell(src, keep, offset, hem=0.0, snap=None):
    """Copy faces where keep(face_center, normal) is True; push verts along normals by offset(co, n)."""
    bm = src.copy()
    bm.faces.ensure_lookup_table()
    kill = [f for f in bm.faces if not keep(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.normal_update()
    moves = [(v, v.normal.copy()) for v in bm.verts]
    for v, n in moves:
        v.co += n * offset(v.co.copy(), n)
    bm.normal_update()
    if snap is not None:
        for v in {v for e in bm.edges if e.is_boundary for v in e.verts}:
            v.co = snap(v.co.copy())
        bm.normal_update()
    if hem > 0:
        bnd = [e for e in bm.edges if e.is_boundary]
        nrm = {v.index: v.normal.copy() for e in bnd for v in e.verts}
        keyed = {tuple(round(c, 6) for c in bm.verts[i].co) if False else None: None for i in []}
        bm.verts.ensure_lookup_table()
        pos = {tuple(round(c, 6) for c in bm.verts[i].co): n for i, n in nrm.items()}
        res = bmesh.ops.extrude_edge_only(bm, edges=bnd)
        for g in res["geom"]:
            if isinstance(g, bmesh.types.BMVert):
                n = pos.get(tuple(round(c, 6) for c in g.co))
                if n is not None:
                    g.co -= n * hem
        bm.normal_update()
    return bm


def delete_faces(bm, pred):
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if pred(f.calc_center_median())], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")


def leg_axis(sx):
    return Vector((sx * 0.109, 0.01, 0.0))


def polo_keep(c, n):
    if is_arm(c):
        a = arm_param(c)
        return a[1] < 0.155
    if c.z < 0.985 or abs(c.x) > 0.3:
        return False
    neck_line = 1.462 - 0.03 * smooth01(0.0, -0.08, c.y) * (1 - smooth01(0.03, 0.06, abs(c.x)))
    return c.z < neck_line


def polo_offset(co, n):
    o = 0.0095
    if is_arm(co):
        t = arm_param(co)[1]
        o += 0.007 * smooth01(0.02, 0.15, t)
    o += 0.006 * g1(co.z - 1.04, 0.035) * (1 if not is_arm(co) else 0)
    phi = math.atan2(co.y, co.x)
    o += 0.0025 * math.sin(phi * 11.0) * g1(co.z - 1.03, 0.03)
    return o


def trouser_keep(c, n):
    if is_arm(c):
        return False
    return 0.07 < c.z < 1.035 and abs(c.x) < 0.3


def trouser_offset(co, n):
    o = 0.011 + 0.004 * smooth01(0.95, 0.75, co.z)
    # knee break and ankle break ripples
    o += 0.0035 * math.sin(co.z * 260.0) * g1(co.z - 0.52, 0.035)
    o += 0.004 * (0.5 + 0.5 * math.sin(co.z * 300.0)) * g1(co.z - 0.12, 0.04) * (1 if co.y < 0.0 else 0.4)
    return o


def straighten_legs(bm, zmax=0.6, rmin=0.069):
    for v in bm.verts:
        if v.co.z > zmax:
            continue
        sx = 1 if v.co.x > 0 else -1
        A = leg_axis(sx)
        rel = Vector((v.co.x - A.x, v.co.y - A.y, 0.0))
        r = rel.length
        w = smooth01(zmax, zmax - 0.12, v.co.z)
        want = max(r, rmin * (1.0 - 0.06 * smooth01(0.3, 0.08, v.co.z)))
        if r > 1e-6:
            rel = rel * ((r + (want - r) * w) / r)
        v.co.x, v.co.y = A.x + rel.x, A.y + rel.y


def collar(bm_polo, P_m):
    """Fold-over collar built from the polo neckline boundary loop."""
    bnd = [e for e in bm_polo.edges if e.is_boundary and all(v.co.z > 1.4 for v in e.verts)]
    vs = list({v for e in bnd for v in e.verts})
    axis = Vector((0, 0.012))

    def ang(v):
        return math.atan2(v.co.y - axis.y, v.co.x - axis.x)
    vs.sort(key=ang)
    out = _tb()
    rings = []
    for v in vs:
        a = ang(v)
        radial = Vector((math.cos(a), math.sin(a), 0.0))
        front = smooth01(-0.3, -1.0, math.sin(a))
        p0 = v.co.copy()
        p1 = p0 + Vector((0, 0, 0.03 - 0.012 * front)) - radial * 0.003
        p2 = p1 + radial * 0.018 + Vector((0, 0, 0.004))
        p3 = p0 + radial * (0.03 + 0.012 * front) - Vector((0, 0, 0.012 + 0.03 * front))
        rings.append((a, [out.verts.new(p) for p in (p0, p1, p2, p3)]))
    n = len(rings)
    for i in range(n):
        a0, r0 = rings[i]
        a1, r1 = rings[(i + 1) % n]
        mid = math.atan2(math.sin(a0) + math.sin(a1), math.cos(a0) + math.cos(a1))
        if abs(mid + math.pi / 2) < math.radians(13):
            continue  # open at the front placket
        for k in range(3):
            out.faces.new((r0[k], r1[k], r1[k + 1], r0[k + 1]))
    bmesh.ops.recalc_face_normals(out, faces=out.faces)
    bmesh.ops.solidify(out, geom=list(out.faces), thickness=0.003)
    return out


def loft_shoe(sx):
    """Golf shoe as a lofted closed shell. Toe points -Y."""
    bm = _tb()
    cx = sx * 0.112
    n_ring, n_sec = 18, 16
    y_heel, y_toe = 0.075, -0.205
    secs = []
    for j in range(n_sec):
        t = j / (n_sec - 1)
        y = y_heel + (y_toe - y_heel) * t
        w = 0.039 + 0.014 * math.sin(math.pi * min(t / 0.72, 1.0) * 0.5) + 0.004 * g1(t - 0.7, 0.15)
        if t > 0.8:
            w *= math.sqrt(max(1 - ((t - 0.8) / 0.2) ** 2, 0.02))
        if t < 0.08:
            w *= math.sqrt(max(1 - ((0.08 - t) / 0.08) ** 2, 0.25)) ** 0.5
        top = 0.1 - 0.016 * smooth01(0.1, 0.45, t) - 0.026 * smooth01(0.55, 0.97, t)
        bot = 0.02 + 0.01 * smooth01(0.82, 1.0, t)
        hgt = max(top - bot, 0.006)
        ring = []
        for i in range(n_ring):
            a = 2 * math.pi * i / n_ring
            ca, sa = math.cos(a), math.sin(a)
            ex = 2.6
            px = w * math.copysign(abs(ca) ** (2 / ex), ca)
            pz = hgt * 0.5 * math.copysign(abs(sa) ** (2 / ex), sa)
            if pz < 0:
                pz *= 0.8
            ring.append(bm.verts.new((cx + px, y, bot + hgt * 0.5 + pz - (hgt * 0.1 if pz < 0 else 0))))
        secs.append(ring)
    for j in range(n_sec - 1):
        a, b = secs[j], secs[j + 1]
        for i in range(n_ring):
            k = (i + 1) % n_ring
            bm.faces.new((a[i], a[k], b[k], b[i]))
    bm.faces.new(list(reversed(secs[0])))
    bm.faces.new(secs[-1])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def shoe_sole(sx):
    bm = _tb()
    cx = sx * 0.112
    pts = []
    for i in range(28):
        a = 2 * math.pi * i / 28
        y = -0.066 + 0.144 * math.sin(a)
        t = (0.075 - y) / 0.28
        w = 0.044 + 0.012 * g1(t - 0.68, 0.22)
        pts.append((cx + w * math.cos(a) * 1.02, y))
    bot = [bm.verts.new((x, y, 0.0)) for x, y in pts]
    top = [bm.verts.new((x, y, 0.022)) for x, y in pts]
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    for i in range(len(pts)):
        k = (i + 1) % len(pts)
        bm.faces.new((bot[i], bot[k], top[k], top[i]))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def cap_mesh():
    """Crown fitted over the skull + curved brim. Returns (crown_bm, brim_bm)."""
    crown = _tb()
    n_a, n_r = 36, 10
    sc = 1.04
    rows = []
    top = crown.verts.new(HC + Vector((0, 0, HCU * sc + 0.004)))
    for j in range(1, n_r + 1):
        ring = []
        for i in range(n_a):
            th = 2 * math.pi * i / n_a
            fy = math.sin(th)
            z_edge = cap_edge_z(fy)
            ce = (z_edge - HC.z) / (HCU * sc)
            ae = math.acos(max(min(ce, 1.0), -1.0))
            pol = ae * j / n_r
            u = Vector((math.sin(pol) * math.cos(th), math.sin(pol) * math.sin(th), math.cos(pol)))
            b = HB * (1.04 if u.y > 0 else 0.985)
            r = 1.0 / math.sqrt((u.x / HA) ** 2 + (u.y / b) ** 2 + (u.z / HCU) ** 2)
            pnt = HC + u * (r * sc + 0.002)
            if u.y < 0:
                pnt.y -= 0.01 * (-u.y) * smooth01(0.0, 0.8, u.z + 0.3)
                pnt.z += 0.006 * (-u.y) * smooth01(0.3, 0.9, u.z)
            ring.append(crown.verts.new(pnt))
        rows.append(ring)
    for i in range(n_a):
        crown.faces.new((top, rows[0][i], rows[0][(i + 1) % n_a]))
    for j in range(n_r - 1):
        for i in range(n_a):
            k = (i + 1) % n_a
            crown.faces.new((rows[j][i], rows[j + 1][i], rows[j + 1][k], rows[j][k]))
    bmesh.ops.recalc_face_normals(crown, faces=crown.faces)
    for f in crown.faces:
        if f.normal.dot(f.calc_center_median() - HC) < 0:
            f.normal_flip()
    edge = rows[-1]
    # brim: hangs off the front arc of the crown edge
    brim = _tb()
    n_u, n_v = 16, 5
    grid = []
    front_idx = [i for i in range(n_a) if math.sin(2 * math.pi * i / n_a) < -0.2]
    th0, th1 = math.radians(-148), math.radians(-32)
    for i in range(n_u + 1):
        th = th0 + (th1 - th0) * i / n_u
        # root point on crown edge (interpolate)
        fi = (th % (2 * math.pi)) / (2 * math.pi) * n_a
        i0 = int(fi) % n_a
        i1 = (i0 + 1) % n_a
        f = fi - int(fi)
        root = edge[i0].co.lerp(edge[i1].co, f)
        out = Vector((math.cos(th), math.sin(th) * 1.15, 0.0)).normalized()
        span = 0.088 * max(math.sin(-th), 0.0) ** 1.3
        col = []
        for j in range(n_v + 1):
            v = j / n_v
            side = abs(math.cos(th))
            p = root + out * (span * v) + Vector((0, 0, -0.01 * v - 0.004 * v * v - 0.02 * side * v))
            col.append(brim.verts.new(p))
        grid.append(col)
    for i in range(n_u):
        for j in range(n_v):
            brim.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    bmesh.ops.recalc_face_normals(brim, faces=brim.faces)
    for f in brim.faces:
        if f.normal.z < 0:
            f.normal_flip()
    bmesh.ops.solidify(brim, geom=list(brim.faces), thickness=0.006)
    return crown, brim


def hair_keep(c, n):
    if c.z < 1.57:
        return False
    q = c - HC
    phi = math.atan2(q.y, q.x)
    fy = math.sin(phi)
    line = 1.742 * smooth01(-0.55, -0.85, fy) + (1 - smooth01(-0.55, -0.85, fy)) * (
        1.648 * (1 - smooth01(0.1, 0.8, fy)) + 1.585 * smooth01(0.1, 0.8, fy))
    # keep ears clear
    if abs(c.x) > 0.07 and abs(c.y - 0.012) < 0.026 and c.z < 1.655:
        return False
    if c.z > cap_edge_z(fy) + 0.006:
        return False
    return c.z > line


def hair_line(co):
    q = co - HC
    fy = math.sin(math.atan2(q.y, q.x))
    return 1.742 * smooth01(-0.55, -0.85, fy) + (1 - smooth01(-0.55, -0.85, fy)) * (
        1.648 * (1 - smooth01(0.1, 0.8, fy)) + 1.585 * smooth01(0.1, 0.8, fy)), cap_edge_z(fy)


def hair_snap(co):
    lo, hi = hair_line(co)
    if abs(co.x) > 0.07 and abs(co.y - 0.012) < 0.03 and co.z < 1.662:
        return co
    if co.z < (lo + hi) / 2:
        co.z = lo
    else:
        co.z = hi + 0.002
    return co


def ear(sx):
    bm = p_sphere(1.0, 12, 8)
    for v in bm.verts:
        x, y, z = v.co
        if x > 0.2:
            v.co.x -= 0.55 * (x - 0.2) * g1(y, 0.6) * g1(z, 0.7)  # concha dent on the outer face
    bmesh.ops.scale(bm, vec=(0.011, 0.024, 0.033), verts=bm.verts)
    M = T((sx * (HA - 0.004), 0.016, 1.652), (math.radians(-8), 0, sx * math.radians(-14)), (1, 1, 1))
    if sx < 0:
        M = M @ T((0, 0, 0), (0, 0, 0), (-1, 1, 1))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def _sleeve_snap(co):
    a = arm_param(co)
    if a is None or co.z < 1.1:
        return co
    sx, t, dist, S, d = a
    if t > 0.1:
        return co - d * (t - 0.155)
    return co


def _trouser_snap(co):
    if co.z < 0.3:
        co.z = 0.076
    elif co.z > 0.9 and not is_arm(co):
        co.z = 1.034
    return co


def _glove_snap(co):
    a = arm_param(co)
    if a is None:
        return co
    sx, t, dist, S, d = a
    return co - d * (t - (WRIST_T - 0.004))


def belt_band(tr_bvh, z0=0.99, z1=1.027, n=56, off=0.0045):
    bm = _tb()
    rows = []
    for z in (z0, (z0 + z1) / 2, z1):
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            dvec = Vector((math.cos(a), math.sin(a), 0.0))
            hit = tr_bvh.ray_cast(Vector((0, 0, z)) + dvec * 0.5, -dvec)[0]
            p = (hit if hit else Vector((0, 0, z)) + dvec * 0.16) + dvec * off
            row.append(bm.verts.new(p))
        rows.append(row)
    for j in range(2):
        for i in range(n):
            k = (i + 1) % n
            bm.faces.new((rows[j][i], rows[j][k], rows[j + 1][k], rows[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y, 0))) < 0:
            f.normal_flip()
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.004)
    return bm


def surface_disc(bvh, x, z, radius, thick=0.0015, seg=16, lift=0.001):
    hit, n = surf_front(bvh, x, z)
    if not hit:
        return None
    d = pp_lib.p_cyl(radius, radius, thick, seg)
    q = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(d, matrix=T(hit + n * lift) @ q, verts=d.verts)
    return d


def _brow_keep(c, n):
    if n.y > -0.35 or c.y > 0:
        return False
    ax = abs(c.x)
    if not (0.016 < ax < 0.062):
        return False
    zc = 1.698 + 0.0045 * math.sin(math.pi * (ax - 0.016) / 0.046)
    half = 0.0048 - 0.0022 * smooth01(0.02, 0.06, ax)
    return abs(c.z - zc) < half


def _lip_keep(c, n):
    return n.y < -0.4 and c.y < 0 and abs(c.x) < 0.021 * (1 - 0.3 * abs(c.z - 1.6) / 0.012) and 1.588 < c.z < 1.611


def _copy_bm(bm):
    t = bm.copy()
    if not t.loops.layers.uv:
        t.loops.layers.uv.new("UVMap")
    return t


def assemble():
    P = palette()
    M = dict(
        skin=mat("Skin", "#D7A27A", 0.5),
        polo=mat("PoloBlue", "#2E6DB6", 0.72),
        khaki=mat("Khaki", "#C9B48A", 0.82),
        belt=mat("Belt", "#2B2420", 0.45),
        chrome=P["chrome"],
        cap=mat("CapWhite", "#F6F6F3", 0.62),
        hair=mat("HairBrown", "#3A281C", 0.65),
        shoe=mat("ShoeWhite", "#F3F3EF", 0.4),
        sole=P["ink"],
        cyan=P["cyan"],
        cream=P["cream"],
        eyew=mat("EyeWhite", "#F1EDE6", 0.25),
        iris=mat("Iris", "#4A3322", 0.3),
        pupil=mat("Pupil", "#0E0D0C", 0.2),
        lip=mat("Lip", "#8E5444", 0.5),
        glove=mat("GloveWhite", "#FAFAF7", 0.55),
    )
    body_ob = bpy.data.objects["PP_CalebBody"]
    src = bmesh.new()
    src.from_mesh(body_ob.data)
    src.normal_update()
    bvh = _bvh(src)

    polo_raw = region_shell(src, polo_keep, polo_offset, hem=0.0)
    col = collar(polo_raw, M["polo"])
    polo_raw.free()
    polo = region_shell(src, polo_keep, polo_offset, hem=0.011, snap=_sleeve_snap)
    pbvh = _bvh(polo)
    placket = region_shell(src, lambda c, n: abs(c.x) < 0.017 and 1.35 < c.z < 1.445 and c.y < 0 and n.y < -0.3,
                           lambda co, n: polo_offset(co, n) + 0.0028, hem=0.003)
    logo = surface_disc(pbvh, 0.085, 1.338, 0.012)
    trousers = region_shell(src, trouser_keep, trouser_offset, hem=0.012, snap=_trouser_snap)
    straighten_legs(trousers)
    belt = belt_band(_bvh(trousers))
    bbvh = _bvh(belt)
    hair = region_shell(src, hair_keep, lambda co, n: 0.0045, hem=0.004, snap=hair_snap)
    brows = region_shell(src, _brow_keep, lambda co, n: 0.0024, hem=0.0015)
    lips = region_shell(src, _lip_keep, lambda co, n: 0.0004)
    glove = region_shell(src, lambda c, n: is_arm(c) and arm_param(c)[0] > 0 and arm_param(c)[1] > WRIST_T - 0.004,
                         lambda co, n: 0.0026, hem=0.003, snap=_glove_snap)
    tab = region_shell(src, lambda c, n: is_arm(c) and arm_param(c)[0] > 0 and WRIST_T + 0.012 < arm_param(c)[1] <
                       WRIST_T + 0.045 and n.x > 0.45, lambda co, n: 0.0045, hem=0.0015)

    # hide what the clothes cover
    body = _copy_bm(src)

    def covered(c):
        if is_arm(c):
            a = arm_param(c)
            return 0.03 < a[1] < 0.125
        if abs(c.x) > 0.3:
            return False
        if c.z > 1.66:
            fy = math.sin(math.atan2(c.y - HC.y, c.x))
            return c.z > cap_edge_z(fy) + 0.014
        return (1.03 < c.z < 1.395) or (c.z < 0.975)
    delete_faces(body, covered)

    b = B("PP_Caleb")
    b.add(body, M["skin"])
    lips.free()
    mpts = []
    for i in range(9):
        x = -0.017 + 0.034 * i / 8
        z = 1.6005 + 0.0022 * (abs(x) / 0.017) ** 2
        hit, n = surf_front(bvh, x, z)
        if hit:
            mpts.append(hit + n * 0.0004)
    if len(mpts) > 3:
        b.add(pp_lib.p_path_tube(mpts, 0.0011, 6), M["lip"])
    b.add(polo, M["polo"])
    b.add(col, M["polo"])
    b.add(placket, M["polo"])
    if logo:
        b.add(logo, M["cyan"], smooth=False)
    b.add(trousers, M["khaki"])
    b.add(belt, M["belt"])
    b.add(hair, M["hair"])
    b.add(brows, M["hair"])
    b.add(glove, M["glove"])
    b.add(tab, M["cyan"])
    for k, z in enumerate((1.378, 1.414)):
        hit, n = surf_front(pbvh, 0.0, z)
        if hit:
            b.add(p_sphere(0.0048, 10, 6), M["cream"], T(hit + Vector((0, -0.003, 0)), (0, 0, 0), (1, 0.5, 1)))
    hit, n = surf_front(bbvh, 0.0, 1.007)
    if hit:
        bk = pp_lib.p_box(0.046, 0.008, 0.03, bevel=0.003, base=False)
        b.add(bk, M["chrome"], T(hit + Vector((0, -0.003, 0))), smooth=False)
    # eyes
    for sx in (-1, 1):
        hit, n = surf_front(bvh, sx * 0.036, 1.673)
        if not hit:
            continue
        r = 0.0142
        cc = hit + Vector((0, 0.0088, 0))
        b.add(p_sphere(r, 16, 10), M["eyew"], T(cc))
        b.add(p_sphere(1.0, 12, 6), M["iris"], T(cc + Vector((sx * -0.001, -r + 0.0016, 0.0005)), (0, 0, 0),
                                                  (0.0074, 0.0026, 0.0078)))
        b.add(p_sphere(1.0, 10, 6), M["pupil"], T(cc + Vector((sx * -0.001, -r + 0.0006, 0.0005)), (0, 0, 0),
                                                   (0.0036, 0.0016, 0.0038)))
        lid = p_sphere(r * 1.12, 16, 10)
        bmesh.ops.delete(lid, geom=[v for v in lid.verts if v.co.z < r * 0.42], context="VERTS")
        b.add(lid, M["skin"], T(cc, (math.radians(10), 0, 0)))
        b.add(ear(sx), M["skin"])
    # cap
    crown, brim = cap_mesh()
    b.add(crown, M["cap"])
    b.add(brim, M["cap"])
    b.add(p_sphere(0.009, 10, 5), M["cyan"], T(HC + Vector((0, 0, HCU * 1.045 + 0.004)), (0, 0, 0), (1, 1, 0.5)))
    cb = _bvh(_tmp := cap_mesh()[0])
    hit, n = surf_front(cb, 0.0, 1.77)
    if hit:
        badge = pp_lib.p_box(0.05, 0.004, 0.026, bevel=0.003, base=False)
        tilt = math.atan2(-n.z, -n.y)
        b.add(badge, M["cyan"], T(hit + n * 0.002, (-tilt, 0, 0)), smooth=False)
    _tmp.free()
    # shoes
    for sx in (-1, 1):
        sh = loft_shoe(sx)
        cx = sx * 0.112
        stripe, upper = [], []
        for f in sh.faces:
            c = f.calc_center_median()
            (stripe if (abs(c.x - cx) > 0.03 and 0.034 < c.z < 0.058 and -0.12 < c.y < 0.04) else upper).append(f.index)
        sbvh = _bvh(sh)
        up_bm = sh.copy()
        up_bm.faces.ensure_lookup_table()
        bmesh.ops.delete(up_bm, geom=[up_bm.faces[i] for i in stripe], context="FACES")
        st_bm = sh.copy()
        st_bm.faces.ensure_lookup_table()
        bmesh.ops.delete(st_bm, geom=[st_bm.faces[i] for i in upper], context="FACES")
        b.add(up_bm, M["shoe"])
        b.add(st_bm, M["cyan"])
        b.add(shoe_sole(sx), M["sole"], smooth=False)
        for k in range(4):
            y = -0.015 - k * 0.022
            hit = sbvh.ray_cast(Vector((cx, y, 0.3)), Vector((0, 0, -1)))[0]
            if hit:
                lace = pp_lib.p_box(0.034, 0.006, 0.004, bevel=0.0015, base=False)
                b.add(lace, M["cream"], T(hit + Vector((0, 0, 0.0015)), (math.radians(-18), 0, 0)), smooth=False)
        sh.free()
    src.free()
    c = pp_lib.asset_coll("PP_Caleb")
    ob = b.finish(c, angle=75, ground="z")
    return ob
