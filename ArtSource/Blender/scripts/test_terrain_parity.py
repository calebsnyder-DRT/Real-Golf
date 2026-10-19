"""C#/Python terrain parity: pp_pebble_terrain.py against the golden TerrainModel.cs heights.

The golden (Assets/_Game/Tests/EditMode/Fixtures/terrain_parity.golden.json, written by Unity's
Puttshack > Course > Write Terrain Parity Golden and checked on the C# side by TerrainParityTests) stores the course's
terrain features and probe heights as IEEE double bit patterns, so both languages evaluate exactly the same numbers.

    python3 ArtSource/Blender/scripts/test_terrain_parity.py           # scalar port (numpy part skipped if missing)
    blender -b --python ArtSource/Blender/scripts/test_terrain_parity.py  # scalar + numpy port

Exits non-zero on a mismatch. Both ports are bit-identical today; the tolerances (scalar 1e-9 m, numpy 1e-6 m) leave
room for harmless reordering in the vectorised code.
"""
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pp_pebble_terrain as T  # noqa: E402

GOLDEN = os.path.normpath(os.path.join(HERE, "..", "..", "..", "Assets", "_Game", "Tests", "EditMode", "Fixtures",
                                       "terrain_parity.golden.json"))
SCALAR_TOL = 1e-9
NUMPY_TOL = 1e-6


def from_bits(hex16):
    return struct.unpack(">d", bytes.fromhex(hex16))[0]


def load(path=GOLDEN):
    with open(path) as f:
        golden = json.load(f)
    assert golden["Format"] == 1, "unknown golden format %r" % golden["Format"]
    # Same shape as the exported hole JSON (Newtonsoft field names), so pp_pebble_terrain.features() reads it as-is.
    hole = {"Terrain": []}
    for f in golden["Features"]:
        hole["Terrain"].append({
            "Kind": f["Kind"],
            "Center": {"x": from_bits(f["Center"][0]), "z": from_bits(f["Center"][1])},
            "InnerRadius": from_bits(f["Inner"]),
            "OuterRadius": from_bits(f["Outer"]),
            "Height": from_bits(f["Height"]),
            "Slope": {"x": from_bits(f["Slope"][0]), "z": from_bits(f["Slope"][1])},
            "Path": [{"x": from_bits(p[0]), "z": from_bits(p[1])} for p in f["Path"]] if f.get("Path") else None,
        })
    probes = [(float(p[0]), float(p[1]), from_bits(p[2])) for p in golden["Probes"]]
    return golden["Course"], T.features(hole), probes


def check_scalar(feats, probes):
    exact, worst, where = 0, 0.0, None
    for x, z, h in probes:
        got = T.height(feats, x, z)
        if got == h:
            exact += 1
        d = abs(got - h)
        if d > worst:
            worst, where = d, (x, z, h, got)
    return exact, worst, where


def check_numpy(feats, probes):
    import numpy as np
    xs = np.array([p[0] for p in probes])
    zs = np.array([p[1] for p in probes])
    hs = np.array([p[2] for p in probes])
    got = T.height_np(feats, xs, zs)
    diff = np.abs(got - hs)
    i = int(np.argmax(diff))
    return int((got == hs).sum()), float(diff[i]), (probes[i][0], probes[i][1], probes[i][2], float(got[i]))


def main():
    course, feats, probes = load()
    kinds = {}
    for f in feats:
        kinds[f["kind"]] = kinds.get(f["kind"], 0) + 1
    print("golden: %s, %d features %s, %d probes" % (course, len(feats), kinds, len(probes)))
    ok = True

    exact, worst, where = check_scalar(feats, probes)
    print("scalar height(): %d/%d bit-identical, max |diff| %.3g m" % (exact, len(probes), worst))
    if worst > SCALAR_TOL:
        print("  FAIL at x=%r z=%r: golden %r, python %r" % where)
        ok = False

    try:
        import numpy  # noqa: F401
    except ImportError:
        print("numpy height_np(): skipped (numpy not installed; run under Blender's python)")
    else:
        exact, worst, where = check_numpy(feats, probes)
        print("numpy height_np(): %d/%d bit-identical, max |diff| %.3g m" % (exact, len(probes), worst))
        if worst > NUMPY_TOL:
            print("  FAIL at x=%r z=%r: golden %r, python %r" % where)
            ok = False

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    code = main()
    if "bpy" in sys.modules:
        # Under `blender -b --python`, sys.exit inside the script is reported but Blender keeps its own exit code.
        import os as _os
        sys.stdout.flush()
        _os._exit(code)
    sys.exit(code)
