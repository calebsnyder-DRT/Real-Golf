"""Pro Shop pipeline: golfer posing, FBX export + verification, preview renders and contact sheets.

Entry point is pp_proshop.py (it registers itself as the `pp_proshop` module before importing this file).
"""
import json
import math
import os
import subprocess

import bpy
from mathutils import Matrix, Vector

from pp_lib import T, rad, srgb
from pp_proshop import *  # noqa: F401,F403  (layout constants, materials, MB helpers, room builders)
import pp_proshop as P


# ================================================================ golfer helpers
def append_golfer():
    with bpy.data.libraries.load(GOLFER_BLEND, link=False) as (src, dst):
        dst.collections = ["PP_Caleb_Modular"]
    col = dst.collections[0]
    bpy.context.scene.collection.children.link(col)
    rig = next(o for o in col.all_objects if o.type == "ARMATURE")
    return col, rig


def rot_world(rig, bone, axis, deg):
    pb = rig.pose.bones[bone]
    bpy.context.view_layer.update()
    h = pb.head.copy()
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


def pose_rest(rig):
    ad = rig.animation_data or rig.animation_data_create()
    ad.action = None
    rig.data.pose_position = "POSE"
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()


def pose_arms_down(rig, deg=80.0):
    pose_rest(rig)
    rot_world(rig, "UpperArm.L", "Y", deg)
    rot_world(rig, "UpperArm.R", "Y", -deg)


def pose_idle(rig, frame=0):
    pose_rest(rig)
    act = bpy.data.actions.get("Idle")
    if act is None:
        pose_arms_down(rig, 76.0)
        return
    ad = rig.animation_data
    ad.action = act
    if hasattr(ad, "action_slot") and len(getattr(act, "slots", [])):
        ad.action_slot = act.slots[0]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def bake(ob, name, coll):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), depsgraph=dg)
    me.name = name
    o = bpy.data.objects.new(name, me)
    o.matrix_world = ob.matrix_world.copy()
    coll.objects.link(o)
    return o


# ================================================================ export & verify
def export_fbx(objs, name):
    os.makedirs(FBX_DIR, exist_ok=True)
    vl = bpy.context.view_layer
    for o in vl.objects:
        o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    vl.objects.active = objs[0]
    path = os.path.join(FBX_DIR, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z",
                             axis_up="Y", use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             bake_anim=False, path_mode="COPY")
    meshes = [o for o in objs if o.type == "MESH"]
    lo, hi = world_bbox(meshes)
    mats = []
    for o in meshes:
        for s in o.material_slots:
            if s.material and s.material.name not in mats:
                mats.append(s.material.name)
    MANIFEST["files"][name + ".fbx"] = dict(
        objects=[o.name for o in meshes], empties=sum(1 for o in objs if o.type == "EMPTY"),
        materials=mats, tris=sum(obj_tris(o) for o in meshes),
        bbox_min=[round(v, 4) for v in lo], bbox_max=[round(v, 4) for v in hi])
    for o in objs:
        o.select_set(False)
    print("[export] %-28s %6d tris  %s" % (name, MANIFEST["files"][name + ".fbx"]["tris"], mats))
    return path


def verify():
    bpy.ops.wm.read_homefile(use_empty=True)
    report = {}
    for fn in sorted(os.listdir(FBX_DIR)):
        if not fn.lower().endswith(".fbx"):
            continue
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        for m in list(bpy.data.meshes):
            bpy.data.meshes.remove(m)
        for m in list(bpy.data.materials):
            bpy.data.materials.remove(m)
        bpy.ops.import_scene.fbx(filepath=os.path.join(FBX_DIR, fn))
        bpy.context.view_layer.update()
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        empties = [o for o in bpy.context.scene.objects if o.type == "EMPTY"]
        lo, hi = world_bbox(meshes)
        ent = dict(meshes={}, empties={}, tris=0, bbox_min=[round(v, 4) for v in lo],
                   bbox_max=[round(v, 4) for v in hi])
        for o in meshes:
            t = obj_tris(o)
            ent["tris"] += t
            ent["meshes"][o.name] = dict(tris=t, materials=[s.material.name if s.material else None
                                                            for s in o.material_slots],
                                         scale=[round(v, 4) for v in o.matrix_world.to_scale()])
        for o in empties:
            mw = o.matrix_world
            ent["empties"][o.name] = dict(pos=[round(v, 4) for v in mw.translation],
                                          rot=[round(math.degrees(a), 2) for a in mw.to_euler()])
        report[fn] = ent
        print("\n== %s  tris=%d  bbox=%s..%s" % (fn, ent["tris"], ent["bbox_min"], ent["bbox_max"]))
        for n, d in ent["meshes"].items():
            print("   MESH  %-26s %6d  %s scale=%s" % (n, d["tris"], d["materials"], d["scale"]))
        if empties:
            print("   EMPTIES %d: %s" % (len(empties), ", ".join(sorted(o.name for o in empties))))
    os.makedirs(REVIEW, exist_ok=True)
    with open(os.path.join(REVIEW, "fbx_verify.json"), "w") as f:
        json.dump(report, f, indent=1)


