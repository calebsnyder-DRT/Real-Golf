"""Golf clubs held by the course golfer: driver, 7-iron, wedge and putter.

Each club is modelled in an *address frame* (sole on z=0, face square to +X = target, toe toward -Y, shaft
rising toward the golfer at the lie angle) and then expressed in the ClubGrip.L bone frame:
  bone +Y = down the shaft toward the head, bone +Z = face normal (square), origin = lead-hand grip point.
The character rigs carry a ClubGrip.L bone under Wrist.L with that frame, and every club mesh is skinned 100% to it.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

# length (sole heel to butt, m), lie (deg), loft (deg)
CLUBS = {
    "Driver": dict(length=1.156, lie=58.0, loft=10.5),
    "Iron": dict(length=0.940, lie=62.5, loft=34.0),
    "Wedge": dict(length=0.895, lie=64.0, loft=56.0),
    "Putter": dict(length=0.864, lie=70.0, loft=3.0),
}
GRIP_FROM_BUTT = 0.055
GRIP_LEN = 0.27


def _mat(name, rgb, rough, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*rgb, 1)
    return m


def materials():
    return {
        "grip": _mat("Mat_ClubGrip", (0.018, 0.018, 0.02), 0.75),
        "shaft": _mat("Mat_ClubShaft", (0.85, 0.86, 0.88), 0.18, 1.0),
        "head": _mat("Mat_ClubHead", (0.74, 0.75, 0.77), 0.28, 1.0),
        "crown": _mat("Mat_ClubCrown", (0.012, 0.013, 0.016), 0.22),
        "accent": _mat("Mat_ClubAccent", (0.0, 0.6, 0.72), 0.35),
    }


def _ring(bm, center, axis, radius, seg, ref=None):
    axis = axis.normalized()
    ref = ref or (Vector((1, 0, 0)) if abs(axis.x) < 0.9 else Vector((0, 1, 0)))
    a = axis.cross(ref).normalized()
    b = axis.cross(a).normalized()
    return [bm.verts.new(center + (a * math.cos(2 * math.pi * i / seg) + b * math.sin(2 * math.pi * i / seg)) * radius)
            for i in range(seg)]


def _tube(bm, points, radii, seg, mi, cap_start=True, cap_end=True):
    rings = []
    for i, p in enumerate(points):
        d = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        rings.append(_ring(bm, p, d, radii[i], seg, ref=Vector((1, 0, 0))))
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(seg):
            f = bm.faces.new((r0[i], r0[(i + 1) % seg], r1[(i + 1) % seg], r1[i]))
            f.material_index = mi
            f.smooth = True
    if cap_start:
        f = bm.faces.new(list(reversed(rings[0])))
        f.material_index = mi
    if cap_end:
        f = bm.faces.new(rings[-1])
        f.material_index = mi
    return rings


def _shaft(bm, hosel_top, butt, u, mats_idx):
    """Grip + tapered steel shaft from the hosel top to the butt end."""
    grip_start = butt - u * GRIP_LEN
    n = 7
    pts = [hosel_top + (grip_start - hosel_top) * (i / n) for i in range(n + 1)]
    radii = [0.0046 + (0.0072 - 0.0046) * (i / n) for i in range(n + 1)]
    _tube(bm, pts, radii, 10, mats_idx["shaft"], cap_start=False, cap_end=False)
    gp = [grip_start + (butt - grip_start) * t for t in (0.0, 0.08, 0.5, 0.92, 0.985, 1.0)]
    gr = [0.0078, 0.0102, 0.0118, 0.0128, 0.0125, 0.0095]
    _tube(bm, gp, gr, 14, mats_idx["grip"], cap_start=True, cap_end=True)


def _blade_head(bm, spec, mi, wedge=False, putter=False):
    """Iron/wedge/putter head as an extruded face outline, lofted back about the leading edge."""
    loft = math.radians(spec["loft"])
    if putter:
        outline = [(0.035, 0.0), (-0.065, 0.0), (-0.068, 0.006), (-0.068, 0.020), (-0.064, 0.024), (0.030, 0.024), (0.035, 0.018)]
        depth_bottom, depth_top = 0.026, 0.024
    elif wedge:
        outline = [(0.028, 0.002), (0.0, 0.0), (-0.045, 0.0), (-0.062, 0.008), (-0.07, 0.03), (-0.066, 0.05), (-0.05, 0.058),
                   (-0.01, 0.046), (0.022, 0.032), (0.03, 0.018)]
        depth_bottom, depth_top = 0.022, 0.007
    else:
        outline = [(0.028, 0.002), (0.0, 0.0), (-0.045, 0.0), (-0.06, 0.006), (-0.066, 0.024), (-0.062, 0.042), (-0.05, 0.048),
                   (-0.01, 0.038), (0.022, 0.026), (0.03, 0.016)]
        depth_bottom, depth_top = 0.021, 0.006
    zmax = max(z for _, z in outline)
    front = [bm.verts.new((0.0, y, z)) for y, z in outline]
    back = []
    for (y, z) in outline:
        t = z / zmax
        back.append(bm.verts.new((-(depth_bottom + (depth_top - depth_bottom) * t), y, z)))
    n = len(outline)
    f = bm.faces.new(list(reversed(front)))
    f.material_index = mi
    f = bm.faces.new(back)
    f.material_index = mi
    for i in range(n):
        f = bm.faces.new((front[i], front[(i + 1) % n], back[(i + 1) % n], back[i]))
        f.material_index = mi
    verts = front + back
    # Loft: tilt the face back about the leading edge (y axis at x=0, z=0).
    R = Matrix.Rotation(-loft, 4, "Y")
    for v in verts:
        v.co = R @ v.co
    return verts


def _driver_head(bm, mi_head, mi_crown):
    """460cc-ish driver: rounded triangle in plan, flat-ish face at x=0, crown dark."""
    seg_u, seg_v = 24, 10
    cx, cy, cz = -0.058, -0.052, 0.031
    rx, ry, rz = 0.058, 0.062, 0.031
    rows = []
    for j in range(seg_v + 1):
        phi = math.pi * j / seg_v - math.pi / 2
        row = []
        for i in range(seg_u):
            th = 2 * math.pi * i / seg_u
            x = math.cos(phi) * math.cos(th)
            y = math.cos(phi) * math.sin(th)
            z = math.sin(phi)
            # flatten face (front, +x) and sole
            px = cx + rx * (min(x, 0.82) if x > 0 else x * 1.08)
            py = cy + ry * y * (1.0 - 0.18 * max(0.0, -x))
            pz = cz + rz * (z if z > -0.72 else -0.72 - (z + 0.72) * 0.2)
            row.append(bm.verts.new((px, py, pz)))
        rows.append(row)
    for j in range(seg_v):
        for i in range(seg_u):
            q = (rows[j][i], rows[j][(i + 1) % seg_u], rows[j + 1][(i + 1) % seg_u], rows[j + 1][i])
            f = bm.faces.new(q)
            zc = sum(v.co.z for v in q) / 4
            xc = sum(v.co.x for v in q) / 4
            f.material_index = mi_crown if (zc > cz + rz * 0.35 and xc < -0.012) else mi_head
            f.smooth = True
    loft = math.radians(10.5)
    R = Matrix.Rotation(-loft * 0.5, 4, "Y")
    for row in rows:
        for v in row:
            v.co = R @ v.co


def build(name, spec, coll, mats):
    """Returns the club object, modelled in the ClubGrip.L bone frame (see module doc)."""
    bm = bmesh.new()
    order = ["grip", "shaft", "head", "crown", "accent"]
    idx = {k: i for i, k in enumerate(order)}
    lie = math.radians(spec["lie"])
    u = Vector((0.0, math.cos(lie), math.sin(lie)))
    if name == "Driver":
        _driver_head(bm, idx["head"], idx["crown"])
        hosel_base = Vector((-0.012, 0.008, 0.045))
    elif name == "Putter":
        _blade_head(bm, spec, idx["head"], putter=True)
        hosel_base = Vector((-0.012, 0.022, 0.022))
    else:
        _blade_head(bm, spec, idx["head"], wedge=(name == "Wedge"))
        hosel_base = Vector((-0.008, 0.026, 0.02))
    hosel_top = hosel_base + u * (0.055 if name != "Putter" else 0.07)
    _tube(bm, [hosel_base, hosel_top], [0.0068, 0.0058], 10, idx["head"], cap_start=True, cap_end=False)
    butt = hosel_base + u * (spec["length"] - 0.02)
    _shaft(bm, hosel_top, butt, u, idx)
    grip = butt - u * GRIP_FROM_BUTT
    # Address frame -> bone frame.
    yb = (-u).normalized()
    zb = Vector((1.0, 0.0, 0.0))
    xb = yb.cross(zb).normalized()
    B = Matrix((xb, yb, zb)).transposed().to_4x4()
    B.translation = grip
    bmesh.ops.transform(bm, matrix=B.inverted(), verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Club_" + name)
    bm.to_mesh(me)
    bm.free()
    for k in order:
        me.materials.append(mats[k])
    ob = bpy.data.objects.new("Club_" + name, me)
    coll.objects.link(ob)
    ob["pp_club_lie"] = spec["lie"]
    ob["pp_club_length"] = spec["length"]
    # Clubhead sole contact point in the bone frame (for placing the ball).
    sole = B.inverted() @ Vector((-0.004, -0.02 if name != "Driver" else -0.05, 0.0))
    ob["pp_club_sole"] = list(sole)
    return ob


def build_all(coll):
    mats = materials()
    return {n: build(n, s, coll, mats) for n, s in CLUBS.items()}
