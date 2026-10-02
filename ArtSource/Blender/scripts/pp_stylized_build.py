"""Stylized golfer build: head graft, cosmetic part split + variants, decimation, UVs, textures.

    Blender -b --factory-startup --python pp_stylized_build.py -- Caleb

Input: stylized/{Name}_colors.blend (pp_stylized.py prep + colors). Output: stylized/{Name}_parts.blend with the
modular part objects (Head_{Name}, Body_Arms, Top_Polo, Top_Tee, Bottom_Chinos, Bottom_Shorts, Socks_Ankle,
Socks_Crew, Shoes_Golf, Belt) named and materialled for the GolferAppearance role contract, and textures in
ArtSource/Exports/Characters/{Name}/Textures/.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pp_stylized as S  # noqa: E402
import pp_stylized_proj as PJ  # noqa: E402

EXPORTS = os.path.join(os.path.dirname(S.ROOT), "Exports", "Characters")
SKIN_CLASSES = (S.FACE, S.ARMS, S.LEGS, S.UNKNOWN)
# Triangle budgets (default outfit ~30k incl. hands).
BODY_TRIS = 22000
HEAD_TRIS = 9000


# ---------------------------------------------------------------------------------------------- mesh helpers

def vattr(ob, name):
    a = ob.data.attributes[name]
    out = np.empty(len(a.data), dtype=np.int32)
    a.data.foreach_get("value", out)
    return out


def vcolors(ob, name="proj"):
    ca = ob.data.color_attributes[name]
    out = np.empty(len(ca.data) * 4, dtype=np.float32)
    ca.data.foreach_get("color", out)
    return out.reshape(-1, 4)


def face_centers(ob):
    c = np.empty(len(ob.data.polygons) * 3)
    ob.data.polygons.foreach_get("center", c)
    return c.reshape(-1, 3)


def face_normals(ob):
    n = np.empty(len(ob.data.polygons) * 3)
    ob.data.polygons.foreach_get("normal", n)
    return n.reshape(-1, 3)


def delete_verts(ob, mask):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(mask)[0]], context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def delete_faces(ob, mask):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(mask)[0]], context="FACES")
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def keep_islands(ob, min_frac=0.01):
    """Drops connected pieces smaller than min_frac of the vertex count."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    seen = np.zeros(len(bm.verts), bool)
    kill = []
    for v0 in bm.verts:
        if seen[v0.index]:
            continue
        stack, comp = [v0], []
        seen[v0.index] = True
        while stack:
            v = stack.pop()
            comp.append(v)
            for e in v.link_edges:
                o = e.other_vert(v)
                if not seen[o.index]:
                    seen[o.index] = True
                    stack.append(o)
        if len(comp) < min_frac * len(bm.verts):
            kill.extend(comp)
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def apply_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    me.name = ob.name
    bpy.data.meshes.remove(old)


def decimate(ob, target_tris):
    m = ob.modifiers.new("dec", "DECIMATE")
    m.ratio = min(1.0, target_tris / max(tri_count(ob), 1))
    m.use_collapse_triangulate = True
    apply_modifiers(ob)


def kd_of(P):
    kd = KDTree(len(P))
    for i, p in enumerate(P):
        kd.insert(p, i)
    kd.balance()
    return kd


def nearest(kd, Q):
    return np.array([kd.find(q)[1] for q in Q])