# ================================================================ preview rendering
def try_set(obj, attr, val):
    try:
        setattr(obj, attr, val)
        return True
    except Exception:
        return False


def setup_render(sc, res=(1920, 1080)):
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 50 if "fast" in OPT else 100
    sc.render.film_transparent = False
    ee = sc.eevee
    try_set(ee, "taa_render_samples", 24 if "fast" in OPT else 96)
    try_set(ee, "use_raytracing", True)
    try_set(ee, "ray_tracing_method", "SCREEN")
    if hasattr(ee, "ray_tracing_options"):
        try_set(ee.ray_tracing_options, "resolution_scale", "2")
        try_set(ee.ray_tracing_options, "use_denoise", True)
    try_set(ee, "use_fast_gi", True)
    try_set(ee, "fast_gi_method", "GLOBAL_ILLUMINATION")
    try_set(ee, "fast_gi_distance", 3.0)
    try_set(ee, "fast_gi_resolution", "2")
    try_set(ee, "fast_gi_ray_count", 4)
    try_set(ee, "fast_gi_step_count", 8)
    try_set(ee, "shadow_ray_count", 2)
    try_set(ee, "shadow_step_count", 8)
    try_set(ee, "use_shadows", True)
    try_set(ee, "shadow_pool_size", "1024")
    for vt in ("Khronos PBR Neutral", "AgX", "Filmic"):
        if try_set(sc.view_settings, "view_transform", vt):
            break
    try_set(sc.view_settings, "look", "None")
    sc.view_settings.exposure = 0.0
    sc.view_settings.gamma = 1.0


def sky_world(sc, strength=1.0, rot=200.0):
    w = bpy.data.worlds.new("PS_Preview_World")
    sc.world = w
    try_set(w, "use_nodes", True)
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = strength
    nt.links.new(bg.outputs[0], out.inputs[0])
    if os.path.exists(HDRI):
        env = nt.nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(HDRI, check_existing=True)
        tc = nt.nodes.new("ShaderNodeTexCoord")
        mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Rotation"].default_value = (0, 0, rad(rot))
        nt.links.new(tc.outputs["Generated"], mp.inputs["Vector"])
        nt.links.new(mp.outputs[0], env.inputs[0])
        nt.links.new(env.outputs[0], bg.inputs[0])
    else:
        bg.inputs["Color"].default_value = srgb("#9CC4E8")
    return w


def flat_world(sc, hexcol, strength):
    w = bpy.data.worlds.new("PS_Studio_World")
    sc.world = w
    try_set(w, "use_nodes", True)
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = srgb(hexcol)
    bg.inputs["Strength"].default_value = strength
    return w


def add_light(coll, name, kind, loc, target=None, power=100.0, color="#FFFFFF", size=1.0, spot=45.0, shadow=True,
              size_y=None):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = power
    ld.color = srgb(color)[:3]
    try_set(ld, "use_shadow", shadow)
    if kind == "AREA":
        if size_y:
            ld.shape = "RECTANGLE"
            ld.size, ld.size_y = size, size_y
        else:
            ld.size = size
    elif kind == "SPOT":
        ld.spot_size = rad(spot)
        ld.spot_blend = 0.5
        ld.shadow_soft_size = size
    elif kind == "POINT":
        ld.shadow_soft_size = size
    elif kind == "SUN":
        ld.angle = rad(size)
    o = bpy.data.objects.new(name, ld)
    o.location = loc
    if target is not None:
        o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    coll.objects.link(o)
    return o


