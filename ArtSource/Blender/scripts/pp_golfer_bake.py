"""Skin and eye maps for a built golfer (run after pp_golfer_build.py, Cycles, headless).

  Blender -b ArtSource/Blender/PuttshackPro_Golfer_<Name>.blend --factory-startup --python pp_golfer_bake.py -- <Name>

Writes into ArtSource/Exports/Characters/<Name>/Textures:
  <Name>_Face_Mask.png, <Name>_Skin_Mask.png   R ambient occlusion, G smoothness, B thickness (0 thin), A cavity (1 open)
  <Name>_Face_Normal.png, <Name>_Skin_Normal.png  creases from the albedo
  <Name>_Eye_Mask.png                           R lid occlusion
and re-tones <Name>_Face_Albedo.png / <Name>_Skin_Albedo.png in place (redness zones, freckles for Caleb).
Shared/Textures/Skin_Pores_Normal.png is the tiling pore detail used by every skin material.
"""
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pp_golfer_build as B  # noqa: E402

BAKE_RES = 1024
LOOKS = {
    # base smoothness, T-zone gain, redness strength, freckles, hair colour at the roots (sRGB)
    "Caleb": dict(smooth=0.36, oily=0.2, red=0.32, freckles=0.5, roots=(0.36, 0.27, 0.2)),
    "Maya": dict(smooth=0.36, oily=0.1, red=0.14, freckles=0.0, roots=(0.17, 0.13, 0.12)),
}


def face_ids_with(ob, mat_prefix):
    names = [s.material.name if s.material else "" for s in ob.material_slots]
    ids = {i for i, n in enumerate(names) if n.startswith(mat_prefix)}
    return [p.index for p in ob.data.polygons if p.material_index in ids]


def split_copy(ob, keep, name):
    me = ob.data.copy()
    cp = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(cp)
    cp.matrix_world = ob.matrix_world.copy()
    if me.shape_keys:
        cp.shape_key_clear()
    keep = set(keep)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    bm.to_mesh(me)
    bm.free()
    return cp