def face_neighbours(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    nb = [[g.index for e in f.edges for g in e.link_faces if g is not f] for f in bm.faces]
    bm.free()
    return nb


def majority(cls, nb, iters=3):
    c = cls.copy()
    for _ in range(iters):
        nxt = c.copy()
        for i, n in enumerate(nb):
            if not n:
                continue
            votes = np.bincount(np.concatenate([[c[i]], c[n]]), minlength=len(S.CLASS_NAMES))
            if votes.max() > votes[c[i]]:
                nxt[i] = votes.argmax()
        c = nxt
    return c


def clean_components(cls, nb, min_faces=40, passes=3):
    """Small same-class face patches (projection noise) take the class most of their border shares."""
    c = cls.copy()
    for _ in range(passes):
        comp = np.full(len(c), -1)
        changed = False
        for seed in range(len(c)):
            if comp[seed] >= 0:
                continue
            stack, members = [seed], []
            comp[seed] = seed
            while stack:
                f = stack.pop()
                members.append(f)
                for g in nb[f]:
                    if comp[g] < 0 and c[g] == c[seed]:
                        comp[g] = seed
                        stack.append(g)
            if len(members) >= min_faces:
                continue
            border = [c[g] for f in members for g in nb[f] if c[g] != c[seed]]
            if border:
                c[members] = np.bincount(border).argmax()
                changed = True
        if not changed:
            break
    return c


def keep_largest(cls, nb, counts):
    """Garments are single pieces (shoes two): stray regions of a class take their border's majority class."""
    c = cls.copy()
    for k, keep in counts.items():
        comp = np.full(len(c), -1)
        groups = []
        for seed in np.nonzero(c == k)[0]:
            if comp[seed] >= 0:
                continue
            stack, members = [seed], []
            comp[seed] = seed
            while stack:
                f = stack.pop()
                members.append(f)
                for g in nb[f]:
                    if comp[g] < 0 and c[g] == k:
                        comp[g] = seed
                        stack.append(g)
            groups.append(members)
        groups.sort(key=len, reverse=True)
        for members in groups[keep:]:
            border = [c[g] for f in members for g in nb[f] if c[g] != k]
            if border:
                c[members] = np.bincount(border).argmax()
    return c


def split_off(ob, keep, name):
    """New object with only the faces in keep (face order preserved)."""
    new = ob.copy()
    new.data = ob.data.copy()
    new.name = new.data.name = name
    for col in ob.users_collection:
        col.objects.link(new)
    delete_faces(new, ~keep)
    return new


def material(name, color=(0.8, 0.8, 0.8, 1)):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = color
    if not m.use_nodes:
        m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = color
    return m


def set_materials(ob, names, index=None, colors=None):
    ob.data.materials.clear()
    for i, n in enumerate(names):
        ob.data.materials.append(material(n, colors[i] if colors else (0.8, 0.8, 0.8, 1)))
    idx = np.zeros(len(ob.data.polygons), np.int32) if index is None else index.astype(np.int32)
    ob.data.polygons.foreach_set("material_index", idx)
    ob.data.update()


def ring_center(P, z, half=0.006):
    sel = P[np.abs(P[:, 2] - z) < half]
    return sel[:, :2].mean(0) if len(sel) else None


# ---------------------------------------------------------------------------------------------- stages

def graft_head(body, head, neck):
    """Head scan above the neck (all of its hair) onto the body scan below; the body's neck top tucks 3 mm under."""
    cut = neck - 0.01
    ch, vh = vattr(head, "part"), S.verts(head)
    delete_verts(head, (ch == S.TOP) | ((vh[:, 2] < cut) & (ch != S.HAIR)))
    keep_islands(head, 0.02)
    cb, vb = vattr(body, "part"), S.verts(body)
    kill = np.isin(cb, [S.HAIR, S.CAP]) | ((vb[:, 2] > cut + 0.015) & np.isin(cb, [S.FACE, S.UNKNOWN]))
    delete_verts(body, kill)
    keep_islands(body, 0.02)
    vh = S.verts(head)
    ring = vh[(vh[:, 2] > cut) & (vh[:, 2] < cut + 0.02) & (vattr(head, "part") != S.HAIR)]
    c = ring[:, :2].mean(0)
    r_neck = np.linalg.norm(ring[:, :2] - c, axis=1).mean()
    vb, cb = S.verts(body), vattr(body, "part")
    band = (vb[:, 2] > cut - 0.01) & np.isin(cb, SKIN_CLASSES)
    d = vb[band, :2] - c
    r = np.linalg.norm(d, axis=1, keepdims=True)
    vb[band, :2] = c + d * np.maximum(r - 0.003, 0) / np.maximum(r, 1e-6)
    S.set_verts(body, vb)
    print(f"  graft: cut {cut:.3f}, neck radius {r_neck * 1000:.0f} mm, body {len(vb)} v, head {len(vh)} v")
    return dict(cut=cut, neck_c=c, r_neck=r_neck)


def body_face_classes(body, hiP, hicls, hicol, s):
    kd = kd_of(hiP)
    near = nearest(kd, face_centers(body))
    cls = hicls[near].copy()
    cls[cls == S.LEGS] = S.PANTS   # both golfers wear long trousers by default
    cls[np.isin(cls, [S.FACE, S.UNKNOWN, S.HAIR, S.CAP])] = S.ARMS
    nb = face_neighbours(body)
    cls = clean_components(majority(cls, nb, 3), nb)
    cls = sleeve_hems(cls, face_centers(body), s)
    cls = keep_largest(cls, nb, {S.TOP: 1, S.PANTS: 1, S.SHOES: 2})
    return cls, hicol[near]


def sleeve_hems(cls, fc, s):
    """Arms are skin and sleeve: trouser/shoe votes on the inner forearm (it hangs against the thighs in the
    concept views) become skin, then 1 cm slices out along the arm stay TOP until less than half is shirt."""
    c = cls.copy()
    for side in (1, -1):
        zone = np.nonzero((side * fc[:, 0] > 0.24 * s) & (fc[:, 2] > 0.6 * s))[0]
        arm = zone[np.isin(c[zone], [S.TOP, S.ARMS])]
        if len(arm) < 50:
            continue
        p = fc[arm]
        m = p.mean(0)
        d = np.linalg.svd(p - m, full_matrices=False)[2][0]
        d = -d if d[2] > 0 else d
        t = (p - m) @ d
        tz = (fc[zone] - m) @ d
        fixed = 0
        for b in np.arange(t.min(), t.max(), 0.01):
            core = arm[(t >= b) & (t < b + 0.01)]
            if len(core) < 6:
                continue
            cen = fc[core].mean(0)

            def radial(Q):
                q = Q - cen
                return np.linalg.norm(q - np.outer(q @ d, d), axis=1)

            sl = zone[(tz >= b) & (tz < b + 0.01)]
            other = sl[~np.isin(c[sl], [S.TOP, S.ARMS])]
            hit = other[radial(fc[other]) < 1.35 * np.median(radial(fc[core]))]
            c[hit] = S.ARMS
            fixed += len(hit)
        arm = zone[np.isin(c[zone], [S.TOP, S.ARMS])]
        t = (fc[arm] - m) @ d
        if fixed:
            print(f"  arm {'L' if side > 0 else 'R'}: {fixed} trouser/shoe faces back to skin")
        hem = None
        for b in np.arange(t.min(), t.max(), 0.01):
            sl = (t >= b) & (t < b + 0.01)
            if sl.sum() >= 6 and np.mean(c[arm[sl]] == S.TOP) < 0.5:
                hem = b
                break
        if hem is None:
            continue
        c[arm[t >= hem]] = S.ARMS
        c[arm[t < hem]] = S.TOP
        print(f"  sleeve {'L' if side > 0 else 'R'}: hem {hem - t.min():.3f} m out along the arm")
    return c


def head_face_classes(head, hiP, hicls):
    cls = hicls[nearest(kd_of(hiP), face_centers(head))].copy()
    cls[~np.isin(cls, [S.CAP, S.HAIR])] = S.FACE
    return majority(cls, face_neighbours(head), 2)


def legs_info(pants, s):
    P = S.verts(pants)
    mid = P[np.abs(P[:, 0]) < 0.012]
    crotch = mid[:, 2].min() if len(mid) else P[:, 2].max() - 0.25 * s
    hem = P[:, 2].min()
    knee = hem + 0.52 * (crotch - hem)
    shorts = knee + 0.30 * (crotch - knee)
    return dict(crotch=crotch, hem=hem, knee=knee, shorts=shorts)


def leg_radius(z, L, s):
    zs = [L["hem"], L["hem"] + 0.45 * (L["knee"] - L["hem"]), L["knee"], L["shorts"] + 0.03]
    rs = [0.037, 0.054, 0.050, 0.062]
    return np.interp(z, zs, rs) * s


def make_shorts(chinos, L, s, name="Bottom_Shorts"):
    """Chinos cut above the knee; the legs below shrink to skin-tight calves and tuck into the shoes."""
    sh = split_off(chinos, np.ones(len(chinos.data.polygons), bool), name)
    P0 = S.verts(chinos)
    bm = bmesh.new()
    bm.from_mesh(sh.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, L["shorts"]), plane_no=(0, 0, 1))
    cut_edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    bmesh.ops.split_edges(bm, edges=cut_edges)
    bm.faces.ensure_lookup_table()
    leg = np.array([f.calc_center_median().z < L["shorts"] for f in bm.faces])
    for f, is_leg in zip(bm.faces, leg):
        f.material_index = 1 if is_leg else 0
    leg_verts = list({v for f, is_leg in zip(bm.faces, leg) if is_leg for v in f.verts})
    # Leg axis per side: trouser ring centres in 1 cm bins, smoothed along z (folds make single rings noisy).
    zs = np.arange(L["hem"] - 0.01, L["shorts"] + 0.05, 0.01)
    axes = {}
    for side, sel in ((1, P0[:, 0] > 0), (-1, P0[:, 0] <= 0)):
        cs = np.array([ring_center(P0[sel], z, 0.008) if ring_center(P0[sel], z, 0.008) is not None else (np.nan, np.nan)
                       for z in zs])
        ok = ~np.isnan(cs[:, 0])
        cs = np.column_stack([np.interp(zs, zs[ok], cs[ok, 0]), np.interp(zs, zs[ok], cs[ok, 1])])
        k = np.ones(5) / 5
        axes[side] = np.column_stack([np.convolve(np.pad(cs[:, i], 2, mode="edge"), k, "valid") for i in (0, 1)])
    for v in leg_verts:
        ax = axes[1 if v.co.x > 0 else -1]
        c = (np.interp(v.co.z, zs, ax[:, 0]), np.interp(v.co.z, zs, ax[:, 1]))
        d = Vector((v.co.x - c[0], v.co.y - c[1], 0))
        d = d.normalized() if d.length > 1e-6 else Vector((1, 0, 0))
        r = leg_radius(v.co.z, L, s)
        # Slightly deeper than wide, like a calf.
        v.co.x = c[0] + d.x * r * 0.96
        v.co.y = c[1] + d.y * r * 1.04
        # Top of the leg rises inside the shorts; the bottom sinks into the shoe.
        if v.co.z > L["shorts"] - 0.002:
            v.co.z += 0.025
        elif v.co.z < L["hem"] + 0.01:
            v.co.z -= 0.02 * (1 - (v.co.z - L["hem"]) / 0.01)
    for _ in range(4):
        bmesh.ops.smooth_vert(bm, verts=leg_verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=False)
    bm.to_mesh(sh.data)
    bm.free()
    return sh, leg


