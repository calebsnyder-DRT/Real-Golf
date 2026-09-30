"""Sunny HDRI review stage for the CC0 set (scene PP_CC0)."""
import math

import bpy
from mathutils import Vector

import pp_lib

HDRI = "/Users/calebsnyder/Real-Golf/ArtSource/Blender/textures/kloofendal_48d_partly_cloudy_puresky_2k.hdr"
STAGE = "PP_Stage"


def setup(scene, hdri_strength=1.1, sun_energy=3.2):
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [
        e.identifier for e in scene.render.bl_rna.properties["engine"].enum_items] else scene.render.engine
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        pass
    ee = scene.eevee
    for attr, val in (("taa_render_samples", 64), ("use_raytracing", True), ("use_shadows", True)):
        if hasattr(ee, attr):
            setattr(ee, attr, val)
    try:
        scene.view_settings.view_transform = "Khronos PBR Neutral"
    except TypeError:
        scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "None"
    scene.render.film_transparent = False

    w = bpy.data.worlds.get("PP_SkyWorld") or bpy.data.worlds.new("PP_SkyWorld")
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Rotation"].default_value = (0, 0, math.radians(110))
    env = nt.nodes.new("ShaderNodeTexEnvironment")
    env.image = bpy.data.images.load(HDRI, check_existing=True)
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = hdri_strength
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(tc.outputs["Generated"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], env.inputs["Vector"])
    nt.links.new(env.outputs["Color"], bg.inputs["Color"])
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
    scene.world = w

    coll = bpy.data.collections.get(STAGE)
    if not coll:
        coll = bpy.data.collections.new(STAGE)
        scene.collection.children.link(coll)
    sun = bpy.data.objects.get("PP_StageSun")
    if not sun:
        sun = bpy.data.objects.new("PP_StageSun", bpy.data.lights.new("PP_StageSun", "SUN"))
        coll.objects.link(sun)
    sun.data.energy = sun_energy
    sun.data.angle = math.radians(3.0)
    sun.data.color = (1.0, 0.96, 0.9)
    sun.rotation_euler = (math.radians(42), math.radians(6), math.radians(-35))

    ground = bpy.data.objects.get("PP_StageGround")
    if not ground:
        me = bpy.data.meshes.new("PP_StageGround")
        import bmesh
        bm = bmesh.new()
        bmesh.ops.create_circle(bm, cap_ends=True, segments=64, radius=30)
        bm.to_mesh(me)
        bm.free()
        ground = bpy.data.objects.new("PP_StageGround", me)
        coll.objects.link(ground)
    gm = grass_mat()
    ground.data.materials.clear()
    ground.data.materials.append(gm)

    cam = bpy.data.objects.get("PP_StageCam")
    if not cam:
        cam = bpy.data.objects.new("PP_StageCam", bpy.data.cameras.new("PP_StageCam"))
        coll.objects.link(cam)
    scene.camera = cam
    return coll


def grass_mat():
    m = bpy.data.materials.get("PP_StageGrass") or bpy.data.materials.new("PP_StageGrass")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.6
    noise.inputs["Detail"].default_value = 3.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = pp_lib.srgb("#5E9E3A")
    ramp.color_ramp.elements[1].color = pp_lib.srgb("#7DB84A")
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m


def bbox_of(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objs:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        mw = o.matrix_world
        pts += [mw @ v.co for v in me.vertices]
        ev.to_mesh_clear()
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    return lo, hi


def frame(objs, az=25.0, el=8.0, lens=60, margin=1.12, res=(1400, 1400)):
    scene = bpy.context.scene
    cam = scene.camera
    cam.data.lens = lens
    scene.render.resolution_x, scene.render.resolution_y = res
    lo, hi = bbox_of(objs)
    c = (lo + hi) / 2
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z * res[0] / res[1])
    fov = 2 * math.atan(36 / 2 / lens)
    dist = size * margin / 2 / math.tan(fov / 2) + (hi - lo).length * 0.3
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = c + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_end = 500


def show_only(colls):
    """Show only the given collection names (plus the stage) in the view layer and render."""
    keep = set(colls) | {STAGE}
    for lc in bpy.context.view_layer.layer_collection.children:
        on = lc.name in keep
        lc.exclude = not on
        lc.collection.hide_render = not on


def render(path):
    scene = bpy.context.scene
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def stitch(paths, out_path, gap=0):
    """Place rendered PNGs side by side (same height) into one PNG."""
    import numpy as np
    imgs = [bpy.data.images.load(p, check_existing=False) for p in paths]
    h = max(i.size[1] for i in imgs)
    w = sum(i.size[0] for i in imgs) + gap * (len(imgs) - 1)
    canvas = np.ones((h, w, 4), dtype=np.float32)
    x = 0
    for im in imgs:
        iw, ih = im.size
        px = np.empty(iw * ih * 4, dtype=np.float32)
        im.pixels.foreach_get(px)
        canvas[0:ih, x:x + iw, :] = px.reshape(ih, iw, 4)
        x += iw + gap
    out = bpy.data.images.new("stitch_tmp", w, h, alpha=True)
    out.pixels.foreach_set(canvas.ravel())
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    for im in imgs + [out]:
        bpy.data.images.remove(im)


def stack(paths, out_path, gap=0):
    """Stack same-width PNGs vertically (first path on top)."""
    import numpy as np
    imgs = [bpy.data.images.load(p, check_existing=False) for p in paths]
    w = max(i.size[0] for i in imgs)
    h = sum(i.size[1] for i in imgs) + gap * (len(imgs) - 1)
    canvas = np.ones((h, w, 4), dtype=np.float32)
    y = h
    for im in imgs:
        iw, ih = im.size
        px = np.empty(iw * ih * 4, dtype=np.float32)
        im.pixels.foreach_get(px)
        y -= ih
        canvas[y:y + ih, 0:iw, :] = px.reshape(ih, iw, 4)
        y -= gap
    out = bpy.data.images.new("stack_tmp", w, h, alpha=True)
    out.pixels.foreach_set(canvas.ravel())
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    for im in imgs + [out]:
        bpy.data.images.remove(im)
