"""Python port of Assets/_Game/Scripts/Core/Content/TerrainModel.cs plus hole JSON helpers.

The Pebble Beach scenery samples this so the surround terrain meets the baked gameplay ground exactly at the seam.
Keep it in lockstep with TerrainModel.cs (Plateau, Tilt, Shelf, Bowl).
"""
import json
import math


def load_hole(path):
    with open(path) as f:
        return json.load(f)


def _falloff(r, inner, outer):
    if r <= inner:
        return 1.0
    if r >= outer:
        return 0.0
    t = (r - inner) / (outer - inner)
    s = 1 - t
    return s * s * (3 - 2 * s)


def _ramp(d, start, end):
    if d <= start:
        return 0.0
    if d >= end:
        return 1.0
    t = (d - start) / (end - start)
    return t * t * (3 - 2 * t)


def contains(poly, x, z):
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[j]
        if (az > z) != (bz > z):
            xc = (bx - ax) * (z - az) / (bz - az) + ax
            if x < xc:
                inside = not inside
        j = i
    return inside


def distance_to_edge(poly, x, z):
    best = float("inf")
    n = len(poly)
    j = n - 1
    for i in range(n):
        ax, az = poly[j]
        bx, bz = poly[i]
        abx, abz = bx - ax, bz - az
        l2 = abx * abx + abz * abz
        t = ((x - ax) * abx + (z - az) * abz) / l2 if l2 > 1e-12 else 0.0
        t = 0.0 if t < 0 else 1.0 if t > 1 else t
        dx, dz = x - (ax + abx * t), z - (az + abz * t)
        d2 = dx * dx + dz * dz
        if d2 < best:
            best = d2
        j = i
    return math.sqrt(best)


def _left_normal(a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]
    l = math.hypot(dx, dz)
    if l < 1e-12:
        return 0.0, 0.0
    return -dz / l, dx / l


def signed_distance_left(path, x, z):
    n = len(path)
    best = float("inf")
    sign = 1.0
    for i in range(n - 1):
        ax, az = path[i]
        bx, bz = path[i + 1]
        abx, abz = bx - ax, bz - az
        l2 = abx * abx + abz * abz
        if l2 < 1e-12:
            continue
        t = ((x - ax) * abx + (z - az) * abz) / l2
        vertex = -1
        if t < 0 and i > 0:
            t, vertex = 0.0, i
        elif t > 1 and i < n - 2:
            t, vertex = 1.0, i + 1
        dx, dz = x - (ax + abx * t), z - (az + abz * t)
        d2 = dx * dx + dz * dz
        if d2 >= best:
            continue
        best = d2
        if vertex < 0:
            sign = 1.0 if abx * dz - abz * dx >= 0 else -1.0
        else:
            p, v, q = path[vertex - 1], path[vertex], path[vertex + 1]
            n1 = _left_normal(p, v)
            n2 = _left_normal(v, q)
            sign = 1.0 if (x - v[0]) * (n1[0] + n2[0]) + (z - v[1]) * (n1[1] + n2[1]) >= 0 else -1.0
    return 0.0 if best == float("inf") else sign * math.sqrt(best)


def features(hole):
    """Normalises hole['Terrain'] into tuples with python lists for fast evaluation."""
    out = []
    for f in hole["Terrain"]:
        path = [(p["x"], p["z"]) for p in f["Path"]] if f.get("Path") else None
        out.append({
            "kind": f["Kind"], "cx": f["Center"]["x"], "cz": f["Center"]["z"],
            "inner": f["InnerRadius"], "outer": f["OuterRadius"], "height": f["Height"],
            "sx": f["Slope"]["x"], "sz": f["Slope"]["z"], "path": path,
        })
    return out


def height(feats, x, z):
    h = 0.0
    for f in feats:
        k = f["kind"]
        if k in ("Plateau", "Tilt"):
            dx, dz = x - f["cx"], z - f["cz"]
            w = _falloff(math.sqrt(dx * dx + dz * dz), f["inner"], f["outer"])
            if w <= 0:
                continue
            if k == "Plateau":
                h += f["height"] * w
            else:
                h += (f["sx"] * dx + f["sz"] * dz) * w
        elif k == "Shelf":
            if not f["path"] or len(f["path"]) < 2:
                continue
            w = _ramp(signed_distance_left(f["path"], x, z), f["inner"], f["outer"])
            if w > 0:
                h += (f["height"] - h) * w
        elif k == "Bowl":
            if not f["path"] or len(f["path"]) < 3 or not contains(f["path"], x, z):
                continue
            h += f["height"] * _ramp(distance_to_edge(f["path"], x, z), 0, f["inner"])
    return h


# ---------------------------------------------------------------- numpy versions (same maths, arrays of points)
def _np():
    import numpy
    return numpy


def falloff_np(r, inner, outer):
    np = _np()
    t = np.clip((r - inner) / max(outer - inner, 1e-12), 0, 1)
    s = 1 - t
    w = s * s * (3 - 2 * s)
    return np.where(r <= inner, 1.0, np.where(r >= outer, 0.0, w))


def ramp_np(d, start, end):
    np = _np()
    t = np.clip((d - start) / max(end - start, 1e-12), 0, 1)
    return np.where(d <= start, 0.0, np.where(d >= end, 1.0, t * t * (3 - 2 * t)))


def contains_np(poly, x, z):
    np = _np()
    inside = np.zeros(x.shape, dtype=bool)
    n = len(poly)
    j = n - 1
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[j]
        cond = (az > z) != (bz > z)
        with np.errstate(divide="ignore", invalid="ignore"):
            xc = (bx - ax) * (z - az) / (bz - az) + ax
        inside ^= cond & (x < xc)
        j = i
    return inside


