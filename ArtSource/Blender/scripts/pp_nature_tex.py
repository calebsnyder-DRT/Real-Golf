"""Puttshack Pro - procedural foliage textures for the course trees (pp_nature.py).

Leaf-card atlases are painted with anti-aliased numpy primitives (2x supersampled) so they are deterministic and need no
photo sources: each 2048 atlas holds three spray clusters (cells 0-2, stem at the bottom centre of the cell, alpha cut
out) and one opaque "dense foliage" fill (cell 3) for the dark inner canopy shells.

    cell layout (u right, v up):   2 | 3
                                   0 | 1

Species:
  cypress - Monterey cypress: dense cords of tiny scale leaves forming domed sprays, deep green with olive tips
  pine    - Monterey pine: bottle-brush tufts of long needles (3 per bundle) on twigs, brighter green
  oak     - coast live oak: small cupped, leathery leaves on zig-zag twigs, dark glossy green with paler undersides

Colours are authored in sRGB and written raw (Non-Color) so Unity imports them as sRGB albedo.
Also writes a tileable bark albedo + OpenGL normal map shared by every species.
"""
import math
import os
import random

import bpy
import numpy as np

CELL = 1024  # final pixels per atlas cell
SS = 2       # supersampling


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


class Canvas:
    """Premultiplied RGBA canvas, y up (row 0 = bottom, matching UV v)."""

    def __init__(self, w, h, bg=None):
        self.w, self.h = w, h
        self.c = np.zeros((h, w, 3), np.float32)
        self.a = np.zeros((h, w), np.float32)
        if bg is not None:
            self.c[:] = bg
            self.a[:] = 1

    def _box(self, x0, y0, x1, y1):
        ix0, iy0 = max(0, int(math.floor(x0))), max(0, int(math.floor(y0)))
        ix1, iy1 = min(self.w, int(math.ceil(x1)) + 1), min(self.h, int(math.ceil(y1)) + 1)
        if ix1 <= ix0 or iy1 <= iy0:
            return None
        Y, X = np.mgrid[iy0:iy1, ix0:ix1].astype(np.float32)
        return ix0, iy0, X + 0.5, Y + 0.5

    def _over(self, ix0, iy0, cov, rgb):
        h, w = cov.shape
        sl = (slice(iy0, iy0 + h), slice(ix0, ix0 + w))
        a = cov[..., None]
        self.c[sl] = self.c[sl] * (1 - a) + rgb * a
        self.a[sl] = self.a[sl] * (1 - cov) + cov

    def disc(self, cx, cy, r, rgb, shade=0.35, alpha=1.0):
        """Scale leaf / bead: a disc lit from above (lighter upper half, darker rim)."""
        b = self._box(cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1)
        if b is None:
            return
        ix0, iy0, X, Y = b
        dx, dy = X - cx, Y - cy
        d = np.sqrt(dx * dx + dy * dy)
        cov = np.clip(r - d + 0.5, 0, 1) * alpha
        if not cov.any():
            return
        k = 1 + shade * (dy / r) - 0.25 * shade * (d / r) ** 2
        rgb = np.asarray(rgb, np.float32)[None, None, :] * k[..., None]
        self._over(ix0, iy0, cov, rgb)

    def ellipse(self, cx, cy, a, b_, ang, rgb, rib=None, shade=0.3, alpha=1.0, point=0.0):
        """Leaf: ellipse along angle `ang` (semi-axes a along, b_ across), optional midrib colour, pointed tip."""
        R = a + 1.5
        bx = self._box(cx - R, cy - R, cx + R, cy + R)
        if bx is None:
            return
        ix0, iy0, X, Y = bx
        ca, sa = math.cos(ang), math.sin(ang)
        lx = (X - cx) * ca + (Y - cy) * sa
        ly = -(X - cx) * sa + (Y - cy) * ca
        # narrow toward the tip (lx > 0) for a pointed leaf
        bw = b_ * (1 - point * np.clip(lx / a, 0, 1) ** 2)
        d = np.sqrt((lx / a) ** 2 + (ly / np.maximum(bw, 1e-3)) ** 2)
        cov = np.clip((1 - d) * b_ + 0.5, 0, 1) * alpha
        if not cov.any():
            return
        col = np.asarray(rgb, np.float32)[None, None, :]
        k = 1 + shade * (ly / b_) * 0.6 - 0.35 * shade * d ** 2
        col = col * k[..., None]
        if rib is not None:
            rw = np.clip(1.2 - np.abs(ly) * 1.2, 0, 1) * np.clip(1 - lx / a, 0, 1) * (lx > -a * 0.9)
            col = col * (1 - rw[..., None] * 0.6) + np.asarray(rib, np.float32) * rw[..., None] * 0.6
        self._over(ix0, iy0, cov, col)

    def line(self, x0, y0, x1, y1, w0, w1, rgb0, rgb1=None, alpha=1.0):
        """Tapered capsule from (x0, y0) width w0 to (x1, y1) width w1 with a colour ramp."""
        rgb1 = rgb0 if rgb1 is None else rgb1
        m = max(w0, w1) + 1.5
        b = self._box(min(x0, x1) - m, min(y0, y1) - m, max(x0, x1) + m, max(y0, y1) + m)
        if b is None:
            return
        ix0, iy0, X, Y = b
        vx, vy = x1 - x0, y1 - y0
        L2 = max(vx * vx + vy * vy, 1e-6)
        t = np.clip(((X - x0) * vx + (Y - y0) * vy) / L2, 0, 1)
        px, py = x0 + vx * t - X, y0 + vy * t - Y
        d = np.sqrt(px * px + py * py)
        w = (w0 + (w1 - w0) * t) * 0.5
        cov = np.clip(w - d + 0.5, 0, 1) * alpha
        if not cov.any():
            return
        c0, c1 = np.asarray(rgb0, np.float32), np.asarray(rgb1, np.float32)
        rgb = c0[None, None, :] * (1 - t[..., None]) + c1[None, None, :] * t[..., None]
        self._over(ix0, iy0, cov, rgb)

    def downsample(self):
        h, w = self.h // SS, self.w // SS
        c = self.c.reshape(h, SS, w, SS, 3).mean((1, 3))
        a = self.a.reshape(h, SS, w, SS).mean((1, 3))
        return c, a


