"""Generates the in-round HUD sprites: glass panels, card frames, roster medallions, lane keys, power meter art,
lie badges, icons, the Puttshack Pro emblem and wordmark, scorecard marks and keyboard keycaps.

Run: /tmp/pyenv/bin/python ArtSource/UI/HUD/make_hud_ui.py   (needs Pillow + numpy)
Output: Assets/_Game/Art/HUD/Sprites/*.png plus a review contact sheet in ArtSource/Review/HUD/hud_sprites_sheet.png.
The Unity importer (HudImportRules) slices them with BORDERS below, so keep the two in sync.

Conventions match ArtSource/UI/make_proshop_ui.py: chamfered charcoal glass, cream rims, cyan accents. "white"
sprites are white on transparent so Image.color tints them; "baked" sprites carry their final colours. Everything is
drawn supersampled and downsampled with Lanczos, and sized at roughly 1-2x its on-screen size because UI sprites
import without mipmaps.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.environ.get("HUD_SPRITE_OUT", os.path.join(ROOT, "Assets", "_Game", "Art", "HUD", "Sprites"))
REVIEW = os.path.join(ROOT, "ArtSource", "Review", "HUD")
FONTS = os.path.join(ROOT, "Assets", "_Game", "Art", "ProShop", "UI", "Fonts")
SS = 4

CREAM = (242, 237, 223)
INK = (25, 32, 32)
CHAR = (20, 26, 26)
CYAN = (0, 203, 231)
CYAN_DEEP = (0, 140, 161)
BRASS = (184, 161, 123)
LEAF = (107, 165, 46)
AMBER = (217, 130, 43)

# name -> 9-slice border in px (left, bottom, right, top); mirrored in HudImportRules.cs
BORDERS = {
    "hud_glass": (24, 24, 24, 24),
    "hud_card": (44, 44, 44, 44),
    "hud_card_accent": (44, 44, 44, 44),
    "hud_plate": (14, 14, 14, 14),
    "hud_plate_line": (14, 14, 14, 14),
    "hud_pill": (20, 20, 20, 20),
    "hud_pill_line": (20, 20, 20, 20),
    "hud_lane_key": (14, 16, 14, 14),
    "hud_lane_frame": (14, 16, 14, 14),
    "hud_lane_glow": (38, 38, 38, 38),
    "key_blank": (22, 26, 22, 22),
}


# ---------------------------------------------------------------- helpers

def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name + ".ttf"), size)


def mask(w, h, draw_fn):
    """Draws white shapes on a supersampled L canvas (draw_fn(draw, s)) and returns a float [0,1] array at w x h."""
    big = Image.new("L", (w * SS, h * SS), 0)
    draw_fn(ImageDraw.Draw(big), SS)
    return np.asarray(big.resize((w, h), Image.LANCZOS), np.float32) / 255.0


def to_image(rgb, a):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    a = (np.clip(a, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(np.dstack([rgb, a]), "RGBA")


def white(a):
    h, w = a.shape
    return to_image(np.full((h, w, 3), 255, np.float32), a)


def solid(h, w, color):
    return np.ones((h, w, 3), np.float32) * np.array(color, np.float32)


def vgrad(h, w, top, bottom, power=1.0):
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None, None] ** power
    return np.broadcast_to(np.array(top, np.float32) * (1 - t) + np.array(bottom, np.float32) * t, (h, w, 3)).copy()


def over(rgb, a, src_rgb, src_a):
    """Porter-Duff source-over on straight-alpha float arrays."""
    src_a = src_a[..., None] if src_a.ndim == 2 else src_a
    a3 = a[..., None]
    out_a = src_a + a3 * (1 - src_a)
    safe = np.where(out_a > 1e-6, out_a, 1)
    out_rgb = (src_rgb * src_a + rgb * a3 * (1 - src_a)) / safe
    return out_rgb, out_a[..., 0]


def chamfer_poly(x0, y0, x1, y1, c):
    return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c), (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]


def scaled(pts, s):
    return [(x * s, y * s) for x, y in pts]


def outline(w, h, outer_fn, inner_fn):
    """Mask of outer shape minus inner shape (both callables taking (draw, s, fill))."""
    def draw(d, s):
        outer_fn(d, s, 255)
        inner_fn(d, s, 0)
    return mask(w, h, draw)


def blur(a, radius):
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(radius)), np.float32) / 255.0


def downsample(img, size):
    return img.resize(size, Image.LANCZOS)


def text_mask(text, fnt, tracking=0.0, s=1):
    """Tight L mask (float) of a text run drawn at scale s with extra tracking (px at s=1)."""
    widths = []
    for ch in text:
        widths.append(fnt.getlength(ch))
    total = sum(widths) + tracking * s * (len(text) - 1)
    asc, desc = fnt.getmetrics()
    img = Image.new("L", (int(math.ceil(total)) + 8, asc + desc + 8), 0)
    d = ImageDraw.Draw(img)
    x = 4.0
    for ch, wch in zip(text, widths):
        d.text((x, 4), ch, font=fnt, fill=255)
        x += wch + tracking * s
    bbox = img.getbbox()
    return img.crop(bbox) if bbox else img


# ---------------------------------------------------------------- panels

def glass(size=128, cut=14, top=(36, 45, 45), bottom=(16, 21, 21), alpha=0.88, rim=0.30):
    """Charcoal translucent glass with a soft top sheen and a cream rim (baked colour)."""
    w = h = size
    fill = mask(w, h, lambda d, s: d.polygon(scaled(chamfer_poly(0, 0, w, h, cut), s), fill=255))
    rim_m = outline(w, h,
                    lambda d, s, f: d.polygon(scaled(chamfer_poly(0, 0, w, h, cut), s), fill=f),
                    lambda d, s, f: d.polygon(scaled(chamfer_poly(1.5, 1.5, w - 1.5, h - 1.5, cut - 0.6), s), fill=f))
    rgb = vgrad(h, w, top, bottom, 0.8)
    a = fill * alpha
    sheen = mask(w, h, lambda d, s: d.polygon(scaled(chamfer_poly(2, 2, w - 2, h * 0.5, cut - 1), s), fill=255))
    sheen *= np.linspace(0.07, 0.0, h, dtype=np.float32)[:, None] * 1.0
    rgb, a = over(rgb, a, solid(h, w, CREAM), sheen * fill)
    rgb, a = over(rgb, a, solid(h, w, CREAM), rim_m * rim)
    return to_image(rgb, a)


def card(size=192, cut=20):
    w = h = size
    img = glass(size, cut, top=(34, 43, 43), bottom=(15, 20, 20), alpha=0.97, rim=0.34)
    rgb = np.asarray(img, np.float32)[..., :3]
    a = np.asarray(img, np.float32)[..., 3] / 255.0
    inner = outline(w, h,
                    lambda d, s, f: d.polygon(scaled(chamfer_poly(7, 7, w - 7, h - 7, cut - 4), s), fill=f),
                    lambda d, s, f: d.polygon(scaled(chamfer_poly(8, 8, w - 8, h - 8, cut - 4.4), s), fill=f))
    rgb, a = over(rgb, a, solid(h, w, CREAM), inner * 0.10)
    return to_image(rgb, a)


def card_accent(size=192, cut=20, band=4.0):
    """Top edge band of the card (white, tinted per card: cyan, amber, identity)."""
    w = h = size
    shape = mask(w, h, lambda d, s: d.polygon(scaled(chamfer_poly(0, 0, w, h, cut), s), fill=255))
    bandm = mask(w, h, lambda d, s: d.rectangle((0, 0, w * s, band * s), fill=255))
    return white(shape * bandm)


def plate(size=48, cut=8):
    return white(mask(size, size, lambda d, s: d.polygon(scaled(chamfer_poly(0, 0, size, size, cut), s), fill=255)))


def plate_line(size=48, cut=8, width=1.6):
    return white(outline(size, size,
                         lambda d, s, f: d.polygon(scaled(chamfer_poly(0, 0, size, size, cut), s), fill=f),
                         lambda d, s, f: d.polygon(scaled(chamfer_poly(width, width, size - width, size - width, cut - width * 0.41), s), fill=f)))


def pill(w=80, h=40):
    return white(mask(w, h, lambda d, s: d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), h * s / 2, fill=255)))


def pill_line(w=80, h=40, width=1.8):
    return white(outline(w, h,
                         lambda d, s, f: d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), h * s / 2, fill=f),
                         lambda d, s, f: d.rounded_rectangle((width * s, width * s, (w - width) * s, (h - width) * s), (h / 2 - width) * s, fill=f)))


def wash_corner(w=640, h=300):
    """Soft radial darkening from the top-left corner (white alpha; tinted ink). Flip for the right corner."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt((xx / w) ** 2 + (yy / h) ** 2)
    t = np.clip(1 - r, 0, 1)
    return white(t * t * (3 - 2 * t))


