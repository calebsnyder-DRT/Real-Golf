"""Generates the flat brand HUD sprites (Puttshack Pro HUD round 1, Claude Design "Puttshack Pro HUD.dc.html").

Every element of that design is a flat shape: a rounded rect, a pill, a circle, a ring or text. These sprites are white
on transparent so Image.color tints them, and the rounded ones are 9-sliced so one texture serves every radius:
HudUIBuilder sets Image.pixelsPerUnitMultiplier = source radius / wanted radius, and UGUI shrinks the borders on its
own when a rect is shorter than two radii, which turns flat_round into a perfect pill or circle.

Run: python ArtSource/UI/HUD/make_hud_flat.py   (needs Pillow + numpy; rsvg-convert for the wordmark)
Output: Assets/_Game/Art/HUD/Sprites/flat_*.png. BORDERS below are mirrored in HudBaker.Borders.
"""
import os
import shutil
import subprocess

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.environ.get("HUD_SPRITE_OUT", os.path.join(ROOT, "Assets", "_Game", "Art", "HUD", "Sprites"))
BRAND = os.path.join(HERE, "Brand")
SS = 4

# name -> 9-slice border in px (left, bottom, right, top); mirrored in HudBaker.Borders
BORDERS = {
    "flat_round": (128, 128, 128, 128),
    "flat_line_r10": (22, 22, 22, 22),
    "flat_line_pill": (48, 48, 48, 48),
    "flat_line_r28": (58, 58, 58, 58),
    "flat_line_r24": (52, 52, 52, 52),
}


def coverage(w, h, sdf):
    """Antialiased alpha from a signed distance function evaluated on a supersampled pixel grid."""
    ys, xs = np.mgrid[0:h * SS, 0:w * SS].astype(np.float64)
    d = sdf((xs + 0.5) / SS, (ys + 0.5) / SS)
    a = np.clip(0.5 - d * SS, 0.0, 1.0)
    return a.reshape(h, SS, w, SS).mean(axis=(1, 3))


def rounded_rect_sdf(w, h, r):
    def f(x, y):
        qx = np.abs(x - w / 2) - (w / 2 - r)
        qy = np.abs(y - h / 2) - (h / 2 - r)
        outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0))
        inside = np.minimum(np.maximum(qx, qy), 0)
        return outside + inside - r
    return f


def stroke(sdf, width):
    return lambda x, y: np.abs(sdf(x, y) + width / 2) - width / 2


def segment_sdf(ax, ay, bx, by, half):
    def f(x, y):
        px, py = x - ax, y - ay
        vx, vy = bx - ax, by - ay
        t = np.clip((px * vx + py * vy) / (vx * vx + vy * vy), 0, 1)
        return np.hypot(px - vx * t, py - vy * t) - half
    return f


def union(*fs):
    return lambda x, y: np.minimum.reduce([f(x, y) for f in fs])


def save(name, alpha):
    h, w = alpha.shape
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = np.round(alpha * 255).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(os.path.join(OUT, name + ".png"))
    print("wrote", name, w, "x", h)


def gradient_h(w=256, h=4):
    """Left-to-right fade used by the attract screen: opaque, holding 0.82 to 40%, clear by 75%."""
    t = (np.arange(w) + 0.5) / w
    a = np.interp(t, [0.0, 0.4, 0.75, 1.0], [1.0, 0.82, 0.0, 0.0])
    return np.tile(a, (h, 1))


def arrow(size=128):
    """Lucide arrow-up (24-unit box, stroke 2.8, round caps) scaled to `size`."""
    k = size / 24.0
    half = 1.4 * k
    return coverage(size, size, union(
        segment_sdf(12 * k, 19 * k, 12 * k, 5 * k, half),
        segment_sdf(5 * k, 12 * k, 12 * k, 5 * k, half),
        segment_sdf(19 * k, 12 * k, 12 * k, 5 * k, half),
    ))


def wordmark():
    svg = os.path.join(BRAND, "logo_wordmark_white.svg")
    dst = os.path.join(OUT, "flat_wordmark.png")
    subprocess.run(["rsvg-convert", "-w", "1040", "-o", dst, svg], check=True)
    print("wrote flat_wordmark")


def main():
    os.makedirs(OUT, exist_ok=True)
    save("flat_round", coverage(256, 256, rounded_rect_sdf(256, 256, 128)))
    save("flat_line_r10", coverage(64, 64, stroke(rounded_rect_sdf(64, 64, 20), 4)))
    save("flat_line_pill", coverage(96, 96, stroke(rounded_rect_sdf(96, 96, 48), 4)))
    save("flat_line_r28", coverage(128, 128, stroke(rounded_rect_sdf(128, 128, 56), 4)))
    save("flat_line_r24", coverage(112, 112, stroke(rounded_rect_sdf(112, 112, 48), 6)))
    save("flat_ring_thin", coverage(256, 256, stroke(rounded_rect_sdf(256, 256, 128), 5.5)))
    save("flat_ring_mid", coverage(256, 256, stroke(rounded_rect_sdf(256, 256, 128), 15)))
    save("flat_ring_band", coverage(512, 512, stroke(rounded_rect_sdf(512, 512, 256), 20.5)))
    save("flat_arrow", arrow())
    save("flat_fade_h", gradient_h())
    wordmark()
    shutil.copyfile(os.path.join(BRAND, "logo_pin.png"), os.path.join(OUT, "flat_logo_pin.png"))
    print("wrote flat_logo_pin")


if __name__ == "__main__":
    main()
