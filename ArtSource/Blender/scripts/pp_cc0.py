"""Restyle Quaternius Universal Base Characters (CC0) into golfers.

Clothing is cut from the rigged body mesh itself, so every garment inherits the
body's skin weights and deforms with the Universal Animation Library clips.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

import pp_lib
from pp_lib import srgb

UBC = "/tmp/cc0/ubc/Universal Base Characters[Standard]/"
HAIR_DIR = UBC + "Hairstyles/Origin at 0/glTF (Godot)/"
TEX_DIR = UBC + "Base Characters/Textures/"


def smooth01(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- materials

def cloth_mat(name, hexcol, rough=0.85, sheen=0.25):
    m = pp_lib.mat(name, hexcol, rough, 0.0)
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if "Sheen Weight" in bsdf.inputs:
        bsdf.inputs["Sheen Weight"].default_value = sheen
    return m


def tinted_hair_mat(src, name, hexcol):
    """Hair/eyebrow textures ship as grey masks; multiply them by a hair colour."""
    m = src.copy()
    m.name = "PP_" + name
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    link = bsdf.inputs["Base Color"].links
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    mix.inputs[7].default_value = (*srgb(hexcol)[:3], 1.0)
    if link:
        nt.links.new(link[0].from_socket, mix.inputs[6])
    else:
        mix.inputs[6].default_value = (1, 1, 1, 1)
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    return m


def swap_skin_texture(mat, filename):
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image and "BaseColor" in n.image.name or (
                n.type == "TEX_IMAGE" and n.image and n.image.name.startswith("T_Superhero") and "Normal" not in n.image.name
                and "Rough" not in n.image.name):
            n.image = bpy.data.images.load(TEX_DIR + filename, check_existing=True)
            return True
    return False


# ---------------------------------------------------------------- helpers

def bone_pos(arm, name, tail=False):
    b = arm.data.bones[name]
    return (b.tail_local if tail else b.head_local).copy()


def body_bvh(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    t = BVHTree.FromBMesh(bm)
    bm.free()
    return t


def shell_from_body(body, name, keep, offset, smooth_iters=0, min_off=0.004, hem=0.0, post=None, snap=None):
    """Duplicate the body object, keep faces where keep(center, normal), push outward.

    Returns the new object (keeps vertex groups + Armature modifier)."""
    ob = body.copy()
    ob.data = body.data.copy()
    ob.name = name
    ob.data.name = name
    for c in body.users_collection:
        c.objects.link(ob)
    bvh = body_bvh(body.data)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    kill = [f for f in bm.faces if not keep(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    moves = [(v, v.normal.copy()) for v in bm.verts]
    for v, n in moves:
        v.co += n * offset(v.co.copy(), n)
    inner = [v for v in bm.verts if not v.is_boundary]
    for _ in range(smooth_iters):
        bmesh.ops.smooth_vert(bm, verts=inner, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    if post:
        post(bm)
    bm.normal_update()
    # keep the shell outside the skin
    for v in bm.verts:
        loc, nrm, _, _ = bvh.find_nearest(v.co)
        if loc is None:
            continue
        need = max(min_off, 0.6 * offset(v.co.copy(), nrm))
        d = (v.co - loc).dot(nrm)
        if d < need:
            v.co += nrm * (need - d)
    bm.normal_update()
    if snap is not None:
        bverts = {v for e in bm.edges if e.is_boundary for v in e.verts}
        for v in bverts:
            v.co = snap(v.co.copy())
        # relax the ring just inside the hem so the snap doesn't leave a crease
        ring = {w for v in bverts for e in v.link_edges for w in e.verts} - bverts
        bmesh.ops.smooth_vert(bm, verts=list(ring), factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        bm.normal_update()
    if hem > 0:
        bnd = [e for e in bm.edges if e.is_boundary]
        nrm = {v: v.normal.copy() for e in bnd for v in e.verts}
        res = bmesh.ops.extrude_edge_only(bm, edges=bnd)
        new_verts = [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]
        # extruded verts are created in the same order as the unique boundary verts
        src_verts = []
        seen = set()
        for e in bnd:
            for v in e.verts:
                if v not in seen:
                    seen.add(v)
                    src_verts.append(v)
        pos = {tuple(round(c, 6) for c in v.co): nrm[v] for v in src_verts}
        for g in new_verts:
            n = pos.get(tuple(round(c, 6) for c in g.co))
            if n is not None:
                g.co -= n * hem
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.materials.clear()
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def bind_like(ob, body, arm):
    """Copy skin weights from the nearest body vertex onto a freshly built mesh object."""
    kd = KDTree(len(body.data.vertices))
    for v in body.data.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    names = [g.name for g in body.vertex_groups]
    for n in names:
        if n not in ob.vertex_groups:
            ob.vertex_groups.new(name=n)
    for v in ob.data.vertices:
        _, idx, _ = kd.find(v.co)
        for g in body.data.vertices[idx].groups:
            ob.vertex_groups[names[g.group]].add([v.index], g.weight, "REPLACE")
    if not any(m.type == "ARMATURE" for m in ob.modifiers):
        md = ob.modifiers.new("Armature", "ARMATURE")
        md.object = arm
    ob.parent = arm


def bind_bone(ob, arm, bone):
    ob.vertex_groups.clear()
    g = ob.vertex_groups.new(name=bone)
    g.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    if not any(m.type == "ARMATURE" for m in ob.modifiers):
        md = ob.modifiers.new("Armature", "ARMATURE")
        md.object = arm
    ob.parent = arm


def new_obj(name, bm, coll, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def set_mat(ob, m):
    ob.data.materials.clear()
    ob.data.materials.append(m)


# ---------------------------------------------------------------- garments

class Fit:
    """Landmarks measured off the armature, so the same rules fit male and female bodies."""

    def __init__(self, arm, eyes):
        self.pelvis = bone_pos(arm, "pelvis")
        self.neck = bone_pos(arm, "neck_01")
        self.head = bone_pos(arm, "Head")
        self.sh = bone_pos(arm, "upperarm_l")
        self.elbow = bone_pos(arm, "lowerarm_l")
        self.wrist = bone_pos(arm, "hand_l")
        self.knee = bone_pos(arm, "calf_l")
        self.ankle = bone_pos(arm, "foot_l")
        cs = [Vector(c) for c in eyes.bound_box]
        self.eye = sum(cs, Vector()) / 8
        self.belt = self.pelvis.z + 0.07
        self.sleeve_x = self.sh.x + 0.42 * (self.elbow.x - self.sh.x)

    def is_arm(self, c):
        return abs(c.x) > self.sh.x - 0.02 and c.z > self.belt + 0.2

    def neck_z(self, c):
        v = math.exp(-(c.x / 0.045) ** 2) if c.y < 0 else 0.0
        return self.neck.z - 0.012 - 0.03 * v


def polo_rules(F):
    def keep(c, n):
        if c.z < F.belt - 0.035 or c.z > F.neck_z(c):
            return False
        if abs(c.x) > F.sleeve_x:
            return False
        return True

    def offset(co, n):
        o = 0.0065
        if abs(co.x) > F.sh.x:
            o += 0.012 * smooth01(F.sh.x, F.sleeve_x, abs(co.x))
        if co.z < F.belt + 0.12 and abs(co.x) < F.sh.x:
            o += 0.006 * smooth01(F.belt + 0.12, F.belt, co.z)
        return o

    def snap(co):
        if abs(co.x) > F.sh.x + 0.03 and co.z > F.belt + 0.2:
            co.x = math.copysign(F.sleeve_x, co.x)
        elif co.z < F.belt + 0.05:
            co.z = F.belt - 0.035
        return co

    return keep, offset, snap


def trouser_rules(F, top_extra=0.028):
    top = F.belt + top_extra
    hem = F.ankle.z + 0.022

    def keep(c, n):
        if c.z > top or c.z < hem:
            return False
        if F.is_arm(c) or abs(c.x) > 0.35:
            return False
        return True

    def offset(co, n):
        o = 0.011
        if co.z < F.knee.z:
            o += 0.012 * smooth01(F.knee.z, hem, co.z)
        return o

    def post(bm):
        # straight-legged trousers: push lower-leg verts out to a minimum radius about each leg axis
        for v in bm.verts:
            if v.co.z > F.knee.z + 0.1:
                continue
            sx = 1 if v.co.x > 0 else -1
            ax = Vector((sx * F.knee.x, 0.012, v.co.z))
            d = v.co - ax
            d.z = 0
            r = d.length
            t = smooth01(F.knee.z + 0.1, hem, v.co.z)
            want = 0.056 + 0.012 * t
            if r < want and r > 1e-6:
                v.co = ax + d * (want / r) + Vector((0, 0, v.co.z - ax.z))

    def snap(co):
        if co.z < F.knee.z:
            co.z = hem
        else:
            co.z = top
        return co

    return keep, offset, post, snap


def shoe_rules(F):
    top = F.ankle.z + 0.04

    def keep(c, n):
        return c.z < top and not F.is_arm(c)

    def offset(co, n):
        return 0.012

    def post(bm):
        for v in bm.verts:
            if v.co.z < 0.012:
                v.co.z = 0.0

    def snap(co):
        co.z = top
        return co

    return keep, offset, post, snap


def glove_rules(F, side=1):
    x0 = F.wrist.x - 0.012

    def keep(c, n):
        return c.x * side > x0 and F.is_arm(c)

    def snap(co):
        co.x = side * x0
        return co

    return keep, (lambda co, n: 0.0025), snap


def collar(F, polo_ob, coll, m):
    """Folded polo collar grown from the neckline boundary of the polo shell."""
    me = polo_ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bnd = [e for e in bm.edges if e.is_boundary and all(v.co.z > F.neck.z - 0.08 and abs(v.co.x) < 0.09 for v in e.verts)]
    verts = {v for e in bnd for v in e.verts}
    center = Vector((0, sum(v.co.y for v in verts) / len(verts), 0))
    pts = sorted(((math.atan2(v.co.y - center.y, v.co.x), v.co.copy()) for v in verts), key=lambda t: t[0])
    bm.free()
    # front opening sits at angle -90deg (towards -Y)
    ring = [p for a, p in pts if abs(a + math.pi / 2) > 0.16]
    ring.sort(key=lambda p: (math.atan2(p.y - center.y, p.x) + math.pi / 2) % (2 * math.pi))
    out = bmesh.new()
    rows = []
    prof = [(0.0, 0.0), (0.004, 0.026), (0.02, 0.03), (0.034, 0.004)]
    for p in ring:
        d = Vector((p.x, p.y - center.y, 0)).normalized()
        front = smooth01(0.4, 1.0, -d.y)
        row = []
        for k, (dr, dz) in enumerate(prof):
            spread = dr * (1 + 0.5 * front)
            q = p + d * (0.003 + spread) + Vector((0, 0, dz * (1 - 0.3 * front)))
            row.append(out.verts.new(q))
        rows.append(row)
    for i in range(len(rows) - 1):
        for k in range(len(prof) - 1):
            out.faces.new((rows[i][k], rows[i + 1][k], rows[i + 1][k + 1], rows[i][k + 1]))
    bmesh.ops.recalc_face_normals(out, faces=out.faces)
    bmesh.ops.solidify(out, geom=list(out.faces), thickness=0.0035)
    return new_obj(polo_ob.name + "_Collar", out, coll, [m])


def placket_and_buttons(F, body, coll, m_placket, m_button):
    bvh = body_bvh(body.data)
    bm = bmesh.new()
    top = F.neck_z(Vector((0, -1, 0))) - 0.002
    zs = [top - i * 0.02 for i in range(6)]
    pts = []
    for z in zs:
        hit = bvh.ray_cast(Vector((0, -1, z)), Vector((0, 1, 0)))[0]
        if hit:
            pts.append(hit)
    rows = []
    for p in pts:
        rows.append([bm.verts.new(p + Vector((dx, -0.0085, 0))) for dx in (-0.011, 0.011)])
    for i in range(len(rows) - 1):
        bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.002)
    placket = new_obj("Placket", bm, coll, [m_placket])
    btn = bmesh.new()
    for p in (pts[1], pts[3]):
        b = pp_lib.p_cyl(0.0042, 0.0042, 0.002, 12)
        bmesh.ops.transform(b, matrix=Matrix.Translation(p + Vector((0, -0.0105, 0))) @ Matrix.Rotation(math.radians(90), 4, "X"), verts=b.verts)
        tmp = bpy.data.meshes.new("tmp")
        b.to_mesh(tmp)
        b.free()
        btn.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    buttons = new_obj("Buttons", btn, coll, [m_button])
    return placket, buttons


def chest_logo(F, body, coll, m, x=0.075, dz=-0.085, r=0.013):
    bvh = body_bvh(body.data)
    z = F.neck.z + dz
    hit, n, _, _ = bvh.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)))
    d = pp_lib.p_cyl(r, r, 0.0015, 20)
    q = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(d, matrix=Matrix.Translation(hit + n * 0.0085) @ q, verts=d.verts)
    return new_obj("Logo", d, coll, [m])


def belt(F, trousers_ob, coll, m_belt, m_buckle):
    bvh = body_bvh(trousers_ob.data)
    z0, z1 = F.belt - 0.004, F.belt + 0.024
    bm = bmesh.new()
    n = 64
    rows = []
    for z in (z0, z1):
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            d = Vector((math.cos(a), math.sin(a), 0))
            o = Vector((0, 0.01, z))
            hit = bvh.ray_cast(o + d * 0.5, -d)[0]
            p = (hit if hit else o + d * 0.15) + d * 0.004
            row.append(bm.verts.new(p))
        rows.append(row)
    for i in range(n):
        k = (i + 1) % n
        bm.faces.new((rows[0][i], rows[0][k], rows[1][k], rows[1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y - 0.01, 0))) < 0:
            f.normal_flip()
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.004)
    belt_ob = new_obj("Belt", bm, coll, [m_belt])
    front = bvh.ray_cast(Vector((0, -1, (z0 + z1) / 2)), Vector((0, 1, 0)))[0]
    bk = pp_lib.p_box(0.034, 0.006, 0.026, bevel=0.002, seg=1, base=False)
    bmesh.ops.translate(bk, vec=front + Vector((0, -0.009, 0)), verts=bk.verts)
    buckle = new_obj("Buckle", bk, coll, [m_buckle])
    return belt_ob, buckle


def cap(F, targets, coll, m_cap, m_accent, front_lift=0.036, back_drop=0.0):
    """Baseball cap: crown shrink-fitted over skull + hair, curved brim, top button."""
    bvhs = [body_bvh(t.data) if t.type == "MESH" else None for t in targets]
    C = Vector((0, F.eye.y + 0.075, F.eye.z + 0.035))

    def edge_z(fy):
        # fy = -1 front, +1 back
        return F.eye.z + front_lift * smooth01(0.0, 1.0, -fy) + 0.012 * (1 - abs(fy)) - (0.03 + back_drop) * smooth01(0.1, 1.0, fy)

    def surf(u):
        best = None
        for b in bvhs:
            hit = b.ray_cast(C + u * 0.4, -u)[0]
            if hit is not None:
                r = (hit - C).length
                best = r if best is None else max(best, r)
        return best if best is not None else 0.1

    crown = bmesh.new()
    n_a, n_r = 40, 12
    top_r = surf(Vector((0, 0, 1)))
    top = crown.verts.new(C + Vector((0, 0, top_r + 0.012)))
    rows = []
    for j in range(1, n_r + 1):
        ring = []
        for i in range(n_a):
            th = 2 * math.pi * i / n_a
            fy = math.sin(th)
            ze = edge_z(fy)
            # polar angle at which this meridian reaches the edge height (iterate against the surface)
            pol_e = math.radians(95)
            for _ in range(8):
                u = Vector((math.sin(pol_e) * math.cos(th), math.sin(pol_e) * math.sin(th), math.cos(pol_e)))
                z = C.z + u.z * (surf(u) + 0.01)
                pol_e += (z - ze) * 4.0
                pol_e = max(0.6, min(2.3, pol_e))
            pol = pol_e * (j / n_r) ** 0.95
            u = Vector((math.sin(pol) * math.cos(th), math.sin(pol) * math.sin(th), math.cos(pol)))
            r = surf(u) + 0.01 + 0.004 * (1 - j / n_r)
            p = C + u * r
            if fy < 0:  # structured front panel
                p.y -= 0.006 * (-fy) * smooth01(0.2, 0.9, j / n_r) * (1 - j / n_r) * 3
            ring.append(crown.verts.new(p))
        rows.append(ring)
    for i in range(n_a):
        crown.faces.new((top, rows[0][i], rows[0][(i + 1) % n_a]))
    for j in range(n_r - 1):
        for i in range(n_a):
            k = (i + 1) % n_a
            crown.faces.new((rows[j][i], rows[j][k], rows[j + 1][k], rows[j + 1][i]))
    bmesh.ops.recalc_face_normals(crown, faces=crown.faces)
    edge = rows[-1]
    edge_pts = [v.co.copy() for v in edge]
    bmesh.ops.solidify(crown, geom=list(crown.faces), thickness=0.003)
    crown_ob = new_obj("CapCrown", crown, coll, [m_cap])

    # brim
    brim = bmesh.new()
    grid = []
    n_b, n_s = 22, 6
    for i in range(n_b + 1):
        th = math.radians(-160 + 140 * i / n_b)  # -90 = straight front
        a = (th % (2 * math.pi)) / (2 * math.pi) * n_a
        i0 = int(a) % n_a
        f = a - int(a)
        root = edge_pts[i0].lerp(edge_pts[(i0 + 1) % n_a], f)
        side = abs(math.cos(th))
        span = 0.082 * max(-math.sin(th), 0) ** 1.2
        out = Vector((math.cos(th), math.sin(th), 0))
        col = []
        for s in range(n_s + 1):
            v = s / n_s
            p = root + out * (span * v) + Vector((0, 0, -0.012 * v - 0.006 * v * v - 0.028 * side * v))
            col.append(brim.verts.new(p))
        grid.append(col)
    for i in range(n_b):
        for s in range(n_s):
            brim.faces.new((grid[i][s], grid[i + 1][s], grid[i + 1][s + 1], grid[i][s + 1]))
    bmesh.ops.recalc_face_normals(brim, faces=brim.faces)
    for fc in brim.faces:
        if fc.normal.z < 0:
            fc.normal_flip()
    bmesh.ops.solidify(brim, geom=list(brim.faces), thickness=0.0055)
    brim_ob = new_obj("CapBrim", brim, coll, [m_cap])

    acc = bmesh.new()
    b = pp_lib.p_sphere(0.009, 12, 6)
    for v in b.verts:
        v.co.z *= 0.5
    bmesh.ops.translate(b, vec=top.co if False else C + Vector((0, 0, top_r + 0.015)), verts=b.verts)
    tmp = bpy.data.meshes.new("tmp")
    b.to_mesh(tmp)
    b.free()
    acc.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    # front badge: small disc on the front panel
    u = Vector((0, -math.sin(math.radians(55)), math.cos(math.radians(55))))
    r = surf(u) + 0.0145
    d = pp_lib.p_cyl(0.017, 0.017, 0.0016, 20)
    q = u.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(d, matrix=Matrix.Translation(C + u * r) @ q, verts=d.verts)
    d.to_mesh(tmp := bpy.data.meshes.new("tmp"))
    d.free()
    acc.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    acc_ob = new_obj("CapAccent", acc, coll, [m_accent])
    return [crown_ob, brim_ob, acc_ob], edge_z, C


def trim_hair_under_cap(hair_ob, edge_z, C, margin=0.004):
    """Delete hair faces that would poke through the cap crown."""
    bm = bmesh.new()
    bm.from_mesh(hair_ob.data)
    kill = []
    for f in bm.faces:
        c = f.calc_center_median()
        fy = math.sin(math.atan2(c.y - C.y, c.x - C.x))
        if c.z > edge_z(fy) + margin:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(hair_ob.data)
    bm.free()


def delete_body_faces(body, pred):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if pred(f.calc_center_median())], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(body.data)
    bm.free()


def import_hair(name, coll):
    before = set(bpy.data.objects)
    lc = bpy.context.view_layer.layer_collection.children[coll.name]
    bpy.context.view_layer.active_layer_collection = lc
    bpy.ops.import_scene.gltf(filepath=HAIR_DIR + name + ".gltf")
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    return new[0]


def join(objs, name):
    ctx = bpy.context.copy()
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
        bpy.ops.object.join()
    objs[0].name = name
    objs[0].data.name = name
    return objs[0]


# ---------------------------------------------------------------- character driver

CALEB = dict(
    name="Caleb", body="Superhero_Male_FullBody", hair="Hair_SimpleParted", hair_col="#3A2A20",
    skin=None, polo="#2F6BD8", trousers="#CDB68A", cap="#F7F4EC", shoes="#F7F7F4", sole="#192020",
    accent="#00CBE7", belt="#2A2420", glove=True, cap_on=True, x=0.0,
)
MAYA = dict(
    name="Maya", body="Superhero_Female_FullBody", hair="Hair_Long", hair_col="#241815",
    skin="T_Superhero_Female_Dark_BaseColor.png", polo="#7B4FC9", trousers="#F2EDDF", cap="#F2EDDF",
    shoes="#F7F7F4", sole="#192020", accent="#00CBE7", belt="#192020", glove=True, cap_on=False, x=0.0,
)


def remove_collection(name):
    c = bpy.data.collections.get(name)
    if not c:
        return
    for o in list(c.all_objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for ch in list(c.children_recursive):
        bpy.data.collections.remove(ch)
    bpy.data.collections.remove(c)


def build_character(spec, scene_coll):
    name = spec["name"]
    remove_collection("PP_" + name)
    coll = bpy.data.collections.new("PP_" + name)
    scene_coll.children.link(coll)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[coll.name]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=UBC + "Base Characters/Godot - UE/" + spec["body"] + ".gltf")
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    junk = [o for o in new if o.type == "MESH" and o.name.startswith("Icosphere")]
    new = [o for o in new if o not in junk]
    for o in junk:
        bpy.data.objects.remove(o)
    body = next(o for o in new if o.type == "MESH" and o.name.lower().startswith("superhero"))
    eyes = next(o for o in new if o.name.startswith("Eyes"))
    brows = next(o for o in new if o.name.startswith("Eyebrows"))
    arm.name = name + "_Rig"
    arm.data.pose_position = "REST"
    body.name = name + "_Body"
    bpy.context.view_layer.update()

    F = Fit(arm, eyes)
    skin = body.data.materials[0]
    skin.name = "PP_" + name + "_Skin"
    if spec["skin"]:
        swap_skin_texture(skin, spec["skin"])
    M = dict(
        polo=cloth_mat(name + "_Polo", spec["polo"]),
        polo_dark=cloth_mat(name + "_PoloTrim", spec["polo"], 0.8),
        trousers=cloth_mat(name + "_Trousers", spec["trousers"], 0.9),
        cap=cloth_mat(name + "_Cap", spec["cap"], 0.8, 0.15),
        shoes=pp_lib.mat(name + "_Shoes", spec["shoes"], 0.45),
        sole=pp_lib.mat(name + "_Sole", spec["sole"], 0.7),
        accent=pp_lib.mat(name + "_Accent", spec["accent"], 0.4),
        belt=pp_lib.mat(name + "_Belt", spec["belt"], 0.45),
        buckle=pp_lib.mat(name + "_Buckle", "#D9DCDF", 0.25, 1.0),
        button=pp_lib.mat(name + "_Button", "#F2EDDF", 0.4),
        glove=pp_lib.mat(name + "_Glove", "#F7F7F4", 0.6),
    )
    trim = M["polo_dark"].node_tree.nodes
    bsdf = next(n for n in trim if n.type == "BSDF_PRINCIPLED")
    c = srgb(spec["polo"])
    bsdf.inputs["Base Color"].default_value = (c[0] * 0.7, c[1] * 0.7, c[2] * 0.7, 1)

    hair = import_hair(spec["hair"], coll)
    hair.name = name + "_Hair"
    hair.data.materials[0] = tinted_hair_mat(hair.data.materials[0], name + "_Hair", spec["hair_col"])
    brows.data.materials[0] = tinted_hair_mat(brows.data.materials[0], name + "_Brows", spec["hair_col"])
    bind_bone(hair, arm, "Head")

    parts = []
    k, off, snp = polo_rules(F)
    polo = shell_from_body(body, name + "_Polo", k, off, smooth_iters=6, min_off=0.005, hem=0.006, snap=snp)
    set_mat(polo, M["polo"])
    parts.append(polo)
    col = collar(F, polo, coll, M["polo"])
    pl, bt = placket_and_buttons(F, body, coll, M["polo_dark"], M["button"])
    logo = chest_logo(F, body, coll, M["accent"])

    k, off, post, snp = trouser_rules(F)
    tr = shell_from_body(body, name + "_Trousers", k, off, smooth_iters=10, min_off=0.008, hem=0.008, post=post, snap=snp)
    set_mat(tr, M["trousers"])
    parts.append(tr)
    bl, bk = belt(F, tr, coll, M["belt"], M["buckle"])

    k, off, post, snp = shoe_rules(F)
    sh = shell_from_body(body, name + "_Shoes", k, off, smooth_iters=12, min_off=0.008, post=post, snap=snp, hem=0.006)
    sh.data.materials.append(M["shoes"])
    sh.data.materials.append(M["sole"])
    sh.data.materials.append(M["accent"])
    for p in sh.data.polygons:
        c = p.center
        if c.z < 0.018 or p.normal.z < -0.6:
            p.material_index = 1
        elif abs(abs(c.x) - F.ankle.x) > 0.035 and 0.03 < c.z < 0.045:
            p.material_index = 2
    parts.append(sh)

    if spec["glove"]:
        k, off, snp = glove_rules(F, side=1)
        gl = shell_from_body(body, name + "_Glove", k, off, smooth_iters=0, min_off=0.002, hem=0.002, snap=snp)
        set_mat(gl, M["glove"])
        parts.append(gl)

    extras = [col, pl, bt, logo, bl, bk]
    head_parts = []
    if spec["cap_on"]:
        capparts, edge_z, C = cap(F, [body, hair], coll, M["cap"], M["accent"])
        trim_hair_under_cap(hair, edge_z, C)
        head_parts += capparts

    # hide skin that is fully under clothes
    bt_z = F.belt
    def covered(c):
        if abs(c.x) < F.sh.x - 0.03 and bt_z + 0.02 < c.z < F.neck.z - 0.07:
            return True
        if not F.is_arm(c) and F.ankle.z + 0.06 < c.z < bt_z - 0.01 and abs(c.x) < 0.3:
            return True
        if c.z < F.ankle.z - 0.02 and not F.is_arm(c):
            return True
        if F.is_arm(c) and F.sh.x + 0.02 < abs(c.x) < F.sleeve_x - 0.03:
            return True
        if spec["glove"] and F.is_arm(c) and c.x > F.wrist.x + 0.01:
            return True
        return False
    for o in extras:
        bind_like(o, body, arm)
    delete_body_faces(body, covered)

    for o in head_parts:
        bind_bone(o, arm, "Head")
    bpy.context.view_layer.update()
    return dict(arm=arm, body=body, eyes=eyes, brows=brows, hair=hair, parts=parts, extras=extras,
                head=head_parts, coll=coll, fit=F)