def make_cam(coll, name, loc, tgt, vfov=None, ortho=None):
    cd = bpy.data.cameras.new(name)
    cd.sensor_fit = "VERTICAL"
    cd.sensor_height = 24.0
    if ortho:
        cd.type = "ORTHO"
        cd.ortho_scale = ortho
    else:
        cd.lens = 12.0 / math.tan(rad(vfov) / 2)
    cd.clip_start, cd.clip_end = 0.05, 600.0
    o = bpy.data.objects.new(name, cd)
    o.location = loc
    o.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    coll.objects.link(o)
    return o


def preview_lights(coll):
    add_light(coll, "PV_Sun", "SUN", (8, 8, 9), (0, 0, 0), 4.2, "#FFE3B8", size=1.5)
    add_light(coll, "PV_Key", "AREA", (-1.9, -2.7, 3.1), (0, 0, 1.15), 330, "#FFDDB5", size=1.6)
    add_light(coll, "PV_Fill", "AREA", (2.4, -3.6, 2.2), (0, 0, 1.0), 110, "#FFF1E0", size=2.5)
    add_light(coll, "PV_Rim", "AREA", (1.6, 2.4, 2.8), (0, 0, 1.3), 200, "#FFE9CF", size=1.0)
    add_light(coll, "PV_Ceiling_Fill", "AREA", (-0.8, -2.3, RH - 0.2), (-0.8, -2.3, 0), 380, "#FFE6C8", size=9.0,
              size_y=11.0, shadow=False)
    add_light(coll, "PV_Plinth_Spot", "SPOT", (1.35, -0.6, 3.3), (PLINTH[0], PLINTH[1], 0.6), 220, "#FFE2B8",
              size=0.15, spot=32)
    add_light(coll, "PV_Window_Bay", "AREA", (3.25, 3.0, 1.8), (1.0, 0.7, 1.4), 260, "#DDEBFF", size=2.6, size_y=3.0,
              shadow=False)
    add_light(coll, "PV_Window_Side", "AREA", (4.1, -1.0, 1.8), (1.0, -1.0, 1.4), 280, "#DDEBFF", size=5.0,
              size_y=3.0, shadow=False)
    for x in (-4.4, -2.2, 0.0, 1.0):
        add_light(coll, "PV_Wall_Wash_%+.1f" % x, "SPOT", (x, 2.7, 3.4), (x, RY1, 1.35), 240, "#FFCB8E", size=0.1,
                  spot=62)
    for y in (0.4, 1.2, 2.0):
        add_light(coll, "PV_Counter_Pendant_%.1f" % y, "POINT", (-5.0, y, 2.4), None, 45, "#FFD9A6", size=0.04)
    for x in (-1.0, 1.0):
        add_light(coll, "PV_Stage_Pendant_%+.0f" % x, "POINT", (x, -0.1, 2.8), None, 70, "#FFD9A6", size=0.05)


def instance(src, coll, name, M, mat_map=None):
    o = src.copy()
    o.name = name
    o.matrix_world = M
    o.hide_render = False
    coll.objects.link(o)
    o.hide_set(False)
    if mat_map:
        for s in o.material_slots:
            name = s.material.name if s.material else None
            if name in mat_map:
                s.link = "OBJECT"
                s.material = mat_map[name]
    return o


def slot_matrix(name):
    return bpy.data.objects[name].matrix_world.copy()


def dress_golfer(gcoll, rig):
    show = {"Head_Caleb", "Body_Arms", "Top_Polo", "Bottom_Chinos", "Belt", "Shoes_Golf"}
    for o in gcoll.all_objects:
        if o.type == "MESH":
            vis = o.name in show
            o.hide_render = not vis
            o.hide_viewport = not vis
    grey = pv("#A9ADB2", 0.8)
    black = pv("#202224", 0.5)
    for o in gcoll.all_objects:
        for s in getattr(o, "material_slots", []):
            name = s.material.name if s.material else None
            if (o.name, name) == ("Bottom_Chinos", "Mat_Bottom_Khaki"):
                s.link = "OBJECT"
                s.material = grey
            elif (o.name, name) == ("Belt", "Mat_Belt_Brown"):
                s.link = "OBJECT"
                s.material = black
    rig.location = (0.0, 0.0, 0.01)
    pose_idle(rig)


def render_views(sc, pc):
    os.makedirs(REVIEW, exist_ok=True)
    cams = {}
    for tag, (loc, tgt, fov) in (("portrait", CAM_PORTRAIT), ("overview", CAM_OVERVIEW)):
        cams[tag] = make_cam(pc, "PV_Cam_" + tag, loc, tgt, fov)
    for tag, cam in cams.items():
        sc.camera = cam
        sc.render.filepath = os.path.join(REVIEW, "proshop_%s.png" % tag)
        bpy.ops.render.render(write_still=True)
        print("[render]", sc.render.filepath)
    sc.camera = cams["portrait"]


