"""Stylized golfer rig stages (headless).

    Blender -b --factory-startup --python pp_stylized_rig.py -- upload <Name>
    Blender -b --factory-startup --python pp_stylized_rig.py -- rig <Name>
    Blender -b --factory-startup --python pp_stylized_rig.py -- export <Name>

upload: the default outfit of stylized/<Name>_parts.blend as one FBX for Mixamo's auto-rigger
        (ArtSource/Mixamo/Upload/<Name>_upload.fbx, Y up, facing +Z, metres).
rig:    golfer skeleton (the shared Humanoid names in ProShopImportRules: Root, Hips, Abdomen, Torso, Chest, Neck,
        Head, Shoulder/UpperArm/LowerArm/Wrist.L/R, Thumb/Index/Middle/Ring/Pinky1-3.L/R,
        UpperLeg/LowerLeg/Foot/Toes.L/R, ClubGrip.L) on every part, T-pose bind, clubs on ClubGrip.L, a static
        arms-down "Idle" take, saved as stylized/<Name>_rig.blend and exported to
        ArtSource/Exports/Characters/<Name>/<Name>_Modular.fbx with the T-pose as the default pose.
        Joints come from body landmarks; weights from bone heat on a welded, hole-filled copy of the default outfit,
        transferred to every part, with analytic finger weights.
        The golf, idle and reaction clips are Mixamo's, retargeted in Unity through the Humanoid avatar.
export: re-export stylized/<Name>_rig.blend without rebuilding the rig.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pp_stylized as S  # noqa: E402
import pp_stylized_build as SB  # noqa: E402

MIXAMO = os.path.join(os.path.dirname(S.ROOT), "Mixamo")
DEFAULT_OUTFIT = ("Body_Arms", "Top_Polo", "Bottom_Chinos", "Belt", "Shoes_Golf")
FINGER_NAMES = ("Index", "Middle", "Ring", "Pinky")   # SB.FINGERS order
SIDES = (("L", 1), ("R", -1))


def open_parts(name):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(S.STYL, f"{name}_parts.blend"))
    return {o.name: o for o in bpy.data.objects if o.type == "MESH"}


def stage_upload(name):
    parts = open_parts(name)
    keep = [parts[n] for n in (f"Head_{name}",) + DEFAULT_OUTFIT]
    bpy.ops.object.select_all(action="DESELECT")
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = keep[0]
    out = os.path.join(MIXAMO, "Upload", f"{name}_upload.fbx")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             mesh_smooth_type="FACE", path_mode="STRIP", embed_textures=False)
    tris = sum(len(p.vertices) - 2 for o in keep for p in o.data.polygons)
    print(f"[rig] {name}: upload {out} ({tris} tris, {len(keep)} meshes)")


# ---------------------------------------------------------------------------------------------- landmarks

def _section(P, z, half=0.008, xsel=None):
    sel = np.abs(P[:, 2] - z) < half
    if xsel is not None:
        sel &= xsel(P)
    return P[sel]


def _spine_point(P, z):
    """Torso centre at height z, set back a little toward the spine."""
    sec = _section(P, z, 0.01, lambda Q: np.abs(Q[:, 0]) < 0.12)
    if len(sec) < 6:
        return None
    y0, y1 = sec[:, 1].min(), sec[:, 1].max()
    return np.array([0.0, (y0 + y1) / 2 + 0.12 * (y1 - y0), z])


def _leg_centre(P, z, side):
    sec = _section(P, z, 0.008, lambda Q: side * Q[:, 0] > 0.01)
    return sec[:, :2].mean(0)


def hand_frame(arms, tag):
    a = np.array(arms[f"pp_hand_{tag}"], float)
    return dict(w0=a[0:3], u=a[3:6], v=a[6:9], n=a[9:12], length=a[12])


def finger_joints(fr):
    """Joint chains in the hand built by pp_stylized_build.hand_mesh: {finger: [p0, p1, p2, tip]}."""
    L, w0, u, v, n = fr["length"], fr["w0"], fr["u"], fr["v"], fr["n"]
    at = lambda c: w0 + L * (u * c[0] + v * c[1] + n * c[2])  # noqa: E731
    out = {}
    for fname, ((ku, kv), spread, segs, _r) in zip(FINGER_NAMES, SB.FINGERS):
        a = math.radians(spread)
        dirv = np.array([math.cos(a), math.sin(a), 0.0])
        p = np.array([ku, kv, 0.0])
        pts, bend = [at(p)], 0.0
        for seg, curl in zip(segs, SB.CURL):
            bend += math.radians(curl)
            p = p + (dirv * math.cos(bend) + np.array([0, 0, 1.0]) * math.sin(bend)) * seg
            pts.append(at(p))
        out[fname] = pts
    out["Thumb"] = [at(np.array(c)) for c in SB.THUMB]
    return out


def landmark_joints(parts, name):
    """Head/tail of every deforming bone (world space, A-pose) from the built parts."""
    sc = bpy.context.scene
    H, s, cut, Lg = sc["pp_height"], sc["pp_s"], sc["pp_cut"], dict(sc["pp_legs"])
    top, chinos = S.verts(parts["Top_Polo"]), S.verts(parts["Bottom_Chinos"])
    shoes, head = S.verts(parts["Shoes_Golf"]), S.verts(parts[f"Head_{name}"])
    arms = parts["Body_Arms"]
    J = {}
    hip_z = Lg["crotch"] + 0.07 * s
    pelvis_z = hip_z + 0.04 * s
    sh_top = _section(top, 0, 1e9, lambda Q: np.abs(np.abs(Q[:, 0]) - 0.1 * s) < 0.01)[:, 2].max()
    neck_z = sh_top - 0.03 * s
    torso = np.vstack([top, chinos])
    spine = {}
    for bone, f in (("Hips", 0.0), ("Abdomen", 0.20), ("Torso", 0.40), ("Chest", 0.62), ("Neck", 1.0)):
        z = pelvis_z + f * (neck_z - pelvis_z)
        spine[bone] = _spine_point(torso, z)
    neck_ring = _section(S.verts(arms), cut - 0.02, 0.01, lambda Q: np.abs(Q[:, 0]) < 0.1)
    neck_y = neck_ring[:, 1].mean() if len(neck_ring) else spine["Neck"][1]
    spine["Neck"][1] = neck_y + 0.01
    head_base = np.array([0.0, neck_y + 0.012, cut + 0.03 * s])
    chain = ["Hips", "Abdomen", "Torso", "Chest", "Neck"]
    for a, b in zip(chain, chain[1:]):
        J[a] = (spine[a], spine[b])
    J["Neck"] = (spine["Neck"], head_base)
    J["Head"] = (head_base, np.array([0.0, head_base[1], head[:, 2].max()]))

    ankle_z = 0.045 * H
    for tag, side in SIDES:
        # Arm: shoulder joint on the arm axis where it leaves the torso, elbow 53% of the way to the wrist.
        fr = hand_frame(arms, tag)
        P = S.verts(arms)
        a = P[(side * P[:, 0] > 0.25 * s) & (P[:, 2] > 0.85 * s) & (P[:, 2] < 1.35 * s)]
        c = a.mean(0)
        d = np.linalg.svd(a - c, full_matrices=False)[2][0]
        d = -d if d[2] > 0 else d
        sh = c - d * ((c[0] - side * 0.215 * s) / d[0])
        w = fr["w0"]
        el = sh + (w - sh) * 0.53 + np.array([0, 0.012 * s, 0])
        clav = np.array([side * 0.03 * s, spine["Neck"][1] - 0.005, neck_z - 0.025 * s])
        J[f"Shoulder.{tag}"] = (clav, sh)
        J[f"UpperArm.{tag}"] = (sh, el)
        J[f"LowerArm.{tag}"] = (el, w)
        fj = finger_joints(fr)
        J[f"Wrist.{tag}"] = (w, fj["Middle"][0])
        for f, pts in fj.items():
            for k in range(3):
                J[f"{f}{k + 1}.{tag}"] = (pts[k], pts[k + 1])
        # Leg: hip joint over the thigh centre, knee 47% down to the ankle, foot to the ball, toes to the tip.
        hx, hy = _leg_centre(chinos, Lg["crotch"] - 0.06 * s, side)
        hip = np.array([hx, hy, hip_z])
        ft = shoes[side * shoes[:, 0] > 0]
        heel_y, tip_y = ft[:, 1].max(), ft[:, 1].min()
        ax_, ay_ = _leg_centre(chinos, Lg["hem"] + 0.03 * s, side)
        ankle = np.array([ax_, heel_y - 0.28 * (heel_y - tip_y), ankle_z])
        kz = ankle_z + 0.47 * (hip_z - ankle_z)
        kx, ky = _leg_centre(chinos, kz, side)
        knee = np.array([kx, ky - 0.01 * s, kz])
        fx = ft[ft[:, 1] < tip_y + 0.35 * (heel_y - tip_y)][:, 0].mean()
        ball = np.array([fx, tip_y + 0.27 * (heel_y - tip_y), 0.022 * H])
        toe = np.array([fx, tip_y + 0.02, 0.016 * H])
        J[f"UpperLeg.{tag}"] = (hip, knee)
        J[f"LowerLeg.{tag}"] = (knee, ankle)
        J[f"Foot.{tag}"] = (ankle, ball)
        J[f"Toes.{tag}"] = (ball, toe)
    return J


PARENTS = {"Hips": "Root", "Abdomen": "Hips", "Torso": "Abdomen", "Chest": "Torso", "Neck": "Chest", "Head": "Neck"}
for _t, _ in SIDES:
    PARENTS.update({f"Shoulder.{_t}": "Chest", f"UpperArm.{_t}": f"Shoulder.{_t}", f"LowerArm.{_t}": f"UpperArm.{_t}",
                    f"Wrist.{_t}": f"LowerArm.{_t}", f"UpperLeg.{_t}": "Hips", f"LowerLeg.{_t}": f"UpperLeg.{_t}",
                    f"Foot.{_t}": f"LowerLeg.{_t}", f"Toes.{_t}": f"Foot.{_t}"})
    for _f in ("Thumb",) + FINGER_NAMES:
        PARENTS.update({f"{_f}1.{_t}": f"Wrist.{_t}", f"{_f}2.{_t}": f"{_f}1.{_t}", f"{_f}3.{_t}": f"{_f}2.{_t}"})
PARENTS["ClubGrip.L"] = "Wrist.L"


def build_armature(name, J, grip):
    arm = bpy.data.armatures.new(f"{name}_Rig")
    rig = bpy.data.objects.new(f"{name}_Rig", arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    root = eb.new("Root")
    root.head, root.tail = Vector((0, 0, 0)), Vector((0, 0.1, 0))
    for bname in PARENTS:
        if bname == "ClubGrip.L":
            continue
        b = eb.new(bname)
        b.head, b.tail = Vector(J[bname][0]), Vector(J[bname][1])
    g = eb.new("ClubGrip.L")
    g.head = Vector(grip["origin"])
    g.tail = Vector(grip["origin"] + grip["y"] * 0.1)
    g.align_roll(Vector(grip["z"]))
    for bname, parent in PARENTS.items():
        b = eb[bname]
        b.parent = eb[parent]
        # Chains connect where the child starts on the parent's tail.
        b.use_connect = (b.head - eb[parent].tail).length < 1e-4 and parent != "Root"
    for b in eb:
        if b.name.startswith(("UpperArm", "LowerArm", "Wrist") + FINGER_NAMES):
            b.align_roll(Vector((0, 0, 1)))
        elif b.name.startswith("Thumb"):
            b.align_roll(Vector((0, -1, 0)))
        elif b.name.startswith(("UpperLeg", "LowerLeg", "Foot", "Toes")):
            b.align_roll(Vector((0, -1, 0)))
    bpy.ops.object.mode_set(mode="OBJECT")
    for b in arm.bones:
        b.use_deform = b.name != "Root"
    return rig


def grip_frame(fr):
    """ClubGrip.L in the lead hand (pp_golfer_clubs: +Y down the shaft to the head, +Z the face normal, origin the
    grip axis at the palm). The shaft crosses the palm from the heel/pinky side toward the index finger."""
    L, w0, u, v, n = fr["length"], fr["w0"], fr["u"], fr["v"], fr["n"]
    origin = w0 + L * (0.5 * u + 0.11 * n)
    y = _unit(0.45 * u + 0.9 * v)
    return dict(origin=origin, y=y, z=_unit(-n + y * (n @ y)))


def _unit(a):
    return a / np.linalg.norm(a)


# ---------------------------------------------------------------------------------------------- weights

def proxy_of(parts, name):
    """One closed surface of the default outfit for heat weighting: the parts were split from one closed scan, so
    welding their shared boundaries and capping what is left (shoe tops, wrist bridges) closes it again."""
    objs = [parts[n] for n in (f"Head_{name}",) + DEFAULT_OUTFIT]
    me = bpy.data.meshes.new("Proxy")
    bm = bmesh.new()
    for o in objs:
        bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0015)
    open_edges = [e for e in bm.edges if e.is_boundary]
    filled = bmesh.ops.holes_fill(bm, edges=open_edges, sides=0)["faces"]
    bmesh.ops.triangulate(bm, faces=filled)
    left = sum(1 for e in bm.edges if e.is_boundary)
    bm.to_mesh(me)
    bm.free()
    px = bpy.data.objects.new("Proxy", me)
    bpy.context.scene.collection.objects.link(px)
    print(f"[rig] proxy: {len(me.vertices)} verts, {len(open_edges)} open edges capped ({len(filled)} faces), "
          f"{left} still open")
    return px


def heat_weights(px, rig):
    no_heat = [b for b in rig.data.bones if b.name == "ClubGrip.L" or b.name[:-3].rstrip("123") in ("Thumb",) + FINGER_NAMES]
    for b in no_heat:
        b.use_deform = False
    bpy.ops.object.select_all(action="DESELECT")
    px.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in no_heat:
        b.use_deform = True
    names = [b.name for b in rig.data.bones if b.use_deform]
    W = np.zeros((len(px.data.vertices), len(names)))
    gi = {g.index: names.index(g.name) for g in px.vertex_groups if g.name in names}
    for v in px.data.vertices:
        for g in v.groups:
            if g.group in gi:
                W[v.index, gi[g.group]] = g.weight
    empty = W.sum(1) < 1e-6
    print(f"[rig] heat: {len(names)} bones, {empty.sum()} of {len(W)} proxy verts unweighted")
    return names, W


def transfer(px, names, W, ob):
    """Weights of ob's vertices from the closest point on the proxy (barycentric over its triangle)."""
    me = px.data
    me.calc_loop_triangles()
    tris = np.array([t.vertices[:] for t in me.loop_triangles])
    PV = S.verts(px)
    bvh = BVHTree.FromPolygons([Vector(p) for p in PV], tris.tolist())
    P = S.verts(ob)
    out = np.zeros((len(P), W.shape[1]))
    for i, p in enumerate(P):
        loc, _n, fi, _d = bvh.find_nearest(Vector(p))
        if fi is None:
            continue
        a, b, c = PV[tris[fi]]
        v0, v1, v2 = b - a, c - a, np.array(loc) - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-14:
            bw = np.array([1 / 3] * 3)
        else:
            y = (d11 * d20 - d01 * d21) / den
            z = (d00 * d21 - d01 * d20) / den
            bw = np.clip(np.array([1 - y - z, y, z]), 0, 1)
            bw /= bw.sum()
        out[i] = bw @ W[tris[fi]]
    return out


