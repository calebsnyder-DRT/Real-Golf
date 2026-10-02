"""Stylized golfers (Caleb, Maya) from the image-to-3D scans: headless build stages.

Sources (ArtSource/Blender/stylized/source/):
  <name>_body.glb   Hunyuan3D-2 shape from the full-body A-pose concept
  <name>_head.glb   Hunyuan3D-2 shape from the face close-up (far more facial detail), grafted onto the body
  views/            the concept views used for texture projection (front / back / side / face) and their mattes

Stages (Blender -b --factory-startup --python pp_stylized.py -- <stage> <name>):
  prep    normalise the body (front -Y, feet on z=0, target height), align the head scan to the body's head by
          similarity ICP and save stylized/<Name>_prep.blend
"""
import math
import os
import sys
import tempfile

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
STYL = os.path.join(ROOT, "stylized")
SRC = os.path.join(STYL, "source")
REVIEW = os.path.join(os.path.dirname(ROOT), "Review", "Characters", "stylized")

CHARS = {
    "Caleb": dict(height=1.83),
    "Maya": dict(height=1.70),
}


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o)
    ob = meshes[0]
    if len(meshes) > 1:
        with bpy.context.temp_override(active_object=ob, selected_editable_objects=meshes):
            bpy.ops.object.join()
    ob.name = ob.data.name = name
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)
    for m in list(ob.data.materials):
        ob.data.materials.clear()
    return ob


def verts(ob):
    a = np.empty(len(ob.data.vertices) * 3, dtype=np.float64)
    ob.data.vertices.foreach_get("co", a)
    return a.reshape(-1, 3)


def set_verts(ob, a):
    ob.data.vertices.foreach_set("co", a.astype(np.float32).ravel())
    ob.data.update()


def width_profile(v, z0, z1, n):
    """Half-width (max |x - centre|) of the vertex cloud in n horizontal bands from z1 (top) down to z0."""
    edges = np.linspace(z1, z0, n + 1)
    out = []
    for hi, lo in zip(edges[:-1], edges[1:]):
        s = v[(v[:, 2] <= hi) & (v[:, 2] > lo)]
        out.append(((hi + lo) / 2, (s[:, 0].max() - s[:, 0].min()) / 2 if len(s) else 0.0, s))
    return out


