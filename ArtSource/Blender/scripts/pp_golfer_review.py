"""Headless review renders of a built golfer .blend (EEVEE, sunny HDRI stage).

Blender -b ArtSource/Blender/PuttshackPro_Golfer_<Name>.blend --factory-startup --python pp_golfer_review.py -- <Name> <out_prefix> [look]
look: polo (default: polo + chinos + belt), tee (tee + shorts + crew socks)
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pp_stage  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
NAME, OUT = argv[0], argv[1]
LOOK = argv[2] if len(argv) > 2 else "polo"
POSE = argv[3] if len(argv) > 3 else ""

LOOKS = {
    "polo": {"Top_Polo", "Bottom_Chinos", "Belt"},
    "tee": {"Top_Tee", "Bottom_Shorts", "Socks_Crew"},
    "shorts": {"Top_Polo", "Bottom_Shorts", "Socks_Ankle", "Belt"},
}
COLORS = {"Caleb": {"Top": "#2F6BD8", "Collar": "#F7F4EC", "Logo": "#00CBE7", "Bottom": "#B8BCC1", "Belt": "#1F1F21"},
          "Maya": {"Top": "#7B4FC9", "Collar": "#F7F4EC", "Logo": "#F28C28", "Bottom": "#EDEBE4", "Belt": "#EDEDEA"}}
EXTRA = {"Socks": "#F4F4F0", "Shoes": "#F5F5F2", "ShoeAccent": "#00CBE7", "Sole": "#2B2D30", "Buckle": "#D9DCDF"}


def srgb(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def tint():
    cols = dict(COLORS.get(NAME, COLORS["Caleb"]))
    cols.update(EXTRA)
    if LOOK == "tee":
        cols["Top"] = "#F4F2EC"
        cols["Bottom"] = "#CDB68A"
    for m in bpy.data.materials:
        if not m.name.startswith("Mat_"):
            continue
        role = m.name[4:].split("_")[0]
        if role in cols and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == "MIX" and n.data_type == "RGBA":
                    n.inputs[7].default_value = (*srgb(cols[role]), 1)
                if n.type == "BSDF_PRINCIPLED" and not any(l.to_node == n and l.to_socket.name == "Base Color" for l in m.node_tree.links):
                    n.inputs["Base Color"].default_value = (*srgb(cols[role]), 1)


def setup():
    sc = bpy.context.scene
    pp_stage.setup(sc, hdri_strength=1.0, sun_energy=3.0)
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    on = LOOKS[LOOK] | {"Head_" + NAME, "Body_Arms", "Shoes_Golf"}
    for o in bpy.data.objects:
        if o.type == "MESH" and o.parent == rig:
            vis = o.name in on
            o.hide_render = not vis
            o.hide_viewport = not vis
    if rig.animation_data and rig.animation_data.action:
        sc.frame_set(1)
    for m in bpy.data.materials:
        if m.name.startswith(("Mat_Hair", "Mat_Brow", "Mat_Lash", "Mat_Logo")):
            if hasattr(m, "surface_render_method"):
                m.surface_render_method = "DITHERED"
        if m.name == "Mat_Cornea" and m.node_tree:
            nt = m.node_tree
            nt.nodes.clear()
            out = nt.nodes.new("ShaderNodeOutputMaterial")
            gl = nt.nodes.new("ShaderNodeBsdfGlossy")
            gl.inputs["Roughness"].default_value = 0.02
            tr = nt.nodes.new("ShaderNodeBsdfTransparent")
            mix = nt.nodes.new("ShaderNodeMixShader")
            fr = nt.nodes.new("ShaderNodeFresnel")
            fr.inputs["IOR"].default_value = 1.376
            nt.links.new(fr.outputs[0], mix.inputs[0])
            nt.links.new(tr.outputs[0], mix.inputs[1])
            nt.links.new(gl.outputs[0], mix.inputs[2])
            nt.links.new(mix.outputs[0], out.inputs[0])
            if hasattr(m, "surface_render_method"):
                m.surface_render_method = "BLENDED"
    tint()
    return rig


def shot(path, target, az, el, dist, lens=85, res=(1200, 1500)):
    sc = bpy.context.scene
    cam = sc.camera
    cam.data.lens = lens
    sc.render.resolution_x, sc.render.resolution_y = res
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = 0.02
    pp_stage.render(path)


if __name__ == "__main__":
    rig = setup()
    head = rig.matrix_world @ rig.data.bones["Head"].head_local
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    shot(OUT + "_body.png", (0, 0, 0.92), 18, 6, 5.2, lens=60)
    shot(OUT + "_back.png", (0, 0, 0.92), 160, 6, 5.2, lens=60)
    face = head + Vector((0, -0.03, 0.04))
    for o in bpy.data.objects:
        if o.type == "MESH" and o.data.shape_keys:
            for k in o.data.shape_keys.key_blocks[1:]:
                k.value = 0.0
    shot(OUT + "_face.png", face, 0, 2, 0.8, lens=85, res=(1200, 1200))
    shot(OUT + "_face34.png", face, 35, 4, 0.8, lens=85, res=(1200, 1200))
    if "--waist" in sys.argv:
        shot(OUT + "_waist.png", (0, -0.05, 1.08), 20, 10, 1.2, lens=85, res=(1200, 900))
        shot(OUT + "_feet.png", (0, -0.05, 0.15), 25, 15, 1.4, lens=85, res=(1200, 900))
