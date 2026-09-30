"""Pro Shop golf bags: Stand, Cart and Tour.

Origin at the base centre, front (pockets) toward -Y. Every bag uses exactly these material slots, in this order:
Bag_Body, Bag_Accent, Bag_Trim, Bag_Strap, Bag_Metal, Bag_Panel, HeadCover, HeadCover_Accent, Club_Steel,
Club_Grip, Club_Shaft.  Club grips sit inside the bag; Club_Grip is used for the ferrules and rubber feet.
"""
import math

import bmesh
from mathutils import Matrix, Vector

from pp_lib import T, _tb, displace, p_box, p_cyl, p_ico, p_path_tube, p_sphere, p_tube, rad
from pp_proshop import *  # noqa: F401,F403
from pp_proshop_items import club_mb


class BagShape:
    """Slightly tapered oval tube: radius r0 at the base top (zb) growing to r1 at the collar (zc); rim at zt."""

    def __init__(self, r0, r1, oy, zb, zc, zt):
        self.r0, self.r1, self.oy, self.zb, self.zc, self.zt = r0, r1, oy, zb, zc, zt

    def R(self, z):
        return self.r0 + (self.r1 - self.r0) * clamp01((z - self.zb) / (self.zc - self.zb))

    def at(self, a, r, z):
        a = rad(a)
        return Vector((r * math.sin(a), -r * math.cos(a) * self.oy, z))

    def surf(self, a, z, off=0.0):
        return self.at(a, self.R(z) + off, z)

    def outward(self, a):
        a = rad(a)
        return Vector((math.sin(a), -math.cos(a) / self.oy, 0.0)).normalized()


def oval(tb, S):
    bmesh.ops.scale(tb, vec=(1.0, S.oy, 1.0), verts=tb.verts)
    return tb


def bent_box(S, a_c, width, z0, z1, depth, bevel=0.01, bulge=0.0, nu=8, nv=4, ref=None):
    """Bevelled box wrapped onto the bag surface. Local -y (the box front) becomes the outward direction."""
    tb = p_box(width, depth, z1 - z0, bevel=min(bevel, depth * 0.45, (z1 - z0) * 0.45), seg=2, base=True)
    bmesh.ops.translate(tb, vec=(0, 0, z0), verts=tb.verts)
    for i in range(1, nu):
        x = -width / 2 + width * i / nu
        bmesh.ops.bisect_plane(tb, geom=list(tb.verts) + list(tb.edges) + list(tb.faces), plane_co=(x, 0, 0),
                               plane_no=(1, 0, 0))
    for j in range(1, nv):
        z = z0 + (z1 - z0) * j / nv
        bmesh.ops.bisect_plane(tb, geom=list(tb.verts) + list(tb.edges) + list(tb.faces), plane_co=(0, 0, z),
                               plane_no=(0, 0, 1))
    W, zm, hz = ref or (width, (z0 + z1) / 2, (z1 - z0) / 2)
    for v in tb.verts:
        x, y, z = v.co
        w = depth / 2 - y
        if bulge:
            u = clamp01(1 - (2 * x / W) ** 2)
            t = clamp01(1 - ((z - zm) / hz) ** 2)
            w += bulge * u * t * (w / depth)
        r = S.R(z) - 0.004 + w
        v.co = S.at(a_c + math.degrees(x / S.R(z)), r, z)
    tb.normal_update()
    return tb