def wash_bottom(w=32, h=256):
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    return white(np.repeat(t ** 1.7, w, axis=1))


def rule_fade(w=256, h=8, both=True, width=2.0):
    x = np.linspace(0, 1, w, dtype=np.float32)
    if both:
        fade = np.clip(np.minimum(x, 1 - x) / 0.28, 0, 1)
    else:
        fade = np.clip(x / 0.85, 0, 1)
    fade = fade * fade * (3 - 2 * fade)
    y = np.arange(h, dtype=np.float32) + 0.5
    line = np.clip(width / 2 + 0.5 - np.abs(y - h / 2), 0, 1)
    return white(line[:, None] * fade[None, :])


def divider_v(w=8, h=128):
    return rule_fade(h, w, True, 2.0).transpose(Image.ROTATE_90)


# ---------------------------------------------------------------- roster medallion

def disc(size=128):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    c = size / 2
    r = np.sqrt((xx - c) ** 2 + (yy - c * 0.8) ** 2) / c
    rgb = (np.array((46, 57, 57), np.float32) * (1 - np.clip(r, 0, 1))[..., None] + np.array((16, 21, 21), np.float32) * np.clip(r, 0, 1)[..., None])
    a = mask(size, size, lambda d, s: d.ellipse((1 * s, 1 * s, (size - 1) * s, (size - 1) * s), fill=255))
    return to_image(rgb, a * 0.96)


def ring(size=128, width=9.0):
    return white(outline(size, size,
                         lambda d, s, f: d.ellipse((1 * s, 1 * s, (size - 1) * s, (size - 1) * s), fill=f),
                         lambda d, s, f: d.ellipse(((1 + width) * s, (1 + width) * s, (size - 1 - width) * s, (size - 1 - width) * s), fill=f)))


def ring_glow(size=192, inner=128):
    pad = (size - inner) / 2
    base = outline(size, size,
                   lambda d, s, f: d.ellipse((pad * s, pad * s, (size - pad) * s, (size - pad) * s), fill=f),
                   lambda d, s, f: d.ellipse(((pad + 8) * s, (pad + 8) * s, (size - pad - 8) * s, (size - pad - 8) * s), fill=f))
    halo = np.clip(blur(base, 11) * 2.2, 0, 1)
    return white(halo)


def badge_check(size=48):
    disc_m = mask(size, size, lambda d, s: d.ellipse((1 * s, 1 * s, (size - 1) * s, (size - 1) * s), fill=255))
    rim_m = outline(size, size,
                    lambda d, s, f: d.ellipse((1 * s, 1 * s, (size - 1) * s, (size - 1) * s), fill=f),
                    lambda d, s, f: d.ellipse((4 * s, 4 * s, (size - 4) * s, (size - 4) * s), fill=f))

    def chk(d, s):
        pts = [(size * 0.28, size * 0.53), (size * 0.44, size * 0.68), (size * 0.73, size * 0.36)]
        d.line(scaled(pts, s), fill=255, width=int(5 * s), joint="curve")
        for x, y in (pts[0], pts[-1]):
            r = 2.5 * s
            d.ellipse((x * s - r, y * s - r, x * s + r, y * s + r), fill=255)
    check_m = mask(size, size, chk)
    rgb, a = solid(size, size, LEAF), disc_m
    rgb, a = over(rgb, a, solid(size, size, CREAM), rim_m * disc_m)
    rgb, a = over(rgb, a, solid(size, size, (255, 255, 255)), check_m)
    return to_image(rgb, a)


# ---------------------------------------------------------------- icons (white)

def icon_flag(size=64):
    def draw(d, s):
        px = size * 0.34
        d.rounded_rectangle(((px - 2.2) * s, size * 0.10 * s, (px + 2.2) * s, size * 0.84 * s), 2 * s, fill=255)
        pts = [(px + 1, size * 0.10), (size * 0.86, size * 0.25), (px + 1, size * 0.42)]
        d.polygon(scaled(pts, s), fill=255)
        d.ellipse((size * 0.16 * s, size * 0.80 * s, size * 0.52 * s, size * 0.92 * s), fill=255)
    return white(mask(size, size, draw))


