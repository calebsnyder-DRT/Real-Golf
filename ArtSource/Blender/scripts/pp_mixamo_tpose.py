"""Re-exports Mixamo clips with the T-pose as their default pose (headless).

    Blender -b --factory-startup --python pp_mixamo_tpose.py -- [src_dir] [dst_dir]

Mixamo's "Without Skin" FBX files carry the first animation frame as the joints' default pose, and Unity builds a
clip's Humanoid avatar from that default pose, so a clip starting at address would treat the address as its T-pose
and lose the spine bend and arm reach when retargeted. Blender reads the real T-pose rest from the file; writing the
clip back out with the rest as the default pose and the animation as its take gives Unity a true T-pose reference.
Defaults: ArtSource/Mixamo/YBot -> ArtSource/Mixamo/YBot_TPose. Files whose output is newer are skipped.
"""
import os
import sys

import bpy
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))


def convert(src, dst):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    act = arm.animation_data.action if arm.animation_data else None
    if act is None:
        raise RuntimeError(f"{src} has no animation")
    act.name = os.path.splitext(os.path.basename(src))[0]
    for a in list(bpy.data.actions):
        if a != act:
            bpy.data.actions.remove(a)
    start, end = (int(round(v)) for v in act.frame_range)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = start, end
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"ARMATURE"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             use_armature_deform_only=False, add_leaf_bones=False, primary_bone_axis="Y",
                             secondary_bone_axis="X", bake_anim=True, bake_anim_use_all_bones=True,
                             bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode="STRIP")
    print(f"[tpose] {os.path.basename(src)} frames {start}-{end} -> {dst}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    src_dir = argv[0] if argv else os.path.join(ROOT, "ArtSource", "Mixamo", "YBot")
    dst_dir = argv[1] if len(argv) > 1 else os.path.join(ROOT, "ArtSource", "Mixamo", "YBot_TPose")
    os.makedirs(dst_dir, exist_ok=True)
    for f in sorted(os.listdir(src_dir)):
        if not f.lower().endswith(".fbx"):
            continue
        src, dst = os.path.join(src_dir, f), os.path.join(dst_dir, f)
        if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
            continue
        convert(src, dst)


if __name__ == "__main__":
    main()