def lerp3(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def jitter(rgb, rnd, amount=0.08):
    k = 1 + rnd.uniform(-amount, amount)
    h = rnd.uniform(-amount, amount) * 0.5
    return (rgb[0] * k * (1 + h), rgb[1] * k, rgb[2] * k * (1 - h))


# ================================================================ species painters (cell units -> canvas pixels)
class Cell:
    def __init__(self, cv, S):
        self.cv, self.S = cv, S

    def P(self, u, v):
        return u * self.S, v * self.S


def walk(rnd, x, y, ang, length, step, wander, contain=True):
    """Wandering polyline; with contain=True it steers back toward the cell centre near the edges (cards must not
    be cut off at the cell border)."""
    pts = [(x, y)]
    n = max(2, int(length / step))
    for _ in range(n):
        ang += rnd.uniform(-wander, wander)
        if contain:
            m = 0.1
            if x < m or x > 1 - m or y > 1 - m:
                to = math.atan2(0.5 - y, 0.5 - x)
                d = (to - ang + math.pi) % (2 * math.pi) - math.pi
                ang += d * 0.35
        x, y = x + math.cos(ang) * length / n, y + math.sin(ang) * length / n
        if contain:
            x, y = min(max(x, 0.04), 0.96), min(max(y, 0.02), 0.96)
        pts.append((x, y))
    return pts, ang


CYPRESS = dict(dark=(0.07, 0.12, 0.06), mid=(0.15, 0.24, 0.10), tip=(0.32, 0.40, 0.16), stem=(0.20, 0.15, 0.10),
               fill_bg=(0.04, 0.07, 0.035))
PINE = dict(dark=(0.07, 0.14, 0.08), mid=(0.16, 0.29, 0.13), tip=(0.36, 0.48, 0.20), stem=(0.30, 0.20, 0.12),
            fill_bg=(0.045, 0.08, 0.04))
OAK = dict(dark=(0.06, 0.10, 0.04), mid=(0.13, 0.21, 0.08), tip=(0.29, 0.36, 0.14), under=(0.33, 0.37, 0.21),
           stem=(0.26, 0.20, 0.15), fill_bg=(0.035, 0.06, 0.03))


def paint_cypress(c, rnd, fill=False):
    """Domed fan of sprays; every stem is wrapped in overlapping scale leaves (beads)."""
    pal = CYPRESS
    S = c.S
    sprays = []

    def spray(x, y, ang, L, depth, w):
        pts, _ = walk(rnd, x, y, ang, L, 0.02, 0.1, not fill)
        sprays.append((pts, depth, w))
        if depth < 2:
            n = len(pts)
            side = rnd.choice((-1, 1))
            for k in range(1, n - 1, 2):
                if rnd.random() < (0.9 if depth == 0 else 0.6):
                    side = -side
                    (ax, ay), (bx, by) = pts[k], pts[k + 1]
                    a = math.atan2(by - ay, bx - ax) + side * rnd.uniform(0.45, 0.95)
                    rem = (n - k) / n
                    spray(ax, ay, a, L * rnd.uniform(0.3, 0.5) * (0.45 + 0.55 * rem), depth + 1, w * 0.65)

    if fill:
        for _ in range(42):
            spray(rnd.uniform(-0.1, 1.1), rnd.uniform(-0.1, 1.1), rnd.uniform(0, 2 * math.pi), rnd.uniform(0.25, 0.45),
                  0, 1.0)
    else:
        n = rnd.randint(9, 12)
        for i in range(n):
            th = math.radians(14 + 152 * (i + rnd.uniform(0.2, 0.8)) / n)
            L = rnd.uniform(0.5, 0.72) * (0.62 + 0.38 * math.sin(th))
            spray(0.5 + rnd.uniform(-0.04, 0.04), 0.05, th, L, 0, 1.0)
    # back to front: main stems first, then side cords, tips last
    sprays.sort(key=lambda s: s[1])
    beads = []
    for pts, depth, w in sprays:
        for i in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            c.cv.line(x0 * S, y0 * S, x1 * S, y1 * S, 0.008 * S * w, 0.005 * S * w, pal["stem"])
        n = len(pts)
        for i in range(n - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            t = i / max(1, n - 2)
            for k in range(3):
                f = k / 3
                x, y = x0 + (x1 - x0) * f, y0 + (y1 - y0) * f
                r = (0.016 - 0.0045 * depth) * (1 - 0.4 * t) * rnd.uniform(0.8, 1.15)
                jx, jy = rnd.uniform(-1, 1) * r * 0.6, rnd.uniform(-1, 1) * r * 0.6
                beads.append((depth + t * 0.9 + rnd.uniform(0, 0.3), x + jx, y + jy, r, depth, t))
    beads.sort(key=lambda b: b[0])
    for _, x, y, r, depth, t in beads:
        g = min(1.0, 0.25 * depth + 0.6 * t + rnd.uniform(-0.15, 0.15))
        base = lerp3(pal["dark"], pal["mid"], min(1, g * 1.6)) if g < 0.6 else lerp3(pal["mid"], pal["tip"], (g - 0.6) / 0.4)
        c.cv.disc(x * S, y * S, r * S, jitter(base, rnd, 0.1), shade=0.45)


def paint_pine(c, rnd, fill=False):
    """Twigs with bottle-brush needle tufts, densest toward the twig tips."""
    pal = PINE
    S = c.S
    twigs = []
    if fill:
        for _ in range(26):
            twigs.append((rnd.uniform(-0.1, 1.1), rnd.uniform(-0.1, 1.1), rnd.uniform(0, 2 * math.pi), rnd.uniform(0.3, 0.5)))
    else:
        n = rnd.randint(4, 6)
        for i in range(n):
            th = math.radians(30 + 120 * (i + rnd.uniform(0.25, 0.75)) / n)
            twigs.append((0.5 + rnd.uniform(-0.05, 0.05), 0.04, th, rnd.uniform(0.5, 0.68) * (0.7 + 0.3 * math.sin(th))))
    needles = []
    for (x, y, ang, L) in twigs:
        pts, end = walk(rnd, x, y, ang, L, 0.03, 0.08, not fill)
        for i in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            t = i / (len(pts) - 1)
            c.cv.line(x0 * S, y0 * S, x1 * S, y1 * S, 0.008 * S * (1 - 0.5 * t), 0.008 * S * (1 - 0.5 * t - 0.05),
                      pal["stem"], lerp3(pal["stem"], (0.4, 0.3, 0.18), t))
        n = len(pts)
        for i in range(n - 1):
            t = i / (n - 1)
            if t < 0.18:
                continue
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            a = math.atan2(y1 - y0, x1 - x0)
            for k in range(rnd.randint(9, 13)):
                side = rnd.choice((-1, 1))
                na = a + side * rnd.uniform(0.2, 1.1)
                nl = rnd.uniform(0.14, 0.24) * (0.7 + 0.3 * t)
                needles.append((rnd.random(), x0, y0, na, nl, t))
        # terminal tuft: needles all round the bud
        (xe, ye) = pts[-1]
        for k in range(36):
            na = end + rnd.uniform(-1.7, 1.7)
            needles.append((0.5 + rnd.random(), xe, ye, na, rnd.uniform(0.1, 0.2), 1.0))
    needles.sort(key=lambda n_: n_[0])
    for _, x0, y0, na, nl, t in needles:
        x1, y1 = x0 + math.cos(na) * nl, y0 + math.sin(na) * nl
        # gentle droop: bend the needle via a mid point
        mx, my = x0 + math.cos(na) * nl * 0.55, y0 + math.sin(na) * nl * 0.55 - nl * 0.04
        g = min(1.0, 0.3 + 0.5 * t + rnd.uniform(-0.2, 0.2))
        c0 = jitter(lerp3(pal["dark"], pal["mid"], g), rnd, 0.1)
        c1 = jitter(lerp3(pal["mid"], pal["tip"], g), rnd, 0.1)
        w = 0.0042 * S
        c.cv.line(x0 * S, y0 * S, mx * S, my * S, w, w * 0.9, c0, lerp3(c0, c1, 0.5))
        c.cv.line(mx * S, my * S, x1 * S, y1 * S, w * 0.9, w * 0.35, lerp3(c0, c1, 0.5), c1)


def paint_oak(c, rnd, fill=False):
    """Zig-zag twigs carrying small cupped leaves; some show their paler undersides."""
    pal = OAK
    S = c.S
    leaves = []

    def twig(x, y, ang, L, depth):
        pts, _ = walk(rnd, x, y, ang, L, 0.04, 0.22, not fill)
        for i in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            w = (0.011 - 0.003 * depth) * S
            c.cv.line(x0 * S, y0 * S, x1 * S, y1 * S, w, w * 0.8, pal["stem"])
        n = len(pts)
        for i in range(1, n):
            (x0, y0) = pts[i]
            a = math.atan2(pts[i][1] - pts[i - 1][1], pts[i][0] - pts[i - 1][0])
            for side in (-1, 1):
                if rnd.random() < 0.85:
                    la = a + side * rnd.uniform(0.5, 1.2)
                    size = rnd.uniform(0.032, 0.05)
                    leaves.append((rnd.random() + depth * 0.3, x0, y0, la, size))
            if depth < 2 and rnd.random() < 0.4:
                twig(x0, y0, a + rnd.choice((-1, 1)) * rnd.uniform(0.5, 0.9), L * 0.5, depth + 1)
        leaves.append((2.0, pts[-1][0], pts[-1][1], math.atan2(pts[-1][1] - pts[-2][1], pts[-1][0] - pts[-2][0]),
                       0.045))

    if fill:
        for _ in range(30):
            twig(rnd.uniform(-0.1, 1.1), rnd.uniform(-0.1, 1.1), rnd.uniform(0, 2 * math.pi), rnd.uniform(0.3, 0.5), 1)
    else:
        n = rnd.randint(4, 6)
        for i in range(n):
            th = math.radians(30 + 120 * (i + rnd.uniform(0.25, 0.75)) / n)
            twig(0.5 + rnd.uniform(-0.04, 0.04), 0.05, th, rnd.uniform(0.38, 0.5), 0)
    leaves.sort(key=lambda l_: l_[0])
    for _, x, y, la, size in leaves:
        cx, cy = x + math.cos(la) * size * 0.9, y + math.sin(la) * size * 0.9
        under = rnd.random() < 0.22
        g = rnd.uniform(0.0, 1.0)
        base = pal["under"] if under else lerp3(pal["dark"], pal["tip"], g ** 1.4)
        rib = lerp3(base, (0.45, 0.5, 0.3), 0.5)
        c.cv.ellipse(cx * S, cy * S, size * S, size * S * rnd.uniform(0.5, 0.62), la, jitter(base, rnd, 0.08), rib=rib,
                     shade=0.45, point=0.35)


PAINTERS = {"cypress": (paint_cypress, CYPRESS), "pine": (paint_pine, PINE), "oak": (paint_oak, OAK)}


# ================================================================ atlas assembly
def dilate(c, a, iters=24):
    """Bleeds colour into transparent texels so mip maps and bilinear filtering never pick up black fringes."""
    c = c.copy()
    filled = a > 1e-3
    out = np.where(filled[..., None], c / np.maximum(a, 1e-3)[..., None], 0)
    for _ in range(iters):
        if filled.all():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros(filled.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1)):
            f = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc += np.roll(np.roll(out, dy, 0), dx, 1) * f[..., None]
            cnt += f
        grow = (~filled) & (cnt > 0)
        out[grow] = acc[grow] / cnt[grow][:, None]
        filled = filled | grow
    out[~filled] = out[filled].mean(0) if filled.any() else 0
    return out


def save_png(path, rgba):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def leaf_atlas(species, seed, path):
    painter, pal = PAINTERS[species]
    atlas = np.zeros((2 * CELL, 2 * CELL, 4), np.float32)
    coverage = []
    for k in range(4):
        rnd = random.Random(seed * 101 + k)
        fill = k == 3
        cv = Canvas(CELL * SS, CELL * SS, bg=pal["fill_bg"] if fill else None)
        painter(Cell(cv, CELL * SS), rnd, fill)
        c, a = cv.downsample()
        if fill:
            a[:] = 1
        rgb = dilate(c, a)
        col, row = k % 2, k // 2
        atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL, :3] = rgb
        atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL, 3] = a
        coverage.append(float((a > 0.5).mean()))
    save_png(path, atlas)
    print("[tex] %s  coverage %s" % (os.path.basename(path), ", ".join("%.2f" % x for x in coverage)))
    return coverage