def neck_level(v, top, head_guess):
    """Height of the narrowest band between the widest head band and the shoulders."""
    prof = width_profile(v, top - head_guess * 1.6, top, 64)
    widths = [w for _, w, _ in prof]
    # Widest band in the upper half of the window is the cap brim / ears.
    widest = int(np.argmax(widths[: len(widths) // 2]))
    # Shoulders: first band below the head that is much wider than the head.
    head_w = widths[widest]
    end = next((i for i in range(widest + 1, len(widths)) if widths[i] > head_w * 1.6), len(widths) - 1)
    neck = widest + int(np.argmin(widths[widest:end + 1]))
    return prof[neck][0], head_w


def normalise_body(ob, height):
    v = verts(ob)
    lo, hi = v.min(0), v.max(0)
    s = height / (hi[2] - lo[2])
    v = (v - np.array([(lo[0] + hi[0]) / 2, 0, lo[2]])) * s
    # Centre depth on the feet so the golfer stands on the origin.
    feet = v[v[:, 2] < 0.08]
    v[:, 1] -= (feet[:, 1].min() + feet[:, 1].max()) / 2
    set_verts(ob, v)
    return v


def similarity(src, dst):
    """Least-squares similarity transform (s, R, t) with dst ~ s R src + t (Umeyama)."""
    mu_s, mu_d = src.mean(0), dst.mean(0)
    a, b = src - mu_s, dst - mu_d
    cov = b.T @ a / len(src)
    U, D, Vt = np.linalg.svd(cov)
    S = np.eye(3)
    if np.linalg.det(U) * np.linalg.det(Vt) < 0:
        S[2, 2] = -1
    R = U @ S @ Vt
    var = (a ** 2).sum() / len(src)
    s = np.trace(np.diag(D) @ S) / var
    t = mu_d - s * R @ mu_s
    return s, R, t


def icp(src, dst, iters=40, trim=0.8, rigid_rot=True):
    """Aligns point cloud src onto dst; returns 4x4 matrix. Trimmed nearest-neighbour similarity ICP."""
    kd = KDTree(len(dst))
    for i, p in enumerate(dst):
        kd.insert(p, i)
    kd.balance()
    M = np.eye(4)
    cur = src.copy()
    for it in range(iters):
        pairs = [kd.find(p) for p in cur]
        d = np.array([q[2] for q in pairs])
        idx = np.array([q[1] for q in pairs])
        keep = d <= np.quantile(d, trim)
        s, R, t = similarity(cur[keep], dst[idx[keep]])
        if not rigid_rot:
            R = np.eye(3)
        step = np.eye(4)
        step[:3, :3] = s * R
        step[:3, 3] = t
        M = step @ M
        cur = (s * (R @ cur.T)).T + t
        if it % 10 == 9:
            print(f"  icp {it + 1}: mean {d[keep].mean() * 1000:.2f} mm, scale {s:.4f}")
    return M


def stage_prep(name):
    cfg = CHARS[name]
    clear()
    body = import_glb(os.path.join(SRC, f"{name.lower()}_body.glb"), f"{name}_Scan")
    vb = normalise_body(body, cfg["height"])
    head = import_glb(os.path.join(SRC, f"{name.lower()}_head.glb"), f"{name}_HeadScan")
    vh = verts(head)

    top_b = vb[:, 2].max()
    neck_b, head_wb = neck_level(vb, top_b, 0.26)
    top_h = vh[:, 2].max()
    neck_h, head_wh = neck_level(vh, top_h, (top_h - vh[:, 2].min()) * 0.6)
    s0 = (top_b - neck_b) / (top_h - neck_h)
    print(f"{name}: body neck {neck_b:.3f} (top {top_b:.3f}, head half-width {head_wb:.3f}); head scan neck {neck_h:.3f}, s0 {s0:.4f}")

    band_b = vb[vb[:, 2] > neck_b]
    band_h = vh[vh[:, 2] > neck_h]
    c_b = np.array([np.median(band_b[:, 0]), np.median(band_b[:, 1]), top_b])
    c_h = np.array([np.median(band_h[:, 0]), np.median(band_h[:, 1]), top_h])
    vh = (vh - c_h) * s0 + c_b
    set_verts(head, vh)

    rng = np.random.default_rng(1)
    src = vh[vh[:, 2] > neck_b + 0.01]
    dst = band_b
    src = src[rng.choice(len(src), min(6000, len(src)), replace=False)]
    dst = dst[rng.choice(len(dst), min(12000, len(dst)), replace=False)]
    M = icp(src, dst)
    vh = (M[:3, :3] @ vh.T).T + M[:3, 3]
    set_verts(head, vh)
    # The scan was generated in the close-up's camera frame; the projection camera needs the rotation ICP applied.
    R = M[:3, :3] / np.cbrt(np.linalg.det(M[:3, :3]))
    head["pp_face_rot"] = [float(x) for x in R.ravel()]
    head["pp_neck"] = float(neck_b)
    body["pp_neck"] = float(neck_b)
    body["pp_height"] = cfg["height"]

    os.makedirs(STYL, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(STYL, f"{name}_prep.blend"))
    print("saved", os.path.join(STYL, f"{name}_prep.blend"))


def view_images(name):
    """Concept images and mattes per view (row 0 = top); the side concept doubles as the mirrored left view."""
    import pp_stylized_proj as PJ
    V = os.path.join(SRC, "views")
    low = name.lower()
    images, mattes, labels = {}, {}, {}
    for view, file, mirror in (("front", "front", False), ("back", "back", False), ("right", "side", False),
                               ("left", "side", True), ("face", "face", False)):
        img = PJ.load_rgba(os.path.join(V, f"{low}_{file}.jpg"), mirror)
        matte = PJ.load_rgba(os.path.join(V, f"{low}_{file}_matte.png"), mirror)[..., 3] > 0.5
        images[view], mattes[view] = img, matte
        parse = os.path.join(V, f"{low}_{file}_parse.png")
        if os.path.exists(parse):
            labels[view] = np.round(PJ.load_rgba(parse, mirror)[..., 0] * 255).astype(np.int64)
    return images, mattes, labels


# Parse labels (pp_stylized_parse.py) -> part classes.
UNKNOWN, CAP, HAIR, FACE, TOP, PANTS, BELT, SHOES, ARMS, LEGS = range(10)
CLASS_NAMES = ["unknown", "cap", "hair", "face", "top", "pants", "belt", "shoes", "arms", "legs"]
LABEL_CLASS = np.array([UNKNOWN, CAP, HAIR, FACE, TOP, PANTS, PANTS, TOP, BELT, SHOES, SHOES, FACE, LEGS, LEGS, ARMS, ARMS,
                        TOP, TOP])
CLASS_COLORS = np.array([(0.2, 0.2, 0.2), (1, 1, 1), (0.25, 0.12, 0.05), (1, 0.75, 0.6), (0.15, 0.35, 0.9), (0.6, 0.6, 0.6),
                         (0.05, 0.05, 0.05), (0, 0.85, 0.85), (0.95, 0.55, 0.35), (0.8, 0.4, 0.3)])


def edges_of(ob):
    e = np.empty(len(ob.data.edges) * 2, dtype=np.int64)
    ob.data.edges.foreach_get("vertices", e)
    return e.reshape(-1, 2)


def smooth_votes(votes, edges, iters=8):
    """Neighbour-averaged votes; unseen vertices inherit from the surface around them."""
    v = votes.copy()
    deg = np.bincount(edges.ravel(), minlength=len(v)).astype(float)
    for _ in range(iters):
        acc = v.copy()
        np.add.at(acc, edges[:, 0], v[edges[:, 1]])
        np.add.at(acc, edges[:, 1], v[edges[:, 0]])
        v = acc / (1 + deg)[:, None]
    return v


def classify(ob, votes):
    cls_votes = np.zeros((len(votes), len(CLASS_NAMES)))
    for lab, c in enumerate(LABEL_CLASS):
        cls_votes[:, c] += votes[:, lab]
    cls_votes[:, UNKNOWN] = 0
    E = edges_of(ob)
    sm = smooth_votes(cls_votes, E, 6)
    # Fill vertices nobody saw by spreading further.
    unseen = sm.sum(1) <= 1e-9
    spread = sm
    for _ in range(30):
        if not unseen.any():
            break
        spread = smooth_votes(spread, E, 4)
        unseen = spread.sum(1) <= 1e-9
    cls = np.where(sm.sum(1) > 1e-9, sm.argmax(1), spread.argmax(1))
    cls[spread.sum(1) <= 1e-9] = UNKNOWN
    return cls


def store_classes(ob, cls):
    me = ob.data
    if "part" in me.attributes:
        me.attributes.remove(me.attributes["part"])
    a = me.attributes.new("part", "INT", "POINT")
    a.data.foreach_set("value", cls.astype(np.int32))
    store_colors(ob, CLASS_COLORS[cls], np.ones(len(cls)), "partcol")


def make_views(name, body, head):
    import pp_stylized_proj as PJ
    images, mattes, labels = view_images(name)
    views = []
    for vn, weight in (("front", 1.0), ("back", 1.0), ("right", 0.5), ("left", 0.5)):
        look, right, _ = PJ.VIEWS[vn]
        h, w = mattes[vn].shape
        v = PJ.View(vn, look, right, w, h)
        PJ.fit_axis(v, [body], mattes[vn])
        v.weight = weight
        views.append(v)
        print(f"  view {vn}: fit {tuple(round(f, 3) for f in v.fit)}, warped rows {sum(r is not None for r in v.rows) if v.rows else 0}")
    face = None
    if head is not None:
        face = fit_face_view(head, mattes["face"], name)
        for bv in [v for v in views if v.name in ("back", "right", "left")]:
            views.append(fit_head_view(bv, head, images, mattes, labels))
    return views, face, images, mattes, labels


def landmark_face_view(name, head, matte):
    """Camera for the close-up solved from hand-placed landmarks (views/{name}_face_landmarks.json): clay-render
    points are lifted onto the scan by ray casting, then matched to the concept's points. The image-to-3D scans
    square the head up and come out narrower than the concept, so silhouettes alone can't place the features."""
    import json
    import pp_stylized_proj as PJ
    path = os.path.join(SRC, "views", f"{name.lower()}_face_landmarks.json")
    if not os.path.isfile(path):
        return None
    L = json.load(open(path))
    cam = L["clay_camera"]
    o, r, u, look = (np.array(cam[k]) for k in ("origin", "right", "up", "look"))
    bvh = PJ.bvh_of([head])
    P3, uv, used = [], [], []
    for nm, a, b in zip(L["names"], L["clay"], L["image"]):
        if a is None or b is None:
            continue
        p = o + r * (a[0] - cam["w"] / 2) / cam["scale"] - u * (a[1] - cam["h"] / 2) / cam["scale"]
        hit = bvh.ray_cast(Vector(p - look * 5), Vector(look), 20.0)
        if hit[0] is None:
            print(f"  landmark {nm}: no hit")
            continue
        P3.append(list(hit[0]))
        uv.append(b)
        used.append(nm)
    h, w = matte.shape
    face = PJ.solve_affine_view("face", np.array(P3), np.array(uv, float), Vector(look), w, h)
    face.weight = 4.0
    f = face.look
    print(f"  view face: landmarks {len(used)}, affine residual mean {face.residual.mean():.1f}px max "
          f"{face.residual.max():.1f}px ({used[int(face.residual.argmax())]}), look yaw "
          f"{math.degrees(math.atan2(-f.x, f.y)):+.1f} pitch {math.degrees(math.asin(f.z)):+.1f}")
    return face


def fit_face_view(head, matte, name=None):
    """Camera for the close-up: landmarks when available, else the scan's own front (it was generated roughly in
    the close-up's frame) carried through the rotation prep applied, fitted on silhouettes."""
    import pp_stylized_proj as PJ
    if name is not None:
        lv = landmark_face_view(name, head, matte)
        if lv is not None:
            return lv
    h, w = matte.shape
    rows = np.nonzero(matte.any(1))[0]
    R = Matrix(np.array(head["pp_face_rot"]).reshape(3, 3).tolist())
    face = PJ.View("face", R @ Vector((0, 1, 0)), R @ Vector((1, 0, 0)), w, h, up=R @ Vector((0, 0, 1)))
    # Start from a bbox fit on the head (the close-up's shoulders run off the frame), then refine a uniform
    # scale + offset on silhouette overlap: the camera's orientation is known, so no axis should stretch.
    PJ.fit_axis(face, [head], matte, region=(0, int(rows[0] + (rows[-1] - rows[0]) * 0.62)), warp_rows=False)
    full = PJ.render_mask([head], face, os.path.join(tempfile.gettempdir(), f"pp_mask_{os.getpid()}_facefull.png"))
    face.fit, score = fit_uniform(full, matte, face.fit)
    face.weight = 4.0
    f = face.look
    print(f"  view face: yaw {math.degrees(math.atan2(-f.x, f.y)):+.1f}, pitch {math.degrees(math.asin(f.z)):+.1f}, "
          f"fit {tuple(round(v, 3) for v in face.fit)}, IoU {score:.3f}")
    return face


def fit_uniform(mask, matte, fit0, ds=4):
    """Coordinate descent on (s, tx, ty), image = s * render + t, maximising silhouette IoU over the image."""
    A = matte[::ds, ::ds]
    H, W = A.shape
    yi, xi = np.mgrid[0:H, 0:W] * ds

    def iou(p):
        s, tx, ty = p
        xr = np.round((xi - tx) / s).astype(int)
        yr = np.round((yi - ty) / s).astype(int)
        ok = (xr >= 0) & (xr < mask.shape[1]) & (yr >= 0) & (yr < mask.shape[0])
        m = np.zeros_like(A)
        m[ok] = mask[yr[ok], xr[ok]]
        return (m & A).sum() / max((m | A).sum(), 1)
    sx, tx, sy, ty = fit0
    s = (sx + sy) / 2
    # Keep the bbox fit's centre when switching to one scale.
    cx = np.nonzero(mask.any(0))[0]
    mid = (cx[0] + cx[-1]) / 2
    p = np.array([s, tx + mid * (sx - s), ty])
    best = iou(p)
    steps = np.array([0.04, 24.0, 24.0])
    while steps[1] >= 1.0:
        improved = False
        for k in range(3):
            for sign in (1, -1):
                q = p.copy()
                q[k] += sign * steps[k]
                v = iou(q)
                if v > best:
                    best, p, improved = v, q, True
        if not improved:
            steps /= 2
    return (p[0], p[1], p[0], p[2]), best


def fit_head_view(bv, head, images, mattes, labels):
    """Refits a full-body view on the head alone (above the neck) so the close-up head scan lines up with the
    concept's head instead of inheriting the body's fit."""
    import pp_stylized_proj as PJ
    name = "h" + bv.name
    for d in (images, mattes, labels):
        d[name] = d[bv.name]
    v = PJ.View(name, bv.look, bv.right, bv.w, bv.h)
    v.scale, v.origin = bv.scale, bv.origin.copy()
    mask = PJ.render_mask([head], v, os.path.join(tempfile.gettempdir(), f"pp_mask_{os.getpid()}_{name}.png"))
    neck = Vector((0, 0, head["pp_neck"]))
    yr = int(v.render_px(np.array([list(neck)]))[0, 1])
    yi = int(bv.image_px(np.array([list(neck)]))[0, 1])
    mask[yr:] = False
    matte = mattes[name].copy()
    matte[yi:] = False

    def bbox(m):
        rr, cc = np.nonzero(m.any(1))[0], np.nonzero(m.any(0))[0]
        return cc[0], cc[-1], rr[0], rr[-1]
    mx0, mx1, my0, my1 = bbox(mask)
    ix0, ix1, iy0, iy1 = bbox(matte)
    sy = (iy1 - iy0) / max(my1 - my0, 1)
    sx = (ix1 - ix0) / max(mx1 - mx0, 1)
    # A ponytail or brim the scan shaped differently skews a bbox fit; settle on one scale by overlap instead.
    v.fit, score = fit_uniform(mask, matte, (sx, ix0 - mx0 * sx, sy, iy0 - my0 * sy))
    v.mask = mask
    v.weight = 1.0
    print(f"  view {name}: fit {tuple(round(f, 3) for f in v.fit)}, IoU {score:.3f}")
    return v


def store_colors(ob, rgb, w, attr="proj"):
    me = ob.data
    if attr in me.color_attributes:
        me.color_attributes.remove(me.color_attributes[attr])
    ca = me.color_attributes.new(attr, "FLOAT_COLOR", "POINT")
    rgba = np.concatenate([rgb, np.clip(w, 0, 1)[:, None]], 1).astype(np.float32)
    ca.data.foreach_set("color", rgba.ravel())


def normals(ob):
    n = np.empty(len(ob.data.vertices) * 3, dtype=np.float64)
    ob.data.vertices.foreach_get("normal", n)
    return n.reshape(-1, 3)


def stage_colors(name):
    import pp_stylized_proj as PJ
    bpy.ops.wm.open_mainfile(filepath=os.path.join(STYL, f"{name}_prep.blend"))
    body = bpy.data.objects[f"{name}_Scan"]
    head = bpy.data.objects.get(f"{name}_HeadScan")
    views, face, images, mattes, labels = make_views(name, body, head)
    bvh = PJ.bvh_of([body])
    P, N = verts(body), normals(body)
    bviews = body_views(views)
    rgb, w = PJ.project(P, N, bviews, images, mattes, bvh)
    store_colors(body, rgb, w)
    store_classes(body, classify(body, PJ.project_labels(P, N, bviews, labels, mattes, bvh)))
    print(f"  body: {np.mean(w > 0) * 100:.1f}% of vertices coloured")
    if head is not None:
        hb = PJ.bvh_of([head])
        P, N = verts(head), normals(head)
        cls = classify(head, head_labels(P, N, views, face, labels, mattes, hb))
        store_classes(head, cls)
        rgb, w = head_colors(P, N, cls, views, face, images, mattes, hb)
        store_colors(head, rgb, np.ones(len(P)))
        print(f"  head: {np.mean(w > 0) * 100:.1f}% of vertices projected, palette {np.round(palette_of(rgb, w, cls), 2).tolist()}")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(STYL, f"{name}_colors{os.environ.get('PP_SUFFIX', '')}.blend"))


def body_views(views):
    return [v for v in views if v.name in ("front", "back", "right", "left")]


def head_views(views, face, use_sides):
    """The close-up owns the front of the head; the full-body front view would ghost a second, softer face over it,
    and the side concepts don't line up with the close-up's geometry well enough to paint features."""
    keep = ("hback", "hright", "hleft") if use_sides else ("hback",)
    hv = [v for v in views if v.name in keep] + [face]
    face.yield_below = 0.3
    for v in hv:
        v.yield_to = face if v is not face else None
    return hv


def head_labels(P, N, views, face, labels, mattes, bvh):
    import pp_stylized_proj as PJ
    hv = head_views(views, face, True)
    votes = PJ.project_labels(P, N, hv, labels, mattes, bvh)
    for v in hv:
        v.yield_to = None
    return votes


def palette_of(rgb, w, cls):
    """Median colour of the well-seen points of each class."""
    pal = np.full((len(CLASS_NAMES), 3), 0.5)
    for c in range(len(CLASS_NAMES)):
        m = (cls == c) & (w > 0.25 * max(w.max(), 1e-6))
        if m.sum() > 20:
            pal[c] = np.median(rgb[m], axis=0)
    return pal


def head_colors(P, N, cls, views, face, images, mattes, bvh, pal=None, fill=0.05, use_sides=True):
    """Head-fitted view colours, fading to a flat per-class colour where no view sees the surface."""
    import pp_stylized_proj as PJ
    hv = head_views(views, face, use_sides)
    rgb, w = PJ.project(P, N, hv, images, mattes, bvh, match_to=face)
    for v in hv:
        v.yield_to = None
    if pal is None:
        pal = palette_of(rgb, w, cls)
    out = (rgb * w[:, None] + pal[cls] * fill) / (w + fill)[:, None]
    return out, w


if __name__ == "__main__":
    sys.path.insert(0, HERE)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stage, name = argv[0], argv[1]
    {"prep": stage_prep, "colors": stage_colors}[stage](name)