def distance_to_edge_np(poly, x, z):
    np = _np()
    best = np.full(x.shape, np.inf)
    n = len(poly)
    j = n - 1
    for i in range(n):
        ax, az = poly[j]
        bx, bz = poly[i]
        abx, abz = bx - ax, bz - az
        l2 = abx * abx + abz * abz
        t = np.clip(((x - ax) * abx + (z - az) * abz) / l2, 0, 1) if l2 > 1e-12 else np.zeros(x.shape)
        d2 = (x - (ax + abx * t)) ** 2 + (z - (az + abz * t)) ** 2
        best = np.minimum(best, d2)
        j = i
    return np.sqrt(best)


def signed_distance_left_np(path, x, z):
    np = _np()
    P = np.asarray(path, dtype=np.float64)
    n = len(P)
    A, B = P[:-1], P[1:]
    AB = B - A
    l2 = (AB ** 2).sum(1)
    keep = l2 > 1e-12
    xs = x.reshape(-1, 1)
    zs = z.reshape(-1, 1)
    t = ((xs - A[:, 0]) * AB[:, 0] + (zs - A[:, 1]) * AB[:, 1]) / np.where(keep, l2, 1)
    idx = np.arange(n - 1)
    start_clamp = (t < 0) & (idx > 0)
    end_clamp = (t > 1) & (idx < n - 2)
    tc = np.where(start_clamp, 0.0, np.where(end_clamp, 1.0, t))
    dx = xs - (A[:, 0] + AB[:, 0] * tc)
    dz = zs - (A[:, 1] + AB[:, 1] * tc)
    d2 = np.where(keep, dx * dx + dz * dz, np.inf)
    k = np.argmin(d2, axis=1)
    rows = np.arange(len(k))
    best = d2[rows, k]
    cross = AB[k, 0] * dz[rows, k] - AB[k, 1] * dx[rows, k]
    sign = np.where(cross >= 0, 1.0, -1.0)
    # vertex cases: averaged left normals of the two segments meeting there
    L = np.sqrt(np.maximum(l2, 1e-24))
    NL = np.stack([-AB[:, 1] / L, AB[:, 0] / L], 1)
    sc = start_clamp[rows, k]
    ec = end_clamp[rows, k]
    v = np.where(sc, k, np.where(ec, k + 1, k))
    vn = NL[np.clip(v - 1, 0, n - 2)] + NL[np.clip(v, 0, n - 2)]
    vs = np.where((x.reshape(-1) - P[v, 0]) * vn[:, 0] + (z.reshape(-1) - P[v, 1]) * vn[:, 1] >= 0, 1.0, -1.0)
    sign = np.where(sc | ec, vs, sign)
    return (sign * np.sqrt(best)).reshape(x.shape)


def height_np(feats, x, z, chunk=8000):
    np = _np()
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    out = np.empty(x.shape)
    fx, fz, fo = x.reshape(-1), z.reshape(-1), out.reshape(-1)
    for s in range(0, fx.size, chunk):
        cx, cz = fx[s:s + chunk], fz[s:s + chunk]
        h = np.zeros(cx.shape)
        for f in feats:
            k = f["kind"]
            if k in ("Plateau", "Tilt"):
                dx, dz = cx - f["cx"], cz - f["cz"]
                w = falloff_np(np.sqrt(dx * dx + dz * dz), f["inner"], f["outer"])
                h = h + (f["height"] * w if k == "Plateau" else (f["sx"] * dx + f["sz"] * dz) * w)
            elif k == "Shelf" and f["path"] and len(f["path"]) >= 2:
                w = ramp_np(signed_distance_left_np(f["path"], cx, cz), f["inner"], f["outer"])
                h = h + (f["height"] - h) * w
            elif k == "Bowl" and f["path"] and len(f["path"]) >= 3:
                inside = contains_np(f["path"], cx, cz)
                if inside.any():
                    d = distance_to_edge_np(f["path"], cx, cz)
                    h = h + np.where(inside, f["height"] * ramp_np(d, 0, f["inner"]), 0.0)
        fo[s:s + chunk] = h
    return out


def catmull_rom(control, samples):
    pts = []
    n = len(control)
    for i in range(n - 1):
        p0, p1, p2, p3 = control[max(0, i - 1)], control[i], control[i + 1], control[min(n - 1, i + 2)]
        for s in range(samples):
            t = s / samples
            t2, t3 = t * t, t * t * t
            pts.append(tuple(0.5 * (2 * p1[c] + (-p0[c] + p2[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t2
                                    + (-p0[c] + 3 * p1[c] - 3 * p2[c] + p3[c]) * t3) for c in range(2)))
    pts.append(tuple(control[-1]))
    return pts


def boundary_rect(hole):
    xs = [p["x"] for p in hole["PlayableBoundary"]]
    zs = [p["z"] for p in hole["PlayableBoundary"]]
    return min(xs), min(zs), max(xs), max(zs)


def ground_grid(hole, margin=30.0, spacing=0.8):
    """Exact vertex extents of CourseVisualBuilder.BuildGroundMesh (float math as in C#)."""
    minx, minz, maxx, maxz = boundary_rect(hole)
    minx -= margin
    minz -= margin
    maxx += margin
    maxz += margin
    nx = math.ceil((maxx - minx) / spacing) + 1
    nz = math.ceil((maxz - minz) / spacing) + 1
    return minx, minz, minx + (nx - 1) * spacing, minz + (nz - 1) * spacing, nx, nz