def _periodic(n, m, slope, seed, sx=1.0, sy=1.0, cutoff=1.5):
    rng = np.random.default_rng(seed)
    kx = np.fft.fftfreq(m) * m
    ky = np.fft.fftfreq(n) * n
    KX, KY = np.meshgrid(kx, ky)
    k = np.hypot(KX * sx, KY * sy)
    amp = np.where(k > cutoff, k ** -slope, 0.0)
    spec = amp * (rng.normal(size=(n, m)) + 1j * rng.normal(size=(n, m)))
    f = np.real(np.fft.ifft2(spec))
    return (f - f.mean()) / f.std()


def bark_textures(albedo_path, normal_path, n=1024):
    """Tileable fibrous bark (1 tile ~ 1.2 m around x 2.4 m along the stem): long vertical ridges and furrows broken
    into plates, grey-brown with lichen-pale ridge tops."""
    ridges = _periodic(n, n, 1.25, 5, sx=1.0, sy=9.0, cutoff=7.0)
    plates = _periodic(n, n, 2.0, 9, sx=1.0, sy=2.5, cutoff=2.0)
    fine = _periodic(n, n, 1.2, 13, sx=1.0, sy=4.0, cutoff=30.0)
    h = np.tanh(ridges * 1.1) * 0.6 + 0.3 * plates + 0.12 * fine
    h = (h - h.min()) / (h.max() - h.min())
    furrow = smooth(0.15, 0.5, h)
    dark = np.array([0.11, 0.09, 0.075])
    mid = np.array([0.33, 0.28, 0.23])
    pale = np.array([0.52, 0.49, 0.44])
    t = furrow[..., None]
    rgb = dark * (1 - t) + mid * t
    lichen = smooth(0.72, 0.95, h + 0.15 * _periodic(n, n, 1.8, 17, cutoff=4.0))[..., None]
    rgb = rgb * (1 - lichen * 0.7) + pale * lichen * 0.7
    rgb *= (0.92 + 0.08 * fine)[..., None]
    a = np.ones((n, n, 1))
    save_png(albedo_path, np.concatenate([rgb, a], -1))
    s = 6.0
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * s
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * s
    nrm = np.stack([-dx, -dy, np.ones_like(h)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    save_png(normal_path, np.concatenate([nrm * 0.5 + 0.5, a], -1))
    print("[tex] %s, %s" % (os.path.basename(albedo_path), os.path.basename(normal_path)))


def make_all(tex_dir):
    os.makedirs(tex_dir, exist_ok=True)
    cov = {}
    for species, seed in (("cypress", 3), ("pine", 5), ("oak", 7)):
        cov[species] = leaf_atlas(species, seed, os.path.join(tex_dir, "Leaf_%s.png" % species.capitalize()))
    bark_textures(os.path.join(tex_dir, "Bark_Tree.png"), os.path.join(tex_dir, "Bark_Tree_Normal.png"))
    return cov