def smooth_boundary(bm, edges, iters=4):
    """Relaxes vertices along open edge chains (decimation leaves cut edges zig-zagging)."""
    verts = {v for e in edges for v in e.verts}
    for _ in range(iters):
        new = {}
        for v in verts:
            nbs = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nbs) == 2:
                new[v] = (v.co * 2 + nbs[0].co + nbs[1].co) / 4
        for v, co in new.items():
            v.co = co


def make_socks(shorts, leg_mask, L, height, name):
    keep = leg_mask & (face_centers(shorts)[:, 2] < L["hem"] + height)
    sk = split_off(shorts, keep, name)
    bm = bmesh.new()
    bm.from_mesh(sk.data)
    smooth_boundary(bm, [e for e in bm.edges if e.is_boundary], 6)
    bm.to_mesh(sk.data)
    bm.free()
    me = sk.data
    me.update()
    P = S.verts(sk)
    N = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", N)
    S.set_verts(sk, P + N.reshape(-1, 3) * 0.0018)
    set_materials(sk, ["Mat_Socks_Knit"], colors=[(0.95, 0.95, 0.95, 1)])
    return sk


def cap_shoes(shoes, L, s, legs_c):
    """Sock-band collar from each shoe's opening in to the ankle, so the opening reads as a sock top with shorts."""
    bm = bmesh.new()
    bm.from_mesh(shoes.data)
    zmid = L["hem"] - 0.03
    edges = [e for e in bm.edges if e.is_boundary and min(v.co.z for v in e.verts) > zmid]
    smooth_boundary(bm, edges, 6)
    inner = {}
    r_in = leg_radius(L["hem"], L, s) + 0.006
    band_mat = len(shoes.data.materials) - 1
    for e in edges:
        for v in e.verts:
            if v in inner:
                continue
            c = legs_c[0] if v.co.x > 0 else legs_c[1]
            d = Vector((v.co.x - c[0], v.co.y - c[1], 0))
            d = d.normalized() if d.length > 1e-6 else Vector((1, 0, 0))
            inner[v] = bm.verts.new((c[0] + d.x * r_in, c[1] + d.y * r_in, v.co.z - 0.008))
    for e in edges:
        a, b = e.verts
        f0 = e.link_faces[0]
        # Keep winding consistent with the shoe face that owns the edge.
        loop = next(l for l in f0.loops if l.edge == e)
        if loop.vert is a:
            a, b = b, a
        try:
            f = bm.faces.new((a, b, inner[b], inner[a]))
            f.material_index = band_mat
        except ValueError:
            pass
    bm.to_mesh(shoes.data)
    bm.free()


