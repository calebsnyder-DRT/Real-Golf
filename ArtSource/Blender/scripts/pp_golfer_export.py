"""Re-exports a built golfer FBX with the body clips from PuttshackPro_GolferAnims.blend (headless, re-runnable).

  Blender -b ArtSource/Blender/PuttshackPro_Golfer_<Name>.blend --factory-startup \
      --python ArtSource/Blender/scripts/pp_golfer_export.py -- <Name> [--no-anims]

Every action in the anims file whose channels all resolve on <Name>_Rig becomes an FBX take; Unity names the clip after
the action (ProShopImportRules strips the "<object>|" prefix). Idle_Shop is renamed Idle, the take ProShopBaker poses
the shop and the mannequins with. Maya ships with the placeholder take only: her Animator plays Caleb's clips through
the shared Humanoid mapping. The FBX lands in ArtSource/Exports/Characters; Puttshack > Characters > Bake Golfers copies
it into the project.
"""
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pp_golfer as G  # noqa: E402
import pp_golfer_build as B  # noqa: E402

ANIMS = os.path.join(G.BLEND_DIR, "PuttshackPro_GolferAnims.blend")
RENAME = {"Idle_Shop": "Idle"}
SKIP_PREFIX = ("_", "AddressStatic")


def append_clips(rig):
    with bpy.data.libraries.load(ANIMS, link=False) as (src, dst):
        dst.actions = [a for a in src.actions if not a.startswith(SKIP_PREFIX)]
    clips = [a for a in dst.actions if a is not None]
    placeholder = bpy.data.actions.get("Idle")
    if any(RENAME.get(a.name) == "Idle" for a in clips) and placeholder is not None and placeholder not in clips:
        bpy.data.actions.remove(placeholder)
    for a in clips:
        a.name = RENAME.get(a.name, a.name)
        a.use_fake_user = True
    return [a.name for a in clips]


def main(name, anims=True):
    rig = bpy.data.objects[name + "_Rig"]
    parts = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig]
    for a in [a for a in bpy.data.actions if a.name != "Idle"]:
        bpy.data.actions.remove(a)
    clips = append_clips(rig) if anims and os.path.exists(ANIMS) else []
    print("[export] %s clips: %s" % (name, sorted(clips) or ["Idle (placeholder)"]))
    rig.animation_data_create()
    B.export_fbx(rig, parts, os.path.join(B.UNITY_CHAR, name, "%s_Modular.fbx" % name))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    who = [a for a in argv if not a.startswith("--")] or ["Caleb"]
    main(who[0], anims="--no-anims" not in argv)
