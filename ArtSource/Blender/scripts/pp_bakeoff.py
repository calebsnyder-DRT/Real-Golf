"""Character bake-off builders.

A: Quaternius Ultimate Modular Men (CC0) - outfit authored for its own body + rig.
B: MPFB2 native human + MakeHuman CC0 system clothes fitted to the same base mesh.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import pp_cc0
import pp_lib
from pp_lib import srgb

QM = "/tmp/cc0/umc/Ultimate Modular Men- Feb 2022/All together/Blends/Humans_Master.blend"


def s01(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def apply_modifier(ob, name):
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob], selected_editable_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=name)


def tri_count(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    n = 0
    for o in objs:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        n += sum(len(p.vertices) - 2 for p in me.polygons)
        ev.to_mesh_clear()
    return n


def recolor(mat, hexcol, rough=None):
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = srgb(hexcol)
    for l in list(bsdf.inputs["Base Color"].links):
        mat.node_tree.links.remove(l)
    if rough is not None:
        bsdf.inputs["Roughness"].default_value = rough
    if "Sheen Weight" in bsdf.inputs and rough and rough > 0.7:
        bsdf.inputs["Sheen Weight"].default_value = 0.25


def fabric(mat, scale=180.0, strength=0.12):
    """Subtle woven bump so cloth doesn't read as plastic."""
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    if bsdf.inputs["Normal"].links:
        return
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wave = nt.nodes.new("ShaderNodeTexNoise")
    wave.inputs["Scale"].default_value = scale
    wave.inputs["Detail"].default_value = 1.0
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.001
    nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
    nt.links.new(wave.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])


def add_subd(ob, levels=1):
    for p in ob.data.polygons:
        p.use_smooth = True
    if "Subd" in ob.modifiers:
        return
    s = ob.modifiers.new("Subd", "SUBSURF")
    s.levels = levels
    s.render_levels = levels
    arm_idx = [i for i, m in enumerate(ob.modifiers) if m.type == "ARMATURE"]
    if arm_idx:
        ob.modifiers.move(len(ob.modifiers) - 1, arm_idx[0])


# ------------------------------------------------------------------ candidate A

def build_A(spec, scene_coll, coll_name, parts=None, x_offset=0.0):
    parts = parts or {"Body": "Casual2_Body", "Legs": "Casual2_Legs", "Feet": "Casual2_Feet", "Head": "Casual_Head"}
    pp_cc0.remove_collection(coll_name)
    coll = bpy.data.collections.new(coll_name)
    scene_coll.children.link(coll)
    with bpy.data.libraries.load(QM) as (src, dst):
        dst.objects = list(parts.values())
        dst.actions = [a for a in ("Idle", "Walk", "Wave") if a not in bpy.data.actions]
    objs = {}
    arm = None
    for key, o in zip(parts.keys(), dst.objects):
        coll.objects.link(o)
        o.name = spec["name"] + "_" + key
        objs[key] = o
        am = next((m for m in o.modifiers if m.type == "ARMATURE"), None)
        if am and arm is None:
            arm = am.object
    # all parts onto one armature
    if arm.name not in coll.objects:
        coll.objects.link(arm)
    arm.name = spec["name"] + "_Rig"
    for o in objs.values():
        for m in o.modifiers:
            if m.type == "ARMATURE":
                m.object = arm
        o.parent = arm
    arm.data.pose_position = "REST"
    arm.location.x = x_offset
    bpy.context.view_layer.update()
    for o in objs.values():
        for m in list(o.modifiers):
            if m.type == "MIRROR":
                apply_modifier(o, m.name)
            elif m.type == "NODES":
                o.modifiers.remove(m)
        # own material copies so recolors don't leak between roster golfers
        for i, m in enumerate(o.data.materials):
            nm = m.copy()
            nm.name = "PP_" + spec["name"] + "_" + m.name.split(".")[0]
            o.data.materials[i] = nm

    body, legs, feet, head = objs["Body"], objs["Legs"], objs["Feet"], objs["Head"]
    for o in objs.values():
        for m in o.data.materials:
            base = m.name.split("_", 2)[-1]
            if base.startswith("Skin"):
                recolor(m, spec["skin"] if base == "Skin" else spec["skin_dark"], 0.55)
            elif base.startswith("Hair") or base.startswith("Eyebrows"):
                recolor(m, spec["hair_col"], 0.6)
    shirt = next(m for m in body.data.materials if not m.name.split("_", 2)[-1].startswith("Skin"))
    recolor(shirt, spec["polo"], 0.8)
    fabric(shirt)
    recolor(legs.data.materials[0], spec["trousers"], 0.85)
    fabric(legs.data.materials[0], 220, 0.1)
    # sneaker upper -> white leather, sole -> ink
    for m in feet.data.materials:
        recolor(m, spec["shoes"], 0.45)
    sole = pp_lib.mat(spec["name"] + "_Sole", spec["sole"], 0.6)
    feet.data.materials.append(sole)
    si = len(feet.data.materials) - 1
    for p in feet.data.polygons:
        if p.center.z < 0.022:
            p.material_index = si

    wrist = arm.data.bones["Wrist.L"].head_local if "Wrist.L" in arm.data.bones else None
    M = dict(
        collar=pp_cc0.cloth_mat(spec["name"] + "_Collar", spec["collar"], 0.8),
        belt=pp_lib.mat(spec["name"] + "_Belt", spec["belt"], 0.45),
        buckle=pp_lib.mat(spec["name"] + "_Buckle", "#D9DCDF", 0.25, 1.0),
        cap=pp_cc0.cloth_mat(spec["name"] + "_Cap", spec["cap"], 0.8, 0.15),
        accent=pp_lib.mat(spec["name"] + "_Accent", spec["accent"], 0.4),
    )
    extras = []
    col = collar_A(body, shirt, coll, M["collar"])
    if col:
        extras.append(col)
    bl, bk = belt_A(body, legs, coll, M["belt"], M["buckle"], z0=spec.get("belt_z", 1.02))
    extras += [bl, bk]
    logo = logo_A(body, coll, M["accent"])
    btn = buttons_A(body, coll, M["collar"])
    if btn:
        extras.append(btn)
    if logo:
        extras.append(logo)
    for e in extras:
        pp_cc0.bind_like(e, body, arm)
    head_parts = []
    if spec.get("cap_on", True):
        head_parts = cap_A(head, coll, M["cap"], M["accent"], arm)
        for h in head_parts:
            pp_cc0.bind_bone(h, arm, "Head")
    for o in list(objs.values()) + extras + head_parts:
        add_subd(o, 1)
    return dict(arm=arm, objs=objs, extras=extras, head=head_parts, coll=coll)


