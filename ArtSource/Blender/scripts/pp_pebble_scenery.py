"""Rebuilds and exports only the Pebble Beach scenery (PebbleBeach_Scenery.fbx) around the current course layout.

    Blender -b ArtSource/Blender/PuttshackPro_PebbleBeach.blend --python ArtSource/Blender/scripts/pp_pebble_scenery.py

Run Unity's Puttshack > Export Course Layout For Blender first. The open .blend is copied to *_backup.blend before it
is saved. The cypress and tee-marker models are left untouched (pp_pebble.build_all() rebuilds those too).
"""
import os
import shutil
import sys

import bpy

ROOT = "/Users/calebsnyder/Real-Golf"
sys.path.insert(0, os.path.join(ROOT, "ArtSource/Blender/scripts"))
import pp_pebble as P  # noqa: E402

blend = bpy.data.filepath
if blend:
    backup = blend.replace(".blend", "_backup.blend")
    shutil.copy2(blend, backup)
    print("[backup]", os.path.relpath(backup, ROOT))
P.build_all(export=True, models=False)
if blend:
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    print("[saved]", os.path.relpath(blend, ROOT))
