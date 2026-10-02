"""Projects the 2D concept views onto the scanned meshes (per vertex or per texel).

Each view is an orthographic camera matched to its concept image: the mesh silhouette rendered from that camera is
fitted to the image matte with an axis-aligned scale/offset, then (for full-body views) a per-row piecewise-linear
x warp that lines up every silhouette run (arm | torso | arm, leg | leg). Samples are weighted by how squarely the
surface faces the view and dropped where the surface is hidden from it.
"""
import math
import os
import tempfile

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# Blender convention: the golfer faces -Y, +X is the golfer's left.
VIEWS = {
    # name: (direction the camera looks along, image-right axis, mirrored source)
    "front": (Vector((0, 1, 0)), Vector((1, 0, 0)), False),
    "back": (Vector((0, -1, 0)), Vector((-1, 0, 0)), False),
    "right": (Vector((1, 0, 0)), Vector((0, -1, 0)), False),   # side concept: camera on the golfer's right
    "left": (Vector((-1, 0, 0)), Vector((0, 1, 0)), True),     # same image mirrored
}


def load_rgba(path, mirror=False):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(h, w, 4)[::-1]  # row 0 = top
    if mirror:
        a = a[:, ::-1]
    return np.ascontiguousarray(a)


class View:
    """World -> image pixel mapping for one concept view."""

    def __init__(self, name, look, right, width, height, up=None):
        self.name = name
        self.look = look.normalized()
        self.right = right.normalized()
        self.up = (up or Vector((0, 0, 1))).normalized()
        self.w, self.h = width, height
        self.scale = 1.0     # pixels per metre (before fitting)
        self.origin = Vector((0, 0, 0))
        self.fit = (1.0, 0.0, 1.0, 0.0)  # x' = sx*x + tx, y' = sy*y + ty (render px -> image px)
        self.rows = None     # per-row warp knots: list of (src_knots, dst_knots) or None

    def render_px(self, P):
        """World points (n,3) -> render pixel coords (n,2), y down."""
        r = np.array(self.right)
        u = np.array(self.up)
        o = np.array(self.origin)
        x = (P - o) @ r * self.scale + self.w / 2
        y = self.h / 2 - (P - o) @ u * self.scale
        return np.stack([x, y], 1)

    def image_px(self, P):
        q = self.render_px(P)
        sx, tx, sy, ty = self.fit
        x = q[:, 0] * sx + tx
        y = q[:, 1] * sy + ty
        if self.rows is not None:
            x = self.warp_x(x, y)
        return np.stack([x, y], 1)

    def warp_x(self, x, y):
        out = x.copy()
        ri = np.clip(np.round(y).astype(int), 0, self.h - 1)
        for row in np.unique(ri):
            knots = self.rows[row]
            if knots is None:
                continue
            sel = ri == row
            out[sel] = np.interp(x[sel], knots[0], knots[1])
        return out


def tps_fit(src, dst, lam=1e-3):
    """2D thin-plate spline mapping src points onto dst; returns a callable on (n,2) arrays."""
    def U(r2):
        return np.where(r2 > 0, 0.5 * r2 * np.log(np.maximum(r2, 1e-12)), 0.0)
    n = len(src)
    scale = np.ptp(src, axis=0).max()
    s = src / scale
    K = U(((s[:, None] - s[None]) ** 2).sum(-1)) + lam * np.eye(n)
    Pm = np.hstack([np.ones((n, 1)), s])
    L = np.zeros((n + 3, n + 3))
    L[:n, :n], L[:n, n:], L[n:, :n] = K, Pm, Pm.T
    rhs = np.zeros((n + 3, 2))
    rhs[:n] = dst
    coef = np.linalg.solve(L, rhs)

    def f(q):
        qs = q / scale
        Kq = U(((qs[:, None] - s[None]) ** 2).sum(-1))
        return Kq @ coef[:n] + np.hstack([np.ones((len(q), 1)), qs]) @ coef[n:]
    return f


class AffineView:
    """World -> image pixel mapping solved from landmarks: an affine (orthographic) camera, plus a thin-plate
    correction that pins every landmark exactly and fades to nothing outside the face."""

    def __init__(self, name, A, b, look, width, height, warp=None):
        self.name, self.A, self.b = name, A, b
        self.look = look.normalized()
        self.w, self.h = width, height
        self.warp = warp
        self.rows = None
        self.weight = 1.0

    def image_px(self, P):
        q = P @ self.A.T + self.b
        return self.warp(q) if self.warp is not None else q


