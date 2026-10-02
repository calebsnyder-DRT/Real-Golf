"""Clothes parsing of the concept views (SegFormer-B2 'clothes', ONNX on CPU) -> <name>_<view>_parse.png label maps.

Run with a Python that has onnxruntime, numpy and Pillow:
  python pp_stylized_parse.py <model.onnx> <views dir> caleb maya
Labels: 0 bg, 1 hat, 2 hair, 4 upper clothes, 6 pants, 8 belt, 9/10 shoes, 11 face, 12/13 legs, 14/15 arms.
"""
import os
import sys

import numpy as np
import onnxruntime as ort
from PIL import Image

MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)
PALETTE = np.array([
    (0, 0, 0), (255, 255, 255), (90, 50, 20), (0, 0, 0), (40, 90, 220), (200, 0, 200), (150, 150, 150), (255, 0, 255),
    (20, 20, 20), (0, 220, 220), (0, 160, 160), (255, 190, 150), (230, 140, 100), (200, 110, 80), (255, 160, 120),
    (220, 120, 90), (255, 255, 0), (0, 255, 0)], dtype=np.uint8)


def parse(sess, img):
    w, h = img.size
    s = max(w, h)
    sq = Image.new("RGB", (s, s), (200, 200, 200))
    sq.paste(img, ((s - w) // 2, (s - h) // 2))
    x = np.asarray(sq.resize((512, 512), Image.BILINEAR), dtype=np.float32) / 255.0
    x = ((x - MEAN) / STD).transpose(2, 0, 1)[None]
    logits = sess.run(None, {sess.get_inputs()[0].name: x})[0][0]
    # Upsample logits to the padded square, crop back to the image.
    up = np.stack([np.asarray(Image.fromarray(l).resize((s, s), Image.BILINEAR)) for l in logits])
    lab = up.argmax(0).astype(np.uint8)
    return lab[(s - h) // 2:(s - h) // 2 + h, (s - w) // 2:(s - w) // 2 + w]


def main():
    model, views = sys.argv[1], sys.argv[2]
    sess = ort.InferenceSession(model, providers=["CPUExecutionProvider"])
    for name in sys.argv[3:]:
        for view in ("front", "back", "side", "face"):
            src = os.path.join(views, f"{name}_{view}.jpg")
            img = Image.open(src).convert("RGB")
            matte = np.asarray(Image.open(os.path.join(views, f"{name}_{view}_matte.png")).getchannel("A")) > 128
            lab = parse(sess, img)
            lab[~matte] = 0
            Image.fromarray(lab).save(os.path.join(views, f"{name}_{view}_parse.png"))
            Image.fromarray(PALETTE[lab]).save(os.path.join(views, f"{name}_{view}_parse_vis.png"))
            counts = {int(k): int(v) for k, v in zip(*np.unique(lab, return_counts=True))}
            print(name, view, counts)


if __name__ == "__main__":
    main()
