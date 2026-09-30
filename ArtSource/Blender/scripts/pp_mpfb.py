"""Transfer MPFB (MakeHuman) library clothes onto a Quaternius Universal Base Character.

1. Pose the MPFB game_engine rig onto the Quaternius rest skeleton (same UE bone names).
2. Bake posed MPFB body + clothes to static meshes.
3. Surface Deform the clothes to the baked MPFB body, Shrinkwrap that body onto the
   Quaternius body, then bake the clothes: they now sit on the Quaternius body in its rest pose.
4. Re-skin clothes from the Quaternius body (Data Transfer), add thickness, mask hidden skin.
"""
import glob
import math

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from bl_ext.user_default.mpfb.services.humanservice import HumanService as H

CLOTHES_DIR = "/Users/calebsnyder/Library/Application Support/Blender/5.2/extensions/.user/user_default/mpfb/data/clothes/"

CHAIN = [
    "pelvis", "spine_01", "spine_02", "spine_03", "neck_01",
    "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l",
    "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
    "thigh_l", "calf_l", "foot_l", "ball_l",
    "thigh_r", "calf_r", "foot_r", "ball_r",
]
HEAD_BONES = {"head": "Head"}


def mhclo(name):
    return glob.glob(CLOTHES_DIR + name + "/*.mhclo")[0]


def make_dressed_human(coll, clothes, macro=None):
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[coll.name]
    macro = macro or {"gender": 1.0, "age": 0.5, "muscle": 0.75, "weight": 0.55, "proportions": 0.8,
                      "height": 0.7, "cupsize": 0.5, "firmness": 0.5,
                      "race": {"african": 0.33, "asian": 0.33, "caucasian": 0.34}}
    bm = H.create_human(macro_detail_dict=macro)
    H.add_builtin_rig(bm, "game_engine")
    items = []
    for n in clothes:
        H.add_mhclo_asset(mhclo(n), bm, asset_type="Clothes", subdiv_levels=0)
    rig = bm.parent
    for o in coll.all_objects:
        if o.type == "MESH" and o is not bm and o.parent is rig:
            items.append(o)
    return bm, rig, items


def match_pose(rig, qarm):
    """Pose MPFB rig so each shared bone lies exactly on the Quaternius rest bone."""
    rig.data.pose_position = "POSE"
    qm = qarm.matrix_world
    rm_inv = rig.matrix_world.inverted()
    for name in CHAIN + list(HEAD_BONES):
        qn = HEAD_BONES.get(name, name)
        if name not in rig.pose.bones or qn not in qarm.data.bones:
            continue
        pb = rig.pose.bones[name]
        qb = qarm.data.bones[qn]
        qh = rm_inv @ (qm @ qb.head_local)
        qt = rm_inv @ (qm @ qb.tail_local)
        bpy.context.view_layer.update()
        cur = pb.matrix.copy()
        cur_dir = (pb.tail - pb.head).normalized()
        tgt_dir = (qt - qh).normalized()
        rot = cur_dir.rotation_difference(tgt_dir).to_matrix().to_4x4()
        R = rot @ Matrix(cur.to_3x3().normalized()).to_4x4()
        s = (qt - qh).length / max(pb.bone.length, 1e-6)
        if name == "head":
            s = 1.0
        S = Matrix.Diagonal((1.0, s, 1.0, 1.0)) if name not in ("pelvis",) else Matrix.Identity(4)
        pb.matrix = Matrix.Translation(qh) @ R @ S
        bpy.context.view_layer.update()


def bake_evaluated(ob, name, coll):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    me.transform(ob.matrix_world)
    out = bpy.data.objects.new(name, me)
    coll.objects.link(out)
    return out


def transfer_to(body_src, clothes_src, target_body, coll, shrink_offset=0.0, smooth=6):
    """Bake body+clothes, wrap body onto target, carry clothes along. Returns baked clothes objects."""
    mb = bake_evaluated(body_src, "MB_T", coll)
    baked = [bake_evaluated(c, c.name.split(".", 1)[-1] + "_T", coll) for c in clothes_src]
    for c in baked:
        md = c.modifiers.new("SD", "SURFACE_DEFORM")
        md.target = mb
        md.falloff = 4.0
        with bpy.context.temp_override(object=c, active_object=c):
            bpy.ops.object.surfacedeform_bind(modifier="SD")
    sw = mb.modifiers.new("SW", "SHRINKWRAP")
    sw.target = target_body
    sw.wrap_method = "NEAREST_SURFACEPOINT"
    sw.offset = shrink_offset
    if smooth:
        cs = mb.modifiers.new("Relax", "CORRECTIVE_SMOOTH")
        cs.iterations = smooth
        cs.smooth_type = "LENGTH_WEIGHTED"
        cs.rest_source = "ORCO"
    bpy.context.view_layer.update()
    result = []
    for c in baked:
        o = bake_evaluated(c, c.name[:-2] + "_Q", coll)
        result.append(o)
    return mb, baked, result