def _seg_dist(P, a, b):
    ab = b - a
    t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(P - (a + np.outer(t, ab)), axis=1)


def hand_weights(ob, Wob, names, J, arms):
    """Fingers by distance to their phalanx segments (the proxy fuses them); the palm stays on the wrist bone."""
    P = S.verts(ob)
    for tag, _ in SIDES:
        fr = hand_frame(arms, tag)
        t = (P - fr["w0"]) @ fr["u"]
        near = (t > -0.01) & (np.linalg.norm(P - fr["w0"], axis=1) < fr["length"] * 1.3)
        if not near.any():
            continue
        Q = P[near]
        bones = [f"Wrist.{tag}"] + [f"{f}{k}.{tag}" for f in ("Thumb",) + FINGER_NAMES for k in (1, 2, 3)]
        D = np.column_stack([_seg_dist(Q, np.array(J[b][0]), np.array(J[b][1])) for b in bones])
        # The palm: anything short of the knuckle line (but not the thumb) belongs to the wrist bone.
        tq = t[near] / fr["length"]
        thumb = D[:, 1:4].min(1) < 0.09 * fr["length"]
        D[(tq < 0.47) & ~thumb, 0] = 0.0
        Wl = 1.0 / np.maximum(D, 1e-4) ** 6
        Wl /= Wl.sum(1, keepdims=True)
        blend = np.clip((t[near] + 0.01) / 0.04, 0, 1)[:, None]
        idx = [names.index(b) for b in bones]
        rows = np.nonzero(near)[0]
        new = Wob[rows] * (1 - blend)
        new[:, idx] += Wl * blend
        Wob[rows] = new
    return Wob