def _boundary_between(ob, mat_a, pred):
    """Edges shared by a face of material mat_a and a face of another material, filtered by pred(midpoint)."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    ia = list(ob.data.materials).index(mat_a)
    pts = []
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        m0, m1 = (f.material_index for f in e.link_faces)
        if (m0 == ia) != (m1 == ia):
            mid = (e.verts[0].co + e.verts[1].co) / 2
            if pred(mid):
                pts += [e.verts[0].co.copy(), e.verts[1].co.copy()]
    bm.free()
    uniq = []
    for p in pts:
        if all((p - q).length > 1e-5 for q in uniq):
            uniq.append(p)
    return uniq


def collar_A(body, shirt, coll, m):
    pts = _boundary_between(body, shirt, lambda c: c.z > 1.4 and abs(c.x) < 0.12)
    if len(pts) < 6:
        bm = bmesh.new()
        bm.from_mesh(body.data)
        pts = []
        for e in bm.edges:
            if e.is_boundary:
                mid = (e.verts[0].co + e.verts[1].co) / 2
                if mid.z > 1.4 and abs(mid.x) < 0.12:
                    pts += [v.co.copy() for v in e.verts]
        bm.free()
        uniq = []
        for p in pts:
            if all((p - q).length > 1e-5 for q in uniq):
                uniq.append(p)
        pts = uniq
    if len(pts) < 6:
        # closed neckline: take the highest shirt vertex in each direction around the neck axis
        ia = list(body.data.materials).index(shirt)
        sv = {v for p in body.data.polygons if p.material_index == ia for v in p.vertices}
        cand = [body.data.vertices[i].co.copy() for i in sv if abs(body.data.vertices[i].co.x) < 0.11 and body.data.vertices[i].co.z > 1.42]
        if not cand:
            return None
        ax = Vector((0.0, sum(c.y for c in cand) / len(cand), 0.0))
        bins = {}
        for c in cand:
            k = int(((math.atan2(c.y - ax.y, c.x) + math.pi) / (2 * math.pi)) * 20) % 20
            if k not in bins or c.z > bins[k].z:
                bins[k] = c
        pts = list(bins.values())
    if len(pts) < 6:
        return None
    cy = sum(p.y for p in pts) / len(pts)
    ring = sorted(pts, key=lambda p: (math.atan2(p.y - cy, p.x) + math.pi / 2) % (2 * math.pi))
    ring = [p for p in ring if abs(((math.atan2(p.y - cy, p.x) + math.pi / 2) % (2 * math.pi)) - math.pi) < math.pi - 0.22]
    ring = [p for p in ring]
    out = bmesh.new()
    rows = []
    prof = [(-0.004, -0.004), (0.002, 0.022), (0.016, 0.028), (0.032, 0.006)]
    for p in ring:
        d = Vector((p.x, p.y - cy, 0)).normalized()
        front = s01(0.3, 1.0, -d.y)
        row = []
        for dr, dz in prof:
            q = p + d * (0.004 + dr * (1 + 0.6 * front)) + Vector((0, 0, dz * (1 - 0.35 * front)))
            row.append(out.verts.new(q))
        rows.append(row)
    for i in range(len(rows) - 1):
        for k in range(len(prof) - 1):
            out.faces.new((rows[i][k], rows[i + 1][k], rows[i + 1][k + 1], rows[i][k + 1]))
    bmesh.ops.recalc_face_normals(out, faces=out.faces)
    bmesh.ops.solidify(out, geom=list(out.faces), thickness=0.004)
    return pp_cc0.new_obj(body.name + "_Collar", out, coll, [m])


def belt_A(body, legs, coll, m_belt, m_buckle, z0=1.02, h=0.03):
    bm = bmesh.new()
    for o in (body, legs):
        tmp = bmesh.new()
        tmp.from_mesh(o.data)
        tm = bpy.data.meshes.new("tmp")
        tmp.to_mesh(tm)
        tmp.free()
        bm.from_mesh(tm)
        bpy.data.meshes.remove(tm)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    cy = -0.05
    ring = bmesh.new()
    n = 48
    rows = []
    for z in (z0, z0 + h):
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            d = Vector((math.cos(a), math.sin(a), 0))
            o = Vector((0, cy, z))
            hit = bvh.ray_cast(o + d * 0.5, -d)[0]
            p = (hit if hit else o + d * 0.15) + d * 0.005
            row.append(ring.verts.new(p))
        rows.append(row)
    for i in range(n):
        k = (i + 1) % n
        ring.faces.new((rows[0][i], rows[0][k], rows[1][k], rows[1][i]))
    bmesh.ops.recalc_face_normals(ring, faces=ring.faces)
    for f in ring.faces:
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y - cy, 0))) < 0:
            f.normal_flip()
    bmesh.ops.solidify(ring, geom=list(ring.faces), thickness=0.005)
    belt = pp_cc0.new_obj(body.name.replace("_Body", "") + "_Belt", ring, coll, [m_belt])
    front = bvh.ray_cast(Vector((0, -1, z0 + h / 2)), Vector((0, 1, 0)))[0]
    bk = pp_lib.p_box(0.04, 0.008, 0.032, bevel=0.003, seg=1, base=False)
    bmesh.ops.translate(bk, vec=front + Vector((0, -0.011, 0)), verts=bk.verts)
    buckle = pp_cc0.new_obj(body.name.replace("_Body", "") + "_Buckle", bk, coll, [m_buckle])
    return belt, buckle


def buttons_A(body, coll, m):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    top = None
    for z in [1.56 - i * 0.004 for i in range(60)]:
        hit = bvh.ray_cast(Vector((0, -1, z)), Vector((0, 1, 0)))[0]
        if hit is not None:
            top = z
            break
    if top is None:
        return None
    out = bmesh.new()
    for dz in (0.035, 0.065):
        hit, n, _, _ = bvh.ray_cast(Vector((0, -1, top - dz)), Vector((0, 1, 0)))
        if hit is None:
            continue
        b = pp_lib.p_cyl(0.0055, 0.0055, 0.003, 10)
        q = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
        bmesh.ops.transform(b, matrix=Matrix.Translation(hit + n * 0.002) @ q, verts=b.verts)
        tm = bpy.data.meshes.new("tmp")
        b.to_mesh(tm)
        b.free()
        out.from_mesh(tm)
        bpy.data.meshes.remove(tm)
    return pp_cc0.new_obj(body.name.replace("_Body", "") + "_Buttons", out, coll, [m])


def logo_A(body, coll, m):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    hit, n, _, _ = bvh.ray_cast(Vector((0.08, -1, 1.425)), Vector((0, 1, 0)))
    if hit is None:
        return None
    d = pp_lib.p_cyl(0.011, 0.011, 0.002, 16)
    q = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(d, matrix=Matrix.Translation(hit + n * 0.003) @ q, verts=d.verts)
    return pp_cc0.new_obj(body.name.replace("_Body", "") + "_Logo", d, coll, [m])


def cap_A(head, coll, m_cap, m_acc, arm):
    """Low-poly baseball cap sized to the head+hair bounds (subdivided later)."""
    me = head.data
    pts = [v.co for v in me.vertices]
    top = max(p.z for p in pts)
    upper = [p for p in pts if p.z > top - 0.11]
    x0, x1 = min(p.x for p in upper), max(p.x for p in upper)
    y0, y1 = min(p.y for p in upper), max(p.y for p in upper)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    rx, ry = (x1 - x0) / 2 + 0.01, (y1 - y0) / 2 + 0.012
    brow = [me.vertices[v].co.z for p in me.polygons
            if head.data.materials[p.material_index].name.split("_", 2)[-1].startswith("Eyebrows") for v in p.vertices]
    brow_z = (sum(brow) / len(brow)) if brow else top - 0.14
    zc = brow_z + 0.02
    rz = top - zc + 0.014
    crown = bmesh.new()
    na, nr = 16, 5
    rows = []
    apex_co = Vector((cx, cy + 0.004, zc + rz))
    apex = crown.verts.new(apex_co)
    for j in range(1, nr + 1):
        pol = math.radians(92) * j / nr
        ring = []
        for i in range(na):
            th = 2 * math.pi * i / na
            u = Vector((math.sin(pol) * math.cos(th), math.sin(pol) * math.sin(th), math.cos(pol)))
            p = Vector((cx + u.x * rx, cy + u.y * ry, zc + u.z * rz))
            fy = math.sin(th)
            if j == nr:
                p.z = brow_z + 0.03 * s01(0.0, 1.0, -fy) + 0.012 * (1 - abs(fy)) - 0.03 * s01(0.2, 1.0, fy)
                p.x = cx + (p.x - cx) * 1.02
                p.y = cy + (p.y - cy) * 1.02
            ring.append(crown.verts.new(p))
        rows.append(ring)
    for i in range(na):
        crown.faces.new((apex, rows[0][i], rows[0][(i + 1) % na]))
    for j in range(nr - 1):
        for i in range(na):
            k = (i + 1) % na
            crown.faces.new((rows[j][i], rows[j][k], rows[j + 1][k], rows[j + 1][i]))
    bmesh.ops.recalc_face_normals(crown, faces=crown.faces)
    edge = [v.co.copy() for v in rows[-1]]
    bmesh.ops.solidify(crown, geom=list(crown.faces), thickness=0.004)
    crown_ob = pp_cc0.new_obj(head.name + "_CapCrown", crown, coll, [m_cap])

    brim = bmesh.new()
    grid = []
    nb, ns = 8, 3
    for i in range(nb + 1):
        th = math.radians(-155 + 130 * i / nb)
        a = (th % (2 * math.pi)) / (2 * math.pi) * na
        i0 = int(a) % na
        f = a - int(a)
        root = edge[i0].lerp(edge[(i0 + 1) % na], f)
        side = abs(math.cos(th))
        span = 0.095 * max(-math.sin(th), 0) ** 1.1
        out = Vector((math.cos(th), math.sin(th), 0))
        col = []
        for s in range(ns + 1):
            v = s / ns
            col.append(brim.verts.new(root + out * (span * v) + Vector((0, 0, -0.012 * v - 0.022 * side * v))))
        grid.append(col)
    for i in range(nb):
        for s in range(ns):
            brim.faces.new((grid[i][s], grid[i + 1][s], grid[i + 1][s + 1], grid[i][s + 1]))
    bmesh.ops.recalc_face_normals(brim, faces=brim.faces)
    for fc in brim.faces:
        if fc.normal.z < 0:
            fc.normal_flip()
    bmesh.ops.solidify(brim, geom=list(brim.faces), thickness=0.007)
    brim_ob = pp_cc0.new_obj(head.name + "_CapBrim", brim, coll, [m_cap])

    acc = bmesh.new()
    b = pp_lib.p_sphere(0.011, 8, 4)
    for v in b.verts:
        v.co.z *= 0.55
    bmesh.ops.translate(b, vec=apex_co + Vector((0, 0, 0.003)), verts=b.verts)
    tm = bpy.data.meshes.new("tmp")
    b.to_mesh(tm)
    b.free()
    acc.from_mesh(tm)
    bpy.data.meshes.remove(tm)
    u = Vector((0, -math.sin(math.radians(58)), math.cos(math.radians(58))))
    p = Vector((cx + u.x * rx, cy + u.y * ry, zc + u.z * rz)) + u * 0.004
    d = pp_lib.p_cyl(0.02, 0.02, 0.002, 16)
    q = u.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(d, matrix=Matrix.Translation(p) @ q, verts=d.verts)
    tm = bpy.data.meshes.new("tmp")
    d.to_mesh(tm)
    d.free()
    acc.from_mesh(tm)
    bpy.data.meshes.remove(tm)
    acc_ob = pp_cc0.new_obj(head.name + "_CapAccent", acc, coll, [m_acc])
    return [crown_ob, brim_ob, acc_ob]


CALEB_A = dict(name="CalebA", skin="#F1C7A6", skin_dark="#D9A184", hair_col="#3A2A20", polo="#2F6BD8",
               collar="#F7F4EC", trousers="#CDB68A", shoes="#F7F7F4", sole="#192020", belt="#5A3A22",
               cap="#F7F4EC", accent="#00CBE7")