# ---------------------------------------------------------------------------------------------- hands

# Hunyuan fuses the fingers into mittens; each hand is rebuilt from capsules in a frame measured off the mitten
# (u wrist -> fingertips, v toward the thumb, n out of the palm), all lengths in units of the mitten's length.
FINGERS = (  # knuckle (u, v), spread (deg), phalanx lengths, radius
    ((0.50, 0.150), 5.0, (0.18, 0.105, 0.09), 0.056),
    ((0.53, 0.050), 1.0, (0.195, 0.12, 0.10), 0.058),
    ((0.51, -0.050), -3.0, (0.185, 0.115, 0.09), 0.055),
    ((0.47, -0.145), -8.0, (0.14, 0.09, 0.08), 0.049),
)
CURL = (8.0, 14.0, 10.0)
THUMB = ((0.06, 0.09, 0.02), (0.23, 0.22, 0.07), (0.37, 0.29, 0.10), (0.49, 0.32, 0.11))
THUMB_R = (0.084, 0.068, 0.060, 0.053)
HAND_LEN = 0.10   # of body height, wrist crease to middle fingertip
HAND_TRIS = 1800


def hand_frame(P, side, s, height):
    """Wrist plane and axes of one mitten (side +1 = golfer's left, +X)."""
    arm = P[(side * P[:, 0] > 0.22 * s) & (P[:, 2] < 1.40 * s)]
    pts = arm
    for refine in (True, False):
        c0 = pts.mean(0)
        d = np.linalg.svd(pts - c0, full_matrices=False)[2][0]
        d = -d if d[2] > 0 else d
        t = (arm - c0) @ d
        if refine:
            pts = arm[t > t.max() - 0.32 * s]
    L = HAND_LEN * height
    # The narrowest ring near the expected wrist (the mitten flares into the palm just past it).
    best = None
    for b in np.arange(t.max() - L - 0.04, t.max() - L + 0.02, 0.003):
        ring = arm[np.abs(t - b) < 0.003]
        if len(ring) < 8:
            continue
        q = ring - ring.mean(0)
        q -= np.outer(q @ d, d)
        r = np.linalg.norm(q, axis=1).mean()
        if best is None or r < best[0]:
            best = (r, b)
    tw = best[1]
    ring = arm[np.abs(t - tw) < 0.004]
    w0 = ring.mean(0) - d * ((ring.mean(0) - c0) @ d - tw)
    # Palm width axis from the hand just past the wrist; the thumb side faces forward (-Y).
    palm = arm[(t > tw + 0.03 * s) & (t < tw + 0.09 * s)]
    qp = palm - palm.mean(0)
    qp -= np.outer(qp @ d, d)
    v = np.linalg.svd(qp, full_matrices=False)[2][0]
    v = -v if v[1] > 0 else v
    n = np.cross(d, v)
    n = -n if n[0] * side > 0 else n
    return dict(w0=w0, u=d, v=v, n=n, length=L)


def _polar(pts, c, u, v, n):
    """Angle and radius of points around the axis (c, u) in the (v, n) plane, sorted by angle."""
    q = pts - c
    q = q - np.outer(q @ u, u)
    a, r = np.arctan2(q @ n, q @ v), np.linalg.norm(q, axis=1)
    o = np.argsort(a)
    return a[o], r[o]


def _ring_radius(a_ring, r_ring, a):
    return np.interp(a, a_ring, r_ring, period=2 * np.pi)


def _blob(bm, centre, axes, radii, seg=16, rings=10, power=1.0):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for vt in geom["verts"]:
        x, y, z = (math.copysign(abs(c) ** power, c) for c in vt.co)
        vt.co = Vector(centre + axes[0] * x * radii[0] + axes[1] * y * radii[1] + axes[2] * z * radii[2])


def _capsule(bm, a, b, ra, rb, axes):
    a, b = np.asarray(a, float), np.asarray(b, float)
    steps = max(2, int(np.linalg.norm(b - a) / (0.3 * min(ra, rb))) + 1)
    for k in range(steps + 1):
        f = k / steps
        r = ra + (rb - ra) * f
        _blob(bm, a + (b - a) * f, axes, (r, r, r), 12, 8)


