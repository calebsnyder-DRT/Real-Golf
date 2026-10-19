"""Puttshack Pro golfers (Caleb, Maya) on MPFB / MakeHuman (CC0 assets), one shared skeleton layout.

Headless:  Blender -b --factory-startup --python pp_golfer.py -- <stage> [Caleb|Maya ...]
Stages: rig (skeleton + clubs reference file for animation), build (full modular character .blend + FBX).

Skeleton: MPFB game_engine rig renamed to the existing Caleb_Rig names (Hips, Abdomen, Torso, Chest, Neck, Head,
Shoulder/UpperArm/LowerArm/Wrist/UpperLeg/LowerLeg/Foot/Toes .L/.R, Thumb1..Pinky3) so the Unity Humanoid map and
ProShopBaker (Neck/Head/Wrist.*) keep working. Rest pose is a T-pose. ClubGrip.L (child of Wrist.L) holds the clubs.
"""
import json
import math
import os
import sys

import addon_utils
import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

MPFB = "bl_ext.user_default.mpfb"
ROOT = "/Users/calebsnyder/Real-Golf"
ART = os.path.join(ROOT, "ArtSource")
BLEND_DIR = os.path.join(ART, "Blender")
REVIEW = os.path.join(ART, "Review", "Characters")
HDRI = os.path.join(BLEND_DIR, "textures", "kloofendal_48d_partly_cloudy_puresky_2k.hdr")
RIG_BLEND = os.path.join(BLEND_DIR, "PuttshackPro_GolferRig.blend")


def enable_mpfb():
    if MPFB not in bpy.context.preferences.addons:
        a = bpy.context.preferences.addons.new()
        a.module = MPFB
    addon_utils.enable(MPFB, default_set=False)


# ------------------------------------------------------------------ characters

CHARS = {
    "Caleb": dict(
        height=1.80,
        phenotype={"gender": 1.0, "age": 0.5, "muscle": 0.64, "weight": 0.5, "height": 0.542, "proportions": 0.9,
                   "race": {"african": 0.04, "asian": 0.06, "caucasian": 0.9}},
        targets=[
            ("chin/chin-prominent-incr", 0.25), ("chin/chin-width-incr", 0.2), ("chin/chin-height-incr", 0.1),
            ("head/head-square", 0.25), ("eyes/r-eye-scale-incr", 0.15), ("eyes/l-eye-scale-incr", 0.15),
            ("nose/nose-scale-horiz-decr", 0.1), ("mouth/mouth-lowerlip-volume-incr", 0.15),
            ("mouth/mouth-upperlip-volume-incr", 0.1), ("mouth/mouth-angles-up", 0.25),
            ("cheek/l-cheek-bones-incr", 0.2), ("cheek/r-cheek-bones-incr", 0.2),
            ("neck/neck-scale-horiz-incr", 0.2), ("torso/torso-scale-horiz-incr", 0.1),
        ],
        skin="young_caucasian_male/young_caucasian_male.mhmat",
        eyes="brown",
        eyebrows="eyebrow001/eyebrow001.mhclo",
        eyelashes="eyelashes01/eyelashes01.mhclo",
        hair="short02/short02.mhclo",
        skin_tone=(1.0, 0.97, 0.94),
        hair_tint=(0.23, 0.16, 0.11),
        iris=(0.42, 0.29, 0.17),
    ),
    "Maya": dict(
        height=1.66,
        phenotype={"gender": 0.0, "age": 0.47, "muscle": 0.58, "weight": 0.44, "height": 0.59, "proportions": 0.9,
                   "cupsize": 0.45, "firmness": 0.6,
                   "race": {"african": 0.18, "asian": 0.42, "caucasian": 0.4}},
        targets=[
            ("eyes/r-eye-scale-incr", 0.2), ("eyes/l-eye-scale-incr", 0.2), ("mouth/mouth-angles-up", 0.3),
            ("mouth/mouth-lowerlip-volume-incr", 0.25), ("mouth/mouth-upperlip-volume-incr", 0.2),
            ("nose/nose-scale-horiz-decr", 0.15), ("nose/nose-point-width-decr", 0.2),
            ("cheek/l-cheek-bones-incr", 0.25), ("cheek/r-cheek-bones-incr", 0.25),
            ("chin/chin-prominent-incr", 0.1), ("eyebrows/eyebrows-angle-up", 0.15),
        ],
        skin="young_asian_female/young_asian_female.mhmat",
        eyes="brown",
        eyebrows="eyebrow010/eyebrow010.mhclo",
        eyelashes="eyelashes02/eyelashes02.mhclo",
        hair="long01/long01.mhclo",
        skin_tone=(0.88, 0.79, 0.72),
        hair_tint=(0.06, 0.045, 0.04),
        iris=(0.22, 0.13, 0.08),
    ),
}