def write_groups(ob, names, Wob, rig, limit=4):
    """Top `limit` influences per vertex, normalised; only bones that end up used get a group."""
    ob.vertex_groups.clear()
    top = np.argsort(-Wob, axis=1)[:, :limit]
    w = np.take_along_axis(Wob, top, 1)
    w[w < 0.01] = 0.0
    w /= np.maximum(w.sum(1, keepdims=True), 1e-9)
    groups = {}
    for i, (ks, ws) in enumerate(zip(top, w)):
        for k, x in zip(ks, ws):
            if x > 0:
                if k not in groups:
                    groups[k] = ob.vertex_groups.new(name=names[k])
                groups[k].add([i], float(x), "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    ob.parent = rig


# ---------------------------------------------------------------------------------------------- poses

def _aim(rig, bone, direction, up=None):
    """Rotates a pose bone about its head so it points along direction (armature space)."""
    pb = rig.pose.bones[bone]
    bpy.context.view_layer.update()
    M = pb.matrix.copy()
    head = M.translation.copy()
    cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
    R = cur.rotation_difference(Vector(direction).normalized()).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ M
    bpy.context.view_layer.update()


def tpose(rig, meshes):
    """Arms out level, palms down, fingers straight; applied as the bind pose of every part."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for tag, side in SIDES:
        for b in (f"UpperArm.{tag}", f"LowerArm.{tag}", f"Wrist.{tag}"):
            _aim(rig, b, (side, 0, 0))
        for f in FINGER_NAMES:
            base = rig.pose.bones[f"{f}1.{tag}"]
            d = (base.matrix.to_3x3() @ Vector((0, 1, 0)))
            d.z = 0.0
            _aim(rig, f"{f}1.{tag}", d)
            for k in (2, 3):
                _aim(rig, f"{f}{k}.{tag}", rig.pose.bones[f"{f}1.{tag}"].matrix.to_3x3() @ Vector((0, 1, 0)))
    bpy.ops.object.mode_set(mode="OBJECT")
    dg = bpy.context.evaluated_depsgraph_get()
    posed = {o.name: S.verts(o.evaluated_get(dg)) for o in meshes}
    for o in meshes:
        S.set_verts(o, posed[o.name])
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def idle_take(rig):
    """Static relaxed arms-down pose as the "Idle" take (ProShopBaker poses the shop golfer and the mannequins with it;
    the course plays Mixamo's idle through the Humanoid avatar)."""
    act = bpy.data.actions.new("Idle")
    rig.animation_data_create()
    rig.animation_data.action = act
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.matrix_basis = Matrix.Identity(4)
    for tag, side in SIDES:
        _aim(rig, f"UpperArm.{tag}", (side * 0.22, 0.03, -1.0))
        _aim(rig, f"LowerArm.{tag}", (side * 0.14, -0.22, -1.0))
        _aim(rig, f"Wrist.{tag}", (side * 0.10, -0.18, -1.0))
    for f in (1, 30):
        for pb in rig.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=f)
            pb.keyframe_insert("location", frame=f)
    bpy.ops.object.mode_set(mode="OBJECT")
    act.use_fake_user = True
    return act


# ---------------------------------------------------------------------------------------------- main

def add_clubs(rig):
    import pp_golfer_clubs as GC
    clubs = GC.build_all(bpy.context.scene.collection)
    M = rig.matrix_world @ rig.data.bones["ClubGrip.L"].matrix_local
    for ob in clubs.values():
        ob.data.transform(M)
        ob.matrix_world = Matrix.Identity(4)
        g = ob.vertex_groups.new(name="ClubGrip.L")
        g.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        ob.parent = rig
    return list(clubs.values())


def export_fbx(rig, parts, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for o in parts:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    # Unity builds the Humanoid avatar from the file's default pose, so that must be the T-pose rest; the Idle action
    # still goes out as its own take.
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="FACE", use_tspace=False, use_armature_deform_only=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=True, bake_anim_use_all_bones=True,
                             bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.0, path_mode="STRIP", embed_textures=False)
    print("[rig] exported", path)


def stage_rig(name):
    parts = open_parts(name)
    hidden = [o for o in parts.values() if o.hide_get() or o.hide_viewport]
    for o in hidden:
        o.hide_set(False)
        o.hide_viewport = False
    arms = parts["Body_Arms"]
    J = landmark_joints(parts, name)
    grip = grip_frame(hand_frame(arms, "L"))
    rig = build_armature(name, J, grip)
    px = proxy_of(parts, name)
    names, W = heat_weights(px, rig)
    for o in parts.values():
        Wob = transfer(px, names, W, o)
        if o.name in ("Body_Arms",):
            Wob = hand_weights(o, Wob, names, J, arms)
        write_groups(o, names, Wob, rig)
        for p in o.data.polygons:
            p.use_smooth = True
    bpy.data.objects.remove(px)
    meshes = list(parts.values())
    tpose(rig, meshes)
    clubs = add_clubs(rig)
    idle_take(rig)
    export_fbx(rig, meshes + clubs, os.path.join(SB.EXPORTS, name, f"{name}_Modular.fbx"))
    for o in hidden + clubs[1:]:
        o.hide_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(S.STYL, f"{name}_rig.blend"))
    print(f"[rig] {name}: {len(rig.data.bones)} bones, {len(meshes)} parts, {len(clubs)} clubs")


def stage_export(name):
    """Re-exports the saved rig (stylized/<Name>_rig.blend) without rebuilding it."""
    bpy.ops.wm.open_mainfile(filepath=os.path.join(S.STYL, f"{name}_rig.blend"))
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig]
    hidden = [o for o in meshes if o.hide_get()]
    export_fbx(rig, meshes, os.path.join(SB.EXPORTS, name, f"{name}_Modular.fbx"))
    for o in hidden:
        o.hide_set(True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    {"upload": stage_upload, "rig": stage_rig, "export": stage_export}[argv[0]](argv[1])