def _tri_mesh(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    tris = [tuple(v.index for v in f.verts) for f in bm.faces]
    co = [v.co.copy() for v in bm.verts]
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    return co, tris, bvh


def _vnormals(co, tris):
    n = [Vector() for _ in co]
    for a, b, c in tris:
        fn = (co[b] - co[a]).cross(co[c] - co[a])
        for i in (a, b, c):
            n[i] += fn
    return [x.normalized() if x.length > 1e-12 else Vector((0, 0, 1)) for x in n]


def surface_bind(cloth_co, pre_me):
    """Bind points to a surface: (tri, barycentric weights, normal offset)."""
    from mathutils.interpolate import poly_3d_calc
    co, tris, bvh = _tri_mesh(pre_me)
    vn = _vnormals(co, tris)
    binds = []
    for p in cloth_co:
        loc, nrm, fi, dist = bvh.find_nearest(p)
        t = tris[fi]
        w = poly_3d_calc([co[i] for i in t], loc)
        n = sum((vn[i] * wi for i, wi in zip(t, w)), Vector()).normalized()
        binds.append((t, w, (p - loc).dot(n), (p - loc) - n * (p - loc).dot(n)))
    return binds, tris


def surface_apply(binds, post_co, tris):
    vn = _vnormals(post_co, tris)
    out = []
    for t, w, d, tang in binds:
        loc = sum((post_co[i] * wi for i, wi in zip(t, w)), Vector())
        n = sum((vn[i] * wi for i, wi in zip(t, w)), Vector()).normalized()
        out.append(loc + n * d + tang)
    return out


def push_outside(ob, target, clearance=0.004, iters=2):
    """Move cloth verts that sit inside/too close to the target body outwards along the body normal."""
    dg = bpy.context.evaluated_depsgraph_get()
    tb = BVHTree.FromObject(target, dg)
    me = ob.data
    moved = 0
    for _ in range(iters):
        for v in me.vertices:
            loc, nrm, _, _ = tb.find_nearest(v.co)
            if loc is None:
                continue
            d = (v.co - loc).dot(nrm)
            if d < clearance:
                v.co = v.co + nrm * (clearance - d)
                moved += 1
    me.update()
    return moved


def _s01(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def transfer_barycentric(body_src, clothes_src, target_body, coll, smooth=0, clearance=0.004, neck_z=None):
    mb = bake_evaluated(body_src, "MB_T", coll)
    pre = mb.data.copy()
    sw = mb.modifiers.new("SW", "SHRINKWRAP")
    sw.target = target_body
    sw.wrap_method = "NEAREST_SURFACEPOINT"
    if neck_z is not None:
        g = mb.vertex_groups.new(name="Wrap")
        for v in mb.data.vertices:
            w = 1.0 - _s01(neck_z - 0.06, neck_z + 0.01, v.co.z) * (1.0 - _s01(0.1, 0.16, abs(v.co.x)))
            g.add([v.index], w, "REPLACE")
        sw.vertex_group = "Wrap"
    if smooth:
        cs = mb.modifiers.new("Relax", "CORRECTIVE_SMOOTH")
        cs.iterations = smooth
        cs.rest_source = "ORCO"
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    post_me = bpy.data.meshes.new_from_object(mb.evaluated_get(dg), depsgraph=dg)
    post_co = [v.co.copy() for v in post_me.vertices]
    # post mesh topology == pre topology; triangulate the same way
    _, tris, _ = _tri_mesh(pre)
    out = []
    for c in clothes_src:
        baked = bake_evaluated(c, c.name.split(".", 1)[-1] + "_Q", coll)
        binds, tris = surface_bind([v.co.copy() for v in baked.data.vertices], pre)
        new = surface_apply(binds, post_co, tris)
        for v, p in zip(baked.data.vertices, new):
            v.co = p
        baked.data.update()
        push_outside(baked, target_body, clearance)
        out.append(baked)
    bpy.data.meshes.remove(post_me)
    bpy.data.meshes.remove(pre)
    mb.hide_set(True)
    return mb, out


def fit_rigid(ob, src_pts, dst_pts):
    """Uniform scale + translate so the src point cloud's bbox matches dst (width-driven, top-aligned)."""
    def bb(pts):
        return (Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)]))
    a0, a1 = bb(src_pts)
    b0, b1 = bb(dst_pts)
    s = ((b1.x - b0.x) / (a1.x - a0.x) + (b1.y - b0.y) / (a1.y - a0.y)) / 2
    top_a = Vector(((a0.x + a1.x) / 2, (a0.y + a1.y) / 2, a1.z))
    top_b = Vector(((b0.x + b1.x) / 2, (b0.y + b1.y) / 2, b1.z))
    M = Matrix.Translation(top_b) @ Matrix.Scale(s, 4) @ Matrix.Translation(-top_a)
    ob.data.transform(M)
    ob.data.update()
    return s