def pocket(b, S, M, a_c, width, z0, z1, depth, mat="Bag_Body", bulge=0.012, piping=None, zip_top=True,
           zip_side=None):
    ref = (width, (z0 + z1) / 2, (z1 - z0) / 2)
    if piping:
        b.add(bent_box(S, a_c, width + 0.012, z0 - 0.006, z1 + 0.006, depth - 0.006, 0.004, 0.0, 8, 3), M[piping])
    b.add(bent_box(S, a_c, width, z0, z1, depth, 0.012, bulge, 8, 4, ref), M[mat])
    R = S.R(z1)
    if zip_top:
        zz = z1 - 0.022
        b.add(bent_box(S, a_c, width * 0.84, zz, zz + 0.009, depth + 0.0035, 0.002, bulge, 8, 1, ref), M["Bag_Accent"])
        a_p = a_c + math.degrees(width * 0.4 / R)
        p = S.at(a_p, S.R(zz) - 0.004 + depth + 0.005, zz - 0.012)
        b.add(bx(-0.005, 0.005, -0.003, 0.003, 0.0, 0.024, 0.0015), M["Bag_Metal"], T(p, (0, 0, rad(a_p))))
    if zip_side:
        a_z = a_c + math.degrees(zip_side * width * 0.4 / R)
        b.add(bent_box(S, a_z, 0.009, z0 + 0.02, z1 - 0.02, depth + 0.0035, 0.002, 0.0, 1, 6), M["Bag_Accent"])
        p = S.at(a_z, R - 0.004 + depth + 0.005, z1 - 0.045)
        b.add(bx(-0.005, 0.005, -0.003, 0.003, 0.0, 0.024, 0.0015), M["Bag_Metal"], T(p, (0, 0, rad(a_z))))


def ribbon(pts, widths, thick, nrms):
    tb = _tb()
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        nn = nrms[i] - t * nrms[i].dot(t)
        nn.normalize()
        bb = t.cross(nn).normalized() * (widths[i] / 2)
        rings.append([tb.verts.new(p + bb), tb.verts.new(p - bb), tb.verts.new(p - bb + nn * thick),
                      tb.verts.new(p + bb + nn * thick)])
    for i in range(n - 1):
        a, c = rings[i], rings[i + 1]
        for k in range(4):
            j = (k + 1) % 4
            tb.faces.new((a[k], a[j], c[j], c[k]))
    tb.faces.new(list(reversed(rings[0])))
    tb.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces)
    return tb


def strap(b, S, M, a0, z0, a1, z1, lift, w=0.045, pad=0.065, thick=0.012, n=12, mat="Bag_Strap"):
    P0, P2 = S.surf(a0, z0, 0.004), S.surf(a1, z1, 0.004)
    am, zm = (a0 + a1) / 2, (z0 + z1) / 2
    P1 = S.surf(am, zm, lift * 2)
    pts, nrms, ws = [], [], []
    for i in range(n + 1):
        t = i / n
        p = P0 * (1 - t) ** 2 + P1 * 2 * t * (1 - t) + P2 * t * t
        pts.append(p)
        nrms.append(S.outward(math.degrees(math.atan2(p.x, -p.y / S.oy))))
        ws.append(w + (pad - w) * math.sin(t * math.pi) ** 2)
    b.add(ribbon(pts, ws, thick, nrms), M[mat])
    for a, z in ((a0, z0), (a1, z1)):
        p = S.surf(a, z, 0.0)
        b.add(bx(-w * 0.6, w * 0.6, -0.012, 0.004, -0.025, 0.025, 0.004), M["Bag_Accent"], T(p, (0, 0, rad(a))))


def bag_core(b, S, M, seg=32):
    r0, r1, zb, zc, zt = S.r0, S.r1, S.zb, S.zc, S.zt
    b.add(oval(p_tube([(r0 - 0.012, 0.0), (r0 + 0.006, 0.006), (r0 + 0.01, 0.022), (r0 + 0.009, zb),
                       (r0 + 0.002, zb + 0.004)], seg), S), M["Bag_Accent"])
    b.add(oval(p_tube([(r0 + 0.003, zb - 0.004), (r0 + 0.0055, zb), (r0 + 0.0055, zb + 0.012),
                       (r0 + 0.0025, zb + 0.016)], seg, caps=False), S), M["Bag_Trim"])
    zs = [zb + (zc - zb) * k / 8 for k in range(9)]
    b.add(oval(p_tube([(S.R(z), z) for z in zs], seg, caps=False), S), M["Bag_Body"])
    b.add(oval(p_tube([(r1 - 0.001, zc - 0.006), (r1 + 0.008, zc + 0.01), (r1 + 0.009, zt - 0.03),
                       (r1 + 0.006, zt - 0.02)], seg, caps=False), S), M["Bag_Accent"])
    b.add(oval(p_tube([(r1 + 0.005, zt - 0.022), (r1 + 0.012, zt - 0.013), (r1 + 0.012, zt - 0.003),
                       (r1 + 0.004, zt + 0.004), (r1 - 0.006, zt), (r1 - 0.008, zt - 0.012)], seg, caps=False), S),
          M["Bag_Trim"])
    b.add(oval(p_tube([(r1 - 0.008, zt - 0.012), (r1 - 0.012, zt - 0.03), (0.0, zt - 0.03)], seg, caps=False), S),
          M["Bag_Accent"])
    yb = r1 * 0.22 * S.oy
    xl = math.sqrt(max(0.0, 1 - (yb / (r1 * S.oy)) ** 2)) * (r1 - 0.01)
    b.add(bx(-xl, xl, yb - 0.009, yb + 0.009, zt - 0.032, zt - 0.012, 0.005, 2), M["Bag_Accent"])
    for sx in (-1, 1):
        x = sx * r1 * 0.36
        yl = math.sqrt(max(0.0, 1 - (x / (r1 - 0.01)) ** 2)) * (r1 - 0.01) * S.oy
        b.add(bx(x - 0.008, x + 0.008, -yl, yb, zt - 0.032, zt - 0.014, 0.005, 2), M["Bag_Accent"])