def hand_mesh(fr, name):
    """Palm, four fingers and a thumb as overlapping blobs, voxel-remeshed into one smooth closed surface."""
    L, w0, u, v, n = fr["length"], fr["w0"], fr["u"], fr["v"], fr["n"]
    at = lambda cu, cv, cn: w0 + L * (u * cu + v * cv + n * cn)  # noqa: E731
    axes = (u, v, n)
    bm = bmesh.new()
    # Wrist tube (the forearm's cross-section where it joins) blending into a boxy palm.
    for cu in np.arange(-0.02, 0.16, 0.025):
        k = np.clip(cu / 0.16, 0, 1)
        _blob(bm, at(cu, 0, 0), axes, (0.05 * L, fr["wv"] * (1 + 0.25 * k), fr["wn"] * (1 - 0.1 * k)), 16, 10)
    _blob(bm, at(0.31, 0.0, -0.005), axes, (0.22 * L, 0.205 * L, 0.082 * L), 20, 12, power=0.6)
    for (ku, kv), spread, segs, r in FINGERS:
        a = math.radians(spread)
        dirv = np.array([math.cos(a), math.sin(a), 0.0])
        p = np.array([ku, kv, 0.0])
        bend = 0.0
        rr = r
        for seg, curl in zip(segs, CURL):
            bend += math.radians(curl)
            dd = dirv * math.cos(bend) + np.array([0, 0, 1.0]) * math.sin(bend)
            q = p + dd * seg
            _capsule(bm, at(*p), at(*q), rr * L, rr * 0.93 * L, axes)
            p, rr = q, rr * 0.93
    for (a, b), ra, rb in zip(zip(THUMB[:-1], THUMB[1:]), THUMB_R[:-1], THUMB_R[1:]):
        _capsule(bm, at(*a), at(*b), ra * L, rb * L, axes)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    rm = ob.modifiers.new("remesh", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = 0.0016
    sm = ob.modifiers.new("smooth", "SMOOTH")
    sm.factor, sm.iterations = 0.5, 4
    apply_modifiers(ob)
    decimate(ob, HAND_TRIS)
    return ob


def _boundary_loop(bm, plane_co, plane_no, centre, radius):
    return [e for e in bm.edges if e.is_boundary
            and all(abs((Vector(v.co) - plane_co).dot(plane_no)) < 0.0025 and (v.co - centre).length < radius
                    for v in e.verts)]


def make_hands(arms, s, height):
    """Swaps both mittens for built hands, bridged to the forearms at the wrist."""
    P = S.verts(arms)
    for side in (1, -1):
        fr = hand_frame(P, side, s, height)
        w0, u = Vector(fr["w0"]), Vector(fr["u"])
        bm = bmesh.new()
        bm.from_mesh(arms.data)
        near = [vt for vt in bm.verts if side * vt.co.x > 0.2 * s and (vt.co - w0).length < 0.35 * s
                and (vt.co - w0).dot(u) > -0.03]
        geom = list({x for vt in near for x in [vt, *vt.link_edges, *vt.link_faces]})
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=w0, plane_no=u, clear_outer=True)
        # Anything still hanging past the wrist (mitten pieces outside the cut region) goes too.
        far = [vt for vt in bm.verts if side * vt.co.x > 0.2 * s and (vt.co - w0).dot(u) > 0.002
               and (vt.co - w0).length < 0.40 * s]
        bmesh.ops.delete(bm, geom=far, context="VERTS")
        arm_loop = _boundary_loop(bm, w0, u, w0, 0.09 * s)
        # No flare into the cut: the last 4 cm of forearm stay inside the ring 4 cm up.
        fore = [vt for vt in bm.verts if side * vt.co.x > 0.2 * s and -0.04 <= (vt.co - w0).dot(u) <= 0.001
                and (vt.co - w0).length < 0.12 * s]
        ref = np.array([vt.co for vt in fore if abs((vt.co - w0).dot(u) + 0.04) < 0.004])
        if len(ref) >= 8:
            cr = ref.mean(0) - fr["u"] * ((ref.mean(0) - fr["w0"]) @ fr["u"])
            a_ref, r_ref = _polar(ref, cr, fr["u"], fr["v"], fr["n"])
            for vt in fore:
                p = np.array(vt.co)
                t = (p - fr["w0"]) @ fr["u"]
                q = p - cr - fr["u"] * ((p - cr) @ fr["u"])
                r = np.linalg.norm(q)
                cap = _ring_radius(a_ref, r_ref, math.atan2(q @ fr["n"], q @ fr["v"]))
                if r > cap > 0:
                    vt.co = Vector(p - q + q * cap / r)
        loop_p = np.array([vt.co for e in arm_loop for vt in e.verts])
        # Centre the hand on the forearm's cut ring and size its wrist tube to that ring.
        c = loop_p.mean(0)
        c = c - fr["u"] * ((c - fr["w0"]) @ fr["u"])
        fr["w0"] = c
        q = loop_p - c
        fr["wv"] = np.sqrt(2 * np.mean((q @ fr["v"]) ** 2))
        fr["wn"] = np.sqrt(2 * np.mean((q @ fr["n"]) ** 2))
        w0 = Vector(c)
        cut_h = w0 + u * 0.004

        hand = hand_mesh(fr, f"Hand_{side}")
        hb = bmesh.new()
        hb.from_mesh(hand.data)
        bmesh.ops.bisect_plane(hb, geom=hb.verts[:] + hb.edges[:] + hb.faces[:], dist=1e-5,
                               plane_co=cut_h, plane_no=u, clear_inner=True)
        # Conform the hand's wrist to the forearm ring (full at the cut, fading out over 3 cm).
        a_arm, r_arm = _polar(loop_p, c, fr["u"], fr["v"], fr["n"])
        edge = np.array([vt.co for vt in hb.verts if abs((vt.co - cut_h).dot(u)) < 1e-3])
        a_h, r_h = _polar(edge, c, fr["u"], fr["v"], fr["n"])
        for vt in hb.verts:
            p = np.array(vt.co)
            t = (p - c) @ fr["u"]
            wgt = np.clip(1 - (t - 0.004) / 0.03, 0, 1)
            if wgt <= 0:
                continue
            q = p - c - fr["u"] * t
            ang = math.atan2(q @ fr["n"], q @ fr["v"])
            k = _ring_radius(a_arm, r_arm, ang) / max(_ring_radius(a_h, r_h, ang), 1e-4)
            vt.co = Vector(c + fr["u"] * t + q * (1 + (k - 1) * wgt))
        hb.to_mesh(hand.data)
        hb.free()

        bm.from_mesh(hand.data)
        hand_loop = _boundary_loop(bm, cut_h, u, cut_h, 0.09 * s)
        try:
            res = bmesh.ops.bridge_loops(bm, edges=arm_loop + hand_loop)
            ok = f"bridged ({len(res['faces'])} faces)"
        except RuntimeError as err:
            ok = f"NOT bridged ({err})"
        wrist = [f for f in bm.faces if (f.calc_center_median() - w0).length < 0.12 * s]
        bmesh.ops.recalc_face_normals(bm, faces=wrist)
        bm.to_mesh(arms.data)
        bm.free()
        arms.data.update()
        bpy.data.objects.remove(hand)
        # The rig stage places the finger joints from the same frame.
        arms[f"pp_hand_{'L' if side > 0 else 'R'}"] = [*fr["w0"], *fr["u"], *fr["v"], *fr["n"], fr["length"]]
        print(f"  hand {'L' if side > 0 else 'R'}: length {fr['length'] * 1000:.0f} mm, wrist "
              f"{fr['wv'] * 2000:.0f}x{fr['wn'] * 2000:.0f} mm, loops {len(arm_loop)}/{len(hand_loop)} {ok}")


