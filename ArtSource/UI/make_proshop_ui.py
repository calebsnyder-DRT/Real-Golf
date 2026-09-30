"""Generates the Pro Shop UI chrome sprites (chamfered panels, outlines, focus glow, parchment, glyphs).

Run: /tmp/pyenv/bin/python ArtSource/UI/make_proshop_ui.py   (needs Pillow + numpy)
Output: Assets/_Game/Art/ProShop/UI/Sprites/*.png. The Unity importer (ProShopImportRules) slices them with the
border listed in BORDERS below, so keep the two in sync.

Everything is white on transparent unless noted, so Image.color tints it to a spec token at runtime.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Game", "Art", "ProShop", "UI", "Sprites")
SS = 4  # supersampling for anti-aliased edges

# name -> 9-slice border in px (left, bottom, right, top); mirrored in Unity import rules
BORDERS = {
    "ps_chamfer_fill": 22,
    "ps_chamfer_line": 22,
    "ps_chamfer_line_thin": 22,
    "ps_chamfer_glow": 40,
    "ps_paper": 40,
    "ps_soft_fill": 16,
}


def chamfer_poly(w, h, c, inset=0.0):
    i = inset
    return [(c + i, i), (w - c - i, i), (w - i, c + i), (w - i, h - c - i), (w - c - i, h - i), (c + i, h - i), (i, h - c - i), (i, c + i)]


def supersampled(w, h, draw_fn):
    big = Image.new("L", (w * SS, h * SS), 0)
    draw_fn(ImageDraw.Draw(big), SS)
    return big.resize((w, h), Image.LANCZOS)


def white_with_alpha(alpha):
    img = Image.new("RGBA", alpha.size, (255, 255, 255, 0))
    img.putalpha(alpha)
    return img


def chamfer_fill(size=96, cut=14):
    a = supersampled(size, size, lambda d, s: d.polygon([(x * s, y * s) for x, y in chamfer_poly(size, size, cut)], fill=255))
    return white_with_alpha(a)


def chamfer_line(size=96, cut=14, width=2.0):
    def draw(d, s):
        d.polygon([(x * s, y * s) for x, y in chamfer_poly(size, size, cut)], fill=255)
        d.polygon([(x * s, y * s) for x, y in chamfer_poly(size, size, cut - width * 0.41, inset=width)], fill=0)
    return white_with_alpha(supersampled(size, size, draw))


def chamfer_glow(size=160, cut=14, pad=26):
    """Soft cyan-able halo around a chamfered frame; drawn outside the element by `pad` px."""
    inner = size - 2 * pad
    poly = [(x + pad, y + pad) for x, y in chamfer_poly(inner, inner, cut)]
    base = Image.new("L", (size, size), 0)
    ImageDraw.Draw(base).polygon(poly, outline=255, width=5)
    halo = np.clip(np.asarray(base.filter(ImageFilter.GaussianBlur(9)), np.float32) * 2.2, 0, 255)
    crisp = np.asarray(frame_line(size, poly, 2.4), np.float32)
    return white_with_alpha(Image.fromarray(np.maximum(halo, crisp).astype(np.uint8)))


def frame_line(size, poly, width):
    def draw(d, s):
        d.polygon([(x * s, y * s) for x, y in poly], fill=255)
        cx, cy = size / 2, size / 2
        shrunk = []
        for x, y in poly:
            vx, vy = x - cx, y - cy
            ln = math.hypot(vx, vy)
            k = (ln - width) / ln
            shrunk.append((cx + vx * k, cy + vy * k))
        d.polygon([(x * s, y * s) for x, y in shrunk], fill=0)
    return supersampled(size, size, draw)


def soft_fill(size=48, radius=10):
    a = supersampled(size, size, lambda d, s: d.rounded_rectangle((0, 0, size * s - 1, size * s - 1), radius * s, fill=255))
    return white_with_alpha(a)


def paper(size=512, cut=16):
    """Cream parchment: warm base, faint fibres and mottling, slightly darker rim. Colour baked in (not tinted)."""
    rng = np.random.default_rng(7)
    base = np.array([242, 237, 223], np.float32)  # #F2EDDF
    h = w = size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    mott = np.zeros((h, w), np.float32)
    for scale, amp in ((96, 5.0), (32, 2.6), (8, 1.4)):
        n = rng.normal(0, 1, (h // scale + 2, w // scale + 2)).astype(np.float32)
        img = Image.fromarray(((n - n.min()) / (n.max() - n.min()) * 255).astype(np.uint8)).resize((w + scale, h + scale), Image.BICUBIC)
        mott += (np.asarray(img, np.float32)[:h, :w] / 255 - 0.5) * amp
    fib = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(fib)
    for _ in range(420):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        ang = rng.uniform(0, math.pi)
        ln = rng.uniform(6, 22)
        d.line((x, y, x + math.cos(ang) * ln, y + math.sin(ang) * ln), fill=int(rng.uniform(40, 110)), width=1)
    fib = np.asarray(fib.filter(ImageFilter.GaussianBlur(0.6)), np.float32) / 255
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    rim = np.clip(1 - edge / 34.0, 0, 1) ** 2 * 10
    rgb = base[None, None, :] + mott[..., None] - fib[..., None] * 9 - rim[..., None] * np.array([1.0, 1.1, 1.4])
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    img = Image.fromarray(rgb, "RGB").convert("RGBA")
    mask = supersampled(w, h, lambda dd, s: dd.polygon([(x * s, y * s) for x, y in chamfer_poly(w, h, cut)], fill=255))
    img.putalpha(mask)
    return img


def chevron(size=64, width=7, left=True):
    def draw(d, s):
        pts = [(size * 0.62, size * 0.18), (size * 0.30, size * 0.5), (size * 0.62, size * 0.82)]
        if not left:
            pts = [(size - x, y) for x, y in pts]
        d.line([(x * s, y * s) for x, y in pts], fill=255, width=int(width * s), joint="curve")
        for x, y in pts:
            r = width * s / 2
            d.ellipse((x * s - r, y * s - r, x * s + r, y * s + r), fill=255)
    return white_with_alpha(supersampled(size, size, draw))


def rotate_arc(size=128, left=True):
    """Curved arrow drawn on the rug either side of the golfer (turntable hint)."""
    def draw(d, s):
        box = (size * 0.18 * s, size * 0.08 * s, size * 1.6 * s, size * 0.92 * s)
        d.arc(box, 150, 210, fill=255, width=int(7 * s))
        ang = math.radians(150)
        cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
        rx, ry = (box[2] - box[0]) / 2, (box[3] - box[1]) / 2
        tip = (cx + rx * math.cos(ang), cy - ry * math.sin(ang) * -1)
        tx, ty = tip
        d.polygon([(tx - 13 * s, ty + 6 * s), (tx + 12 * s, ty + 2 * s), (tx - 2 * s, ty - 18 * s)], fill=255)
    img = white_with_alpha(supersampled(size, size, draw))
    return img if left else img.transpose(Image.FLIP_LEFT_RIGHT)


def check(size=64):
    def draw(d, s):
        pts = [(size * 0.2, size * 0.54), (size * 0.42, size * 0.76), (size * 0.82, size * 0.28)]
        d.line([(x * s, y * s) for x, y in pts], fill=255, width=int(8 * s), joint="curve")
    return white_with_alpha(supersampled(size, size, draw))


def dot(size=32):
    return white_with_alpha(supersampled(size, size, lambda d, s: d.ellipse((2 * s, 2 * s, (size - 2) * s, (size - 2) * s), fill=255)))


def tri(size=32, down=False):
    def draw(d, s):
        pts = [(size * 0.2, size * 0.7), (size * 0.8, size * 0.7), (size * 0.5, size * 0.25)]
        if down:
            pts = [(x, size - y) for x, y in pts]
        d.polygon([(x * s, y * s) for x, y in pts], fill=255)
    return white_with_alpha(supersampled(size, size, draw))


def vignette(w=512, h=256):
    """Bottom-up darkening behind the player tabs so the golfer's feet and rug stay readable."""
    yy = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    a = np.clip((yy - 0.1) / 0.9, 0, 1) ** 1.6 * 255
    a = np.repeat(a, w, axis=1).astype(np.uint8)
    return white_with_alpha(Image.fromarray(a))


