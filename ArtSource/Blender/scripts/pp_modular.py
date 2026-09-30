"""Game-ready modular golfer on the Quaternius Ultimate Modular Men rig (CC0).

Every part is a separate skinned mesh on ONE armature so Unity can swap parts by
enabling/disabling SkinnedMeshRenderers and swap colours by material.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import pp_bakeoff as BO
import pp_cc0
import pp_lib
from pp_lib import srgb

QM = BO.QM
DEFORM_DROP = ("PT.L", "PT.R")

PALETTE = {
    "Top": {"Blue": "#2F6BD8", "White": "#F4F2EC", "Green": "#3E9B4F", "Purple": "#7B4FC9",
            "Gold": "#E3B23C", "Teal": "#1FA3A0"},
    "Collar": {"White": "#F7F4EC", "Navy": "#28324A"},
    "Bottom": {"Khaki": "#CDB68A", "Navy": "#28324A", "White": "#EDEBE4", "Grey": "#8C9096",
               "Black": "#2A2C2F", "Coral": "#E86A58"},
    "Shoes": {"White": "#F5F5F2", "Black": "#27282B"},
    "ShoeAccent": {"Cyan": "#00CBE7", "White": "#F5F5F2", "Black": "#27282B", "Grey": "#9A9DA3"},
    "Socks": {"White": "#F4F4F0", "Black": "#27282B", "Cyan": "#00CBE7", "Navy": "#28324A"},
    "Belt": {"Brown": "#5A3A22", "Black": "#1F1F21", "White": "#EDEDEA"},
    "Logo": {"Cyan": "#00CBE7", "Orange": "#F28C28"},
}
ROUGH = {"Top": 0.8, "Collar": 0.8, "Bottom": 0.85, "Shoes": 0.45, "ShoeAccent": 0.45, "Socks": 0.9,
         "Belt": 0.45, "Logo": 0.4}


def material(name, hexcol, rough=0.6, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        nt.nodes.clear()
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    for l in list(bsdf.inputs["Base Color"].links):
        nt.links.remove(l)
    bsdf.inputs["Base Color"].default_value = srgb(hexcol)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    m.diffuse_color = srgb(hexcol)
    m.use_fake_user = True
    return m


def make_palette(char):
    mats = {}
    for group, cols in PALETTE.items():
        for cname, hexcol in cols.items():
            mats[(group, cname)] = material("Mat_%s_%s" % (group, cname), hexcol, ROUGH[group])
    mats[("Sole", "Dark")] = material("Mat_Sole_Dark", "#2B2D30", 0.7)
    mats[("Buckle", "Silver")] = material("Mat_Buckle_Silver", "#D9DCDF", 0.25, 1.0)
    mats[("Skin", char["name"])] = material("Mat_Skin_" + char["name"], char["skin"], 0.55)
    mats[("SkinShade", char["name"])] = material("Mat_SkinShade_" + char["name"], char["skin_dark"], 0.55)
    mats[("Hair", char["name"])] = material("Mat_Hair_" + char["name"], char["hair_col"], 0.6)
    mats[("Eye", "Dark")] = material("Mat_Eye_Dark", "#1B1B1D", 0.3)
    return mats


# ------------------------------------------------------------------ rig

def edit_armature(arm, fn):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    with bpy.context.temp_override(active_object=arm, object=arm, selected_objects=[arm]):
        bpy.ops.object.mode_set(mode="EDIT")
        fn(arm.data.edit_bones)
        bpy.ops.object.mode_set(mode="OBJECT")


def make_game_rig(src, name):
    """FK copy of the Quaternius rig with a Unity-Humanoid-friendly hierarchy."""
    rig = src.copy()
    rig.data = src.data.copy()
    rig.name = name
    rig.data.name = name
    rig.animation_data_clear()
    for c in src.users_collection:
        c.objects.link(rig)

    def fix(eb):
        for s in ("L", "R"):
            eb["Foot." + s].parent = eb["LowerLeg." + s]
            eb["UpperLeg." + s].parent = eb["Hips"]
        for n in DEFORM_DROP:
            if n in eb:
                eb.remove(eb[n])
    edit_armature(rig, fix)
    for pb in rig.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    rig.data.pose_position = "POSE"
    return rig


def bake_clip(src, rig, action, clip_name):
    """Sample the IK-driven source clip and key the FK game rig bone by bone (armature space)."""
    sc = bpy.context.scene
    src.animation_data_create()
    src.animation_data.action = action
    if action.slots:
        src.animation_data.action_slot = action.slots[0]
    src.data.pose_position = "POSE"
    f0, f1 = int(action.frame_range[0]), int(action.frame_range[1])
    rig.animation_data_create()
    new = bpy.data.actions.new(clip_name)
    rig.animation_data.action = new
    order = []

    def walk(b):
        order.append(b.name)
        for c in b.children:
            walk(c)
    for b in rig.data.bones:
        if b.parent is None:
            walk(b)
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        mats = {n: src.pose.bones[n].matrix.copy() for n in order if n in src.pose.bones}
        for n in order:
            if n not in mats:
                continue
            pb = rig.pose.bones[n]
            pb.matrix = mats[n]
            bpy.context.view_layer.update()
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("rotation_quaternion" if pb.rotation_mode == "QUATERNION" else "rotation_euler", frame=f)
            pb.keyframe_insert("scale", frame=f)
    new.use_fake_user = True
    return new


def push_to_nla(rig, actions):
    ad = rig.animation_data
    ad.action = None
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    for a in actions:
        tr = ad.nla_tracks.new()
        tr.name = a.name
        st = tr.strips.new(a.name, int(a.frame_range[0]), a)
        st.name = a.name
        tr.mute = True


# ------------------------------------------------------------------ parts

def load_parts(names, coll):
    with bpy.data.libraries.load(QM) as (src, dst):
        dst.objects = list(names) + ["CharacterArmature"]
        dst.actions = ["Idle", "Walk", "Wave"]
    objs = {}
    arm = None
    for n, o in zip(list(names) + ["CharacterArmature"], dst.objects):
        coll.objects.link(o)
        if o.type == "ARMATURE":
            arm = o
        else:
            objs[n] = o
    acts = {a.name.split(".")[0]: a for a in dst.actions}
    for o in objs.values():
        for m in list(o.modifiers):
            if m.type == "MIRROR":
                BO.apply_modifier(o, m.name)
            elif m.type == "NODES":
                o.modifiers.remove(m)
            elif m.type == "ARMATURE":
                m.object = arm
        o.parent = arm
    return arm, objs, acts


def split_by_material(ob, keep_pred, name):
    """Duplicate ob keeping only faces whose material satisfies keep_pred(material)."""
    new = ob.copy()
    new.data = ob.data.copy()
    new.name = name
    new.data.name = name
    for c in ob.users_collection:
        c.objects.link(new)
    bm = bmesh.new()
    bm.from_mesh(new.data)
    mats = list(new.data.materials)
    kill = [f for f in bm.faces if not keep_pred(mats[f.material_index])]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(new.data)
    bm.free()
    return new


def base_of(mat):
    return mat.name.split(".")[0]


def assign_slots(ob, mapping):
    """Replace materials by mapping(old_material) -> new material, merging duplicates."""
    old = list(ob.data.materials)
    new_list = []
    remap = {}
    for i, m in enumerate(old):
        nm = mapping(m)
        if nm not in new_list:
            new_list.append(nm)
        remap[i] = new_list.index(nm)
    idx = [remap[p.material_index] for p in ob.data.polygons]
    ob.data.materials.clear()
    for m in new_list:
        ob.data.materials.append(m)
    for p, i in zip(ob.data.polygons, idx):
        p.material_index = i


def slot_index(ob, mat):
    mats = list(ob.data.materials)
    if mat not in mats:
        ob.data.materials.append(mat)
        mats.append(mat)
    return mats.index(mat)


def join_into(target, others):
    return pp_cc0.join([target] + others, target.name)


def sock_tube(shorts, skin_mat, z0, z1, name, mat):
    ia = list(shorts.data.materials).index(skin_mat)

    def keep(c, n):
        return z0 - 0.01 < c.z < z1 and abs(c.x) < 0.3

    def keep_skin(c, n, _cache={}):
        return keep(c, n)
    # restrict to skin faces by temporarily marking others
    ob = pp_cc0.shell_from_body(shorts, name, lambda c, n: keep(c, n), lambda co, n: 0.0035,
                                smooth_iters=1, min_off=0.003, hem=0.003,
                                snap=lambda co: (setattr(co, "z", z1) or co) if co.z > (z0 + z1) / 2 else co)
    pp_cc0.set_mat(ob, mat)
    return ob


def apply_subd(ob, levels=1):
    for p in ob.data.polygons:
        p.use_smooth = True
    if "Subd" not in ob.modifiers:
        BO.add_subd(ob, levels)
    BO.apply_modifier(ob, "Subd")


def clean_mesh(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(ob.data)
    bm.free()
    for p in ob.data.polygons:
        p.use_smooth = True


CALEB = dict(name="Caleb", skin="#F1C7A6", skin_dark="#D9A184", hair_col="#3A2A20", head="Casual_Head")


def build(char, scene_coll, coll_name=None):
    coll_name = coll_name or "PP_%s_Modular" % char["name"]
    pp_cc0.remove_collection(coll_name)
    coll = bpy.data.collections.new(coll_name)
    scene_coll.children.link(coll)
    names = ["Casual2_Body", "Casual2_Legs", "Casual2_Feet", "Casual_Legs", char["head"]]
    src_arm, P, acts = load_parts(names, coll)
    src_arm.data.pose_position = "REST"
    M = make_palette(char)
    skin, shade, hair, eye = (M[("Skin", char["name"])], M[("SkinShade", char["name"])],
                              M[("Hair", char["name"])], M[("Eye", "Dark")])

    def common(m):
        b = base_of(m)
        if b.startswith("Skin_Darker"):
            return shade
        if b.startswith("Skin"):
            return skin
        if b.startswith("Hair") or b.startswith("Eyebrows"):
            return hair
        if b.startswith("Eye"):
            return eye
        return None

    # --- body skin (arms/hands) and tops
    body_src = P["Casual2_Body"]
    shirt_mat = next(m for m in body_src.data.materials if not base_of(m).startswith("Skin"))
    arms = split_by_material(body_src, lambda m: base_of(m).startswith("Skin"), "Body_Arms")
    tee = split_by_material(body_src, lambda m: m == shirt_mat, "Top_Tee")
    polo = split_by_material(body_src, lambda m: m == shirt_mat, "Top_Polo")
    assign_slots(arms, lambda m: skin)
    assign_slots(tee, lambda m: M[("Top", "Blue")])
    assign_slots(polo, lambda m: M[("Top", "Blue")])

    collar = BO.collar_A(body_src, shirt_mat, coll, M[("Collar", "White")])
    buttons = BO.buttons_A(body_src, coll, M[("Collar", "White")])
    logos = [BO.logo_A(body_src, coll, M[("Logo", "Cyan")]) for _ in range(2)]
    for o in [collar, buttons] + logos:
        pp_cc0.bind_like(o, body_src, src_arm)
    polo = join_into(polo, [collar, buttons, logos[0]])
    tee = join_into(tee, [logos[1]])

    # --- bottoms
    chinos = P["Casual2_Legs"]
    chinos.name = chinos.data.name = "Bottom_Chinos"
    assign_slots(chinos, lambda m: M[("Bottom", "Khaki")])
    shorts = P["Casual_Legs"]
    shorts.name = shorts.data.name = "Bottom_Shorts"
    assign_slots(shorts, lambda m: common(m) or M[("Bottom", "Navy")])

    belt, buckle = BO.belt_A(body_src, chinos, coll, M[("Belt", "Brown")], M[("Buckle", "Silver")])
    pp_cc0.bind_like(belt, chinos, src_arm)
    pp_cc0.bind_like(buckle, chinos, src_arm)
    belt = join_into(belt, [buckle])
    belt.name = belt.data.name = "Belt"

    # --- socks (from the bare lower leg of the shorts part)
    socks_ankle = sock_tube(shorts, skin, 0.1, 0.2, "Socks_Ankle", M[("Socks", "White")])
    socks_crew = sock_tube(shorts, skin, 0.1, 0.33, "Socks_Crew", M[("Socks", "White")])

    # --- shoes
    feet = P["Casual2_Feet"]
    feet.name = feet.data.name = "Shoes_Golf"
    upper = M[("Shoes", "White")]
    accent = M[("ShoeAccent", "Cyan")]
    sole = M[("Sole", "Dark")]
    sockm = M[("Socks", "White")]
    old = list(feet.data.materials)
    kinds = []
    for p in feet.data.polygons:
        b = base_of(old[p.material_index])
        if p.center.z < 0.022:
            kinds.append(accent if p.center.z >= 0.011 and abs(p.normal.z) < 0.75 else sole)
        elif b.startswith("White"):
            kinds.append(sockm)
        else:
            kinds.append(upper)
    feet.data.materials.clear()
    for m in (upper, accent, sole, sockm):
        feet.data.materials.append(m)
    for p, k in zip(feet.data.polygons, kinds):
        p.material_index = [upper, accent, sole, sockm].index(k)

    # --- head
    head = P[char["head"]]
    head.name = head.data.name = "Head_" + char["name"]
    assign_slots(head, lambda m: common(m) or skin)

    # --- game rig + clips
    rig = make_game_rig(src_arm, char["name"] + "_Rig")
    clips = [bake_clip(src_arm, rig, acts[n], n) for n in ("Idle", "Walk", "Wave") if n in acts]
    push_to_nla(rig, clips)
    rig.data.pose_position = "REST"
    parts = [arms, tee, polo, chinos, shorts, belt, socks_ankle, socks_crew, feet, head]
    for o in parts:
        for m in o.modifiers:
            if m.type == "ARMATURE":
                m.object = rig
        o.parent = rig
        o.matrix_parent_inverse.identity()
        for n in DEFORM_DROP:
            g = o.vertex_groups.get(n)
            if g:
                o.vertex_groups.remove(g)
        apply_subd(o, 1)
        clean_mesh(o)
    bpy.data.objects.remove(src_arm)
    for m in list(bpy.data.materials):
        if m.users == 0 and not m.use_fake_user:
            bpy.data.materials.remove(m)
    return dict(rig=rig, coll=coll, parts={o.name: o for o in parts}, mats=M, clips=clips)


SLOTS = {"Top": ("Top_Polo", "Top_Tee"), "Bottom": ("Bottom_Chinos", "Bottom_Shorts"),
         "Socks": ("Socks_Ankle", "Socks_Crew", None), "Belt": ("Belt", None)}


def set_combo(coll, top="Top_Polo", bottom="Bottom_Chinos", socks=None, belt="Belt", colors=None):
    """Show one part per slot and swap variant materials, e.g. colors={"Top": "Blue", "Collar": "White"}."""
    colors = colors or {}
    shown = {top, bottom, socks, belt}
    swappable = {n for opts in SLOTS.values() for n in opts if n}
    for o in coll.objects:
        if o.type != "MESH":
            continue
        vis = o.name in shown or o.name not in swappable
        o.hide_render = not vis
        o.hide_viewport = not vis
        for i, m in enumerate(o.data.materials):
            if not m or not m.name.startswith("Mat_"):
                continue
            group = m.name.split("_")[1]
            if group in colors:
                new = bpy.data.materials.get("Mat_%s_%s" % (group, colors[group]))
                if new:
                    o.data.materials[i] = new


# ------------------------------------------------------------------ pose test

def rot_world(rig, bone, axis, deg):
    pb = rig.pose.bones[bone]
    bpy.context.view_layer.update()
    h = pb.head.copy()
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


def aim(rig, bone, point):
    pb = rig.pose.bones[bone]
    bpy.context.view_layer.update()
    h = pb.head.copy()
    R = (pb.tail - h).rotation_difference(Vector(point) - h).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


LEGS_FLEX = [("UpperLeg.L", "X", -14), ("UpperLeg.R", "X", -14), ("LowerLeg.L", "X", 26),
             ("LowerLeg.R", "X", 26), ("Foot.L", "X", -12), ("Foot.R", "X", -12)]
POSES = {
    "ArmsDown": [("UpperArm.L", "Y", 72), ("UpperArm.R", "Y", -72)],
    "ArmsRaised": [("UpperArm.L", "Y", -68), ("UpperArm.R", "Y", 68),
                   ("LowerArm.L", "Y", -15), ("LowerArm.R", "Y", 15)],
    "Address": [("Abdomen", "X", 14), ("Chest", "X", 16), ("Neck", "X", -16)] + LEGS_FLEX + [
        ("UpperArm.L", (0.06, -0.34, 1.05)), ("LowerArm.L", (0.015, -0.42, 0.86)),
        ("UpperArm.R", (-0.06, -0.34, 1.05)), ("LowerArm.R", (-0.015, -0.42, 0.86))],
    "Backswing": [("Abdomen", "X", 12), ("Chest", "X", 10), ("Abdomen", "Z", -22), ("Chest", "Z", -40),
                  ("Neck", "Z", 45)] + LEGS_FLEX + [
        ("UpperArm.L", (-0.22, -0.22, 1.42)), ("LowerArm.L", (-0.4, -0.1, 1.62)),
        ("UpperArm.R", (-0.36, 0.08, 1.3)), ("LowerArm.R", (-0.4, -0.08, 1.62))],
}


def set_pose(rig, name):
    rig.animation_data.action = None
    rig.data.pose_position = "POSE"
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    for op in POSES.get(name, []):
        if len(op) == 3:
            rot_world(rig, *op)
        else:
            aim(rig, *op)
    ground(rig)


def ground(rig):
    """Drop the Body bone so the lower foot sits at its rest height."""
    rest = min(rig.data.bones[b].head_local.z for b in ("Foot.L", "Foot.R"))
    now = min(rig.pose.bones[b].head.z for b in ("Foot.L", "Foot.R"))
    pb = rig.pose.bones["Body"]
    pb.matrix = Matrix.Translation((0, 0, rest - now)) @ pb.matrix
    bpy.context.view_layer.update()


PRESETS = {
    "ClassicPolo": dict(top="Top_Polo", bottom="Bottom_Chinos", socks=None, belt="Belt",
                        colors=dict(Top="Blue", Collar="White", Bottom="Khaki", Belt="Brown", Shoes="White",
                                    ShoeAccent="Cyan", Socks="White", Logo="Cyan")),
    "SummerTee": dict(top="Top_Tee", bottom="Bottom_Shorts", socks="Socks_Crew", belt=None,
                      colors=dict(Top="White", Bottom="Navy", Shoes="White", ShoeAccent="Cyan", Socks="White",
                                  Logo="Cyan")),
    "GreenClub": dict(top="Top_Polo", bottom="Bottom_Chinos", socks=None, belt="Belt",
                      colors=dict(Top="Green", Collar="White", Bottom="White", Belt="Black", Shoes="Black",
                                  ShoeAccent="White", Socks="Black", Logo="Cyan")),
    "PurpleShorts": dict(top="Top_Polo", bottom="Bottom_Shorts", socks="Socks_Ankle", belt="Belt",
                         colors=dict(Top="Purple", Collar="White", Bottom="Grey", Belt="Black", Shoes="White",
                                     ShoeAccent="Black", Socks="Black", Logo="Orange")),
    "GoldTee": dict(top="Top_Tee", bottom="Bottom_Chinos", socks=None, belt="Belt",
                    colors=dict(Top="Gold", Bottom="Navy", Belt="Brown", Shoes="White", ShoeAccent="Cyan",
                                Socks="White", Logo="Cyan")),
    "TealCoral": dict(top="Top_Polo", bottom="Bottom_Shorts", socks="Socks_Crew", belt="Belt",
                      colors=dict(Top="Teal", Collar="White", Bottom="Coral", Belt="White", Shoes="White",
                                  ShoeAccent="Cyan", Socks="Cyan", Logo="Orange")),
    "WhiteNavy": dict(top="Top_Polo", bottom="Bottom_Chinos", socks=None, belt="Belt",
                      colors=dict(Top="White", Collar="Navy", Bottom="Black", Belt="White", Shoes="Black",
                                  ShoeAccent="Cyan", Socks="Black", Logo="Cyan")),
    "CasualBlue": dict(top="Top_Tee", bottom="Bottom_Shorts", socks="Socks_Ankle", belt=None,
                       colors=dict(Top="Blue", Bottom="Khaki", Shoes="White", ShoeAccent="Grey", Socks="Navy",
                                   Logo="Cyan")),
}


def apply_preset(coll, name):
    set_combo(coll, **PRESETS[name])


# ------------------------------------------------------------------ export

def swatch_object(coll, rig, name):
    """Tiny quad per variant material so the modular FBX/GLB carries every Mat_* variant."""
    mats = sorted((m for m in bpy.data.materials if m.name.startswith("Mat_")), key=lambda m: m.name)
    bm = bmesh.new()
    s = 0.01
    for i, m in enumerate(mats):
        x = (i % 8) * s * 1.5
        y = (i // 8) * s * 1.5
        vs = [bm.verts.new((x, y, 0)), bm.verts.new((x + s, y, 0)), bm.verts.new((x + s, y + s, 0)),
              bm.verts.new((x, y + s, 0))]
        f = bm.faces.new(vs)
        f.material_index = i
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old)
    ob = pp_cc0.new_obj(name, bm, coll, mats)
    pp_cc0.bind_bone(ob, rig, "Root")
    return ob


def export(rig, objs, base, anim=True, glb=True):
    sc = bpy.context.scene
    rig.data.pose_position = "REST"
    ad = rig.animation_data
    ad.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    for tr in ad.nla_tracks:
        tr.mute = False
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in [rig] + objs:
        o.hide_viewport = False
        o.hide_render = False
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    rig.data.pose_position = "POSE"
    bpy.ops.export_scene.fbx(
        filepath=base + ".fbx", use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        use_mesh_modifiers=True, mesh_smooth_type="FACE", use_tspace=False,
        use_armature_deform_only=True, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=anim, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode="COPY", embed_textures=True)
    if glb:
        lc = bpy.context.view_layer.layer_collection.children[rig.users_collection[0].name]
        bpy.context.view_layer.active_layer_collection = lc
        bpy.ops.export_scene.gltf(
            filepath=base + ".glb", export_format="GLB", use_selection=True, use_active_scene=True,
            use_active_collection=True, use_active_collection_with_nested=True,
            export_yup=True,
            export_apply=False, export_skins=True, export_animations=anim,
            export_animation_mode="NLA_TRACKS", export_def_bones=True)
    for tr in ad.nla_tracks:
        tr.mute = True