def bake_image(target, kind, distance, name, res=BAKE_RES, attr=None, uv=None):
    """Bakes AO or an emitted colour attribute of `target` into a new float image; returns HxWx4 array."""
    img = bpy.data.images.new(name, res, res, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    m = bpy.data.materials.new(name + "_bake")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    if attr:
        a = nt.nodes.new("ShaderNodeAttribute")
        a.attribute_name = attr
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(a.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    else:
        d = nt.nodes.new("ShaderNodeBsdfDiffuse")
        nt.links.new(d.outputs[0], out.inputs["Surface"])
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.nodes.active = tex
    target.data.materials.clear()
    target.data.materials.append(m)
    if uv:
        target.data.uv_layers.active = target.data.uv_layers[uv]
    sc = bpy.context.scene
    sc.world.light_settings.distance = distance
    for o in sc.objects:
        o.select_set(False)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    sc.render.bake.margin = 12
    sc.render.bake.use_clear = True
    bpy.ops.object.bake(type="EMIT" if attr else "AO")
    a = np.empty(res * res * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(res, res, 4)


def setup_cycles():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 48
    sc.cycles.use_denoising = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")


def thickness_attr(ob, occluders, name="pp_mask", reach=0.035):
    """Per-vertex distance through the surface (ears, nostrils, eyelids are thin)."""
    deps = bpy.context.evaluated_depsgraph_get()
    trees = []
    for o in occluders:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        trees.append(BVHTree.FromBMesh(bm))
        bm.free()
    me = ob.data
    mw = ob.matrix_world
    out = np.ones(len(me.vertices), np.float32)
    for v in me.vertices:
        p = mw @ v.co
        n = (mw.to_3x3() @ v.normal).normalized()
        best = reach
        for t in trees:
            hit = t.ray_cast(p - n * 0.0006, -n, reach)
            if hit[0] is not None:
                best = min(best, (hit[0] - p).length)
        out[v.index] = best / reach
    return out


def hair_roots(face, hair, eye_z, reach=0.01):
    """Per-vertex weight of skin under and just outside the hair cap above the eyes: the scalp and a soft hairline."""
    bm = bmesh.new()
    bm.from_mesh(hair.data)
    bm.transform(hair.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    mw = face.matrix_world
    out = np.zeros(len(face.data.vertices), np.float32)
    for v in face.data.vertices:
        p = mw @ v.co
        if p.z < eye_z + 0.015:
            continue
        hit = tree.find_nearest(p, reach)
        if hit[0] is not None:
            out[v.index] = 1.0 - smoothstep(0.002, reach, np.float32(hit[3]))
    return out


def write_vertex_attr(ob, name, rgb):
    me = ob.data
    if name in me.color_attributes:
        me.color_attributes.remove(me.color_attributes[name])
    ca = me.color_attributes.new(name, "FLOAT_COLOR", "POINT")
    data = np.ones((len(me.vertices), 4), np.float32)
    data[:, :3] = rgb
    ca.data.foreach_set("color", data.ravel())


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def face_zones(face, eye_l, eye_r):
    """Per-vertex (oily, redness) weights from facial landmarks in head space (character faces -Y)."""
    co = np.array([v.co[:] for v in face.data.vertices], np.float32)
    nrm = np.array([v.normal[:] for v in face.data.vertices], np.float32)
    ec = (eye_l + eye_r) * 0.5
    x, y, z = co[:, 0] - ec[0], co[:, 1] - ec[1], co[:, 2] - ec[2]
    ax = np.abs(x)
    front = smoothstep(0.2, 0.7, -nrm[:, 1])
    forehead = smoothstep(0.012, 0.03, z) * (1 - smoothstep(0.06, 0.085, z)) * (1 - smoothstep(0.03, 0.05, ax))
    nose = (1 - smoothstep(0.012, 0.024, ax)) * smoothstep(-0.07, -0.05, z) * (1 - smoothstep(0.0, 0.015, z))
    chin = (1 - smoothstep(0.018, 0.03, ax)) * smoothstep(-0.14, -0.125, z) * (1 - smoothstep(-0.105, -0.09, z))
    oily = np.clip((forehead + nose * 1.2 + chin * 0.7) * front, 0, 1)
    tip = (1 - smoothstep(0.008, 0.02, ax)) * smoothstep(-0.06, -0.045, z) * (1 - smoothstep(-0.03, -0.018, z))
    cheek = smoothstep(0.022, 0.034, ax) * (1 - smoothstep(0.052, 0.066, ax)) * smoothstep(-0.065, -0.05, z) * (1 - smoothstep(-0.02, -0.005, z))
    ear = smoothstep(0.06, 0.07, ax) * (1 - smoothstep(0.03, 0.05, np.abs(z + 0.01)))
    red = np.clip(tip * 0.9 + cheek * front * 0.75 + ear * 0.8, 0, 1)
    return oily, red


def body_zones(ob, rig):
    """Knuckles, elbows and knees run warmer; nothing on the body is oily."""
    bones = {b.name: b for b in rig.data.bones}
    co = np.array([ob.matrix_world @ v.co for v in ob.data.vertices], np.float32)
    red = np.zeros(len(co), np.float32)
    for name, r in (("Index1", 0.035), ("Middle1", 0.035), ("Ring1", 0.03), ("Pinky1", 0.025), ("LowerArm", 0.05), ("LowerLeg", 0.07)):
        for side in ("L", "R"):
            b = bones.get("%s.%s" % (name, side))
            if b is not None:
                c = np.array((rig.matrix_world @ b.head_local)[:], np.float32)
                red = np.maximum(red, (1 - smoothstep(r * 0.4, r, np.linalg.norm(co - c, axis=1))) * 0.7)
    return red


def cavity_from(rgb, radius=5, gain=5.0):
    L = B.luminance(rgb)
    hp = L - B.blur(L[..., None], radius)[..., 0]
    return np.clip(1.0 + np.minimum(hp, 0) * gain, 0.35, 1.0), hp


def pore_normal(size=512, seed=11):
    """Tileable pores (small dimples) over fine cross-hatched skin lines."""
    rng = np.random.default_rng(seed)
    h = np.zeros((size, size), np.float32)
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    pts = rng.random((900, 2)) * size
    rad = rng.uniform(1.2, 2.6, 900)
    for (px, py), r in zip(pts, rad):
        dx = (x - px + size / 2) % size - size / 2
        dy = (y - py + size / 2) % size - size / 2
        d2 = (dx * dx + dy * dy) / (r * r)
        sel = d2 < 4
        h[sel] -= np.exp(-d2[sel] * 1.5)
    lines = B.noise_tile(size, 48, seed + 1, 3)
    h += (np.abs(lines - 0.5) < 0.03).astype(np.float32) * -0.35
    h += B.noise_tile(size, 64, seed + 2, 3) * 0.4
    h = B.blur(h[..., None], 1)[..., 0]
    return B.height_to_normal(h, 1.6)


def main(name):
    look = LOOKS[name]
    setup_cycles()
    ctex = os.path.join(B.UNITY_CHAR, name, "Textures")
    head = bpy.data.objects["Head_" + name]
    eyeballs = bpy.data.objects["Eyes_" + name]
    hair_ob = bpy.data.objects["Hair_" + name]
    arms = bpy.data.objects["Body_Arms"]
    shorts = bpy.data.objects["Bottom_Shorts"]
    for o in bpy.context.scene.objects:
        o.hide_render = True
        o.hide_set(False)

    face = split_copy(head, face_ids_with(head, "Mat_Face_"), "bake_face")
    eyes = split_copy(eyeballs, face_ids_with(eyeballs, "Mat_Eye_"), "bake_eyes")
    hair = split_copy(hair_ob, [p.index for p in hair_ob.data.polygons], "bake_hair")
    legs = split_copy(shorts, face_ids_with(shorts, "Mat_Skin_"), "bake_legs")
    body = split_copy(arms, [p.index for p in arms.data.polygons], "bake_arms")
    for o in (face, eyes, hair, legs, body):
        o.hide_render = False

    # landmarks: eye centres
    eco = np.array([eyes.matrix_world @ v.co for v in eyes.data.vertices], np.float32)
    eye_l = eco[eco[:, 0] > 0].mean(axis=0)
    eye_r = eco[eco[:, 0] < 0].mean(axis=0)

    # ---------- face
    print("[bake] %s face AO" % name)
    ao_face = bake_image(face, "AO", 0.06, "ao_face")[..., 0]
    thick = thickness_attr(face, [face, body])
    oily, red = face_zones(face, eye_l, eye_r)
    write_vertex_attr(face, "pp_mask", np.stack([oily, red, thick], 1))
    print("[bake] %s face zones" % name)
    zones = bake_image(face, "EMIT", 0.0, "zones_face", attr="pp_mask")
    roots = hair_roots(face, hair, float(eye_l[2]))
    write_vertex_attr(face, "pp_hair", np.stack([roots, roots, roots], 1))
    roots_img = bake_image(face, "EMIT", 0.0, "roots_face", attr="pp_hair")[..., 0]

    # ---------- body skin (arms + legs share the skin texture)
    print("[bake] %s body AO" % name)
    hair.hide_render = True
    face.hide_render = False
    skin_join = B.join([body, legs], "bake_skin")
    ao_skin = bake_image(skin_join, "AO", 0.08, "ao_skin")[..., 0]
    red_b = body_zones(skin_join, arms.parent)
    thick_b = thickness_attr(skin_join, [skin_join], reach=0.05)
    write_vertex_attr(skin_join, "pp_mask", np.stack([np.zeros_like(red_b), red_b, thick_b], 1))
    zones_b = bake_image(skin_join, "EMIT", 0.0, "zones_skin", attr="pp_mask")

    # ---------- eyes: lids and lashes shade the top of the eyeball
    print("[bake] %s eye AO" % name)
    face.hide_render = False
    eye_ao = bake_image(eyes, "AO", 0.02, "ao_eye", res=512)[..., 0]

    # ---------- compose
    def up(a, n=B.TEX_RES):
        return B.resize(a[..., None] if a.ndim == 2 else a, n)

    for part, ao, zn, path_key, extra in (
        ("Face", ao_face, zones, "Face", True),
        ("Skin", ao_skin, zones_b, "Skin", False),
    ):
        hw = up(roots_img, B.load_px(os.path.join(ctex, "%s_%s_Albedo.png" % (name, path_key))).shape[0])[..., 0] if extra else 0.0
        alb_path = os.path.join(ctex, "%s_%s_Albedo.png" % (name, path_key))
        alb = B.load_px(alb_path)[..., :3]
        n = alb.shape[0]
        ao_n = up(ao, n)[..., 0]
        z = up(zn, n)
        cav, hp = cavity_from(alb)
        smooth = look["smooth"] + look["oily"] * z[..., 0] - (1 - cav) * 0.25
        # lips and other saturated pink zones are a touch wetter
        sat = (alb[..., 0] - alb[..., 1]) / np.maximum(alb[..., 0], 1e-3)
        smooth += B.blur(np.clip((sat - 0.3) / 0.15, 0, 1)[..., None], 3)[..., 0] * 0.12
        smooth -= hw * 0.2
        thickness = np.clip(z[..., 2], 0, 1)
        mask = np.stack([np.clip((ao_n * 0.85 + 0.15) * (1 - hw * 0.35), 0, 1), np.clip(smooth, 0.05, 0.85), thickness, cav], -1)
        B.save_px(mask, os.path.join(ctex, "%s_%s_Mask.png" % (name, part)), srgb=False)

        redness = z[..., 1][..., None] * look["red"]
        tint = np.array([1.0, 0.8, 0.78], np.float32)
        out = alb * (1 - redness) + alb * tint * redness
        if extra and look["freckles"] > 0:
            rng = np.random.default_rng(5)
            spots = np.zeros((n, n), np.float32)
            for _ in range(2600):
                cx, cy = rng.integers(0, n, 2)
                r = rng.uniform(1.2, 3.0)
                y0, y1 = max(0, cy - 6), min(n, cy + 7)
                x0, x1 = max(0, cx - 6), min(n, cx + 7)
                yy, xx = np.mgrid[y0:y1, x0:x1]
                spots[y0:y1, x0:x1] = np.maximum(spots[y0:y1, x0:x1], np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / (r * r)) * rng.uniform(0.4, 1.0))
            w = spots * np.clip(z[..., 1] * 1.6, 0, 1) * look["freckles"]
            out = out * (1 - w[..., None] * 0.28) + out * np.array([0.78, 0.6, 0.48], np.float32) * w[..., None] * 0.28
        out *= (cav[..., None] * 0.18 + 0.82)
        if extra:
            # hair roots: the scalp under the cap and a soft fade below the hairline take the hair colour
            k = (B.blur(np.asarray(hw)[..., None], 2)[..., 0] * 0.55)[..., None]
            out = out * (1 - k) + np.array(look["roots"], np.float32) * k
        B.save_px(np.clip(out, 0, 1), alb_path)
        hgt = B.blur(hp[..., None], 1)[..., 0]
        B.save_px(B.height_to_normal(hgt * -1.0, 6.0), os.path.join(ctex, "%s_%s_Normal.png" % (name, part)), srgb=False)

    eye_mask = np.ones((512, 512, 4), np.float32)
    eye_mask[..., 0] = np.clip(eye_ao * 0.9 + 0.1, 0, 1)
    B.save_px(eye_mask, os.path.join(ctex, "%s_Eye_Mask.png" % name), srgb=False)

    pores = os.path.join(B.SHARED_TEX, "Skin_Pores_Normal.png")
    if not os.path.exists(pores):
        B.save_px(pore_normal(), pores, srgb=False)
    print("[bake] %s done" % name)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(argv[0] if argv else "Caleb")