# ---------------------------------------------------------------- slot population (preview only)
HANG_ORDER = ["navy", "blue", "sky", "teal", "green", "gold", "orange", "coral", "purple", "white", "cream"]
FOLD_COLS = ["cream", "navy", "#A9ADB2", "#C8B48A", "white", "sky", "green", "coral", "navy", "cream", "gold", "teal",
             "purple", "orange"]
BAG_SCHEMES = [  # slot: (bag, body, accent, trim)
    ("GolfBag_Cart", "cream", "#192020", "blue"), ("GolfBag_Stand", "navy", "white", "orange"),
    ("GolfBag_Tour", "white", "green", "gold"), ("GolfBag_Cart", "#192020", "cream", "coral"),
    ("GolfBag_Stand", "sky", "navy", "white"), ("GolfBag_Tour", "gold", "#192020", "cream")]


def col(c):
    return ROSTER.get(c, c)


def bag_map(body, acc, trim):
    return {"Bag_Body": pv(col(body), 0.6), "Bag_Accent": pv(col(acc), 0.55), "Bag_Trim": pv(col(trim), 0.45),
            "HeadCover": pv(col(acc), 0.9), "HeadCover_Accent": pv(col(trim), 0.85)}


def place(items, key, coll, tag, M, mat_map=None):
    return [instance(o, coll, "PV_%s_%s" % (tag, o.name), M, mat_map) for o in items[key]]


def populate_preview(pc, items):
    for k in range(len(HANG)):
        s = "Slot_Hang_%03d" % (k + 1)
        key = "Display_Tee_Hanger" if k % 4 == 3 else "Display_Polo_Hanger"
        place(items, key, pc, s, slot_matrix(s), {"Mat_Top_Blue": pv(col(HANG_ORDER[k % len(HANG_ORDER)]), 0.75)})
    k = 0
    while "Slot_Fold_%03d" % (k + 1) in bpy.data.objects:
        s = "Slot_Fold_%03d" % (k + 1)
        key = "Display_Shorts_Folded" if k % 3 == 2 else "Display_Chinos_Folded"
        place(items, key, pc, s, slot_matrix(s), {"Mat_Bottom_Khaki": pv(col(FOLD_COLS[k % len(FOLD_COLS)]), 0.8)})
        k += 1
    k = 0
    while "Slot_Shoe_%03d" % (k + 1) in bpy.data.objects:
        s = "Slot_Shoe_%03d" % (k + 1)
        mm = {"Mat_ShoeAccent_Cyan": pv(col(HANG_ORDER[(k * 3) % 11]), 0.5)}
        if k % 3 == 2:
            mm["Mat_Shoes_White"] = pv(col("navy"), 0.5)
        place(items, "Display_Shoes_Pair", pc, s, slot_matrix(s), mm)
        k += 1
    k = 0
    while "Slot_Sock_%03d" % (k + 1) in bpy.data.objects:
        s = "Slot_Sock_%03d" % (k + 1)
        if k % 3 == 2:
            place(items, "Display_Belt_Coiled", pc, s, slot_matrix(s),
                  {"Mat_Belt_Brown": pv("#202224" if k % 2 else "#6B4226", 0.5)})
        else:
            place(items, "Display_Socks_Pair", pc, s, slot_matrix(s),
                  {"Mat_Socks_White": pv(col(["white", "navy", "sky", "coral"][k % 4]), 0.85)})
        k += 1
    for k, (bag, body, acc, trim) in enumerate(BAG_SCHEMES):
        s = "Slot_Bag_%03d" % (k + 1)
        place(items, bag, pc, s, slot_matrix(s), bag_map(body, acc, trim))
    mb = bpy.data.objects["Mark_BagStand"].matrix_world
    place(items, "GolfBag_Tour", pc, "Plinth", mb @ Matrix.Rotation(rad(-15), 4, "Z"))
    neck = Vector(MANIFEST["items"]["MannequinHead"]["neck_base"])
    for k, (top, bottom) in enumerate((("teal", "cream"), ("coral", "navy"))):
        s = "Slot_Mannequin_%03d" % (k + 1)
        M = slot_matrix(s)
        mm = {"Mat_Skin_Caleb": bpy.data.materials["PS_Mannequin"], "Mat_Top_Blue": pv(col(top), 0.75),
              "Mat_Bottom_Khaki": pv(col(bottom), 0.8), "Mat_Belt_Brown": pv("#202224", 0.5)}
        for o in P.PREVIEW["mannequin_body"]:
            instance(o, pc, "PV_%s_%s" % (s, o.name), M, mm)
        place(items, "Display_MannequinHead", pc, s, M @ Matrix.Translation(neck))