def handle(b, S, M, a, z0, z1, lift=0.03):
    strap(b, S, M, a, z0, a, z1, lift, w=0.032, pad=0.04, thick=0.014, n=8)


# ---------------------------------------------------------------- clubs & headcovers
def lean(p, deg):
    """Rotation tilting +Z outward (away from the bag axis) by deg, based on the xy position p."""
    v = Vector((p[0], p[1], 0.0))
    if v.length < 1e-4:
        return Matrix.Identity(4)
    axis = Vector((0, 0, 1)).cross(v.normalized())
    return Matrix.Rotation(rad(deg), 4, axis)


def place_club(b, S, M, kind, xy, stick, spin, tilt):
    """Club head-up in the bag: shaft vertical through xy at the rim, head `stick` m above the rim."""
    mb, _, d = club_mb(kind, M, "InBag_" + kind, lod=1, shaft_len=stick + 0.1)
    remap = {"Club_Head": M["Club_Steel"], "Club_Accent": M["Bag_Trim"]}
    mb.mats = [remap.get(m.name, m) for m in mb.mats]
    hosel = {"wood": Vector((0.012, 0.01, 0.022)), "iron": Vector((0.004, 0.006, 0.012)),
             "putter": Vector((0.0, 0.004, 0.062))}[CLUB_SPECS_HEAD[kind]]
    align = d.rotation_difference(Vector((0, 0, -1))).to_matrix().to_4x4()
    Lm = lean(xy, tilt)
    top = Vector((xy[0], xy[1], S.zt)) + Lm @ Vector((0, 0, stick))
    Mc = Matrix.Translation(top) @ Lm @ Matrix.Rotation(rad(spin), 4, "Z") @ align @ Matrix.Translation(-hosel)
    b.merge(mb, Mc)


CLUB_SPECS_HEAD = {"Driver": "wood", "Wood": "wood", "Iron": "iron", "Wedge": "iron", "Putter": "putter"}


def headcover(b, S, M, xy, h, size, spin, tilt, seg=12):
    Lm = lean(xy, tilt)
    Mh = Matrix.Translation((xy[0], xy[1], S.zt)) @ Lm @ Matrix.Rotation(rad(spin), 4, "Z")
    HC, HA = M["HeadCover"], M["HeadCover_Accent"]
    b.add(p_tube([(0.024, -0.09), (0.025, 0.0), (0.0255, h * 0.5), (0.027, h * 0.8), (0.03, h + 0.01)], seg,
                 caps=False), HC, Mh.copy())
    b.add(p_tube([(0.0262, -0.004), (0.0272, 0.002), (0.0272, 0.03), (0.0262, 0.036)], seg, caps=False), HA, Mh.copy())
    for z in (h * 0.36, h * 0.52):
        b.add(p_tube([(0.0262, z), (0.0268, z + 0.004), (0.0268, z + 0.018), (0.0262, z + 0.022)], seg, caps=False),
              HA, Mh.copy())
    cx, cz = 0.026 * size, h + 0.035 * size
    blob = p_sphere(1.0, 16, 10)
    for v in blob.verts:
        v.co.z *= 1.0 - 0.18 * max(0.0, -v.co.z)
    b.add(blob, HC, Mh @ T((cx, 0.0, cz), (0, rad(-8), 0), (0.07 * size, 0.058 * size, 0.05 * size)))
    b.add(p_tube([(0.03, h - 0.004), (0.034, h + 0.004), (0.033, h + 0.016), (0.029, h + 0.022)], seg, caps=False),
          HA, Mh.copy())
    pom = p_ico(1.0, 2)
    displace(pom, 0.18, 6.0, size * 10)
    b.add(pom, HA, Mh @ T((cx, 0.0, cz + 0.047 * size), (0, 0, 0), 0.02 * size))