def solve_affine_view(name, P3, uv, look_hint, width, height, ring=1.8):
    """Least-squares affine camera uv ~ A P + b from 3D/2D landmark pairs, then a TPS on the residuals anchored to
    zero on a ring around the landmarks (so hair and ears keep the plain affine mapping)."""
    X = np.hstack([P3, np.ones((len(P3), 1))])
    M, *_ = np.linalg.lstsq(X, uv, rcond=None)
    A, b = M[:3].T, M[3]
    q = P3 @ A.T + b
    res = np.linalg.norm(q - uv, axis=1)
    d = Vector(np.cross(A[0], A[1])).normalized()
    if d.dot(look_hint) < 0:
        d = -d
    c = uv.mean(0)
    rad = np.linalg.norm(uv - c, axis=1).max() * ring
    t = np.linspace(0, 2 * np.pi, 16, endpoint=False)
    anchors = c + rad * np.stack([np.cos(t), np.sin(t)], 1)
    src = np.vstack([q, anchors])
    dst = np.vstack([uv, anchors])
    inner = tps_fit(src, dst)
    def warp(p):
        out = p.copy()
        near = np.linalg.norm(p - c, axis=1) < rad
        out[near] = inner(p[near])
        return out
    v = AffineView(name, A, b, d, width, height, warp)
    v.residual = res
    return v