# ---------------------------------------------------------------- contact sheets
def label(coll, text, loc, size=0.03, hexcol="#1E2526"):
    cu = bpy.data.curves.new("TXT", "FONT")
    cu.body = text
    cu.size = size
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new("TXT_" + text.split()[0], cu)
    ob.location = loc
    ob.rotation_euler = (rad(90), 0, 0)
    cu.materials.append(pv(hexcol, 0.6))
    coll.objects.link(ob)
    return ob


def studio_scene(name, key_power=500.0):
    st = bpy.data.scenes.new(name)
    setup_render(st)
    st.view_settings.exposure = 0.0
    flat_world(st, "#EDE6DA", 0.55)
    c = bpy.data.collections.new(name + "_Set")
    st.collection.children.link(c)
    back = MB(name + "_Backdrop")
    back.add(bx(-8, 8, 0.8, 0.85, -1.0, 6.0), pv("#E4DCCD", 0.9))
    back.add(bx(-8, 8, -6, 0.8, -0.05, 0.0), pv("#D9D0BF", 0.9))
    back.finish(c)
    add_light(c, name + "_Key", "AREA", (-2.5, -4.0, 4.0), (0, 0, 0.6), key_power, "#FFF1E0", size=3.0)
    add_light(c, name + "_Fill", "AREA", (3.5, -3.5, 2.0), (0, 0, 0.6), key_power * 0.35, "#F2F6FF", size=4.0)
    add_light(c, name + "_Rim", "AREA", (1.5, 2.5, 3.5), (0, 0, 0.6), key_power * 0.4, "#FFFFFF", size=2.0)
    return st, c


def item_bbox(objs):
    lo, hi = world_bbox(objs)
    return lo, hi


def contact_sheets(items):
    os.makedirs(REVIEW, exist_ok=True)
    st, c = studio_scene("PS_Studio_Bags", 700.0)
    x = -2.1
    for o in P.PREVIEW["mannequin_body"]:
        instance(o, c, "CS_Scale_" + o.name, Matrix.Translation((x, 0.1, 0)),
                 {"Mat_Skin_Caleb": bpy.data.materials["PS_Mannequin"]})
    neck = Vector(MANIFEST["items"]["MannequinHead"]["neck_base"])
    place(items, "Display_MannequinHead", c, "CS_Scale", Matrix.Translation(Vector((x, 0.1, 0)) + neck))
    label(c, "1.815 m golfer", (x, -0.35, 0.002), 0.06)
    x += 0.85
    for bag in ("GolfBag_Stand", "GolfBag_Cart", "GolfBag_Tour"):
        for yaw in (-28.0, 152.0):
            place(items, bag, c, "CS_%s_%d" % (bag, yaw), T((x, 0, 0), (0, 0, rad(yaw))))
            x += 0.62
        tri = MANIFEST["files"].get(bag + ".fbx", {}).get("tris", sum(obj_tris(o) for o in items[bag]))
        label(c, "%s  %d tris" % (bag.replace("GolfBag_", ""), tri), (x - 0.93, -0.35, 0.002), 0.06)
    cx = (-2.1 + x - 0.62) / 2
    cam = make_cam(c, "CS_Cam_Bags", (cx, -9.0, 1.6), (cx, 0, 0.72), 17.0)
    st.camera = cam
    st.render.filepath = os.path.join(REVIEW, "contact_bags.png")
    bpy.ops.render.render(write_still=True, scene=st.name)
    print("[render]", st.render.filepath)

    st, c = studio_scene("PS_Studio_Items", 420.0)
    cells = [("Display_Polo_Hanger", -1.3, 0.98), ("Display_Tee_Hanger", -0.72, 0.98),
             ("Display_MannequinHead", -0.18, 0.98), ("Display_Chinos_Folded", -1.3, 0.16),
             ("Display_Shorts_Folded", -0.82, 0.16), ("Display_Shoes_Pair", -0.38, 0.16),
             ("Display_Socks_Pair", -0.02, 0.16), ("Display_Belt_Coiled", 0.27, 0.16)]
    for i, kind in enumerate(("Driver", "Wood", "Iron", "Wedge", "Putter")):
        cells.append(("Display_Club_" + kind, 0.6 + i * 0.26, 0.16))
    for key, x, z in cells:
        objs = items[key]
        lo, hi = item_bbox(objs)
        dim = hi - lo
        R = Matrix.Rotation(rad(-24.0), 4, "Z")
        if key.startswith("Display_Club_"):
            lie = {"Driver": 58, "Wood": 58, "Iron": 62, "Wedge": 64, "Putter": 70}[key.split("_")[-1]]
            R = R @ Matrix.Rotation(rad(90 - lie), 4, "Y")
        M = R @ Matrix.Translation(-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, 0.0)))
        pts = [M @ Vector((a, b_, c_)) for a in (lo.x, hi.x) for b_ in (lo.y, hi.y) for c_ in (lo.z, hi.z)]
        zmin = min(p.z for p in pts)
        xmid = (min(p.x for p in pts) + max(p.x for p in pts)) / 2
        M = Matrix.Translation((x - xmid, 0.0, z - zmin)) @ M
        place(items, key, c, "CS_" + key, M)
        tri = MANIFEST["files"].get(key + ".fbx", {}).get("tris", sum(obj_tris(o) for o in objs))
        label(c, "%s\n%.0fx%.0fx%.0f cm\n%d tris" % (key.replace("Display_", "").replace("Club_", ""),
                                                      dim.x * 100, dim.y * 100, dim.z * 100, tri),
              (x, -0.3, z - 0.035), 0.03)
    cam = make_cam(c, "CS_Cam_Items", (0.05, -10.0, 0.86 + 10.0 * math.tan(rad(5))), (0.05, 0.0, 0.86), ortho=1.86)
    st.camera = cam
    st.render.filepath = os.path.join(REVIEW, "contact_items.png")
    bpy.ops.render.render(write_still=True, scene=st.name)
    print("[render]", st.render.filepath)