def header_wash(w=512, h=128):
    """Soft top-left wash behind the wordmark and title so the ink lettering reads over dark shelving."""
    xx = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    yy = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    def smooth(t):
        t = np.clip(t, 0, 1)
        return t * t * (3 - 2 * t)
    a = (1 - smooth((xx - 0.45) / 0.55)) * (1 - smooth((yy - 0.45) / 0.55)) * 255
    return white_with_alpha(Image.fromarray(a.astype(np.uint8)))


def main():
    os.makedirs(OUT, exist_ok=True)
    sprites = {
        "ps_chamfer_fill": chamfer_fill(),
        "ps_chamfer_line": chamfer_line(width=2.0),
        "ps_chamfer_line_thin": chamfer_line(width=1.2),
        "ps_chamfer_glow": chamfer_glow(),
        "ps_paper": paper(),
        "ps_soft_fill": soft_fill(),
        "ps_chevron_left": chevron(left=True),
        "ps_chevron_right": chevron(left=False),
        "ps_rotate_left": rotate_arc(left=True),
        "ps_rotate_right": rotate_arc(left=False),
        "ps_check": check(),
        "ps_dot": dot(),
        "ps_tri_up": tri(),
        "ps_tri_down": tri(down=True),
        "ps_vignette": vignette(),
        "ps_header_wash": header_wash(),
    }
    for name, img in sprites.items():
        img.save(os.path.join(OUT, name + ".png"))
        print(name, img.size)


if __name__ == "__main__":
    main()