def icon_wind_arrow(size=96):
    def draw(d, s):
        c = size / 2
        pts = [(c, size * 0.06), (size * 0.84, size * 0.48), (c + size * 0.12, size * 0.40), (c + size * 0.12, size * 0.92),
               (c - size * 0.12, size * 0.92), (c - size * 0.12, size * 0.40), (size * 0.16, size * 0.48)]
        d.polygon(scaled(pts, s), fill=255)
    return white(mask(size, size, draw))


def wind_dial(size=160):
    """Baked compass dial: dark recessed disc, cream ring, 24 ticks with cyan cardinal marks."""
    c = size / 2
    base = mask(size, size, lambda d, s: d.ellipse((1 * s, 1 * s, (size - 1) * s, (size - 1) * s), fill=255))
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    r = np.sqrt((xx - c) ** 2 + (yy - c) ** 2) / c
    rgb = np.array((14, 19, 19), np.float32) * (1 - r[..., None] * 0.2) + np.array((40, 50, 50), np.float32) * (r[..., None] * 0.2)
    rgb = np.clip(rgb, 0, 255)
    a = base * 0.92
    rim_m = outline(size, size,
                    lambda d, s, f: d.ellipse((2 * s, 2 * s, (size - 2) * s, (size - 2) * s), fill=f),
                    lambda d, s, f: d.ellipse((5 * s, 5 * s, (size - 5) * s, (size - 5) * s), fill=f))
    rgb, a = over(rgb, a, solid(size, size, CREAM), rim_m * 0.55)

    def ticks(major):
        def draw(d, s):
            for i in range(24):
                is_major = i % 6 == 0
                if is_major != major:
                    continue
                ang = math.radians(i * 15)
                r0 = c - 10
                r1 = c - (24 if is_major else 16)
                x0, y0 = c + math.sin(ang) * r0, c - math.cos(ang) * r0
                x1, y1 = c + math.sin(ang) * r1, c - math.cos(ang) * r1
                d.line((x0 * s, y0 * s, x1 * s, y1 * s), fill=255, width=int((3.2 if is_major else 2.0) * s))
        return mask(size, size, draw)
    rgb, a = over(rgb, a, solid(size, size, CREAM), ticks(False) * 0.45)
    rgb, a = over(rgb, a, solid(size, size, CYAN), ticks(True))
    return to_image(rgb, a)


def icon_trophy(size=128):
    def draw(d, s):
        c = size / 2
        # cup bowl
        d.pieslice((size * 0.24 * s, size * -0.02 * s, size * 0.76 * s, size * 0.62 * s), 0, 180, fill=255)
        d.rectangle((size * 0.24 * s, size * 0.12 * s, size * 0.76 * s, size * 0.30 * s), fill=255)
        # handles
        for flip in (1, -1):
            x0 = c - flip * size * 0.26
            box = (min(x0, x0 - flip * size * 0.16) * s, size * 0.14 * s, max(x0, x0 - flip * size * 0.16) * s, size * 0.42 * s)
            d.arc(box, 0, 360, fill=255, width=int(size * 0.055 * s))
        # stem + base
        d.polygon(scaled([(c - size * 0.05, size * 0.58), (c + size * 0.05, size * 0.58), (c + size * 0.08, size * 0.74), (c - size * 0.08, size * 0.74)], s), fill=255)
        d.rounded_rectangle((size * 0.32 * s, size * 0.74 * s, size * 0.68 * s, size * 0.82 * s), 2 * s, fill=255)
        d.rounded_rectangle((size * 0.26 * s, size * 0.82 * s, size * 0.74 * s, size * 0.92 * s), 3 * s, fill=255)
    m = mask(size, size, draw)
    star = mask(size, size, lambda d, s: d.polygon(scaled(star_pts(size / 2, size * 0.31, size * 0.12, size * 0.05), s), fill=255))
    return white(np.clip(m - star, 0, 1))


def star_pts(cx, cy, r_out, r_in, points=5):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        ang = math.pi * i / points - math.pi / 2
        pts.append((cx + math.cos(ang) * r, cy + math.sin(ang) * r))
    return pts


def icon_pause(size=128):
    def draw(d, s):
        for x in (0.30, 0.58):
            d.rounded_rectangle((size * x * s, size * 0.22 * s, size * (x + 0.13) * s, size * 0.78 * s), size * 0.04 * s, fill=255)
    return white(mask(size, size, draw))


def icon_alert(size=96):
    def tri(d, s):
        pts = [(size * 0.5, size * 0.08), (size * 0.95, size * 0.88), (size * 0.05, size * 0.88)]
        d.polygon(scaled(pts, s), fill=255)
    body = blur(mask(size, size, tri), 1.2)
    body = np.clip((body - 0.25) * 1.6, 0, 1)

    def mark(d, s):
        d.rounded_rectangle((size * 0.455 * s, size * 0.34 * s, size * 0.545 * s, size * 0.64 * s), size * 0.04 * s, fill=255)
        d.ellipse((size * 0.45 * s, size * 0.70 * s, size * 0.55 * s, size * 0.80 * s), fill=255)
    return white(np.clip(body - mask(size, size, mark), 0, 1))


def icon_players(size=56):
    def draw(d, s):
        for cx, sc in ((0.36, 1.0), (0.66, 0.86)):
            r = size * 0.13 * sc
            cy = size * 0.30 + (1 - sc) * size * 0.1
            d.ellipse(((size * cx - r) * s, (cy - r) * s, (size * cx + r) * s, (cy + r) * s), fill=255)
            bw = size * 0.22 * sc
            d.pieslice(((size * cx - bw) * s, (cy + r * 1.3) * s, (size * cx + bw) * s, (cy + r * 1.3 + bw * 2) * s), 180, 360, fill=255)
    return white(mask(size, size, draw))


def icon_stopwatch(size=56):
    c = size / 2

    def draw(d, s):
        d.ellipse((size * 0.14 * s, size * 0.2 * s, size * 0.86 * s, size * 0.92 * s), fill=255)
        d.rounded_rectangle((size * 0.42 * s, size * 0.04 * s, size * 0.58 * s, size * 0.16 * s), 2 * s, fill=255)
    m = mask(size, size, draw)

    def hole(d, s):
        d.ellipse((size * 0.22 * s, size * 0.28 * s, size * 0.78 * s, size * 0.84 * s), fill=255)
    h = mask(size, size, hole)

    def hand(d, s):
        d.line((c * s, size * 0.56 * s, size * 0.66 * s, size * 0.40 * s), fill=255, width=int(3.2 * s))
        d.ellipse(((c - 3) * s, (size * 0.56 - 3) * s, (c + 3) * s, (size * 0.56 + 3) * s), fill=255)
    return white(np.clip(m - h + mask(size, size, hand), 0, 1))