CLOTHES = {
    "polo": "namuhekam_male_polo_shirt/namuhekam_male_polo_shirt.mhclo",
    "tee": "toigo_basic_tucked_t-shirt/toigo_basic_tucked_t-shirt.mhclo",
    "pants": "toigo_wool_pants/toigo_wool_pants.mhclo",
    "shoes": "shoes05/shoes05.mhclo",
}

BONES = {"Root": "Root", "pelvis": "Hips", "spine_01": "Abdomen", "spine_02": "Torso", "spine_03": "Chest",
         "neck_01": "Neck", "head": "Head"}
for _s, _S in (("l", "L"), ("r", "R")):
    BONES.update({f"clavicle_{_s}": f"Shoulder.{_S}", f"upperarm_{_s}": f"UpperArm.{_S}", f"lowerarm_{_s}": f"LowerArm.{_S}",
                  f"hand_{_s}": f"Wrist.{_S}", f"thigh_{_s}": f"UpperLeg.{_S}", f"calf_{_s}": f"LowerLeg.{_S}",
                  f"foot_{_s}": f"Foot.{_S}", f"ball_{_s}": f"Toes.{_S}"})
    for _f, _F in (("thumb", "Thumb"), ("index", "Index"), ("middle", "Middle"), ("ring", "Ring"), ("pinky", "Pinky")):
        for _i in (1, 2, 3):
            BONES[f"{_f}_0{_i}_{_s}"] = f"{_F}{_i}.{_S}"

# Facial blendshapes exported for runtime blinks and reactions (MPFB expression units, caucasian set).
EXPRESSIONS = {
    "Blink_L": [("eye-left-closure", 1.0)],
    "Blink_R": [("eye-right-closure", 1.0)],
    "Smile": [("mouth-corner-puller", 0.85), ("mouth-elevation", 0.25), ("eye-left-slit", 0.2), ("eye-right-slit", 0.2)],
    "Frown": [("mouth-depression", 0.6), ("eyebrows-left-inner-up", 0.5), ("eyebrows-right-inner-up", 0.5),
              ("mouth-compression", 0.4)],
    "Open": [("mouth-open", 0.55), ("eyebrows-left-up", 0.4), ("eyebrows-right-up", 0.4)],
}


# ------------------------------------------------------------------ helpers

def ctx_obj(ob):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def mode(ob, m):
    ctx_obj(ob)
    if bpy.context.object.mode != m:
        bpy.ops.object.mode_set(mode=m)