def bag_clubs(b, S, M):
    k = S.r1 / 0.116
    oy = S.oy
    covers = [((-0.056 * k, 0.062 * k * oy), 0.25, 1.0, 200, 8), ((0.0, 0.07 * k * oy), 0.215, 0.86, 160, 5),
              ((0.056 * k, 0.062 * k * oy), 0.19, 0.76, 120, 8)]
    for xy, h, size, spin, tilt in covers:
        headcover(b, S, M, xy, h, size, spin, tilt)
    irons = [("Iron", (-0.082 * k, 0.004 * k), 0.19, 205), ("Iron", (-0.058 * k, 0.0), 0.2, 195),
             ("Iron", (-0.014 * k, 0.0), 0.205, 185), ("Iron", (0.016 * k, 0.0), 0.21, 175),
             ("Iron", (0.066 * k, 0.004 * k), 0.215, 165),
             ("Wedge", (-0.03 * k, -0.055 * k * oy), 0.17, 210), ("Wedge", (0.0, -0.062 * k * oy), 0.175, 190)]
    for kind, xy, stick, spin in irons:
        place_club(b, S, M, kind, xy, stick, spin, 6.0)
    place_club(b, S, M, "Putter", (0.068 * k, -0.05 * k * oy), 0.16, 150, 7.0)


# ---------------------------------------------------------------- bag styles
def build_stand(M):
    S = BagShape(0.108, 0.116, 0.86, 0.085, 0.745, 0.86)
    b = MB("GolfBag_Stand")
    bag_core(b, S, M)
    pocket(b, S, M, 0.0, 0.15, 0.26, 0.46, 0.042, bulge=0.014, piping="Bag_Trim")
    pocket(b, S, M, 0.0, 0.12, 0.5, 0.62, 0.014, mat="Bag_Panel", bulge=0.0, zip_top=False)
    pocket(b, S, M, -62.0, 0.12, 0.17, 0.6, 0.034, bulge=0.012, zip_top=False, zip_side=1)
    pocket(b, S, M, 62.0, 0.09, 0.5, 0.64, 0.026, bulge=0.008)
    pocket(b, S, M, 0.0, 0.13, 0.11, 0.2, 0.022, mat="Bag_Accent", bulge=0.0)
    for s in (-1, 1):
        strap(b, S, M, 180 + s * 24, S.zc - 0.04, 180 + s * 44, 0.42, 0.075)
    handle(b, S, M, 90.0, 0.5, 0.66)
    for s in (-1, 1):
        a = 180 + s * 30
        top = S.surf(a, S.zc - 0.055, 0.016)
        foot = S.surf(a + s * 6, 0.03, 0.03)
        b.add(rod(top, foot, 0.0055, 8), M["Bag_Metal"])
        b.add(p_cyl(0.009, 0.008, 0.03, 8), M["Club_Grip"], Matrix.Translation(foot - Vector((0, 0, 0.028))))
        b.add(bx(-0.014, 0.014, -0.02, 0.006, -0.03, 0.03, 0.004), M["Bag_Accent"], T(top, (0, 0, rad(a))))
    kick = S.surf(180.0, 0.02, 0.0)
    b.add(bx(-0.045, 0.045, -0.035, 0.004, 0.0, 0.07, 0.008, 2), M["Bag_Accent"], T(kick, (0, 0, rad(180))))
    bag_clubs(b, S, M)
    return b