def icon_target(size=56):
    c = size / 2

    def draw(d, s):
        for r, w in ((0.42, 3.4), (0.22, 3.4)):
            d.ellipse(((c - size * r) * s, (c - size * r) * s, (c + size * r) * s, (c + size * r) * s), outline=255, width=int(w * s))
        d.ellipse(((c - 3.5) * s, (c - 3.5) * s, (c + 3.5) * s, (c + 3.5) * s), fill=255)
        for ang in range(0, 360, 90):
            a = math.radians(ang)
            d.line(((c + math.cos(a) * size * 0.32) * s, (c + math.sin(a) * size * 0.32) * s,
                    (c + math.cos(a) * size * 0.50) * s, (c + math.sin(a) * size * 0.50) * s), fill=255, width=int(3.4 * s))
    return white(mask(size, size, draw))


def icon_cross(size=64):
    def draw(d, s):
        for a, b in (((0.28, 0.28), (0.72, 0.72)), ((0.72, 0.28), (0.28, 0.72))):
            d.line((size * a[0] * s, size * a[1] * s, size * b[0] * s, size * b[1] * s), fill=255, width=int(7 * s))
    return white(mask(size, size, draw))


def icon_check(size=64):
    def draw(d, s):
        pts = [(size * 0.2, size * 0.54), (size * 0.42, size * 0.76), (size * 0.82, size * 0.28)]
        d.line(scaled(pts, s), fill=255, width=int(8 * s), joint="curve")
    return white(mask(size, size, draw))