# ================================================================ main
def main():
    if "verify" in OPT:
        verify()
        return
    bpy.ops.wm.read_homefile(use_empty=True)
    sc = bpy.context.scene
    sc.name = "PS_ProShop"
    sc.unit_settings.system = "METRIC"
    gcoll, rig = append_golfer()
    M = make_materials()
    room_coll, room_objs, room_empties = build_room(M)
    print("[room] %d objects, %d tris, %d markers" % (len(room_objs), sum(obj_tris(o) for o in room_objs),
                                                     len(room_empties)))
    items = {}
    if "room-only" not in OPT:
        import pp_proshop_bags
        import pp_proshop_items
        items = pp_proshop_items.build_items(M, gcoll, rig)
        items.update(pp_proshop_bags.build_bags(M))
    if "no-export" not in OPT:
        export_fbx(room_objs + room_empties, "ProShop_Room")
        for name, objs in items.items():
            export_fbx(objs, name)
    for lc in bpy.context.view_layer.layer_collection.children:
        if lc.name in ("PS_Items", "PS_Bags", "PS_Preview_Src"):
            lc.exclude = True  # export sources all sit at the origin; the preview uses placed copies
    pc = new_coll("PS_Preview")
    dress_golfer(gcoll, rig)
    if items:
        populate_preview(pc, items)
    if "no-render" not in OPT:
        setup_render(sc)
        sky_world(sc)
        preview_lights(pc)
        g = M["PS_Glass"]
        bsdf = next(n for n in g.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Alpha"].default_value = 0.12  # exported opaque; see-through only in the preview
        try_set(g, "surface_render_method", "BLENDED")
        render_views(sc, pc)
        if items:
            contact_sheets(items)
    os.makedirs(REVIEW, exist_ok=True)
    with open(os.path.join(REVIEW, "proshop_manifest.json"), "w") as f:
        json.dump(MANIFEST, f, indent=1)
    if "no-save" not in OPT:
        if bpy.context.window is not None:
            bpy.context.window.scene = sc
        bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT, check_existing=False)
        print("[save]", BLEND_OUT)
    if "no-export" not in OPT and "no-verify" not in OPT:
        subprocess.run([bpy.app.binary_path, "-b", "--factory-startup", "--python", os.path.join(HERE, "pp_proshop.py"),
                        "--", "--verify"], check=False)