# ---------------------------------------------------------------------------------------------- UVs + textures

def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def smart_uv(ob, margin=0.004):
    select_only([ob])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def copy_uvs(src, dst):
    a = np.empty(len(src.data.loops) * 2)
    src.data.uv_layers.active.data.foreach_get("uv", a)
    if not dst.data.uv_layers:
        dst.data.uv_layers.new(name="UVMap")
    dst.data.uv_layers.active.data.foreach_set("uv", a)


def planar_uvs(ob, faces, origin, axis_u, axis_v, radius):
    """Maps faces onto [0,1]^2 in the plane (origin, axis_u, axis_v) over a disc of radius (for the logo mask)."""
    uv = ob.data.uv_layers.active.data
    for fi in np.nonzero(faces)[0]:
        p = ob.data.polygons[fi]
        for li in p.loop_indices:
            co = np.array(ob.data.vertices[ob.data.loops[li].vertex_index].co)
            d = co - origin
            uv[li].uv = (0.5 + d @ axis_u / (2 * radius), 0.5 + d @ axis_v / (2 * radius))


def rasterize(ob, size):
    """Per-texel world position, smooth normal and dominant-corner vertex index for a triangulated, UV'd mesh."""
    me = ob.data
    me.calc_loop_triangles()
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    V = S.verts(ob)
    VN = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", VN)
    VN = VN.reshape(-1, 3)
    pos = np.zeros((size, size, 3), np.float32)
    nor = np.zeros((size, size, 3), np.float32)
    vid = np.full((size, size), -1, np.int32)
    for t in me.loop_triangles:
        L = list(t.loops)
        tv = list(t.vertices)
        q = uv[L] * size - 0.5
        x0, y0 = np.floor(q.min(0)).astype(int)
        x1, y1 = np.ceil(q.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        p = np.stack([xs.ravel(), ys.ravel()], 1).astype(float)
        a, b, c = q
        m = np.array([[b[0] - a[0], c[0] - a[0]], [b[1] - a[1], c[1] - a[1]]])
        det = np.linalg.det(m)
        if abs(det) < 1e-12:
            continue
        lam = np.linalg.solve(m, (p - a).T).T
        w = np.column_stack([1 - lam.sum(1), lam])
        inside = (w >= -1e-4).all(1)
        if not inside.any():
            continue
        w = w[inside]
        px, py = p[inside, 0].astype(int), p[inside, 1].astype(int)
        pos[py, px] = w @ V[tv]
        n = w @ VN[tv]
        nor[py, px] = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        vid[py, px] = np.array(tv)[w.argmax(1)]
    return pos, nor, vid


def dilate(img, filled, iters=8):
    img, filled = img.copy(), filled.copy()
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros(filled.shape)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh = np.roll(np.roll(img, dy, 0), dx, 1)
            sf = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc[sf] += sh[sf]
            cnt[sf] += 1
        new = (~filled) & (cnt > 0)
        img[new] = acc[new] / cnt[new][:, None]
        filled = filled | new
    return img


def blur(img, r):
    out = img.copy()
    for axis in (0, 1):
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis)
        out = acc / (2 * r + 1)
    return out