def icon_ball(size=96):
    """Baked golf ball: soft shaded sphere with a dimple pattern."""
    c = size / 2
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx, ny = (xx - c) / (c - 2), (yy - c) / (c - 2)
    rr = nx ** 2 + ny ** 2
    nz = np.sqrt(np.clip(1 - rr, 0, 1))
    light = np.clip(nx * -0.35 + ny * -0.55 + nz * 0.75, 0, 1)
    shade = 150 + 105 * light
    rgb = np.dstack([shade, shade, shade * 0.98])
    a = mask(size, size, lambda d, s: d.ellipse((2 * s, 2 * s, (size - 2) * s, (size - 2) * s), fill=255))
    rng = np.random.default_rng(3)
    dimples = Image.new("L", (size * SS, size * SS), 0)
    dd = ImageDraw.Draw(dimples)
    for _ in range(70):
        ang = rng.uniform(0, 2 * math.pi)
        rad = math.sqrt(rng.uniform(0, 0.85)) * (c - 6)
        x, y = c + math.cos(ang) * rad, c + math.sin(ang) * rad
        r = 2.6 * (1 - (rad / c) ** 2 * 0.5)
        dd.ellipse(((x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS), fill=255)
    dm = np.asarray(dimples.resize((size, size), Image.LANCZOS), np.float32) / 255
    rgb = rgb - dm[..., None] * 26
    return to_image(rgb, a)


# ---------------------------------------------------------------- lie badges (baked)

LIES = {
    "tee": ((58, 140, 64), (36, 104, 44)),
    "fairway": ((107, 165, 46), (78, 128, 30)),
    "rough": ((62, 104, 40), (40, 72, 26)),
    "bunker": ((222, 196, 140), (196, 166, 104)),
    "green": ((120, 196, 72), (82, 158, 46)),
    "water": ((48, 136, 196), (26, 96, 160)),
    "path": ((168, 164, 152), (128, 124, 114)),
    "ob": ((194, 65, 45), (150, 42, 28)),
}


def lie_badge(kind, size=112):
    big = size * 4
    c = big / 2
    top, bottom = LIES[kind]
    yy, xx = np.mgrid[0:big, 0:big].astype(np.float32)
    rgb = vgrad(big, big, top, bottom)
    inside = ((xx - c) ** 2 + (yy - c) ** 2) <= (c - 6) ** 2
    glyph = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(glyph)
    if kind == "fairway":
        # mown stripes
        for i in range(-6, 7):
            x = c + i * big * 0.14
            d.polygon([(x, 0), (x + big * 0.07, 0), (x + big * 0.07 - big * 0.3, big), (x - big * 0.3, big)], fill=60)
    if kind == "tee":
        d.polygon([(c - 26, c + 10), (c + 26, c + 10), (c + 9, c + 34), (c + 7, c + 120), (c - 7, c + 120), (c - 9, c + 34)], fill=255)
        d.ellipse((c - 62, c - 112, c + 62, c + 12), fill=255)
    elif kind == "rough":
        for bx in (-110, -40, 30, 100):
            for k in range(5):
                ang = math.radians(-36 + k * 18)
                x0, y0 = c + bx, c + 120
                x1, y1 = x0 + math.sin(ang) * 120, y0 - math.cos(ang) * (150 - abs(k - 2) * 26)
                d.line((x0, y0, x1, y1), fill=255, width=22)
    elif kind == "bunker":
        for i in range(4):
            r = 70 + i * 52
            d.arc((c - r * 1.4, c + 40 - r, c + r * 1.4, c + 40 + r), 200, 340, fill=150, width=14)
        d.ellipse((c - 34, c - 140, c + 34, c - 72), fill=255)
    elif kind == "green":
        d.rounded_rectangle((c - 64, c - 140, c - 46, c + 112), 9, fill=255)
        d.polygon([(c - 50, c - 140), (c + 104, c - 90), (c - 50, c - 40)], fill=255)
        d.ellipse((c - 120, c + 96, c + 40, c + 140), fill=200)
    elif kind == "water":
        for row in (-60, 10, 80):
            pts = []
            for k in range(0, 61):
                x = c - 150 + k * 5
                pts.append((x, c + row + math.sin(k / 60 * math.pi * 3) * 20))
            d.line(pts, fill=255, width=24, joint="curve")
    elif kind == "path":
        d.polygon([(c - 40, c - 150), (c + 40, c - 150), (c + 120, c + 150), (c - 120, c + 150)], fill=110)
        for k in range(3):
            y = c - 120 + k * 100
            d.rectangle((c - 8 - k * 4, y, c + 8 + k * 4, y + 50), fill=255)
    elif kind == "fairway":
        pass
    elif kind == "ob":
        d.rounded_rectangle((c - 22, c - 130, c + 22, c + 130), 10, fill=255)
        d.rectangle((c - 22, c - 70, c + 22, c - 30), fill=120)
    g = np.asarray(glyph, np.float32) / 255
    rgb = rgb * (1 - g[..., None]) + np.array((255, 255, 255), np.float32) * g[..., None] if kind != "fairway" else rgb * (1 - g[..., None] * 0.6) + np.array(top, np.float32) * 1.25 * (g[..., None] * 0.6)
    if kind == "fairway":
        d2 = Image.new("L", (big, big), 0)
        dd = ImageDraw.Draw(d2)
        dd.ellipse((c - 58, c - 58, c + 58, c + 58), fill=255)
        g2 = np.asarray(d2, np.float32)[..., None] / 255
        rgb = rgb * (1 - g2) + 255 * g2
    # inner shading + cream rim
    rr = np.sqrt((xx - c) ** 2 + (yy - c) ** 2) / c
    rgb = rgb * (1 - np.clip(rr - 0.75, 0, 1)[..., None] * 0.6)
    rim = (rr > (c - 22) / c) & inside
    rgb[rim] = rgb[rim] * 0.15 + np.array(CREAM, np.float32) * 0.85
    a = inside.astype(np.float32)
    img = to_image(rgb, a)
    return img.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------- lane strip

def lane_key(w=64, h=76, cut=7, lip=6):
    """Keycap-shaped lane cell, luminance-shaded so Image.color tints it (dark idle, cyan detected)."""
    body = mask(w, h, lambda d, s: d.polygon(scaled(chamfer_poly(0, 0, w, h, cut), s), fill=255))
    face = mask(w, h, lambda d, s: d.polygon(scaled(chamfer_poly(0.5, 0.5, w - 0.5, h - lip, cut - 0.5), s), fill=255))
    lum = vgrad(h, w, (255, 255, 255), (214, 214, 214))
    rgb = solid(h, w, (118, 118, 118))
    rgb = rgb * (1 - face[..., None]) + lum * face[..., None]
    hl = outline(w, h,
                 lambda d, s, f: d.polygon(scaled(chamfer_poly(0.5, 0.5, w - 0.5, h - lip, cut - 0.5), s), fill=f),
                 lambda d, s, f: d.polygon(scaled(chamfer_poly(2, 2.4, w - 2, h - lip - 1, cut - 1.2), s), fill=f))
    top_only = np.linspace(1, 0.2, h, dtype=np.float32)[:, None]
    rgb = rgb + (hl * top_only)[..., None] * 40
    return to_image(np.clip(rgb, 0, 255), body)


def lane_frame(w=64, h=76, cut=7, width=2.2):
    return white(outline(w, h,
                         lambda d, s, f: d.polygon(scaled(chamfer_poly(0, 0, w, h, cut), s), fill=f),
                         lambda d, s, f: d.polygon(scaled(chamfer_poly(width, width, w - width, h - width, cut - width * 0.41), s), fill=f)))


def lane_glow(w=112, h=124, cut=7, pad=24):
    base = outline(w, h,
                   lambda d, s, f: d.polygon(scaled(chamfer_poly(pad, pad, w - pad, h - pad, cut), s), fill=f),
                   lambda d, s, f: d.polygon(scaled(chamfer_poly(pad + 4, pad + 4, w - pad - 4, h - pad - 4, cut), s), fill=f))
    return white(np.clip(blur(base, 8) * 2.4, 0, 1))


def lane_pointer(w=40, h=28):
    def draw(d, s):
        pts = [(w * 0.5, h * 0.08), (w * 0.94, h * 0.92), (w * 0.06, h * 0.92)]
        d.polygon(scaled(pts, s), fill=255)
    return white(np.clip((blur(mask(w, h, draw), 0.6) - 0.2) * 1.4, 0, 1))


# ---------------------------------------------------------------- power meter (drawn at 2x display size)

METER_TRACK = (80, 520)   # display 40 x 260
METER_FILL = (48, 488)    # display 24 x 244, centred in the track (inset 16 px at 2x)
METER_TICKS = (60, 496)   # display 30 x 248: METER_FILL height + 4 px (2x) padding top and bottom


def meter_track():
    w, h = METER_TRACK
    r = w / 2
    outer = mask(w, h, lambda d, s: d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), r * s, fill=255))
    inner = mask(w, h, lambda d, s: d.rounded_rectangle((12 * s, 12 * s, (w - 12) * s, (h - 12) * s), (r - 12) * s, fill=255))
    rgb = vgrad(h, w, (44, 54, 54), (22, 28, 28))
    a = outer * 0.92
    rim = outline(w, h,
                  lambda d, s, f: d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), r * s, fill=f),
                  lambda d, s, f: d.rounded_rectangle((3 * s, 3 * s, (w - 3) * s, (h - 3) * s), (r - 3) * s, fill=f))
    rgb, a = over(rgb, a, solid(h, w, CREAM), rim * 0.4)
    well = solid(h, w, (6, 9, 9))
    rgb, a = over(rgb, a, well, inner * 0.95)
    shadow = np.clip(inner - np.roll(inner, 6, axis=0), 0, 1)
    rgb, a = over(rgb, a, solid(h, w, (0, 0, 0)), blur(shadow, 3) * 0.6 * inner)
    return to_image(rgb, a)


def meter_fill():
    w, h = METER_FILL
    r = w / 2
    shape = mask(w, h, lambda d, s: d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), r * s, fill=255))
    lum = vgrad(h, w, (255, 255, 255), (150, 150, 150))
    xx = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    gloss = np.exp(-((xx - 0.3) / 0.12) ** 2) * 60
    rgb = np.clip(lum + gloss[..., None], 0, 255)
    return to_image(rgb, shape)


def meter_ticks():
    w, h = METER_TICKS
    pad = 4
    span = h - 2 * pad

    def draw(d, s):
        for i in range(21):
            y = pad + span * (1 - i / 20)
            major = i % 5 == 0
            length = 36 if major else 16
            width = 4 if major else 2.4
            d.rectangle((0, (y - width / 2) * s, length * s, (y + width / 2) * s), fill=255)
    m = mask(w, h, draw)
    return white(m)


def meter_pointer(size=40):
    def draw(d, s):
        pts = [(size * 0.92, size * 0.5), (size * 0.12, size * 0.12), (size * 0.30, size * 0.5), (size * 0.12, size * 0.88)]
        d.polygon(scaled(pts, s), fill=255)
    return white(mask(size, size, draw))