def evaluated_coords(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = [v.co.copy() for v in me.vertices]
    ev.to_mesh_clear()
    return co


def mpfb_asset(kind, rel):
    from bl_ext.user_default.mpfb.services.assetservice import AssetService
    return AssetService.find_asset_absolute_path(rel, asset_subdir=kind)


# ------------------------------------------------------------------ build

def build_human(name, clothes=("polo", "tee", "pants", "shoes"), height_iter=True):
    """MPFB human with skin, eyes, brows, lashes, hair, clothes and the game_engine rig, at the target height."""
    from bl_ext.user_default.mpfb.services.humanservice import HumanService as H
    c = CHARS[name]
    pheno = json.loads(json.dumps(c["phenotype"]))

    def make(p):
        info = H._create_default_human_info_dict()
        info["name"] = name
        info["phenotype"].update({k: v for k, v in p.items() if k != "race"})
        info["phenotype"]["race"] = p["race"]
        info["rig"] = "game_engine"
        info["eyes"] = "high-poly/high-poly.mhclo"
        info["eyes_material_type"] = "MAKESKIN"
        info["eyebrows"] = c["eyebrows"]
        info["eyelashes"] = c["eyelashes"]
        info["hair"] = c["hair"]
        info["clothes"] = [CLOTHES[k] for k in clothes]
        info["skin_mhmat"] = c["skin"]
        info["skin_material_type"] = "MAKESKIN"
        info["targets"] = [{"target": t.split("/")[-1], "value": v} for t, v in c["targets"]]
        st = H.get_default_deserialization_settings()
        st["subdiv_levels"] = 0
        return H.deserialize_from_dict(info, st)

    def height_of(body):
        return max(v.z for v in evaluated_coords_no_mask(body))

    body = make(pheno)
    if height_iter:
        h = height_of(body)
        for _ in range(3):
            print("[pp_golfer] %s macro %.3f -> height %.3f" % (name, pheno["height"], h))
            if abs(h - c["height"]) < 0.008:
                break
            pheno["height"] = max(0.0, min(1.0, pheno["height"] + (c["height"] - h) / 1.04))
            remove_human(body)
            body = make(pheno)
            h = height_of(body)
        print("[pp_golfer] %s height %.3f (macro %.3f)" % (name, h, pheno["height"]))
    return body


def ground_fix(rig, meshes, shoes):
    dz = -min(v.co.z for v in shoes.data.vertices)
    T = Matrix.Translation((0, 0, dz))
    for o in meshes:
        o.data.transform(T)
        o.data.update()
    mode(rig, "EDIT")
    for eb in rig.data.edit_bones:
        if eb.name not in ("Root",):
            eb.head.z += dz
            eb.tail.z += dz
    bpy.ops.object.mode_set(mode="OBJECT")
    return dz


def evaluated_coords_no_mask(ob):
    saved = [(m, m.show_viewport) for m in ob.modifiers]
    for m, _ in saved:
        m.show_viewport = False
    co = evaluated_coords(ob)
    for m, s in saved:
        m.show_viewport = s
    return co


def remove_human(body):
    rig = body.parent
    objs = [o for o in bpy.data.objects if o is rig or o.parent is rig or o is body]
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def parts_of(body):
    rig = body.parent
    res = {"rig": rig, "body": body}
    for o in bpy.data.objects:
        if o.parent is rig and o is not body and o.type == "MESH":
            key = o.name.split(".", 1)[1] if "." in o.name else o.name
            res[key] = o
    return res


def expression_deltas(body, bodyparts):
    """Load MPFB expression units as temporary keys and return per-object vertex deltas for each EXPRESSIONS entry."""
    from bl_ext.user_default.mpfb.services.targetservice import TargetService
    from bl_ext.user_default.mpfb.services.clothesservice import ClothesService
    from bl_ext.user_default.mpfb.services.locationservice import LocationService
    unit_dir = os.path.join(LocationService.get_mpfb_data("targets"), "expression", "units", "caucasian")
    if body.data.shape_keys is None:
        body.shape_key_add(name="Basis")
    keys = {}
    for unit in sorted({u for parts in EXPRESSIONS.values() for u, _ in parts}):
        keys[unit] = TargetService.load_target(body, os.path.join(unit_dir, unit + ".target.gz"), weight=0.0, name="x_" + unit)

    def body_co():
        return evaluated_coords_no_mask(body)

    for o in bodyparts:
        ClothesService.fit_clothes_to_human(o, body)
    base_body = body_co()
    base_parts = {o.name: [v.co.copy() for v in o.data.vertices] for o in bodyparts}
    out = {}
    for ex, parts in EXPRESSIONS.items():
        for u, w in parts:
            keys[u].value = w
        bpy.context.view_layer.update()
        co = body_co()
        d = {body.name: [a - b for a, b in zip(co, base_body)]}
        for o in bodyparts:
            ClothesService.fit_clothes_to_human(o, body)
            d[o.name] = [v.co - b for v, b in zip(o.data.vertices, base_parts[o.name])]
        out[ex] = d
        for u, _ in parts:
            keys[u].value = 0.0
    bpy.context.view_layer.update()
    for o in bodyparts:
        ClothesService.fit_clothes_to_human(o, body)
    for k in list(keys.values()):
        body.shape_key_remove(k)
    if body.data.shape_keys and len(body.data.shape_keys.key_blocks) == 1:
        body.shape_key_remove(body.data.shape_keys.key_blocks[0])
    return out


def bake_and_clean(P):
    """Bake MPFB targets into the base mesh, drop masks/helpers, keep Delete.* vertex groups for region logic."""
    from bl_ext.user_default.mpfb.services.targetservice import TargetService
    body = P["body"]
    if body.data.shape_keys:
        TargetService.bake_targets(body)
    for o in [o for o in P.values() if getattr(o, "type", None) == "MESH"]:
        for m in list(o.modifiers):
            if m.type in ("MASK", "SUBSURF"):
                o.modifiers.remove(m)


def delete_helpers(body):
    """Remove MPFB helper geometry (joint cubes, clothing helpers). Returns the original indices of kept verts."""
    gi = body.vertex_groups["body"].index
    bm = bmesh.new()
    bm.from_mesh(body.data)
    dl = bm.verts.layers.deform.active
    kill = [v for v in bm.verts if gi not in v[dl] or v[dl][gi] < 0.5]
    kept = [v.index for v in bm.verts if not (gi not in v[dl] or v[dl][gi] < 0.5)]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    return kept


def rename_rig(rig):
    for old, new in BONES.items():
        b = rig.data.bones.get(old)
        if b:
            b.name = new
    for o in bpy.data.objects:
        if o.type != "MESH" or o.parent is not rig:
            continue
        for g in o.vertex_groups:
            if g.name in BONES:
                g.name = BONES[g.name]


def _aim(rig, bone, direction):
    pb = rig.pose.bones[bone]
    bpy.context.view_layer.update()
    h = pb.head.copy()
    cur = (pb.tail - pb.head).normalized()
    R = cur.rotation_difference(Vector(direction).normalized()).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


def tpose(rig, meshes):
    """Straighten arms horizontally (palms down) and hang the legs straight under the hips (feet kept flat), bake the
    deformation into meshes and make it the rest pose.

    Unity builds the Humanoid avatar from this bind pose, and the Mixamo clips copy the Y Bot's straight-legged one;
    MakeHuman's slight A and soft knees would otherwise be read as straight and add to every clip's leg angles.
    """
    mode(rig, "POSE")
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    for S, sx in (("L", 1.0), ("R", -1.0)):
        for b in (f"UpperArm.{S}", f"LowerArm.{S}", f"Wrist.{S}"):
            _aim(rig, b, (sx, 0.0, 0.0))
        foot = rig.pose.bones[f"Foot.{S}"]
        flat = foot.matrix.copy()
        for b in (f"UpperLeg.{S}", f"LowerLeg.{S}"):
            _aim(rig, b, (0.0, 0.0, -1.0))
        flat.translation = foot.matrix.translation
        foot.matrix = flat
        bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in meshes:
        if o.data.shape_keys:
            raise RuntimeError("tpose needs meshes without shape keys: " + o.name)
        co = evaluated_coords(o)
        if len(co) != len(o.data.vertices):
            raise RuntimeError("modifier changed vertex count on " + o.name)
        for v, c in zip(o.data.vertices, co):
            v.co = c
        o.data.update()
    mode(rig, "POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def add_club_grip(rig, body):
    """ClubGrip.L under Wrist.L: origin in the left palm, +Y down the shaft (thumb side), +Z = back of hand."""
    hand = rig.data.bones["Wrist.L"]
    mid = rig.data.bones["Middle1.L"]
    wrist = rig.matrix_world @ hand.head_local
    knuckle = rig.matrix_world @ mid.head_local
    along = (knuckle - wrist)
    along.z = 0.0
    along.normalize()
    center = wrist + along * (knuckle - wrist).length * 0.62
    me = body.data
    bvh = BVHTree.FromPolygons([body.matrix_world @ v.co for v in me.vertices], [p.vertices[:] for p in me.polygons])
    hit = bvh.ray_cast(center + Vector((0, 0, -0.08)), Vector((0, 0, 1)), 0.2)
    palm = hit[0] if hit[0] is not None else center + Vector((0, 0, -0.02))
    grip_r = 0.0122
    origin = palm + Vector((0, 0, -grip_r * 0.85))
    theta = math.radians(38.0)
    fwd = Vector((0, -1, 0))
    d = (along * math.cos(theta) + fwd * math.sin(theta)).normalized()
    mode(rig, "EDIT")
    eb = rig.data.edit_bones.new("ClubGrip.L")
    eb.head = rig.matrix_world.inverted() @ origin
    eb.tail = rig.matrix_world.inverted() @ (origin + d * 0.1)
    eb.align_roll(Vector((0, 0, 1)))
    eb.parent = rig.data.edit_bones["Wrist.L"]
    eb.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig.data.bones["ClubGrip.L"]


def add_clubs(rig, coll):
    import pp_golfer_clubs as GC
    clubs = GC.build_all(coll)
    bone = rig.data.bones["ClubGrip.L"]
    M = rig.matrix_world @ bone.matrix_local
    for ob in clubs.values():
        ob.data.transform(M)
        ob.matrix_world = Matrix.Identity(4)
        g = ob.vertex_groups.new(name="ClubGrip.L")
        g.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        ob.parent = rig
        sole = M @ Vector(ob["pp_club_sole"])
        ob["pp_club_sole_rest"] = list(sole)
    return clubs


def stage_rig():
    """Reference file for animation: Caleb (A-pose MPFB build -> T-pose rest), renamed rig, ClubGrip.L and clubs."""
    enable_mpfb()
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    body = build_human("Caleb", clothes=("polo", "pants", "shoes"))
    P = parts_of(body)
    bake_and_clean(P)
    delete_helpers(body)
    rig = P["rig"]
    rename_rig(rig)
    meshes = [o for k, o in P.items() if k != "rig"]
    tpose(rig, meshes)
    ground_fix(rig, meshes, next(o for k, o in P.items() if k.startswith("shoes")))
    add_club_grip(rig, body)
    coll = bpy.data.collections.new("Clubs")
    bpy.context.scene.collection.children.link(coll)
    clubs = add_clubs(rig, coll)
    rig.name = rig.data.name = "Golfer_Rig"
    for k, o in P.items():
        if k not in ("rig",):
            o.name = "Ref_" + k
    for n, ob in clubs.items():
        ob.hide_render = ob.hide_viewport = n != "Iron"
    bpy.context.scene.render.fps = 30
    os.makedirs(REVIEW, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=RIG_BLEND)
    info = {b.name: [list(rig.matrix_world @ b.head_local), list(rig.matrix_world @ b.tail_local)] for b in rig.data.bones}
    with open(os.path.join(BLEND_DIR, "golfer_rig.json"), "w") as f:
        json.dump({"bones": info, "clubs": {n: dict(sole_rest=list(o["pp_club_sole_rest"]), length=o["pp_club_length"],
                                                   lie=o["pp_club_lie"]) for n, o in clubs.items()}}, f, indent=1)
    print("[pp_golfer] rig saved", RIG_BLEND)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stage = argv[0] if argv else "rig"
    if stage == "rig":
        stage_rig()