def render_mask(objs, view, path):
    """Workbench silhouette of objs from the view camera at the image resolution."""
    sc = bpy.context.scene
    cam = bpy.data.objects.get("PP_ProjCam") or bpy.data.objects.new("PP_ProjCam", bpy.data.cameras.new("PP_ProjCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.clip_start, cam.data.clip_end = 0.01, 100
    cam.data.ortho_scale = max(view.w, view.h) / view.scale
    z = -view.look
    x = view.right
    y = z.cross(x)
    from mathutils import Matrix
    R = Matrix((x, y, z)).transposed()
    cam.matrix_world = Matrix.Translation(view.origin - view.look * 20) @ R.to_4x4()
    sc.camera = cam
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "FLAT"
    sc.display.shading.color_type = "SINGLE"
    sc.display.shading.single_color = (1, 1, 1)
    sc.display.shading.background_type = "WORLD"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("PP_World")
    sc.world.color = (0, 0, 0)
    sc.render.resolution_x, sc.render.resolution_y = view.w, view.h
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "PNG"
    hidden = []
    for o in sc.objects:
        if o.type == "MESH" and o not in objs and not o.hide_render:
            o.hide_render = True
            hidden.append(o)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in hidden:
        o.hide_render = False
    return load_rgba(path)[..., 0] > 0.5


def fit_axis(view, objs, matte, region=None, warp_rows=True):
    """Sets the view's scale/origin from the mesh bounds, then fits the render silhouette to the matte."""
    pts = np.array([list(o.matrix_world @ Vector(c)) for o in objs for c in o.bound_box])
    r, u = np.array(view.right), np.array(view.up)
    xs, ys = pts @ r, pts @ u
    rows = np.nonzero(matte.any(1))[0]
    cols = np.nonzero(matte.any(0))[0]
    # Start: mesh height spans the matte height.
    view.scale = (rows[-1] - rows[0]) / (ys.max() - ys.min())
    c = pts.mean(0)
    view.origin = Vector(c)
    tmp = os.path.join(tempfile.gettempdir(), f"pp_mask_{os.getpid()}_{view.name}.png")
    mask = render_mask(objs, view, tmp)
    if region is not None:
        y0, y1 = region
        mask = mask.copy()
        matte = matte.copy()
        mask[:y0] = mask[y1:] = False
        matte[:y0] = matte[y1:] = False
    # Axis-aligned fit from bounding boxes of the (region-limited) silhouettes.
    def bbox(m):
        rr = np.nonzero(m.any(1))[0]
        cc = np.nonzero(m.any(0))[0]
        return cc[0], cc[-1], rr[0], rr[-1]
    mx0, mx1, my0, my1 = bbox(mask)
    ix0, ix1, iy0, iy1 = bbox(matte)
    sy = (iy1 - iy0) / max(my1 - my0, 1)
    ty = iy0 - my0 * sy
    sx = (ix1 - ix0) / max(mx1 - mx0, 1)
    tx = ix0 - mx0 * sx
    view.fit = (sx, tx, sy, ty)
    view.mask = mask
    if warp_rows:
        view.rows = row_warp(mask, matte, view.fit, view.h)
    return view


def runs(row):
    d = np.diff(np.concatenate([[0], row.astype(np.int8), [0]]))
    s = np.nonzero(d == 1)[0]
    e = np.nonzero(d == -1)[0] - 1
    keep = (e - s) >= 2
    return s[keep], e[keep]


def row_warp(mask, matte, fit, h):
    """Per image row: knots mapping fitted-render x to image x so silhouette runs coincide."""
    sx, tx, sy, ty = fit
    H, W = mask.shape
    out = [None] * h
    for yi in range(h):
        yr = int(round((yi - ty) / sy))
        if yr < 0 or yr >= H:
            continue
        ms, me = runs(mask[yr])
        js, je = runs(matte[yi])
        if len(ms) == 0 or len(ms) != len(js):
            continue
        src = np.ravel(np.column_stack([ms * sx + tx, me * sx + tx]))
        dst = np.ravel(np.column_stack([js, je])).astype(float)
        out[yi] = (np.concatenate([[-1e6], src, [1e6]]), np.concatenate([[-1e6 + dst[0] - src[0]], dst, [1e6 + dst[-1] - src[-1]]]))
    # Fill rows without a matching run count from the nearest row that has one.
    have = [i for i, k in enumerate(out) if k is not None]
    if not have:
        return None
    for yi in range(h):
        if out[yi] is None:
            j = min(have, key=lambda k: abs(k - yi))
            if abs(j - yi) < 40:
                out[yi] = out[j]
    return out


def bilinear(img, xy):
    h, w = img.shape[:2]
    x = np.clip(xy[:, 0], 0, w - 1.001)
    y = np.clip(xy[:, 1], 0, h - 1.001)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    a = img[y0, x0] * (1 - fx) + img[y0, x0 + 1] * fx
    b = img[y0 + 1, x0] * (1 - fx) + img[y0 + 1, x0 + 1] * fx
    return a * (1 - fy) + b * fy


def bvh_of(objs):
    V, F = [], []
    off = 0
    for o in objs:
        me = o.data
        V.extend(o.matrix_world @ v.co for v in me.vertices)
        F.extend([off + i for i in p.vertices] for p in me.polygons)
        off += len(me.vertices)
    return BVHTree.FromPolygons(V, F)


def visibility(P, N, look, bvh, eps=0.002):
    """True where the point sees the camera (ray toward the viewer escapes the meshes)."""
    d = -look
    out = np.zeros(len(P), dtype=bool)
    for i in range(len(P)):
        o = Vector(P[i]) + Vector(N[i]) * eps + d * eps
        out[i] = bvh.ray_cast(o, d, 50.0)[0] is None
    return out


def erode(mask, r):
    m = mask.copy()
    for _ in range(r):
        m[1:] &= m[:-1].copy()
        m[:-1] &= m[1:].copy()
        m[:, 1:] &= m[:, :-1].copy()
        m[:, :-1] &= m[:, 1:].copy()
    return m


def samples(P, N, view, matte, bvh, power, min_facing, erode_px=3):
    """Indices of points this view sees inside its (eroded) matte, their image coords and facing weights."""
    facing = -(N @ np.array(view.look))
    w = np.clip((facing - min_facing) / (1 - min_facing), 0, 1) ** power
    idx = np.nonzero(w > 0)[0]
    vis = visibility(P[idx], N[idx], view.look, bvh)
    idx = idx[vis]
    xy = view.image_px(P[idx])
    m = erode(matte, erode_px)
    xi = np.clip(np.round(xy[:, 0]).astype(int), 0, m.shape[1] - 1)
    yi = np.clip(np.round(xy[:, 1]).astype(int), 0, m.shape[0] - 1)
    inside = m[yi, xi]
    idx, xy = idx[inside], xy[inside]
    w = w[idx] * view.weight
    # A dominant view (the face close-up) leaves only the surfaces it barely sees to the others.
    owner = getattr(view, "yield_to", None)
    if owner is not None:
        f = -(N[idx] @ np.array(owner.look))
        w = w * np.clip((owner.yield_below - f) / 0.15, 0, 1)
    return idx, xy, w


def project(P, N, views, images, mattes, bvh, power=3.0, min_facing=0.08, match_to=None):
    """Blends every view's colour for points P with normals N; returns rgb (n,3) and total weight (n,).
    match_to: a view whose colours the others are gain-matched to where both see the surface (the concepts were
    generated separately and differ in exposure)."""
    acc = np.zeros((len(P), 3))
    wsum = np.zeros(len(P))
    got = {}
    for view in views:
        # Gains are measured without the yield mask so the overlap with the reference view is non-empty.
        owner = getattr(view, "yield_to", None)
        view.yield_to = None
        idx, xy, w = samples(P, N, view, mattes[view.name], bvh, power, min_facing)
        view.yield_to = owner
        got[view.name] = (idx, bilinear(images[view.name][..., :3], xy), w)
    ref = got.get(match_to.name) if match_to is not None else None
    for view in views:
        idx, col, w = got[view.name]
        if ref is not None and view is not match_to:
            common, ia, ib = np.intersect1d(idx, ref[0], return_indices=True)
            if len(common) > 200:
                gain = np.median(ref[1][ib], axis=0) / np.maximum(np.median(col[ia], axis=0), 1e-3)
                col = col * np.clip(gain, 0.7, 1.4)
        owner = getattr(view, "yield_to", None)
        if owner is not None:
            f = -(N[idx] @ np.array(owner.look))
            w = w * np.clip((owner.yield_below - f) / 0.15, 0, 1)
        acc[idx] += col * w[:, None]
        wsum[idx] += w
    rgb = acc / np.maximum(wsum, 1e-6)[:, None]
    return rgb, wsum


def project_labels(P, N, views, labels, mattes, bvh, n_labels=18, power=2.0, min_facing=0.05):
    """Weighted label votes (n, n_labels) from every view's parse map (nearest pixel)."""
    votes = np.zeros((len(P), n_labels))
    for view in views:
        idx, xy, w = samples(P, N, view, mattes[view.name], bvh, power, min_facing, erode_px=1)
        lab = labels[view.name]
        xi = np.clip(np.round(xy[:, 0]).astype(int), 0, lab.shape[1] - 1)
        yi = np.clip(np.round(xy[:, 1]).astype(int), 0, lab.shape[0] - 1)
        np.add.at(votes, (idx, lab[yi, xi]), w)
    return votes