# ---------------------------------------------------------------- toggles (2x)

def toggle(on, w=120, h=64):
    r = h / 2
    track = mask(w, h, lambda d, s: d.rounded_rectangle((2 * s, 6 * s, (w - 2) * s, (h - 6) * s), (r - 6) * s, fill=255))
    rim = outline(w, h,
                  lambda d, s, f: d.rounded_rectangle((2 * s, 6 * s, (w - 2) * s, (h - 6) * s), (r - 6) * s, fill=f),
                  lambda d, s, f: d.rounded_rectangle((5 * s, 9 * s, (w - 5) * s, (h - 9) * s), (r - 9) * s, fill=f))
    cx = w - r if on else r
    knob = mask(w, h, lambda d, s: d.ellipse(((cx - r + 3) * s, 3 * s, (cx + r - 3) * s, (h - 3) * s), fill=255))
    shadow = blur(np.roll(knob, 3, axis=0), 3)
    rgb = solid(h, w, CYAN if on else (34, 42, 42))
    a = track * (1.0 if on else 0.92)
    if not on:
        rgb, a = over(rgb, a, solid(h, w, CREAM), rim * 0.35)
    rgb, a = over(rgb, a, solid(h, w, (0, 0, 0)), shadow * 0.45)
    knob_rgb = vgrad(h, w, (255, 252, 244), (222, 216, 200)) if on else vgrad(h, w, (170, 172, 168), (128, 132, 128))
    rgb, a = over(rgb, a, knob_rgb, knob)
    return to_image(rgb, a)


# ---------------------------------------------------------------- branding

def arc_text(canvas, text, fnt, cx, cy, radius, center_deg, tracking_deg, top=True, fill=255):
    """Pastes glyphs along a circle. top=True reads clockwise over the top; False reads left-to-right under the bottom."""
    widths = [fnt.getlength(ch) for ch in text]
    total = sum(widths)
    total_deg = math.degrees(total / radius) + tracking_deg * (len(text) - 1)
    start = center_deg - total_deg / 2 if top else center_deg + total_deg / 2
    ang = start
    asc, desc = fnt.getmetrics()
    for ch, wch in zip(text, widths):
        glyph_deg = math.degrees(wch / radius)
        mid = ang + glyph_deg / 2 if top else ang - glyph_deg / 2
        g = Image.new("L", (int(wch) + 20, asc + desc + 20), 0)
        ImageDraw.Draw(g).text((10, 10), ch, font=fnt, fill=fill)
        rot = -mid if top else 180 - mid
        g = g.rotate(rot, resample=Image.BICUBIC, expand=True)
        rad = math.radians(mid)
        gx = cx + math.sin(rad) * radius
        gy = cy - math.cos(rad) * radius
        canvas.paste(Image.new("L", g.size, fill), (int(gx - g.width / 2), int(gy - g.height / 2)), g)
        ang = ang + glyph_deg + tracking_deg if top else ang - glyph_deg - tracking_deg
    return canvas


def emblem(size=384):
    """Puttshack Pro crest: brass bezel, charcoal band with arc lettering, a sunlit hole with a cyan flag."""
    big = size * 2
    c = big / 2
    yy, xx = np.mgrid[0:big, 0:big].astype(np.float32)
    r = np.sqrt((xx - c) ** 2 + (yy - c) ** 2)
    R = c - 4
    rgb = np.zeros((big, big, 3), np.float32)
    a = np.zeros((big, big), np.float32)

    def ring_mask(r0, r1, soft=1.5):
        return np.clip((r1 - r) / soft + 0.5, 0, 1) * np.clip((r - r0) / soft + 0.5, 0, 1)

    disc_all = np.clip((R - r) / 1.5 + 0.5, 0, 1)
    # brass bezel with a vertical sheen
    brass = vgrad(big, big, (222, 200, 156), (150, 126, 88))
    rgb, a = over(rgb, a, brass, disc_all)
    band_r0, band_r1 = R * 0.64, R * 0.915
    band = ring_mask(0, band_r1)
    rgb, a = over(rgb, a, vgrad(big, big, (40, 50, 50), (16, 21, 21)), band)
    rgb, a = over(rgb, a, solid(big, big, CREAM), ring_mask(band_r1 - 6, band_r1 - 2) * 0.6)
    # scene disc
    scene_r = band_r0
    scene = ring_mask(0, scene_r)
    sky = vgrad(big, big, (120, 196, 232), (214, 236, 240))
    rgb, a = over(rgb, a, sky, scene)
    hill = Image.new("L", (big, big), 0)
    hd = ImageDraw.Draw(hill)
    hd.ellipse((c - scene_r * 1.9, c + scene_r * 0.05, c + scene_r * 1.5, c + scene_r * 2.2), fill=255)
    hill_m = np.asarray(hill, np.float32) / 255 * scene
    rgb, a = over(rgb, a, vgrad(big, big, (128, 190, 70), (62, 120, 36)), hill_m)
    back = Image.new("L", (big, big), 0)
    bd = ImageDraw.Draw(back)
    bd.ellipse((c - scene_r * 0.2, c - scene_r * 0.12, c + scene_r * 2.4, c + scene_r * 1.5), fill=255)
    back_m = np.asarray(back, np.float32) / 255 * scene * (1 - hill_m)
    rgb, a = over(rgb, a, solid(big, big, (86, 150, 60)), back_m)
    # stripes on the near hill
    stripes = Image.new("L", (big, big), 0)
    sd = ImageDraw.Draw(stripes)
    for i in range(-8, 9):
        x = c + i * 46
        sd.polygon([(x, c), (x + 22, c), (x + 22 - 160, big), (x - 160, big)], fill=255)
    rgb, a = over(rgb, a, solid(big, big, (150, 206, 92)), np.asarray(stripes, np.float32) / 255 * hill_m * 0.25)
    # cup, flag, ball
    fl = Image.new("L", (big, big), 0)
    fd = ImageDraw.Draw(fl)
    px, base_y = c + scene_r * 0.12, c + scene_r * 0.30
    fd.ellipse((px - 30, base_y - 9, px + 30, base_y + 11), fill=255)
    cup_m = np.asarray(fl, np.float32) / 255 * scene
    rgb, a = over(rgb, a, solid(big, big, (16, 30, 18)), cup_m)
    pole = Image.new("L", (big, big), 0)
    pd = ImageDraw.Draw(pole)
    pd.rounded_rectangle((px - 5, c - scene_r * 0.72, px + 5, base_y), 4, fill=255)
    rgb, a = over(rgb, a, solid(big, big, CREAM), np.asarray(pole, np.float32) / 255 * scene)
    flag = Image.new("L", (big, big), 0)
    gd = ImageDraw.Draw(flag)
    top_y = c - scene_r * 0.72
    pts = [(px + 4, top_y)]
    for k in range(1, 21):
        t = k / 20
        pts.append((px + 4 + t * scene_r * 0.62, top_y + t * scene_r * 0.20 + math.sin(t * math.pi * 1.4) * 10))
    for k in range(20, -1, -1):
        t = k / 20
        pts.append((px + 4 + t * scene_r * 0.62, top_y + scene_r * 0.40 - t * scene_r * 0.20 + math.sin(t * math.pi * 1.4) * 10))
    gd.polygon(pts, fill=255)
    flag_m = np.asarray(flag, np.float32) / 255 * scene
    rgb, a = over(rgb, a, vgrad(big, big, (40, 225, 248), (0, 160, 186)), flag_m)
    ball = Image.new("L", (big, big), 0)
    bd2 = ImageDraw.Draw(ball)
    bx, by = c - scene_r * 0.34, c + scene_r * 0.48
    bd2.ellipse((bx - 24, by - 24, bx + 24, by + 24), fill=255)
    rgb, a = over(rgb, a, solid(big, big, (250, 250, 246)), np.asarray(ball, np.float32) / 255 * scene)
    # scene rim
    rgb, a = over(rgb, a, solid(big, big, CREAM), ring_mask(scene_r - 2, scene_r + 5))
    # lettering on the band
    txt = Image.new("L", (big, big), 0)
    mid_r = (band_r0 + band_r1) / 2
    letter = font("BarlowCondensed-ExtraBold", int(big * 0.105))
    asc, desc = letter.getmetrics()
    arc_text(txt, "PUTTSHACK", letter, c, c, mid_r - asc * 0.02, 0, 3.2, top=True)
    small = font("BarlowCondensed-Bold", int(big * 0.082))
    arc_text(txt, "PRO", small, c, c, mid_r + 4, 180, 7.0, top=False)
    rgb, a = over(rgb, a, solid(big, big, CREAM), np.asarray(txt, np.float32) / 255)
    # stars either side of PRO
    st = Image.new("L", (big, big), 0)
    sdraw = ImageDraw.Draw(st)
    for ang in (-124, 124):
        rad = math.radians(180 + ang * 0.36)
        sx, sy = c + math.sin(rad) * mid_r, c - math.cos(rad) * mid_r
        sdraw.polygon(star_pts(sx, sy, 16, 7), fill=255)
    rgb, a = over(rgb, a, solid(big, big, CYAN), np.asarray(st, np.float32) / 255)
    # bezel inner edge shadow
    rgb, a = over(rgb, a, solid(big, big, (0, 0, 0)), ring_mask(band_r1, band_r1 + 5) * 0.35)
    img = to_image(rgb, a)
    return img.resize((size, size), Image.LANCZOS)