def save_png(path, rgba):
    """rgba float array (h, w, 4), row 0 = top, sRGB-encoded values."""
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(np.ascontiguousarray(rgba[::-1]).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def head_texture(name, head, head_cls_v, views, face, images, mattes, cut, size=2048):
    """Projects the concept views onto the head's texels (skin evened out, hair and cap as painted)."""
    pos, nor, vid = rasterize(head, size)
    filled = vid >= 0
    P, N = pos[filled].astype(float), nor[filled].astype(float)
    cls = head_cls_v[vid[filled]]
    bvh = PJ.bvh_of([head])
    rgb, w = S.head_colors(P, N, cls, views, face, images, mattes, bvh)
    img = np.zeros((size, size, 3))
    img[filled] = rgb
    # De-light the skin: divide out its low-frequency luminance swings (one side of the face is in shadow).
    skin = np.zeros((size, size), bool)
    skin[filled] = cls == S.FACE
    lum = img @ np.array([0.299, 0.587, 0.114])
    base = np.median(lum[skin]) if skin.any() else 0.5
    lb = blur(np.where(skin, lum, base)[..., None], size // 64)[..., 0]
    gain = np.clip(base / np.maximum(lb, 1e-3), 0.8, 1.25)
    img[skin] *= gain[skin][:, None]
    skin_rgb = np.median(img[skin], axis=0) if skin.any() else np.array([0.7, 0.5, 0.4])
    # The neck fades into the body's flat skin over 4 cm above the graft cut, so the seam doesn't show.
    zt = np.full((size, size), 10.0)
    zt[filled] = P[:, 2]
    k = np.clip((zt - cut) / 0.04, 0, 1)[..., None]
    img = np.where(skin[..., None], img * k + skin_rgb * (1 - k), img)
    img = dilate(img, filled, 12)
    tex = os.path.join(EXPORTS, name, "Textures")
    os.makedirs(tex, exist_ok=True)
    # Texel rows run up from v = 0; the PNG is written top row first.
    save_png(os.path.join(tex, f"{name}_Head_Albedo.png"),
             np.concatenate([np.clip(img, 0, 1), np.ones((size, size, 1))], 2)[::-1])
    return skin_rgb, filled


def bake_ao(ob, image_path, size, hide=()):
    """Cycles AO bake of ob (other visible render objects occlude) into a mask texture (R=AO, G=1, B=0, A=1)."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 48
    sc.cycles.device = "CPU"
    img = bpy.data.images.new("AO_" + ob.name, size, size, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    for m in ob.data.materials:
        nt = m.node_tree
        n = nt.nodes.new("ShaderNodeTexImage")
        n.name = "PP_Bake"
        n.image = img
        nt.nodes.active = n
    hidden = [o for o in hide if not o.hide_render]
    for o in hidden:
        o.hide_render = True
    select_only([ob])
    sc.render.bake.margin = 8
    bpy.ops.object.bake(type="AO")
    for o in hidden:
        o.hide_render = False
    for m in ob.data.materials:
        n = m.node_tree.nodes.get("PP_Bake")
        if n:
            m.node_tree.nodes.remove(n)
    a = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(a)
    a = a.reshape(size, size, 4)[::-1]
    ao = np.clip(a[..., 0], 0, 1)
    out = np.stack([0.35 + 0.65 * ao, np.ones_like(ao), np.zeros_like(ao), np.ones_like(ao)], -1)
    save_png(image_path, out)
    bpy.data.images.remove(img)
    return ao


# ---------------------------------------------------------------------------------------------- main

def build(name):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(S.STYL, f"{name}_colors.blend"))
    body = bpy.data.objects[f"{name}_Scan"]
    head = bpy.data.objects[f"{name}_HeadScan"]
    H = S.CHARS[name]["height"]
    s = H / 1.83 * (0.93 if name == "Maya" else 1.0)
    views, face, images, mattes, labels = S.make_views(name, body, head)

    geo = graft_head(body, head, body["pp_neck"])
    hiP_b, hicls_b, hicol_b = S.verts(body), vattr(body, "part"), vcolors(body)[:, :3]
    hiP_h, hicls_h = S.verts(head), vattr(head, "part")

    decimate(body, BODY_TRIS)
    decimate(head, HEAD_TRIS)
    fcls, fcol = body_face_classes(body, hiP_b, hicls_b, hicol_b, s)

    parts = {}
    parts["Body_Arms"] = split_off(body, fcls == S.ARMS, "Body_Arms")
    parts["Top_Polo"] = split_off(body, fcls == S.TOP, "Top_Polo")
    parts["Bottom_Chinos"] = split_off(body, fcls == S.PANTS, "Bottom_Chinos")
    parts["Belt"] = split_off(body, fcls == S.BELT, "Belt")
    parts["Shoes_Golf"] = split_off(body, fcls == S.SHOES, "Shoes_Golf")
    shoe_col = fcol[fcls == S.SHOES]
    head.name = head.data.name = f"Head_{name}"
    parts[head.name] = head
    bpy.data.objects.remove(body)
    for o in parts.values():
        keep_islands(o, 0.01)
        print(f"  {o.name}: {tri_count(o)} tris")
    make_hands(parts["Body_Arms"], s, H)

    # Materials and variants -------------------------------------------------------------------------------
    c, r_neck, cut = geo["neck_c"], geo["r_neck"], geo["cut"]
    top = parts["Top_Polo"]
    fc = face_centers(top)
    rad = np.linalg.norm(fc[:, :2] - c, axis=1)
    collar = (fc[:, 2] > cut - 0.06 * s) & (rad < r_neck + 0.04 * s)
    # Logo disc on the golfer's left chest (+X), found on the front surface.
    hit = PJ.bvh_of([top]).ray_cast(Vector((0.085 * s, -2, cut - 0.17 * s)), Vector((0, 1, 0)), 5)[0]
    logo = np.zeros(len(fc), bool)
    if hit is not None:
        logo = (np.linalg.norm(fc - np.array(hit), axis=1) < 0.032 * s) & (face_normals(top)[:, 1] < -0.3) & ~collar
    set_materials(top, [f"Mat_Top_{name}", "Mat_Collar_Polo", "Mat_Logo_Polo"], np.where(logo, 2, np.where(collar, 1, 0)))
    smart_uv(top)

    tee = split_off(top, np.ones(len(fc), bool), "Top_Tee")
    # Crew neck: the collar flap folds down into a band hugging the neck.
    collar_v = {v for fi in np.nonzero(collar)[0] for v in tee.data.polygons[fi].vertices}
    P = S.verts(tee)
    for vi in collar_v:
        d = P[vi, :2] - c
        r = np.linalg.norm(d)
        wgt = np.clip((r_neck + 0.04 * s - r) / (0.02 * s), 0, 1)
        target_xy = c + d / max(r, 1e-6) * (r_neck + 0.004)
        P[vi, :2] = P[vi, :2] * (1 - wgt) + target_xy * wgt
        P[vi, 2] = P[vi, 2] * (1 - wgt) + min(P[vi, 2], cut - 0.004) * wgt
    S.set_verts(tee, P)
    set_materials(tee, [f"Mat_Top_{name}", "Mat_Logo_Tee"], np.where(logo, 1, 0))
    copy_uvs(top, tee)
    parts["Top_Tee"] = tee

    chinos = parts["Bottom_Chinos"]
    set_materials(chinos, [f"Mat_Bottom_{name}"])
    smart_uv(chinos)
    L = legs_info(chinos, s)
    sc = bpy.context.scene
    sc["pp_legs"] = {k: float(v) for k, v in L.items()}
    sc["pp_cut"], sc["pp_height"], sc["pp_s"] = float(cut), float(H), float(s)
    print(f"  legs: hem {L['hem']:.3f}, knee {L['knee']:.3f}, shorts hem {L['shorts']:.3f}, crotch {L['crotch']:.3f}")
    shorts, leg = make_shorts(chinos, L, s)
    set_materials(shorts, [f"Mat_Bottom_{name}", f"Mat_Skin_{name}"], leg)
    parts["Bottom_Shorts"] = shorts
    parts["Socks_Ankle"] = make_socks(shorts, leg, L, 0.035 * s, "Socks_Ankle")
    parts["Socks_Crew"] = make_socks(shorts, leg, L, 0.15 * s, "Socks_Crew")

    belt = parts["Belt"]
    bfc = face_centers(belt)
    buckle = (np.abs(bfc[:, 0]) < 0.028 * s) & (face_normals(belt)[:, 1] < -0.5)
    set_materials(belt, ["Mat_Belt_Leather", "Mat_Buckle_Metal"], buckle.astype(int))

    shoes = parts["Shoes_Golf"]
    sfc = face_centers(shoes)
    hsv_s = (shoe_col.max(1) - shoe_col.min(1)) / np.maximum(shoe_col.max(1), 1e-3)
    sole = sfc[:, 2] < sfc[:, 2].min() + 0.02 * s
    accent = (hsv_s > 0.35) & (shoe_col.max(1) > 0.2) & ~sole
    set_materials(shoes, ["Mat_Shoes_Golf", "Mat_ShoeAccent_Golf", "Mat_Sole_Golf", "Mat_Socks_Band"],
                  np.where(sole, 2, np.where(accent, 1, 0)))
    P0 = S.verts(chinos)
    legs_c = [ring_center(P0[P0[:, 0] > 0], L["hem"] + 0.01), ring_center(P0[P0[:, 0] <= 0], L["hem"] + 0.01)]
    cap_shoes(shoes, L, s, legs_c)
    print(f"  shoes: {accent.sum()} accent, {sole.sum()} sole faces")

    hcls = head_face_classes(head, hiP_h, hicls_h)
    set_materials(head, [f"Mat_Face_{name}", f"Mat_Hair_{name}", f"Mat_Cap_{name}"],
                  np.where(hcls == S.HAIR, 1, np.where(hcls == S.CAP, 2, 0)))
    smart_uv(head, 0.003)
    hv_cls = hicls_h[nearest(kd_of(hiP_h), S.verts(head))]
    skin_rgb, _ = head_texture(name, head, hv_cls, views, face, images, mattes, cut)
    print(f"  skin {np.round(skin_rgb, 3).tolist()}")

    body_arms = parts["Body_Arms"]
    set_materials(body_arms, [f"Mat_Skin_{name}"], colors=[(*skin_rgb, 1)])
    for o in (shoes, belt, body_arms, shorts):
        smart_uv(o)
    for o in (parts["Socks_Ankle"], parts["Socks_Crew"]):
        smart_uv(o)

    tex = os.path.join(EXPORTS, name, "Textures")
    variants = [parts[n] for n in ("Top_Tee", "Bottom_Shorts", "Socks_Ankle", "Socks_Crew")]
    bake_ao(top, os.path.join(tex, f"{name}_Top_Mask.png"), 1024, hide=variants)
    bake_ao(chinos, os.path.join(tex, f"{name}_Bottom_Mask.png"), 1024, hide=variants)
    bake_ao(head, os.path.join(tex, f"{name}_Head_Mask.png"), 2048, hide=variants)
    skin_ao = bake_ao(body_arms, os.path.join(tex, f"{name}_Skin_Mask.png"), 1024, hide=variants)
    # The shorts' legs end up on the body's own skin material (GolferAppearance swaps every Skin-role slot), so they
    # sample its mask: point their UVs at one fully open texel of it.
    good = skin_ao > np.percentile(skin_ao[skin_ao > 0.05], 90)
    for ax in (0, 1):
        for sh in (-3, 3):
            good &= np.roll(good, sh, ax)
    ys, xs = np.nonzero(good)
    if len(ys):
        open_uv = ((xs[0] + 0.5) / 1024, 1 - (ys[0] + 0.5) / 1024)
        uv = shorts.data.uv_layers.active.data
        for p in shorts.data.polygons:
            if p.material_index == 1:
                for li in p.loop_indices:
                    uv[li].uv = open_uv
    # The logo patch maps the whole shared logo mask, so its UVs go in after the AO bakes.
    if hit is not None:
        for o in (top, tee):
            planar_uvs(o, logo, np.array(hit), np.array([1.0, 0, 0]), np.array([0, 0, 1.0]), 0.032 * s)
    with open(os.path.join(tex, f"{name}_skin.txt"), "w") as f:
        f.write(" ".join(f"{x:.4f}" for x in skin_rgb))

    total = sum(tri_count(parts[n]) for n in (head.name, "Body_Arms", "Top_Polo", "Bottom_Chinos", "Belt", "Shoes_Golf"))
    print(f"  default outfit: {total} tris; parts: " + ", ".join(f"{n} {tri_count(o)}" for n, o in parts.items()))
    for o in list(bpy.data.objects):
        if o.type == "MESH" and o.name not in parts:
            bpy.data.objects.remove(o)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(S.STYL, f"{name}_parts.blend"))
    print("saved", os.path.join(S.STYL, f"{name}_parts.blend"))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build(argv[0])