def build_cart(M):
    S = BagShape(0.136, 0.145, 0.9, 0.11, 0.765, 0.9)
    b = MB("GolfBag_Cart")
    bag_core(b, S, M, 36)
    pocket(b, S, M, 0.0, 0.2, 0.22, 0.6, 0.048, bulge=0.02, piping="Bag_Trim")
    pocket(b, S, M, 0.0, 0.17, 0.635, 0.715, 0.012, mat="Bag_Panel", bulge=0.0, zip_top=False)
    pocket(b, S, M, -58.0, 0.12, 0.3, 0.52, 0.042, bulge=0.014, mat="Bag_Accent")
    pocket(b, S, M, 58.0, 0.12, 0.5, 0.66, 0.03, bulge=0.01)
    pocket(b, S, M, 58.0, 0.13, 0.2, 0.44, 0.04, bulge=0.014, zip_top=False, zip_side=1)
    pocket(b, S, M, 0.0, 0.18, 0.13, 0.2, 0.024, mat="Bag_Accent", bulge=0.0)
    ut = 125.0
    b.add(rod(S.surf(ut, 0.13, 0.018), S.surf(ut, S.zt - 0.03, 0.02), 0.022, 12), M["Bag_Accent"])
    b.add(p_cyl(0.025, 0.024, 0.03, 12), M["Bag_Trim"], Matrix.Translation(S.surf(ut, S.zt - 0.03, 0.02)))
    tr = S.surf(-105.0, S.zc - 0.07, 0.0)
    out = S.outward(-105.0)
    b.add(bx(-0.01, 0.01, -0.02, 0.004, -0.02, 0.02, 0.003), M["Bag_Accent"], T(tr, (0, 0, rad(-105))))
    ring = [tr + out * 0.03 + Vector((0, 0, 0.0)) + (out * math.cos(t) + Vector((0, 0, 1)) * math.sin(t)) * 0.026
            for t in [2 * math.pi * i / 16 for i in range(17)]]
    b.add(p_path_tube(ring, 0.0038, 8, caps=False), M["Bag_Metal"])
    strap(b, S, M, 200.0, S.zc - 0.05, 200.0, 0.3, 0.05, w=0.05, pad=0.07)
    handle(b, S, M, 160.0, S.zc - 0.16, S.zc - 0.02)
    handle(b, S, M, -90.0, 0.52, 0.68)
    bag_clubs(b, S, M)
    return b


def build_tour(M):
    S = BagShape(0.148, 0.156, 0.92, 0.12, 0.745, 0.935)
    b = MB("GolfBag_Tour")
    bag_core(b, S, M, 40)
    b.add(oval(p_tube([(S.r0 + 0.011, 0.018), (S.r0 + 0.014, 0.024), (S.r0 + 0.014, 0.05), (S.r0 + 0.011, 0.056)],
                      40, caps=False), S), M["Bag_Metal"])
    pocket(b, S, M, 0.0, 0.24, 0.29, 0.68, 0.022, mat="Bag_Panel", bulge=0.006, piping="Bag_Trim", zip_top=False)
    pocket(b, S, M, 0.0, 0.2, 0.15, 0.26, 0.036, bulge=0.01, piping="Bag_Trim")
    for s in (-1, 1):
        pocket(b, S, M, s * 72.0, 0.14, 0.2, 0.56, 0.046, bulge=0.016, mat="Bag_Accent" if s > 0 else "Bag_Body",
               zip_top=s > 0, zip_side=None if s > 0 else 1)
    pocket(b, S, M, -72.0, 0.1, 0.6, 0.7, 0.024, bulge=0.006)
    strap(b, S, M, 205.0, S.zc - 0.05, 190.0, 0.3, 0.06, w=0.058, pad=0.08, thick=0.016)
    handle(b, S, M, 160.0, S.zc - 0.17, S.zc - 0.02)
    handle(b, S, M, 90.0, 0.5, 0.68)
    bag_clubs(b, S, M)
    return b


def build_bags(M):
    C = new_coll("PS_Bags")
    out = {}
    order = [M[n] for n in BAG_SLOTS]
    for fn in (build_stand, build_cart, build_tour):
        b = fn(M)
        ob = b.finish(C, angle=34, order=order)
        MANIFEST.setdefault("items", {})[ob.name] = dict(tris=obj_tris(ob))
        out[ob.name] = [ob]
    return out