def wordmark(color, w=480, h=176):
    """PUTTSHACK over a ruled PRO, as on the Pro Shop header (drawn at 2x of a 240 x 88 slot)."""
    scale = 4
    big = Image.new("L", (w * scale, h * scale), 0)
    word = text_mask("PUTTSHACK", font("BarlowCondensed-ExtraBold", 132 * scale), tracking=3, s=scale)
    word = word.resize((int(word.width * (w * scale * 0.98) / word.width), int(word.height * (w * scale * 0.98) / word.width)), Image.LANCZOS)
    big.paste(word, ((w * scale - word.width) // 2, int(6 * scale)), word)
    pro = text_mask("PRO", font("BarlowCondensed-Bold", 56 * scale), tracking=16, s=scale)
    py = 6 * scale + word.height + 16 * scale
    px = (w * scale - pro.width) // 2
    big.paste(pro, (px, py), pro)
    d = ImageDraw.Draw(big)
    rule_y = py + pro.height / 2
    gap = 22 * scale
    d.rectangle((w * scale * 0.12, rule_y - 3 * scale, px - gap, rule_y + 3 * scale), fill=255)
    d.rectangle((px + pro.width + gap, rule_y - 3 * scale, w * scale * 0.88, rule_y + 3 * scale), fill=255)
    bbox = big.getbbox()
    big = big.crop((0, 0, w * scale, max(bbox[3] + 4 * scale, h * scale)))
    m = np.asarray(big.resize((w, int(big.height / scale)), Image.LANCZOS), np.float32) / 255
    return to_image(solid(m.shape[0], m.shape[1], color), m)


# ---------------------------------------------------------------- scorecard marks

def score_circle(size=64, double=False):
    def draw(d, s):
        d.ellipse((4 * s, 4 * s, (size - 4) * s, (size - 4) * s), outline=255, width=int(3 * s))
        if double:
            d.ellipse((10 * s, 10 * s, (size - 10) * s, (size - 10) * s), outline=255, width=int(2.6 * s))
    return white(mask(size, size, draw))


def score_square(size=64, double=False):
    def draw(d, s):
        d.rectangle((6 * s, 6 * s, (size - 6) * s, (size - 6) * s), outline=255, width=int(3 * s))
        if double:
            d.rectangle((12 * s, 12 * s, (size - 12) * s, (size - 12) * s), outline=255, width=int(2.6 * s))
    return white(mask(size, size, draw))


# ---------------------------------------------------------------- keycaps (baked, drawn at 2x a 36 px cap)

KEY_H = 72
KEY_LIP = 7
KEYS = {
    # name: (legend, width)
    "enter": ("ENTER", 136), "esc": ("ESC", 104), "tab": ("TAB", 104), "shift": ("SHIFT", 136),
    "space": ("SPACE", 160), "f1": ("F1", 84),
    "up": ("^up", 72), "down": ("^down", 72), "left": ("^left", 72), "right": ("^right", 72),
    "p": ("P", 72), "e": ("E", 72), "r": ("R", 72), "c": ("C", 72), "q": ("Q", 72),
}
for _d in "0123456789":
    KEYS[_d] = (_d, 72)


def keycap(legend, w, h=KEY_H, lip=KEY_LIP):
    s = SS
    radius = 12
    body = mask(w, h, lambda d, k: d.rounded_rectangle((1 * k, 1 * k, (w - 1) * k, (h - 1) * k), radius * k, fill=255))
    face = mask(w, h, lambda d, k: d.rounded_rectangle((1 * k, 1 * k, (w - 1) * k, (h - 1 - lip) * k), radius * k, fill=255))
    rim = outline(w, h,
                  lambda d, k, f: d.rounded_rectangle((1 * k, 1 * k, (w - 1) * k, (h - 1 - lip) * k), radius * k, fill=f),
                  lambda d, k, f: d.rounded_rectangle((3.2 * k, 3.2 * k, (w - 3.2) * k, (h - 3.2 - lip) * k), (radius - 2) * k, fill=f))
    rgb = solid(h, w, (9, 13, 13))
    a = body.copy()
    rgb, a = over(rgb, a, vgrad(h, w, (62, 74, 74), (34, 42, 42)), face)
    rgb, a = over(rgb, a, solid(h, w, CREAM), rim * 0.62)
    if legend:
        big = Image.new("L", (w * s, h * s), 0)
        d = ImageDraw.Draw(big)
        cx, cy = w * s / 2, (h - lip) * s / 2
        if legend.startswith("^"):
            t = 15 * s
            direction = legend[1:]
            pts = {"up": [(0, -1), (1, 0.75), (-1, 0.75)], "down": [(0, 1), (1, -0.75), (-1, -0.75)],
                   "left": [(-1, 0), (0.75, -1), (0.75, 1)], "right": [(1, 0), (-0.75, -1), (-0.75, 1)]}[direction]
            d.polygon([(cx + x * t, cy + y * t) for x, y in pts], fill=255)
        else:
            size = 40 if len(legend) == 1 else (34 if len(legend) <= 3 else 30)
            fnt = font("BarlowCondensed-Bold", size * s)
            tm = text_mask(legend, fnt, tracking=1.2 if len(legend) > 1 else 0, s=s)
            big.paste(tm, (int(cx - tm.width / 2), int(cy - tm.height / 2)), tm)
        lm = np.asarray(big.resize((w, h), Image.LANCZOS), np.float32) / 255
        rgb, a = over(rgb, a, solid(h, w, CREAM), lm)
    return to_image(rgb, a)


# ---------------------------------------------------------------- output

def build():
    sprites = {
        "hud_glass": glass(),
        "hud_card": card(),
        "hud_card_accent": card_accent(),
        "hud_plate": plate(),
        "hud_plate_line": plate_line(),
        "hud_pill": pill(),
        "hud_pill_line": pill_line(),
        "hud_wash_corner": wash_corner(),
        "hud_wash_bottom": wash_bottom(),
        "hud_rule_fade": rule_fade(),
        "hud_rule_fade_l": rule_fade(both=False),
        "hud_divider_v": divider_v(),
        "hud_disc": disc(),
        "hud_ring": ring(),
        "hud_ring_glow": ring_glow(),
        "hud_badge_check": badge_check(),
        "icon_flag": icon_flag(),
        "icon_wind_arrow": icon_wind_arrow(),
        "hud_wind_dial": wind_dial(),
        "icon_trophy": icon_trophy(),
        "icon_pause": icon_pause(),
        "icon_alert": icon_alert(),
        "icon_players": icon_players(),
        "icon_stopwatch": icon_stopwatch(),
        "icon_target": icon_target(),
        "icon_check": icon_check(),
        "icon_cross": icon_cross(),
        "icon_ball": icon_ball(),
        "hud_lane_key": lane_key(),
        "hud_lane_frame": lane_frame(),
        "hud_lane_glow": lane_glow(),
        "hud_lane_pointer": lane_pointer(),
        "hud_meter_track": meter_track(),
        "hud_meter_fill": meter_fill(),
        "hud_meter_ticks": meter_ticks(),
        "hud_meter_pointer": meter_pointer(),
        "hud_toggle_on": toggle(True),
        "hud_toggle_off": toggle(False),
        "score_circle": score_circle(),
        "score_circle2": score_circle(double=True),
        "score_square": score_square(),
        "score_square2": score_square(double=True),
        "key_blank": keycap(None, 72),
    }
    em = emblem(384)
    sprites["hud_emblem"] = em
    sprites["hud_emblem_sm"] = downsample(emblem(384), (192, 192))
    sprites["hud_wordmark_cream"] = wordmark(CREAM)
    sprites["hud_wordmark_ink"] = wordmark(INK)
    for kind in LIES:
        big = lie_badge(kind, 112)
        sprites[f"lie_{kind}"] = big
        sprites[f"lie_{kind}_sm"] = downsample(big, (72, 72))
    for name, (legend, width) in KEYS.items():
        sprites[f"key_{name}"] = keycap(legend, width)
    return sprites


def contact_sheet(sprites):
    cell, cols = 200, 10
    names = sorted(sprites)
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * cell), (0, 0, 0, 255))
    d = ImageDraw.Draw(sheet)
    label = font("BarlowCondensed-SemiBold", 18)
    for i, name in enumerate(names):
        x, y = (i % cols) * cell, (i // cols) * cell
        # half checker, half grass so alpha edges and dark art both read
        for cy in range(0, cell, 16):
            for cx in range(0, cell, 16):
                shade = (205, 205, 205, 255) if (cx + cy) // 16 % 2 else (150, 150, 150, 255)
                if cx >= cell // 2:
                    shade = (64, 120, 44, 255) if (cx + cy) // 16 % 2 else (74, 132, 52, 255)
                d.rectangle((x + cx, y + cy, x + cx + 15, y + cy + 15), fill=shade)
        img = sprites[name]
        k = min(1.0, (cell - 36) / max(img.width, img.height))
        thumb = img.resize((max(1, int(img.width * k)), max(1, int(img.height * k))), Image.LANCZOS)
        sheet.alpha_composite(thumb, (x + (cell - thumb.width) // 2, y + (cell - 28 - thumb.height) // 2 + 4))
        d.rectangle((x, y + cell - 24, x + cell, y + cell), fill=(0, 0, 0, 255))
        d.text((x + 6, y + cell - 22), name, font=label, fill=(255, 255, 255, 255))
    return sheet


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(REVIEW, exist_ok=True)
    sprites = build()
    for name, img in sprites.items():
        alpha = np.asarray(img)[..., 3]
        assert alpha.min() == 0, f"{name} has no transparent pixels"
        img.save(os.path.join(OUT, name + ".png"), optimize=True)
    contact_sheet(sprites).convert("RGB").save(os.path.join(REVIEW, "hud_sprites_sheet.png"))
    print(f"{len(sprites)} sprites -> {OUT}")


if __name__ == "__main__":
    main()
