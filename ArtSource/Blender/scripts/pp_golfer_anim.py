"""Puttshack Pro golfer body animations, authored procedurally and baked to FK (headless, re-runnable).

  Blender -b ArtSource/Blender/PuttshackPro_GolferRig.blend --factory-startup \
      --python ArtSource/Blender/scripts/pp_golfer_anim.py -- [build] [render] [clips=Swing_Full,Putt] [quick]

No arguments = build + render. The control rig lives in this script: every clip is a set of keyed control channels
(hips, pelvis/spine turn-tilt-bend, clavicles, planted foot controls with heel/toe pivots, knee and elbow poles,
look-at target, finger grip/fist weights) interpolated with clamped Hermite splines. Lead-arm key poses are solved
with analytic two-bone IK against a ClubGrip.L target (club-first keys: address, impact) or against an arm direction
plus a shaft direction with a flat lead wrist (arm-first keys: top, release, finish) and then interpolated in FK, so
the hands travel on arcs. Each frame the legs are re-solved to the foot controls, the trail hand to its slot
0.095 m down the shaft, the head to its look target, and the fingers wrap the grip. The result is written as one
quaternion key per frame on every deform bone of Golfer_Rig (plus Hips location; never Root or ClubGrip.L).
"""
import bisect
import json
import math
import os
import shutil
import subprocess
import sys
import time

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
BLEND_DIR = os.path.join(ROOT, "ArtSource", "Blender")
RIG_JSON = os.path.join(BLEND_DIR, "golfer_rig.json")
OUT_BLEND = os.path.join(BLEND_DIR, "PuttshackPro_GolferAnims.blend")
OUT_JSON = os.path.join(BLEND_DIR, "golfer_anims.json")
REVIEW = os.path.join(ROOT, "ArtSource", "Review", "Characters", "anim")
FFMPEG = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
RIG_NAME = "Golfer_Rig"
FPS = 30
BALL_R = 0.02135
TRAIL_GRIP = 0.095
CLUBS = ("Driver", "Iron", "Wedge", "Putter")
NO_KEYS = ("Root", "ClubGrip.L")
FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")


def log(*a):
    print("[pp_anim]", *a, flush=True)


# ------------------------------------------------------------------ math

def V(x, y, z):
    return Vector((float(x), float(y), float(z)))


def q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


def Rx(d):
    return q_axis((1, 0, 0), d)


def Ry(d):
    return q_axis((0, 1, 0), d)


def Rz(d):
    return q_axis((0, 0, 1), d)


def QI():
    return Quaternion()


def qneg(q):
    return Quaternion((-q.w, -q.x, -q.y, -q.z))


def clamp(x, a, b):
    return a if x < a else b if x > b else x


def smooth(t):
    t = clamp(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def basis_yz(y, z):
    """Rotation matrix whose +Y is y and whose +Z is z orthogonalised against y."""
    y = Vector(y).normalized()
    z = Vector(z)
    z = (z - y * y.dot(z)).normalized()
    return Matrix((y.cross(z), y, z)).transposed()


def frame_sn(s, n):
    n = n - s * s.dot(n)
    n.normalize()
    return Matrix((s, n, s.cross(n))).transposed()


def swing_twist(q, axis):
    """Split q = swing @ twist with twist about axis; returns (twist quaternion, twist angle deg)."""
    p = Vector((q.x, q.y, q.z)).dot(axis)
    w = q.w
    if abs(p) < 1e-9 and abs(w) < 1e-9:
        return QI(), 0.0
    tw = Quaternion((w, axis.x * p, axis.y * p, axis.z * p)).normalized()
    if tw.w < 0:
        tw = qneg(tw)
    ang = 2.0 * math.degrees(math.atan2(Vector((tw.x, tw.y, tw.z)).dot(axis), tw.w))
    return tw, ang


def mat4(pos, q):
    m = q.to_matrix().to_4x4()
    m.translation = pos
    return m


def mirror4(m):
    s = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))
    return s @ m @ s


# ------------------------------------------------------------------ splines

class Track:
    """Hermite spline over keys [(frame, value[, 'flat'|'lin'])]; value float, Vector or Quaternion.

    Tangents: three-point (Bessel) estimate, clamped like Blender's auto-clamped handles (flat at extrema and
    Fritsch-Carlson limited, so there is no overshoot between keys). Quaternions keep tangents through extrema
    (smooth arcs) but are Fritsch-Carlson limited and flat next to holds. Loops use periodic tangents (seamless).
    Clip ends are eased (flat) unless tagged 'lin'.
    """

    def __init__(self, keys, period=None, clamped=True):
        keys = sorted(keys, key=lambda k: k[0])
        dedup = []
        for k in keys:
            if dedup and abs(dedup[-1][0] - k[0]) < 1e-6:
                dedup[-1] = k
            else:
                dedup.append(k)
        keys = dedup
        self.t = [float(k[0]) for k in keys]
        v0 = keys[0][1]
        if isinstance(v0, Quaternion):
            self.kind = "q"
            vals, prev = [], None
            for k in keys:
                q = Quaternion(k[1]).normalized()
                if prev is not None and prev.dot(q) < 0:
                    q = qneg(q)
                vals.append(list(q))
                prev = q
            clamped = False
        elif isinstance(v0, (Vector, tuple, list)):
            self.kind = "v"
            vals = [list(k[1]) for k in keys]
        else:
            self.kind = "f"
            vals = [[float(k[1])] for k in keys]
        self.y = vals
        self.tag = [k[2] if len(k) > 2 else None for k in keys]
        self.period = period
        self.clamped = clamped
        self.n = len(keys)
        self.dim = len(vals[0])
        self.m = self._tangents()

    def _tangents(self):
        t, y, n, dim = self.t, self.y, self.n, self.dim
        m = [[0.0] * dim for _ in range(n)]
        if n < 2:
            return m
        for i in range(n):
            tag = self.tag[i]
            if tag == "flat":
                continue
            if self.period and n > 2:
                ip = i - 1 if i > 0 else n - 2
                tp = t[ip] - (self.period if i == 0 else 0.0)
                inx = i + 1 if i < n - 1 else 1
                tn = t[inx] + (self.period if i == n - 1 else 0.0)
            elif i == 0 or i == n - 1:
                if tag == "lin":
                    j = 1 if i == 0 else n - 2
                    for c in range(dim):
                        m[i][c] = (y[j][c] - y[i][c]) / (t[j] - t[i])
                continue
            else:
                ip, inx, tp, tn = i - 1, i + 1, t[i - 1], t[i + 1]
            h0, h1 = t[i] - tp, tn - t[i]
            if self.kind == "q":
                if (max(abs(y[i][c] - y[ip][c]) for c in range(dim)) < 1e-5
                        or max(abs(y[inx][c] - y[i][c]) for c in range(dim)) < 1e-5):
                    continue
            for c in range(dim):
                d0 = (y[i][c] - y[ip][c]) / h0
                d1 = (y[inx][c] - y[i][c]) / h1
                if tag == "lin":
                    m[i][c] = (d0 + d1) * 0.5
                    continue
                if self.clamped and d0 * d1 <= 0.0:
                    continue
                mm = (h1 * d0 + h0 * d1) / (h0 + h1)
                if self.clamped or self.kind == "q":
                    lim = 3.0 * min(abs(d0), abs(d1))
                    mm = clamp(mm, -lim, lim)
                m[i][c] = mm
        return m

    def __call__(self, f):
        t = self.t
        if self.period:
            f = t[0] + ((f - t[0]) % self.period)
        if f <= t[0] or self.n == 1:
            vals = self.y[0]
        elif f >= t[-1]:
            vals = self.y[-1]
        else:
            i = bisect.bisect_right(t, f) - 1
            h = t[i + 1] - t[i]
            s = (f - t[i]) / h
            s2, s3 = s * s, s * s * s
            a, b, c, d = 2 * s3 - 3 * s2 + 1, s3 - 2 * s2 + s, -2 * s3 + 3 * s2, s3 - s2
            y0, y1, m0, m1 = self.y[i], self.y[i + 1], self.m[i], self.m[i + 1]
            vals = [a * y0[k] + b * h * m0[k] + c * y1[k] + d * h * m1[k] for k in range(self.dim)]
        if self.kind == "q":
            return Quaternion(vals).normalized()
        if self.kind == "v":
            return Vector(vals)
        return vals[0]


# ------------------------------------------------------------------ rig model

class RigModel:
    """Rest data of Golfer_Rig plus pure-Python FK (verified against Blender's pose evaluation)."""

    def __init__(self, ob, rig_json):
        self.ob = ob
        if (ob.matrix_world - Matrix.Identity(4)).to_scale().length > 1e-6 or ob.matrix_world.translation.length > 1e-6:
            raise RuntimeError("Golfer_Rig object transform must be identity")
        bones = ob.data.bones
        order = []

        def visit(b):
            order.append(b.name)
            for c in b.children:
                visit(c)

        for b in bones:
            if b.parent is None:
                visit(b)
        self.order = order
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.rq = {n: m.to_quaternion() for n, m in self.rest.items()}
        self.head = {b.name: b.head_local.copy() for b in bones}
        self.tail = {b.name: b.tail_local.copy() for b in bones}
        self.keyed = [n for n in order if n not in NO_KEYS]
        self.L = {}
        for s in "LR":
            self.L["arm" + s] = ((self.head["LowerArm." + s] - self.head["UpperArm." + s]).length,
                                 (self.tail["LowerArm." + s] - self.head["LowerArm." + s]).length)
            self.L["leg" + s] = ((self.head["LowerLeg." + s] - self.head["UpperLeg." + s]).length,
                                 (self.tail["LowerLeg." + s] - self.head["LowerLeg." + s]).length)
        # Club hold frames: ClubGrip.L relative to Wrist.L, and its mirror for the trail hand.
        self.grip_rel_L = self.rest["Wrist.L"].inverted() @ self.rest["ClubGrip.L"]
        self.grip_rel_R = mirror4(self.grip_rel_L)
        self.rest_grip_R = self.rest["Wrist.R"] @ self.grip_rel_R
        self.trail_slot = Matrix.Translation((0.0, TRAIL_GRIP, 0.0)) @ Matrix.Rotation(math.pi, 4, "Y")
        eyes = bpy.data.objects.get("Ref_high-poly")
        if eyes:
            co = [eyes.matrix_world @ v.co for v in eyes.data.vertices]
            self.eye_rest = sum(co, Vector()) / len(co)
        else:
            self.eye_rest = self.head["Head"] + V(0, -0.09, 0.06)
        self.clubs = self._club_frames(rig_json)
        self.hands = HandPoses(self)

    def _club_frames(self, data):
        """Address frame of each club (sole on z=0, face normal +X, toe -Y) expressed in ClubGrip.L space."""
        out = {}
        rg = self.rest["ClubGrip.L"]
        for name, c in data["clubs"].items():
            lie = math.radians(c["lie"])
            u = V(0, math.cos(lie), math.sin(lie))
            yb = -u
            zb = V(1, 0, 0)
            RB = Matrix((yb.cross(zb), yb, zb)).transposed()
            sole_bone = rg.inverted() @ Vector(c["sole_rest"])
            sole_addr = V(-0.004, -0.05 if name == "Driver" else -0.02, 0.0)
            B = RB.to_4x4()
            B.translation = sole_addr - RB @ sole_bone
            Binv = B.inverted()  # address coords -> grip bone coords
            ob = bpy.data.objects.get("Club_" + name)
            info = {"B": B, "Binv": Binv, "lie": c["lie"], "length": c["length"], "u": u}
            if ob:
                to_addr = B @ rg.inverted()
                pts = [to_addr @ (ob.matrix_world @ v.co) for v in ob.data.vertices]
                info["zmin"] = min(p.z for p in pts)
                head_pts = [p for p in pts if p.z < 0.08]
                info["face_x"] = max(p.x for p in head_pts)
                info["toe_y"] = min(p.y for p in head_pts)
                info["heel_y"] = max(p.y for p in head_pts)
                info["ztop"] = max(p.z for p in head_pts)
            out[name] = info
        loft = {"Driver": 10.5, "Iron": 34.0, "Wedge": 56.0, "Putter": 3.0}
        for name, info in out.items():
            L = math.radians(loft.get(name, 30.0))
            if name == "Driver":
                cy = -0.052
                fc = Matrix.Rotation(-math.radians(10.5) * 0.5, 3, "Y") @ V(-0.058 + 0.058 * 0.82, cy, 0.031)
                n = V(math.cos(L), 0, math.sin(L))
                info["impact"] = fc + n * BALL_R
            else:
                cy = {"Iron": -0.022, "Wedge": -0.022, "Putter": -0.017}[name]
                info["impact"] = V(BALL_R * (1 - math.sin(L)) / math.cos(L), cy, BALL_R)
            info["loft"] = loft.get(name, 30.0)
        return out

    # -- forward kinematics
    def fk(self, D, hips_pos):
        W, P = {}, {}
        par, head = self.parent, self.head
        for n in self.order:
            p = par[n]
            d = D.get(n)
            if p is None:
                W[n] = QI()
                P[n] = head[n].copy()
                continue
            W[n] = W[p] @ d if d is not None else W[p].copy()
            P[n] = P[p] + W[p] @ (head[n] - head[p])
            if n == "Hips":
                P[n] = hips_pos.copy()
        return W, P

    def bone_matrix(self, W, P, n):
        return mat4(P[n], W[n] @ self.rq[n])

    def basis(self, D, n):
        r = self.rq[n]
        return (r.inverted() @ D.get(n, QI()) @ r).normalized()


class Pose:
    def __init__(self, rm):
        self.rm = rm
        self.D = {n: QI() for n in rm.order}
        self.hips = rm.head["Hips"].copy()
        self.W = self.P = None
        self.diag = {}

    def update(self):
        self.W, self.P = self.rm.fk(self.D, self.hips)
        return self.W, self.P

    def set_world(self, n, Wd):
        """Set bone n so its world rotation delta (posed = Wd @ rest) is Wd (parent must be up to date)."""
        p = self.rm.parent[n]
        self.D[n] = (self.W[p].inverted() @ Wd).normalized()

    def copy_from(self, other):
        self.D = {k: v.copy() for k, v in other.D.items()}
        self.hips = other.hips.copy()


# ------------------------------------------------------------------ IK

def q_pow(q, k):
    if q.w < 0:
        q = -q
    axis, ang = q.to_axis_angle()
    return Quaternion(axis, ang * k) if ang > 1e-9 else QI()


def two_bone(pose, upper, lower, target, bend, rest_bend, soft=0.9985):
    """Analytic two-bone IK; bend = world direction the middle joint points (knee cap / olecranon).
    Returns reach deficit (m) if the target is out of reach."""
    rm = pose.rm
    W, P = pose.update()
    root = P[upper]
    L1 = (rm.head[lower] - rm.head[upper]).length
    L2 = (rm.tail[lower] - rm.head[lower]).length
    v = target - root
    d = v.length
    a = v / d
    lim = (L1 + L2) * soft
    deficit = max(0.0, d - lim)
    d = clamp(d, abs(L1 - L2) + 1e-4, lim)
    b = bend - a * a.dot(bend)
    if b.length < 1e-6:
        b = a.orthogonal()
    b.normalize()
    ca = clamp((L1 * L1 + d * d - L2 * L2) / (2 * L1 * d), -1.0, 1.0)
    sa = math.sqrt(max(0.0, 1 - ca * ca))
    mid = root + a * (L1 * ca) + b * (L1 * sa)
    end = root + a * d
    s1, s2 = (mid - root).normalized(), (end - mid).normalized()
    ar = (rm.tail[lower] - rm.head[upper]).normalized()
    nr = ar.cross(rest_bend).normalized()
    n = a.cross(b).normalized()
    s1r = (rm.head[lower] - rm.head[upper]).normalized()
    s2r = (rm.tail[lower] - rm.head[lower]).normalized()
    Wu = (frame_sn(s1, n) @ frame_sn(s1r, nr).transposed()).to_quaternion()
    Wl = (frame_sn(s2, n) @ frame_sn(s2r, nr).transposed()).to_quaternion()
    pose.D[upper] = (W[rm.parent[upper]].inverted() @ Wu).normalized()
    pose.D[lower] = (Wu.inverted() @ Wl).normalized()
    return deficit


ARM_REST_BEND = V(0, 1, 0)   # T-pose palms down: elbows bend so the forearms swing forward (-Y)
LEG_REST_BEND = V(0, -1, 0)  # knee caps forward


def arm_ik(pose, s, wrist_m, pole, twist_share=0.65, auto_clav=True):
    """Place Wrist.<s> at wrist_m (armature-space matrix of the posed wrist bone)."""
    rm = pose.rm
    tgt = wrist_m.translation
    up, lo, wr, cl = "UpperArm." + s, "LowerArm." + s, "Wrist." + s, "Shoulder." + s
    if auto_clav:
        W, P = pose.update()
        L1, L2 = rm.L["arm" + s]
        lim = (L1 + L2) * 0.995
        d0 = (tgt - P[up]).length
        if d0 > lim:
            piv = P[cl]
            cvec = P[up] - piv
            axis = cvec.cross(tgt - piv)
            if axis.length > 1e-6:
                axis.normalize()
                base = pose.D[cl].copy()
                lo_a, hi_a = 0.0, 28.0
                for _ in range(22):
                    mid = 0.5 * (lo_a + hi_a)
                    e = q_axis(axis, mid)
                    pos = piv + e @ cvec
                    if (tgt - pos).length > lim:
                        lo_a = mid
                    else:
                        hi_a = mid
                e = q_axis(axis, hi_a)
                Wc = e @ W[cl]
                pose.D[cl] = (W["Chest"].inverted() @ Wc).normalized()
                pose.diag["clav_" + s] = hi_a
                del base
    deficit = two_bone(pose, up, lo, tgt, pole, ARM_REST_BEND)
    W, P = pose.update()
    Wh = wrist_m.to_quaternion() @ rm.rq[wr].inverted()
    Dw = (W[lo].inverted() @ Wh).normalized()
    axis = (rm.tail[lo] - rm.head[lo]).normalized()
    tw, ang = swing_twist(Dw, axis)
    share = q_axis(axis, ang * twist_share) if abs(ang) > 1e-4 else QI()
    pose.D[lo] = (pose.D[lo] @ share).normalized()
    pose.D[wr] = (share.inverted() @ Dw).normalized()
    pose.diag["twist_" + s] = ang
    pose.diag["deficit_" + s] = deficit
    return deficit


def wrist_angles(rm, D, s):
    """Anatomical (flex, radial dev, twist) of Wrist.<s> local rotation, degrees."""
    axis = (rm.tail["LowerArm." + s] - rm.head["LowerArm." + s]).normalized()
    tw, ang = swing_twist(D, axis)
    sw = D @ tw.inverted()
    f = sw @ axis
    sx = 1.0 if s == "L" else -1.0
    flex = math.degrees(math.atan2(-f.z, f.x * sx))
    dev = math.degrees(math.atan2(-f.y, f.x * sx))
    return flex, dev, ang


# ------------------------------------------------------------------ hands

class HandPoses:
    """Finger poses (bone-relative deltas): relaxed, fist and wrapped around the club grip for each hand."""

    def __init__(self, rm):
        self.rm = rm
        self.relax, self.fist, self.grip = {}, {}, {}
        for s in "LR":
            self.relax[s] = self._curl(s, {"Index": (8, 14, 10), "Middle": (12, 18, 12), "Ring": (16, 22, 14),
                                           "Pinky": (20, 26, 16)}, thumb=(6, 8, 6))
            self.fist[s] = self._curl(s, {"Index": (78, 95, 55), "Middle": (82, 98, 55), "Ring": (86, 98, 55),
                                          "Pinky": (90, 95, 50)}, thumb=(28, 30, 35))
            self.grip[s] = self._wrap(s)

    def _chain(self, s, f):
        return [f"{f}{i}.{s}" for i in (1, 2, 3)]

    def _axis(self, b):
        rm = self.rm
        d = (rm.tail[b] - rm.head[b]).normalized()
        return d.cross(V(0, 0, -1)).normalized()

    def _curl(self, s, table, thumb):
        out = {}
        for f, angs in table.items():
            for b, a in zip(self._chain(s, f), angs):
                out[b] = q_axis(self._axis(b), a)
        tc = self._chain(s, "Thumb")
        for b, a in zip(tc, thumb):
            # thumb flexes toward the palm / index side
            rm = self.rm
            d = (rm.tail[b] - rm.head[b]).normalized()
            toward = (rm.head["Middle1." + s] - rm.head[b])
            ax = d.cross(toward).normalized() if d.cross(toward).length > 1e-6 else self._axis(b)
            out[b] = q_axis(ax, a)
        return out

    def _wrap(self, s):
        rm = self.rm
        G = rm.rest["ClubGrip.L"] if s == "L" else rm.rest_grip_R
        c = G.translation
        u = G.to_3x3() @ V(0, 1, 0)
        gx = G.to_3x3() @ V(1, 0, 0)
        gz = G.to_3x3() @ V(0, 0, 1)
        grip_r = 0.0125 if s == "L" else 0.0116
        out = {}

        def dist(p):
            w = p - c
            return (w - u * u.dot(w)).length

        for f in ("Index", "Middle", "Ring", "Pinky"):
            chain = self._chain(s, f)
            rho = grip_r + {"Index": 0.0085, "Middle": 0.0088, "Ring": 0.0084, "Pinky": 0.0076}[f]
            Wc = QI()
            parent_head = rm.head[chain[0]]
            for b in chain:
                h = parent_head
                seg = rm.tail[b] - rm.head[b]
                ax = self._axis(b)
                cap = {1: 80, 2: 100, 3: 75}[int(b[len(f)])]

                def dd(i):
                    return dist(h + (Wc @ q_axis(ax, i)) @ seg)

                if dd(0) <= rho:
                    ang = 0
                    while ang > -30 and dd(ang) < rho:
                        ang -= 1
                else:
                    ang, best_d = 0, 1e9
                    for i in range(0, cap + 1):
                        d_i = dd(i)
                        if d_i < best_d:
                            best_d, ang = d_i, i
                        if d_i <= rho:
                            ang = i
                            break
                D = q_axis(ax, ang)
                out[b] = D
                Wc = Wc @ D
                parent_head = h + Wc @ seg
        # Thumb: lies along the shaft, on top and toward the trail-hand side (lead) / target side (trail).
        # rest_grip_R is the mirror frame of ClubGrip.L, so the mirror of the lead thumb's +X is its -X.
        rdir = (gx - gz * 0.45).normalized() if s == "L" else (rm.rest_grip_R.to_3x3() @ V(-1, 0, -0.45)).normalized()
        rho_t = grip_r + 0.0112
        tc = self._chain(s, "Thumb")
        Wc = QI()
        h = rm.head[tc[0]]
        s0 = u.dot(h - c)
        aims = [c + u * (s0 + 0.030) + rdir * rho_t, None, None]
        for k, b in enumerate(tc):
            seg = rm.tail[b] - rm.head[b]
            cur = (Wc @ seg).normalized()
            if k == 0:
                want = (aims[0] - h).normalized()
            else:
                w = c + u * (u.dot(h - c) + 0.03) + rdir * (rho_t - 0.002 * k)
                want = (w - h).normalized()
            dq = cur.rotation_difference(want)
            ang = math.degrees(dq.angle)
            if ang > 70:
                dq = Quaternion(dq.axis, math.radians(70))
            Wn = dq @ Wc
            out[b] = (Wc.inverted() @ Wn).normalized()
            Wc = Wn
            h = h + Wc @ seg
        return out

    def blend(self, s, grip, fist):
        out = {}
        for b, r in self.relax[s].items():
            q = r
            if grip > 1e-4:
                q = q.slerp(self.grip[s].get(b, r), grip)
            if fist > 1e-4:
                q = q.slerp(self.fist[s].get(b, r), fist)
            out[b] = q
        return out


# ------------------------------------------------------------------ clips / channels

class Clip:
    """Keyed control channels for one clip. Channel values: float, Vector or Quaternion.

    Body:  hips (world pos of the Hips head), pelvis (turn, tilt fwd, side bend) deg, spine (turn, flex, side) deg,
           clavL/clavR (elevation, protraction) deg.
    Legs:  footL/footR (ankle ground x, y, yaw deg), heel* (heel-lift deg, ball/toe pivots), roll* (outside edge deg),
           lift* (step height m), knee* (knee-cap direction).
    Head:  look (world target), look_w, nod/htilt/hturn (deg, head-local), neck (neutral flex deg),
           headlock (0..1: spine side/flex corrections hold the neck base at its frame-0 spot + headoff).
    Arms:  arm<s>.cl/.up/.lo/.wr (FK quaternions, filled from solved arm keys), elbowR/elbowL (trail IK pole),
           rclub (1 = trail hand on the club via IK, 0 = trail arm FK), grip*/fist* finger weights.
    """

    def __init__(self, name, length, loop=False, club=None, contact=None, ball=None, notes=""):
        self.name, self.length, self.loop, self.club = name, int(length), loop, club
        self.contact, self.ball, self.notes = contact, ball, notes
        self.keys = {}
        self.arm_keys = {"L": [], "R": []}
        self.tracks = {}
        self.events = []
        self.head_ref = None
        self.neck_ref = None

    def key(self, ch, f, v, tag=None):
        if isinstance(v, (tuple, list)):
            v = Vector(v)
        self.keys.setdefault(ch, []).append((f, v, tag) if tag else (f, v))

    def pose(self, f, d, tag=None):
        for ch, v in d.items():
            self.key(ch, f, v, tag)

    def arm(self, s, f, spec):
        self.arm_keys[s].append((f, spec))

    def build_tracks(self):
        per = self.length if self.loop else None
        self.tracks = {ch: Track(k, period=per) for ch, k in self.keys.items()}

    def val(self, ch, f, default=None):
        tr = self.tracks.get(ch)
        return default if tr is None else tr(f)

    def inherit(self, src, src_frame, frame=0, skip=()):
        for ch in src.keys:
            if ch not in skip:
                self.key(ch, frame, src.val(ch, src_frame))

    def hold(self, frame, chans, f_from=None):
        """Re-key channels at `frame` with their current value at f_from (or their last key)."""
        self.build_tracks()
        for ch in chans:
            if ch in self.tracks:
                self.key(ch, frame, self.val(ch, f_from if f_from is not None else frame))


def chest_q(pose):
    return pose.W["Chest"]


def solve_body(rm, clip, f, lock_ok=True):
    pose = Pose(rm)
    val = clip.val
    pose.hips = val("hips", f, rm.head["Hips"].copy())
    pv = val("pelvis", f, V(0, 0, 0))
    sp = val("spine", f, V(0, 0, 0))
    set_spine(pose, pv, sp)
    for s, sx in (("L", 1.0), ("R", -1.0)):
        c = val("clav" + s, f, V(0, 0, 0))
        pose.D["Shoulder." + s] = (Rz(-sx * c.y) @ Ry(-sx * c.x)).normalized()
    pose.update()
    lock = val("headlock", f, 0.0) if lock_ok else 0.0
    if lock > 1e-3:
        if clip.head_ref is None:
            p0 = solve_body(rm, clip, 0, lock_ok=False)
            clip.head_ref = skull(p0)
            clip.neck_ref = p0.W["Neck"] @ (rm.tail["Neck"] - rm.head["Neck"]).normalized()
        tgt = clip.head_ref + val("headoff", f, V(0, 0, 0))

        def neck(o):
            set_spine(pose, pv, sp + V(0, o[0], o[1]))
            pose.update()
            solve_head(pose, clip, f)
            return skull(pose)

        o = [0.0, 0.0]
        for _ in range(4):
            p0 = neck(o)
            e = (tgt - p0).to_2d()
            if e.length < 2e-4:
                break
            jt = (neck((o[0] + 1.0, o[1])) - p0).to_2d()
            js = (neck((o[0], o[1] + 1.0)) - p0).to_2d()
            det = jt.x * js.y - js.x * jt.y
            if abs(det) < 1e-9:
                break
            o[0] = clamp(o[0] + (e.x * js.y - js.x * e.y) / det, -25.0, 25.0)
            o[1] = clamp(o[1] + (jt.x * e.y - e.x * jt.y) / det, -25.0, 25.0)
        pose.diag["headfix"] = (o[0] * lock, o[1] * lock)
        neck((o[0] * lock, o[1] * lock))
    for s in "LR":
        solve_leg(pose, clip, f, s)
    solve_head(pose, clip, f)
    return pose


def skull(pose):
    W, P = pose.update()
    rm = pose.rm
    return P["Head"] + W["Head"] @ ((rm.tail["Head"] - rm.head["Head"]) * 0.5)


def set_spine(pose, pv, sp):
    """Pelvis turns about the vertical; spine bends are world-referenced so the chest turns about its own axis. The pelvis->chest delta is spread over the three spine bones."""
    Wh = (Rz(pv.x) @ Ry(pv.z) @ Rx(pv.y)).normalized()
    pose.D["Hips"] = Wh
    Wc = (Rx(pv.y + sp.y) @ Ry(pv.z + sp.z) @ Rz(pv.x + sp.x)).normalized()
    Dt = (Wh.inverted() @ Wc).normalized()
    for b, k in (("Abdomen", 0.28), ("Torso", 0.32), ("Chest", 0.40)):
        pose.D[b] = q_pow(Dt, k)


def foot_targets(rm, clip, f, s):
    """Foot/toe world rotations and ankle position from the foot controls. footX = (x, y, yaw) of the ankle's
    ground point for the flat foot; yawt = extra yaw pivoting on the ball of the foot; roll pivots on the outside
    edge; heel lifts pivot on the ball of the foot up to 50 deg and then on the toe tip."""
    sx = 1.0 if s == "L" else -1.0
    A_r, B_r, T_r = rm.head["Foot." + s], rm.tail["Foot." + s], rm.tail["Toes." + s]
    P_r = V(A_r.x, A_r.y, 0.0)
    ft = clip.val("foot" + s, f, V(A_r.x, A_r.y, 0.0))
    heel = clip.val("heel" + s, f, 0.0)
    roll = clip.val("roll" + s, f, 0.0)
    lift = clip.val("lift" + s, f, 0.0)
    yawt = clip.val("yawt" + s, f, 0.0)
    Rf = Rz(ft.z)
    ground = V(ft.x, ft.y, lift)
    Bg = ground + Rf @ (V(B_r.x, B_r.y, 0.0) - P_r)
    Rt = Rz(yawt)

    def place(p):
        return Bg + Rt @ (ground + Rf @ (p - P_r) - Bg)

    R0 = Rt @ Rf
    E_r = V(A_r.x + sx * 0.042, A_r.y - 0.03, 0.0)
    Rb = R0 @ Ry(sx * roll)
    E_p = place(E_r)
    T_p = E_p + Rb @ (T_r - E_r)
    phi_b = min(heel, 50.0)
    phi_t = heel - phi_b
    Wt = Rb @ Rx(phi_t)
    B_p = T_p + Wt @ (B_r - T_r)
    Wf = Rb @ Rx(heel)
    A_p = B_p + Wf @ (A_r - B_r)
    return A_p, Wf, Wt, R0


def solve_leg(pose, clip, f, s):
    rm = pose.rm
    sx = 1.0 if s == "L" else -1.0
    A_p, Wf, Wt, Rf = foot_targets(rm, clip, f, s)
    pole = clip.val("knee" + s, f, None)
    if pole is None:
        pole = Rf @ V(sx * 0.15, -1, 0)
    pose.diag["legdef_" + s] = two_bone(pose, "UpperLeg." + s, "LowerLeg." + s, A_p, Vector(pole), LEG_REST_BEND,
                                        soft=0.9975)
    pose.update()
    pose.set_world("Foot." + s, Wf)
    pose.update()
    pose.set_world("Toes." + s, Wt)


def solve_head(pose, clip, f):
    rm = pose.rm
    W, P = pose.update()
    look = clip.val("look", f, None)
    lw = clip.val("look_w", f, 1.0 if look is not None else 0.0)
    nod = clip.val("nod", f, 0.0)
    tilt = clip.val("htilt", f, 0.0)
    turn = clip.val("hturn", f, 0.0)
    neutral = clip.val("neck", f, 0.0)
    Wc = W["Chest"]
    Dtot = Rx(neutral)
    if look is not None and lw > 1e-4:
        Wref = Wc @ Dtot
        eye = P["Neck"] + Wref @ (rm.eye_rest - rm.head["Neck"])
        fwd = Wref @ V(0, -1, 0)
        dq = fwd.rotation_difference((Vector(look) - eye).normalized())
        Dtot = Wc.inverted() @ QI().slerp(dq, clamp(lw, 0, 1)) @ Wref
    Dtot = Dtot @ Rz(turn) @ Ry(tilt) @ Rx(nod)
    Dn = QI().slerp(Dtot, 0.42)
    lock = clip.val("headlock", f, 0.0)
    if lock > 1e-3 and clip.neck_ref is not None:
        ax = (rm.tail["Neck"] - rm.head["Neck"]).normalized()
        a_t = (Wc @ ax).slerp(clip.neck_ref, clamp(lock, 0, 1)) if hasattr(Vector, "slerp") else clip.neck_ref
        Sw = (Wc @ ax).rotation_difference(a_t.normalized())
        S = (Wc.inverted() @ Sw @ Wc).normalized()
        _, tw = swing_twist((S.inverted() @ Dtot).normalized(), ax)
        Dn = S @ q_axis(ax, tw * 0.5)
    pose.D["Neck"] = Dn.normalized()
    pose.D["Head"] = (Dn.inverted() @ Dtot).normalized()
    pose.update()


def anat_wrist(rm, s, flex, dev, twist):
    """Wrist local delta from anatomical angles (deg): flex (+ palm-ward), dev (+ radial/thumb-ward), twist."""
    sx = 1.0 if s == "L" else -1.0
    axis = (rm.tail["LowerArm." + s] - rm.head["LowerArm." + s]).normalized()
    return (Rz(-sx * dev) @ Ry(sx * flex) @ q_axis(axis, twist)).normalized()


def club_matrix(rm, pose):
    W, P = pose.W, pose.P
    return rm.bone_matrix(W, P, "Wrist.L") @ rm.grip_rel_L


def solve_arm_spec(rm, pose, s, spec):
    """Solve one arm key in the context of `pose` (body already posed). Returns (cl, up, lo, wr) deltas."""
    W, P = pose.update()
    Wc = W["Chest"]
    up, lo, wr, cl = "UpperArm." + s, "LowerArm." + s, "Wrist." + s, "Shoulder." + s
    if "D" in spec:
        for b, q in zip((cl, up, lo, wr), spec["D"]):
            pose.D[b] = q.copy()
        pose.update()
        return tuple(pose.D[b].copy() for b in (cl, up, lo, wr))
    if "clav" in spec:
        sx = 1.0 if s == "L" else -1.0
        e, p = spec["clav"]
        pose.D[cl] = (Rz(-sx * p) @ Ry(-sx * e)).normalized()
        W, P = pose.update()
    world_pole = spec.get("pole_w")
    if spec.get("pole") == "auto" or world_pole == "auto":
        best = None
        base = {b: pose.D[b].copy() for b in (cl, up, lo, wr)}
        sub = dict(spec)
        sub.pop("pole", None)
        for i in range(36):
            a = 2 * math.pi * i / 36
            d0 = (P[wr] - P[up]).normalized()
            ref = d0.orthogonal().normalized()
            sub["pole_w"] = q_axis(d0, math.degrees(a)) @ ref
            for b, q in base.items():
                pose.D[b] = q.copy()
            pose.update()
            solve_arm_spec(rm, pose, s, sub)
            tw = abs(pose.diag.get("twist_" + s, 0.0))
            bias = 0.0
            if "pole_bias" in spec:
                bias = (1.0 - sub["pole_w"].normalized().dot(Vector(spec["pole_bias"]).normalized())) * 25.0
            score = tw + bias
            if best is None or score < best[0]:
                best = (score, sub["pole_w"].copy())
        for b, q in base.items():
            pose.D[b] = q.copy()
        pose.update()
        sub["pole_w"] = best[1]
        return solve_arm_spec(rm, pose, s, sub)
    pole = Vector(world_pole) if world_pole is not None else Wc @ Vector(spec.get("pole", (0.0, 1.0, 0.0)))
    rel = rm.grip_rel_L if s == "L" else rm.grip_rel_R
    if "club" in spec:
        G = spec["club"]
        wm = G @ rm.grip_rel_L.inverted() if s == "L" else G @ rm.trail_slot @ rm.grip_rel_R.inverted()
        arm_ik(pose, s, wm, pole)
    else:
        L1, L2 = rm.L["arm" + s]
        if "pos" in spec:
            wpos = Vector(spec["pos"])
        elif "pos_bone" in spec:
            bn, off = spec["pos_bone"]
            wpos = P[bn] + W[bn] @ Vector(off)
        elif "aim" in spec:
            d = (Vector(spec["aim"]) - P[up]).normalized()
            wpos = P[up] + d * (L1 + L2) * spec.get("reach", 0.995)
        else:
            d = Vector(spec["dir_w"]).normalized() if "dir_w" in spec else Wc @ Vector(spec["dir"]).normalized()
            wpos = P[up] + d * (L1 + L2) * spec.get("reach", 0.996)
        two_bone(pose, up, lo, wpos, pole, ARM_REST_BEND)
        W, P = pose.update()
        fdir = (P[wr] - P[lo]).normalized()
        if "shaft" in spec or "shaft_c" in spec or "hinge" in spec:
            if "hinge" in spec:
                h, ref = spec["hinge"]
                e = Vector(ref) - fdir * fdir.dot(Vector(ref))
                Y = (fdir * math.cos(math.radians(h)) + e.normalized() * math.sin(math.radians(h))).normalized()
            elif "shaft" in spec:
                Y = Vector(spec["shaft"]).normalized()
            else:
                Y = (Wc @ Vector(spec["shaft_c"])).normalized()
            n = fdir.cross(Y)
            Z = (-n if s == "L" else n).normalized()
            k = Z.cross(fdir).normalized()
            Rg = basis_yz(Y, Z)
            Rg = q_axis(k, spec.get("flex", 0.0)).to_matrix() @ Rg
            if "roll" in spec:
                Rg = q_axis(Y, spec["roll"]).to_matrix() @ Rg
            Rw = Rg @ rel.to_3x3().inverted()
        elif "hand_w" in spec:
            Rw = (spec["hand_w"] @ rm.rq[wr]).to_matrix()
        else:
            fl, dv, tw = spec.get("wrist", (0.0, 0.0, 0.0))
            Rw = (W[lo] @ anat_wrist(rm, s, fl, dv, tw) @ rm.rq[wr]).to_matrix()
        wm = Rw.to_4x4()
        wm.translation = wpos
        arm_ik(pose, s, wm, pole, auto_clav=False)
    pose.update()
    return tuple(pose.D[b].copy() for b in (cl, up, lo, wr))


ARM_BONES = (".cl", ".up", ".lo", ".wr")


def arm_dir(flex, add, s="L"):
    """Chest-frame arm direction: flex = forward elevation from hanging (deg), add = across the chest (deg)."""
    sx = 1.0 if s == "L" else -1.0
    return Rz(-sx * add) @ Rx(-flex) @ V(0, 0, -1)


def solve_arm_keys(rm, clip):
    clip.build_tracks()
    for s in "LR":
        for f, spec in clip.arm_keys[s]:
            pose = solve_body(rm, clip, f)
            if s == "R" and clip.arm_keys["L"]:
                apply_arm_tracks(rm, clip, pose, "L", f)
            res = solve_arm_spec(rm, pose, s, spec)
            for suf, q in zip(ARM_BONES, res):
                clip.key("arm" + s + suf, f, q, spec.get("tag"))
        clip.build_tracks()


def apply_arm_tracks(rm, clip, pose, s, f):
    if ("arm" + s + ".up") not in clip.tracks:
        return False
    for suf, b in zip(ARM_BONES, ("Shoulder.", "UpperArm.", "LowerArm.", "Wrist.")):
        pose.D[b + s] = clip.val("arm" + s + suf, f)
    pose.update()
    return True


def solve_frame(rm, clip, f):
    pose = solve_body(rm, clip, f)
    apply_arm_tracks(rm, clip, pose, "L", f)
    G = club_matrix(rm, pose) if clip.club else None
    has_fk_r = apply_arm_tracks(rm, clip, pose, "R", f)
    w = clip.val("rclub", f, 1.0 if clip.club else 0.0)
    if G is not None and w > 1e-4:
        fk = {b: pose.D[b].copy() for b in ("Shoulder.R", "UpperArm.R", "LowerArm.R", "Wrist.R")} if has_fk_r else None
        c = clip.val("clavR", f, V(0, 0, 0))
        pose.D["Shoulder.R"] = (Rz(c.y) @ Ry(c.x)).normalized()
        pose.update()
        pole = clip.val("elbowR", f, V(-0.3, 0.6, -0.7))
        wm = G @ rm.trail_slot @ rm.grip_rel_R.inverted()
        arm_ik(pose, "R", wm, Vector(pole))
        if fk and w < 1.0:
            for b, q in fk.items():
                pose.D[b] = q.slerp(pose.D[b], smooth(w))
        pose.update()
    for s in "LR":
        g = clip.val("grip" + s, f, (1.0 if s == "L" else smooth(clamp(w, 0.0, 1.0))) if clip.club else 0.0)
        fi = clip.val("fist" + s, f, 0.0)
        for b, q in rm.hands.blend(s, g, fi).items():
            pose.D[b] = q
    pose.update()
    pose.G = club_matrix(rm, pose) if clip.club else None
    return pose


# ------------------------------------------------------------------ club helpers

def club_pose(rm, club, ball, lean=0.0, face=0.0, lie_d=0.0, back=0.0, lift=0.0, yaw=0.0, path_tilt=0.0):
    """ClubGrip.L world matrix that puts the club's ball-contact point on `ball` (offset by back/lift).
    lean: forward shaft lean deg (hands toward target), face: open(+)/closed(-) deg about the shaft,
    lie_d: shaft more upright (+) deg, yaw: rotation about vertical (club path) deg."""
    c = rm.clubs[club]
    lie = math.radians(c["lie"])
    u = c["u"]
    xb = V(0, -math.sin(lie), math.cos(lie))
    R = (Rz(yaw) @ q_axis(xb, -lean) @ q_axis(V(1, 0, 0), lie_d) @ q_axis(u, -face) @ q_axis(V(0, 1, 0), path_tilt))
    imp = c["impact"]
    A = mat4(Vector(ball) + V(-back, 0, lift) - R @ imp, R)
    return A @ c["B"]


def club_soled(rm, club, ball, **kw):
    """club_pose with the lift chosen so the sole rests on the ground (teed ball / address)."""
    kw.pop("lift", None)
    G = club_pose(rm, club, ball, **kw)
    sole = club_points(rm, club, G)[1]
    return club_pose(rm, club, ball, lift=-sole.z, **kw)


def club_points(rm, club, G):
    """World positions of the club's contact point and sole (for diagnostics/review)."""
    c = rm.clubs[club]
    A = G @ c["Binv"]
    return A @ c["impact"], A @ V(-0.004, -0.02, 0.0), A.to_3x3() @ V(1, 0, 0)


# ------------------------------------------------------------------ bake

def bake_clip(rm, clip, actions=True):
    ob = rm.ob
    t0 = time.time()
    solve_arm_keys(rm, clip)
    N = clip.length + 1
    poses = [solve_frame(rm, clip, f) for f in range(N)]
    clip.poses = poses
    if not actions:
        return poses
    act = bpy.data.actions.get(clip.name)
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(clip.name)
    act.use_fake_user = True
    ob.animation_data_create()
    ob.animation_data.action = act
    for b in rm.keyed:
        path = 'pose.bones["%s"].rotation_quaternion' % b
        qs, prev = [], None
        for p in poses:
            q = rm.basis(p.D, b)
            if prev is not None and prev.dot(q) < 0:
                q = qneg(q)
            qs.append(q)
            prev = q
        for i in range(4):
            fc = act.fcurve_ensure_for_datablock(ob, path, index=i, group_name=b)
            fc.keyframe_points.add(N)
            co = []
            for fr, q in enumerate(qs):
                co += [float(fr), q[i]]
            fc.keyframe_points.foreach_set("co", co)
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR"
            fc.update()
    rq = rm.rq["Hips"].inverted()
    locs = [rq @ (p.hips - rm.head["Hips"]) for p in poses]
    for i in range(3):
        fc = act.fcurve_ensure_for_datablock(ob, 'pose.bones["Hips"].location', index=i, group_name="Hips")
        fc.keyframe_points.add(N)
        co = []
        for fr, l in enumerate(locs):
            co += [float(fr), l[i]]
        fc.keyframe_points.foreach_set("co", co)
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
        fc.update()
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, clip.length
    act.use_cyclic = bool(clip.loop)
    log("baked %-13s %3d frames in %.1fs" % (clip.name, N, time.time() - t0))
    return poses


# ------------------------------------------------------------------ diagnostics

def joint_angle(P, a, b, c):
    u, v = (P[b] - P[a]).normalized(), (P[c] - P[b]).normalized()
    return math.degrees(u.angle(v))


def describe(rm, clip, f, pose=None):
    pose = pose or clip.poses[f]
    P, W = pose.P, pose.W
    kL = joint_angle(P, "UpperLeg.L", "LowerLeg.L", "Foot.L")
    kR = joint_angle(P, "UpperLeg.R", "LowerLeg.R", "Foot.R")
    eL = joint_angle(P, "UpperArm.L", "LowerArm.L", "Wrist.L")
    eR = joint_angle(P, "UpperArm.R", "LowerArm.R", "Wrist.R")
    wl = wrist_angles(rm, pose.D["Wrist.L"], "L")
    wr = wrist_angles(rm, pose.D["Wrist.R"], "R")
    sh = P["UpperArm.L"] - P["UpperArm.R"]
    hp = P["UpperLeg.L"] - P["UpperLeg.R"]
    sh_turn = math.degrees(math.atan2(sh.y, sh.x))
    hip_turn = math.degrees(math.atan2(hp.y, hp.x))
    trunk = (P["Neck"] - P["Hips"])
    spine_tilt = math.degrees(math.atan2(math.hypot(trunk.x, trunk.y), trunk.z))
    eye = P["Head"] + W["Head"] @ (rm.eye_rest - rm.head["Head"])
    dc = W["Chest"].inverted() @ (P["Wrist.L"] - P["UpperArm.L"]).normalized()
    a_flex = math.degrees(math.atan2(-dc.y, -dc.z))
    a_add = math.degrees(math.atan2(-dc.x, math.hypot(dc.y, dc.z)))
    dR = (P["Wrist.R"] - P["UpperArm.R"]).length / sum(rm.L["armR"])
    s = ("f%5.1f armc fl%5.0f ad%5.0f rR%.2f | knee L%5.1f R%5.1f | elbow L%5.1f R%5.1f | wristL fl%6.1f dv%6.1f tw%6.1f | wristR fl%6.1f dv%6.1f "
         "tw%6.1f | sh%6.1f hip%6.1f tilt%5.1f | eye(%.3f %.3f %.3f) hips(%.3f %.3f %.3f)"
         % (f, a_flex, a_add, dR, kL, kR, eL, eR, *wl, *wr, sh_turn, hip_turn, spine_tilt, *eye, *pose.hips))
    if pose.G is not None:
        imp, sole, face = club_points(rm, clip.club, pose.G)
        shaft = pose.G.to_3x3() @ V(0, 1, 0)
        fore = (P["Wrist.L"] - P["LowerArm.L"]).normalized()
        g = pose.G.translation
        s += (" | imp(%.3f %.3f %.3f) grip(%.2f %.2f %.2f) shaft(%.2f %.2f %.2f) face(%.2f %.2f %.2f) hinge %.0f"
              % (*imp, *g, *shaft, *face, math.degrees(fore.angle(shaft))))
    flags = [k + "=%.3f" % v for k, v in pose.diag.items() if (k.startswith("deficit") or k.startswith("legdef")) and v > 1e-4]
    flags += [k + "=%.1f" % v for k, v in pose.diag.items() if k.startswith("clav")]
    if flags:
        s += " | " + " ".join(flags)
    return s


def verify_fk(rm, clip, frames):
    """Compare the pure-Python FK with Blender's evaluation of the baked action."""
    ob = rm.ob
    ob.animation_data.action = bpy.data.actions[clip.name]
    scn = bpy.context.scene
    worst = 0.0
    for f in frames:
        scn.frame_set(f)
        bpy.context.view_layer.update()
        P = clip.poses[f].P
        W = clip.poses[f].W
        for n in rm.order:
            pb = ob.pose.bones[n]
            worst = max(worst, (pb.head - P[n]).length,
                        (pb.tail - (P[n] + W[n] @ (rm.tail[n] - rm.head[n]))).length)
    return worst


def foot_slide(rm, clip):
    """Max ankle travel per foot while the foot channel is planted (heel/lift zero)."""
    out = {}
    for s in "LR":
        ref, worst = None, 0.0
        for f, p in enumerate(clip.poses):
            planted = abs(clip.val("heel" + s, f, 0.0)) < 1e-3 and abs(clip.val("lift" + s, f, 0.0)) < 1e-4
            if not planted:
                ref = None
                continue
            toe = p.P["Toes." + s]
            if ref is None:
                ref = toe.copy()
            worst = max(worst, (toe - ref).length)
        out[s] = worst
    return out


# ------------------------------------------------------------------ review renders

class Review:
    def __init__(self, rm):
        self.rm = rm
        scn = bpy.context.scene
        self.scn = scn
        self.coll = bpy.data.collections.new("Review")
        scn.collection.children.link(self.coll)
        scn.render.engine = "BLENDER_EEVEE"
        try:
            scn.eevee.taa_render_samples = 16
        except AttributeError:
            pass
        scn.render.image_settings.file_format = "PNG"
        scn.render.film_transparent = False
        scn.view_settings.view_transform = "Standard"
        scn.view_settings.look = "None"
        world = scn.world or bpy.data.worlds.new("World")
        scn.world = world
        world.use_nodes = True
        bg = world.node_tree.nodes.get("Background")
        bg.inputs[0].default_value = (0.62, 0.68, 0.76, 1)
        bg.inputs[1].default_value = 0.9
        sun = bpy.data.lights.new("ReviewSun", "SUN")
        sun.energy = 3.2
        sun.angle = math.radians(4)
        so = bpy.data.objects.new("ReviewSun", sun)
        so.rotation_euler = (math.radians(42), math.radians(-18), math.radians(-28))
        self.coll.objects.link(so)
        self.ground = self._plane("ReviewGround", 40.0, (0.20, 0.33, 0.16))
        self.ball = self._sphere("ReviewBall", BALL_R, (0.92, 0.92, 0.9))
        self.tee = self._tee()
        self.stick = self._stick()
        self.cams = {}
        scn.render.use_stamp = True
        for a in dir(scn.render):
            if a.startswith("use_stamp_"):
                try:
                    setattr(scn.render, a, a in ("use_stamp_note", "use_stamp_frame"))
                except (AttributeError, TypeError):
                    pass
        scn.render.use_stamp_note = True
        scn.render.stamp_font_size = 15

    def _mat(self, name, rgb, rough=0.6, emit=0.0):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes.get("Principled BSDF")
        b.inputs["Base Color"].default_value = (*rgb, 1)
        b.inputs["Roughness"].default_value = rough
        if emit:
            b.inputs["Emission Color"].default_value = (*rgb, 1)
            b.inputs["Emission Strength"].default_value = emit
        return m

    def _mesh_obj(self, name, bm, mat):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(mat)
        for p in me.polygons:
            p.use_smooth = True
        ob = bpy.data.objects.new(name, me)
        self.coll.objects.link(ob)
        return ob

    def _plane(self, name, size, rgb):
        import bmesh
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size / 2)
        return self._mesh_obj(name, bm, self._mat(name, rgb, 0.9))

    def _sphere(self, name, r, rgb):
        import bmesh
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=r)
        return self._mesh_obj(name, bm, self._mat(name, rgb, 0.35, emit=0.6))

    def _tee(self):
        import bmesh
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.0025, radius2=0.006, depth=0.03)
        return self._mesh_obj("ReviewTee", bm, self._mat("ReviewTee", (0.95, 0.85, 0.2), 0.5))

    def _stick(self):
        import bmesh
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.006, radius2=0.006, depth=1.6)
        ob = self._mesh_obj("ReviewStick", bm, self._mat("ReviewStick", (0.95, 0.3, 0.1), 0.5))
        ob.rotation_euler = (0, math.radians(90), 0)
        return ob

    def camera(self, name, pos, target, lens=50.0):
        if name in self.cams:
            ob = self.cams[name]
        else:
            cam = bpy.data.cameras.new(name)
            cam.sensor_fit = "VERTICAL"
            cam.sensor_height = 24.0
            ob = bpy.data.objects.new(name, cam)
            self.coll.objects.link(ob)
            self.cams[name] = ob
        ob.data.lens = lens
        ob.location = pos
        ob.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        return ob

    def setup_clip(self, clip):
        rm = self.rm
        rm.ob.animation_data.action = bpy.data.actions[clip.name]
        for n in CLUBS:
            ob = bpy.data.objects.get("Club_" + n)
            if ob:
                ob.hide_render = ob.hide_viewport = (n != clip.club)
        ball = clip.ball
        self.ball.hide_render = ball is None
        self.tee.hide_render = not (ball is not None and ball[2] > BALL_R + 0.005)
        self.stick.hide_render = ball is None
        if ball is not None:
            self.ball.location = ball
            self.tee.location = (ball[0], ball[1], (ball[2] - BALL_R) / 2.0)
            self.tee.scale = (1, 1, max(0.05, (ball[2] - BALL_R) / 0.03))
            self.stick.location = (ball[0] + 0.25, ball[1] - 0.28, 0.006)
        bx = ball[0] if ball is not None else 0.0
        by = ball[1] if ball is not None else -0.3
        self.camera("FaceOn", V(bx * 0.4, -7.2, 1.05), V(bx * 0.4, by * 0.4, 0.98))
        self.camera("DownLine", V(-7.2, by * 0.55, 1.15), V(0.0, by * 0.55, 0.98))
        self.camera("Front3Q", V(3.2, -5.6, 1.5), V(0.0, -0.1, 0.95))

    def render(self, clip, cam, f, path, res=(520, 600), note=None):
        scn = self.scn
        scn.camera = self.cams[cam]
        scn.render.resolution_x, scn.render.resolution_y = res
        scn.render.resolution_percentage = 100
        scn.render.stamp_note_text = note or ("%s  %s" % (clip.name, cam))
        scn.frame_set(f)
        scn.render.filepath = path
        bpy.ops.render.render(write_still=True)


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        log("ffmpeg failed:", " ".join(cmd), r.stderr[-800:])
    return r.returncode == 0


def contact_sheet(rv, clip, frames, out_png, cams=("FaceOn", "DownLine"), res=(360, 420)):
    tmp = os.path.join(REVIEW, "_tmp", clip.name)
    os.makedirs(tmp, exist_ok=True)
    i = 0
    for cam in cams:
        for f in frames:
            rv.render(clip, cam, f, os.path.join(tmp, "%03d.png" % i), res,
                      note="%s %s f%d%s" % (clip.name, cam, f, "  CONTACT" if f == clip.contact else ""))
            i += 1
    ok = run([FFMPEG, "-y", "-loglevel", "error", "-i", os.path.join(tmp, "%03d.png"), "-filter_complex",
              "tile=%dx%d" % (len(frames), len(cams)), "-frames:v", "1", out_png])
    shutil.rmtree(tmp, ignore_errors=True)
    return ok


def clip_video(rv, clip, out_mp4, cams=("FaceOn", "DownLine"), res=(424, 480)):
    tmp = os.path.join(REVIEW, "_tmp", clip.name + "_vid")
    os.makedirs(tmp, exist_ok=True)
    for cam in cams:
        d = os.path.join(tmp, cam)
        os.makedirs(d, exist_ok=True)
        for f in range(clip.length + 1):
            rv.render(clip, cam, f, os.path.join(d, "%03d.png" % f), res)
    args = [FFMPEG, "-y", "-loglevel", "error"]
    for cam in cams:
        args += ["-framerate", str(FPS), "-i", os.path.join(tmp, cam, "%03d.png")]
    args += ["-filter_complex", "hstack=inputs=%d" % len(cams), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
             out_mp4]
    ok = run(args)
    shutil.rmtree(tmp, ignore_errors=True)
    return ok


# ------------------------------------------------------------------ clip authoring

IRON_BALL = V(0.0, -0.65, BALL_R)


def iron_address(rm):
    ball = IRON_BALL
    body = dict(
        hips=V(0.0, 0.09, 0.962),
        pelvis=V(0, 25, -1),
        spine=V(0, 9, -10),
        clavL=V(2, 4, 0), clavR=V(-9, 16, 0),
        footL=V(0.20, -0.02, 16), footR=V(-0.20, -0.02, -6),
        kneeL=V(-0.06, -1, 0), kneeR=V(0.10, -1, 0),
        look=ball + V(-0.04, 0.0, 0.0),
        elbowR=V(-0.15, 0.85, 0.45),
    )
    arm = {"club": club_pose(rm, "Iron", ball, lean=3.0, back=0.012), "pole_w": V(0.2, 0.85, 0.45)}
    return body, arm


def make_address(rm, club, ball, hips, pelvis, spine, feet, lean=3.0, back=0.012, look_off=V(-0.04, 0, 0),
                 clav=(V(2, 4, 0), V(-9, 16, 0)), pole_w=V(0.2, 0.85, 0.45), elbowR=V(-0.15, 0.85, 0.45), lie_d=0.0):
    body = dict(hips=hips, pelvis=pelvis, spine=spine, clavL=clav[0], clavR=clav[1],
                footL=feet[0], footR=feet[1], kneeL=V(-0.06, -1, 0), kneeR=V(0.10, -1, 0),
                look=ball + look_off, elbowR=elbowR)
    arm = {"club": club_soled(rm, club, ball, lean=lean, back=back, lie_d=lie_d), "pole_w": pole_w}
    return body, arm


def clip_address_static(rm, clips):
    c = Clip("AddressStatic", 1, club="Iron", ball=IRON_BALL)
    body, arm = iron_address(rm)
    c.pose(0, body)
    c.arm("L", 0, arm)
    return c


def keyseq(c, ch, seq, tag=None):
    for f, v in seq:
        c.key(ch, f, v, tag)


# Iron swing key tables (frames for the 100-frame iron clip); other swings warp/scale these.
SWING_KEYS = dict(
    pelvis=[(4, (2, 25, -1)), (8, (1, 25, -1)), (18, (-16, 25, -1.5)), (25, (-33, 25, -2)), (29, (-44, 25, -2.5)),
            (32, (-41, 25, -3)), (34.5, (-30, 25, -4)), (36, (-20, 25, -5)), (38.5, (2, 25, -6)), (41, (40, 24, -7)),
            (44, (60, 22, -6)), (47, (74, 19, -5)), (53, (88, 13, -3)), (60, (96, 8, -2)), (65, (98, 7, -2)),
            (72, (96, 7.5, -2)), (100, (96, 7.5, -2))],
    spine=[(4, (1, 9, -10)), (12, (-9, 9, -9)), (18, (-21, 9, -8)), (25, (-38, 9, -6)), (29, (-44, 9, -5)),
           (32, (-49, 9, -5)), (34.5, (-55, 9, -7)), (36, (-46, 9, -9)), (38.5, (-32, 9, -13)), (41, (-24, 9, -18)),
           (44, (0, 7, -19)), (47, (14, 4, -17)), (53, (22, 0, -13)), (60, (26, -3, -10)), (65, (28, -4, -9)),
           (72, (26, -3, -9.5)), (100, (26, -3, -9.5))],
    hips=[(4, (0.012, 0.09, 0.960)), (12, (0.0, 0.09, 0.960)), (18, (-0.02, 0.085, 0.956)),
          (25, (-0.03, 0.075, 0.950)), (32, (-0.025, 0.07, 0.945)), (36, (0.02, 0.075, 0.943)),
          (38.5, (0.05, 0.085, 0.945)), (41, (0.075, 0.065, 0.937)), (44, (0.10, 0.04, 0.940)),
          (47, (0.12, 0.01, 0.950)), (53, (0.15, -0.03, 0.965)), (60, (0.165, -0.055, 0.976)),
          (65, (0.168, -0.06, 0.977)), (72, (0.166, -0.057, 0.976)), (100, (0.166, -0.057, 0.976))],
    headlock=[(0, 1.0), (41, 1.0), (47, 0.0)],
    headoff=[(0, (0, 0, 0)), (6, (0.004, 0, 0)), (32, (-0.025, 0.005, 0.01)), (38.5, (-0.02, 0.0, -0.01)),
             (41, (-0.015, -0.005, -0.015)), (47, (0.02, 0.03, 0.04))],
    rollL=[(0, 0.0), (44, 0.0), (53, 3.0), (60, 5.0), (100, 5.0)],
    heelR=[(0, 0.0), (36, 0.0), (38.5, 2.0), (41, 8.0), (44, 20.0), (47, 35.0), (50, 50.0), (53, 62.0), (60, 76.0),
           (65, 78.0), (100, 78.0)],
    yawtR=[(0, 0.0), (41, 0.0), (44, 12.0), (47, 40.0), (50, 72.0), (53, 80.0), (100, 80.0)],
    kneeL=[(36, (-0.06, -1, 0)), (41, (0.05, -1, 0)), (53, (0.3, -1, 0)), (60, (0.35, -1, 0)), (100, (0.35, -1, 0))],
    kneeR=[(25, (0.12, -1, 0)), (36, (0.2, -1, 0)), (41, (0.45, -1, 0)), (47, (0.8, -0.6, 0)), (60, (1.0, -0.2, 0)),
           (100, (1.0, -0.2, 0))],
    elbowR=[(18, (-0.1, 0.9, -0.2)), (25, (0.0, 0.6, -0.8)), (32, (0.05, 0.35, -1.0)), (38.5, (-0.1, 0.8, -0.6)),
            (41, (-0.2, 0.9, -0.1)), (44, (-0.1, 0.6, -0.8)), (47, (0.2, 0.2, -1)), (60, (0.3, -0.2, -1)),
            (100, (0.3, -0.2, -1))],
)


def iron_arm_keys(rm, club, ball, addr_arm, lean_press=7.0, imp=None):
    imp = imp or {}
    k = {
        0: addr_arm,
        5: {"club": club_pose(rm, club, ball, lean=lean_press, back=0.012), "pole_w": V(0.2, 0.85, 0.45)},
        18: {"dir": arm_dir(48, 22), "pole": "auto", "shaft": V(-1, 0.0, 0.05)},
        25: {"aim": V(-0.50, -0.20, 1.12), "pole": "auto", "hinge": (80, V(0.1, 0.2, 1))},
        32: {"dir_w": V(-0.6, 0.3, 0.85), "pole": "auto", "hinge": (96, V(1, -0.15, 0))},
        36: {"aim": V(-0.45, -0.05, 1.10), "pole": "auto", "hinge": (86, V(0.1, 0.35, 1))},
        38.5: {"aim": V(-0.30, -0.30, 0.70), "pole": "auto", "hinge": (72, V(-1, 0.05, 0.1))},
        41: {"club": club_pose(rm, club, ball, lean=imp.get("lean", 8.0), lift=imp.get("lift", -0.002),
                               lie_d=imp.get("lie_d", 2.0)), "pole_w": V(0.1, 0.9, 0.3)},
        44: {"dir": arm_dir(58, 14), "pole": "auto", "shaft": V(1, 0.0, 0.1), "flex": -5},
        47: {"dir": arm_dir(92, 2), "reach": 0.93, "pole": V(0.3, 0.3, -1), "shaft": V(0.0, 0.3, 1), "flex": -10},
    }
    for f, r in ((60, 0.62), (65, 0.60), (72, 0.62), (100, 0.62)):
        k[f] = {"aim": V(0.22, 0.12, 1.66), "reach": r, "pole_w": V(0.3, -0.1, -1),
                "shaft": V(-0.75, 0.4, -0.5) if f != 65 else V(-0.72, 0.45, -0.52), "flex": -15}
    return k


def warp(f, anchors):
    if not anchors:
        return f
    xs = [a for a, _ in anchors]
    if f <= xs[0]:
        return anchors[0][1] + (f - xs[0])
    for (a0, b0), (a1, b1) in zip(anchors, anchors[1:]):
        if f <= a1:
            return b0 + (b1 - b0) * (f - a0) / (a1 - a0)
    return anchors[-1][1] + (f - xs[-1])


IRON_ADDR = dict(hips=V(0.0, 0.09, 0.962), pelvis=V(0, 25, -1), spine=V(0, 9, -10))


def make_swing(rm, cfg):
    """Swing clip from the iron key tables: time-warped (cfg tmap), backswing/finish turn scaled, hips/pelvis/spine
    expressed relative to the iron address and re-based on this club's address, arm keys overridable per key."""
    ball, club = cfg["ball"], cfg["club"]
    c = Clip(cfg["name"], cfg["length"], club=club, contact=cfg["contact"], ball=ball, notes=cfg["notes"])
    body, addr_arm = cfg["address"](rm)
    c.pose(0, body)
    T = lambda f: round(warp(f, cfg.get("tmap")), 2)
    bs, fs = cfg.get("bs", 1.0), cfg.get("fs", 1.0)
    K = SWING_KEYS

    def turn(x, f):
        return x * (bs if f <= 38.5 else fs) if x < 0 or f > 38.5 else x

    for ch in ("pelvis", "spine"):
        a0 = IRON_ADDR[ch]
        for f, v in K[ch]:
            v = V(*v)
            d = v - a0
            w = cfg.get(ch + "_w", {}).get(f, V(1, 1, 1))
            c.key(ch, T(f), body[ch] + V(turn(v.x, f) - a0.x, d.y * w.y, d.z * w.z) + cfg.get(ch + "_add", {}).get(f, V(0, 0, 0)))
    hs = cfg.get("hip_scale", V(1, 1, 1))
    for f, v in K["hips"]:
        d = V(*v) - IRON_ADDR["hips"]
        c.key("hips", T(f), body["hips"] + V(d.x * hs.x, d.y * hs.y, d.z * hs.z) + cfg.get("hips_add", {}).get(f, V(0, 0, 0)))
    for ch in ("headlock", "headoff", "rollL", "kneeL", "kneeR", "elbowR"):
        for f, v in K[ch]:
            c.key(ch, T(f), V(*v) if isinstance(v, tuple) else v)
    for ch in ("heelR", "yawtR"):
        for f, v in K[ch]:
            c.key(ch, T(f), v * cfg.get("heel", 1.0))
    look0 = body["look"]
    for f, v in [(44, look0)] + cfg.get("look", [(47, V(3.0, -0.8, 0.0)), (53, V(12, -1.2, 1.0)),
                                                 (60, V(30, -1.5, 3.0)), (100, V(30, -1.5, 3.0))]):
        c.key("look", T(f), v)
    keys = iron_arm_keys(rm, club, ball, addr_arm, imp=cfg.get("impact"))
    keys.update(cfg.get("arm", {}))
    for f in sorted(keys):
        if keys[f] is not None:
            c.arm("L", T(f), keys[f])
    return c


def iron_cfg():
    return dict(name="Swing_Full", club="Iron", length=100, contact=41, ball=IRON_BALL, address=iron_address,
                notes="7-iron full swing; frame 0 = Address frame 0; top f32, impact f41, finish f60, held to f100")


def clip_swing_full(rm, clips):
    return make_swing(rm, iron_cfg())


DRIVER_BALL = V(0.15, -1.08, 0.045)
WEDGE_BALL = V(0.0, -0.60, BALL_R)
PUTT_BALL = V(0.03, -0.42, BALL_R)
HOLE = V(4.5, -0.42, 0.0)
FAR = V(30.0, -1.5, 3.0)


def driver_address(rm):
    return make_address(rm, "Driver", DRIVER_BALL, V(-0.01, 0.10, 0.96), V(0, 21, -1), V(0, 8, -12),
                        (V(0.24, -0.02, 18), V(-0.24, -0.02, -6)), lean=-1.0, back=0.03, lie_d=-13.0)


def wedge_address(rm):
    return make_address(rm, "Wedge", WEDGE_BALL, V(0.01, 0.08, 0.958), V(0, 26, -1), V(0, 10, -8),
                        (V(0.16, -0.02, 14), V(-0.16, -0.02, -6)), lean=5.0)


def putt_address(rm):
    return make_address(rm, "Putter", PUTT_BALL, V(0.0, 0.10, 0.95), V(0, 28, 0), V(0, 12, -9),
                        (V(0.15, -0.02, 10), V(-0.15, -0.02, -10)), lean=3.0, back=0.008, look_off=V(0, 0, 0),
                        clav=(V(2, 0, 0), V(-10, 26, 0)), pole_w=V(0.4, 0.8, 0.2), elbowR=V(-0.4, 0.8, 0.2))


def hang(s, flex=5.0, add=-9.0, reach=0.975, wrist=(6.0, -4.0, 0.0)):
    sx = 1.0 if s == "L" else -1.0
    return {"dir": arm_dir(flex, add, s), "reach": reach, "pole": V(0.3 * sx, 1, 0), "wrist": wrist}


def hold_club_down(shaft, flex=16.0, add=4.0, reach=0.96):
    return {"dir": arm_dir(flex, add), "reach": reach, "pole": V(0.3, 1, 0), "shaft": shaft}


def club_waggle(G0, deg, dpos):
    Y = (G0.to_3x3() @ V(0, 1, 0)).normalized()
    a = V(1, 0, 0).cross(Y).normalized()
    p = G0.translation
    R = Matrix.Rotation(math.radians(deg), 4, a)
    return Matrix.Translation(p + dpos) @ R @ Matrix.Translation(-p) @ G0


def clip_idle(rm, clips):
    n = 240
    c = Clip("Idle_Shop", n, loop=True,
             notes="Relaxed shop idle, no club: breathing, two slow weight shifts, glance, shoulder roll, hand squeeze")

    def pulse(f, a, b, ramp):
        return smooth(clamp((f - a) / ramp, 0, 1)) * (1 - smooth(clamp((f - b) / ramp, 0, 1)))

    for f in range(0, n + 1, 6):
        w = 0.0 if f in (0, n) else pulse(f, 30, 95, 25) - pulse(f, 140, 195, 25)
        br = math.sin(2 * math.pi * f / 120.0) if f not in (0, n) else 0.0
        c.key("hips", f, V(0.032 * w, 0.004 * abs(w), 0.970 - 0.004 * abs(w) + 0.0008 * br))
        c.key("pelvis", f, V(3.0 * w, 3.0, -4.0 * w))
        c.key("spine", f, V(-2.0 * w, -1.0 - 0.8 * br, 5.0 * w))
        roll = pulse(f, 172, 200, 12)
        c.key("clavL", f, V(0.8 * br + 6.0 * roll, -3.0 * pulse(f, 182, 205, 10), 0))
        c.key("clavR", f, V(0.8 * br + 6.0 * roll, -3.0 * pulse(f, 182, 205, 10), 0))
    c.pose(0, dict(footL=V(0.13, 0.0, 8), footR=V(-0.13, 0.0, -8), kneeL=V(0.1, -1, 0), kneeR=V(-0.1, -1, 0)))
    c.pose(n, dict(footL=V(0.13, 0.0, 8), footR=V(-0.13, 0.0, -8), kneeL=V(0.1, -1, 0), kneeR=V(-0.1, -1, 0)))
    ahead = V(0.0, -5.0, 1.62)
    keyseq(c, "look", [(0, ahead), (98, ahead), (110, V(-2.6, -4.0, 1.45)), (126, V(-2.6, -4.0, 1.45)),
                       (142, ahead), (n, ahead)])
    keyseq(c, "htilt", [(0, 0.0), (104, 0.0), (114, -3.0), (130, -2.0), (146, 0.0), (n, 0.0)])
    for s in "LR":
        keyseq(c, "fist" + s, [(0, 0.0), (198, 0.0), (205, 0.4), (214, 0.0), (n, 0.0)])
        for f, fl in ((0, 5.0), (60, 7.0), (120, 4.5), (180, 6.0), (n, 5.0)):
            c.arm(s, f, hang(s, flex=fl, reach=0.982))
    return c


def clip_address(rm, clips):
    ball = IRON_BALL
    n = 150
    c = Clip("Address", n, loop=True, club="Iron", ball=ball,
             notes="7-iron address loop: frame 0 = Swing_Full frame 0; two waggles, look at target and back, settles")
    body, arm = iron_address(rm)
    c.pose(0, body)
    c.pose(n, body)
    G0 = arm["club"]
    wag = lambda d, dx: {"club": club_waggle(G0, d, V(dx, 0, 0.004)), "pole_w": arm["pole_w"]}
    for f, spec in ((0, arm), (22, arm), (28, wag(24, -0.02)), (33, wag(3, -0.004)), (39, wag(20, -0.018)),
                    (45, wag(2, -0.002)), (52, arm), (n, arm)):
        c.arm("L", f, spec)
    lb = body["look"]
    keyseq(c, "look", [(0, lb), (64, lb), (78, HOLE * 0 + V(9.0, -0.9, 0.0)), (92, V(9.0, -0.9, 0.0)), (106, lb),
                       (n, lb)])
    h0 = body["hips"]
    keyseq(c, "hips", [(0, h0), (24, h0 + V(-0.004, 0, 0)), (40, h0 + V(-0.003, 0, 0)), (56, h0),
                       (112, h0), (120, h0 + V(0.003, 0.002, -0.004)), (130, h0 + V(0.0, 0.0, -0.002)), (n, h0)])
    p0 = body["pelvis"]
    keyseq(c, "pelvis", [(0, p0), (112, p0), (122, p0 + V(1.2, 0, 0)), (134, p0 + V(-0.4, 0, 0)), (n, p0)])
    return c


def clip_swing_driver(rm, clips):
    ball = DRIVER_BALL
    return make_swing(rm, dict(
        name="Swing_Driver", club="Driver", length=100, contact=41, ball=ball, address=driver_address,
        notes="Driver off a tee: wide stance, ball forward, spine tilted away, fuller turn, sweeping upward strike",
        bs=1.08, hip_scale=V(1.15, 1.0, 1.0), impact=dict(lean=0.0, lift=0.0, lie_d=-10.0),
        arm={5: {"club": club_soled(rm, "Driver", ball, lean=2.0, back=0.03, lie_d=-13.0), "pole_w": V(0.2, 0.85, 0.45)},
             25: {"aim": V(-0.55, -0.25, 1.12), "pole": "auto", "hinge": (80, V(0.1, 0.2, 1))},
             32: {"dir_w": V(-0.62, 0.3, 0.85), "pole": "auto", "hinge": (100, V(1, -0.15, 0))},
             36: {"aim": V(-0.50, -0.10, 1.10), "pole": "auto", "hinge": (88, V(0.1, 0.35, 1))},
             38.5: {"aim": V(-0.30, -0.42, 0.72), "pole": "auto", "hinge": (75, V(-1, 0.05, 0.15))}}))


def clip_swing_pitch(rm, clips):
    ball = WEDGE_BALL
    fin = {"dir": arm_dir(86, 8), "reach": 0.88, "pole": V(0.3, 0.3, -1), "shaft": V(0.2, 0.25, 1), "flex": -8}
    wf = V(1, 0.6, 1)
    fins = (53, 60, 65, 72, 100)
    return make_swing(rm, dict(
        name="Swing_Pitch", club="Wedge", length=80, contact=34, ball=ball, address=wedge_address,
        notes="Wedge pitch: narrow stance, 3/4 backswing, softer tempo, chest-high held finish",
        tmap=[(0, 0), (6, 5), (18, 14), (32, 25), (41, 34), (47, 39), (60, 50), (72, 60), (100, 80)],
        bs=0.72, fs=0.78, heel=0.55, hip_scale=V(0.7, 1.0, 0.6),
        pelvis_w={f: wf for f in fins}, spine_w={f: wf for f in fins},
        impact=dict(lean=9.0, lift=-0.002, lie_d=1.5),
        look=[(47, V(2.0, -0.7, 0.6)), (53, V(6, -0.8, 2.5)), (60, V(12, -1.0, 5.0)), (100, V(15, -1.0, 4.0))],
        arm={5: {"club": club_pose(rm, "Wedge", ball, lean=8.0, back=0.012), "pole_w": V(0.2, 0.85, 0.45)},
             18: {"dir": arm_dir(44, 22), "pole": "auto", "shaft": V(-0.9, 0.0, 0.35)},
             25: {"aim": V(-0.46, -0.25, 1.02), "pole": "auto", "hinge": (70, V(0, 0.2, 1))},
             32: {"aim": V(-0.50, -0.12, 1.28), "pole": "auto", "hinge": (85, V(0.4, 0.1, 1))},
             36: {"aim": V(-0.42, -0.18, 1.02), "pole": "auto", "hinge": (80, V(0.1, 0.3, 1))},
             38.5: {"aim": V(-0.28, -0.32, 0.72), "pole": "auto", "hinge": (65, V(-1, 0.05, 0.3))},
             44: {"dir": arm_dir(55, 12), "pole": "auto", "shaft": V(1, 0.0, 0.25), "flex": -3},
             47: {"dir": arm_dir(75, 6), "reach": 0.95, "pole": "auto", "shaft": V(0.5, 0.2, 0.85), "flex": -6},
             60: fin, 65: dict(fin, reach=0.87), 72: fin, 100: fin}))


def clip_putt_address(rm, clips):
    ball = PUTT_BALL
    n = 120
    c = Clip("Putt_Address", n, loop=True, club="Putter", ball=ball,
             notes="Putting address loop: eyes over ball, look at hole and back, tiny forward press; f0 = Putt f0")
    body, arm = putt_address(rm)
    c.pose(0, body)
    c.pose(n, body)
    lb = body["look"]
    keyseq(c, "look", [(0, lb), (24, lb), (38, HOLE), (56, HOLE), (70, lb), (n, lb)])
    sp = body["spine"]
    keyseq(c, "spine", [(0, sp), (30, sp + V(0, -0.5, 0)), (60, sp), (90, sp + V(0, -0.5, 0)), (n, sp)])
    press = {"club": club_pose(rm, "Putter", ball, lean=6.0, back=0.008), "pole_w": arm["pole_w"]}
    for f, spec in ((0, arm), (92, arm), (101, press), (110, arm), (n, arm)):
        c.arm("L", f, spec)
    return c


def build_putt(rm, tb, t0, tf):
    ball = PUTT_BALL
    c = Clip("Putt", 70, club="Putter", contact=30, ball=ball,
             notes="Putt: shoulder pendulum, quiet wrists, ~0.25 m back / longer through, head down until ~f45")
    body, arm = putt_address(rm)
    c.pose(0, body)
    sp = body["spine"]
    keyseq(c, "spine", [(3, sp), (20, sp + V(tb, 0, 0)), (30, sp + V(t0, 0, 0)), (41, sp + V(tf, 0, 0)),
                        (70, sp + V(tf, 0, 0))])
    keyseq(c, "look", [(45, body["look"]), (53, ball + V(0.9, 0, 0)), (61, HOLE), (70, HOLE)])
    c.arm("L", 0, arm)
    return c


def club_head_x(rm, clip, f):
    solve_arm_keys(rm, clip)
    pose = solve_frame(rm, clip, f)
    return club_points(rm, clip.club, pose.G)[0]


def clip_putt(rm, clips):
    tb, t0, tf = -14.0, 0.5, 20.0
    x_addr = club_head_x(rm, build_putt(rm, tb, t0, tf), 0).x
    for _ in range(3):
        c = build_putt(rm, tb, t0, tf)
        xb = club_head_x(rm, c, 20).x
        c = build_putt(rm, tb, t0, tf)
        xc = club_head_x(rm, c, 30).x
        c = build_putt(rm, tb, t0, tf)
        xf = club_head_x(rm, c, 41).x
        k = (xf - xb) / (tf - tb)
        t0 += (PUTT_BALL.x - xc) / k
        tb += (x_addr - 0.25 - xb) / k
        tf += (x_addr + 0.40 - xf) / k
    log("putt stroke turns back %.2f contact %.2f through %.2f" % (tb, t0, tf))
    return build_putt(rm, tb, t0, tf)


def clip_react_good(rm, clips):
    src = clips["Swing_Full"]
    c = Clip("React_Good", 60, club="Iron", ball=src.ball,
             notes="From Swing_Full held finish: club comes down, trail hand releases, step-in, small nod, relaxed")
    c.inherit(src, src.length)
    fin = src.arm_keys["L"][-1][1]
    keyseq(c, "pelvis", [(6, V(96, 7.5, -2)), (30, V(72, 4, 0)), (60, V(64, 3, 0))])
    keyseq(c, "spine", [(6, V(26, -3, -9.5)), (30, V(8, 1, -3)), (60, V(4, 2, -1.5))])
    keyseq(c, "hips", [(6, V(0.166, -0.057, 0.976)), (18, V(0.15, -0.06, 0.974)), (30, V(0.10, -0.08, 0.970)),
                       (60, V(0.09, -0.08, 0.973))])
    keyseq(c, "heelR", [(10, 78.0), (24, 0.0)])
    keyseq(c, "yawtR", [(10, 80.0), (24, 0.0)])
    keyseq(c, "liftR", [(12, 0.0), (18, 0.045), (25, 0.0)])
    keyseq(c, "footR", [(12, V(-0.20, -0.02, -6)), (25, V(-0.04, -0.17, 66))])
    keyseq(c, "kneeR", [(12, V(1.0, -0.2, 0)), (25, V(0.9, -0.45, 0))])
    keyseq(c, "rollL", [(10, 5.0), (26, 0.0)])
    keyseq(c, "rclub", [(0, 1.0), (14, 1.0), (24, 0.0)])
    keyseq(c, "look", [(8, FAR), (26, V(22.0, -1.0, 0.0)), (60, V(10.0, -1.5, 1.2))])
    keyseq(c, "nod", [(34, 0.0), (41, 12.0), (50, 2.0), (60, 1.0)])
    c.arm("L", 5, fin)
    c.arm("L", 17, {"dir": arm_dir(92, 24), "reach": 0.76, "pole": V(0.3, 0.3, -1), "shaft": V(-0.2, 0.5, 0.85)})
    c.arm("L", 29, {"dir": arm_dir(50, 14), "reach": 0.9, "pole": "auto", "shaft": V(0.6, -0.3, 0.1)})
    c.arm("L", 42, hold_club_down(V(0.42, -0.25, -0.87)))
    c.arm("L", 60, hold_club_down(V(0.42, -0.25, -0.87), flex=15.0))
    c.arm("R", 24, {"dir": arm_dir(40, 15, "R"), "reach": 0.88, "pole": V(-0.3, 1, -0.2), "wrist": (10, 0, 0)})
    c.arm("R", 36, hang("R", flex=7.0))
    c.arm("R", 60, hang("R", flex=6.0))
    return c


def clip_react_miss(rm, clips):
    src = clips["Putt"]
    c = Clip("React_Miss", 75, club="Putter", ball=src.ball,
             notes="From Putt final pose: head drops, trail hand to forehead then hip, small step back, relaxed")
    c.inherit(src, src.length)
    p0, s0, h0 = src.val("pelvis", src.length), src.val("spine", src.length), src.val("hips", src.length)
    keyseq(c, "pelvis", [(4, p0), (30, V(-6, 9, 0)), (56, V(-14, 6, 1)), (75, V(-14, 6, 1))])
    keyseq(c, "spine", [(4, s0), (30, V(-4, 6, -2)), (56, V(-4, 3, 0)), (75, V(-4, 3, 0))])
    keyseq(c, "hips", [(4, h0), (30, V(0.0, 0.05, 0.962)), (42, V(-0.02, 0.06, 0.960)), (56, V(-0.04, 0.09, 0.970)),
                       (75, V(-0.04, 0.09, 0.972))])
    keyseq(c, "liftR", [(40, 0.0), (46, 0.04), (53, 0.0)])
    keyseq(c, "footR", [(40, V(-0.15, -0.02, -10)), (53, V(-0.19, 0.13, -28))])
    keyseq(c, "kneeR", [(40, V(-0.1, -1, 0)), (53, V(-0.45, -0.9, 0))])
    keyseq(c, "rclub", [(0, 1.0), (5, 1.0), (13, 0.0)])
    keyseq(c, "nod", [(4, 0.0), (15, 22.0), (34, 16.0), (52, 5.0), (75, 3.0)])
    keyseq(c, "htilt", [(4, 0.0), (15, 7.0), (40, 4.0), (75, 0.0)])
    keyseq(c, "look_w", [(8, 1.0), (30, 0.6), (75, 0.4)])
    keyseq(c, "look", [(8, HOLE), (60, HOLE + V(-2.0, -1.0, 0.6))])
    c.arm("L", 3, src.arm_keys["L"][-1][1])
    c.arm("L", 24, hold_club_down(V(0.4, -0.45, -0.8), flex=30.0, add=12.0, reach=0.92))
    c.arm("L", 50, hold_club_down(V(0.3, -0.35, -0.89), flex=14.0))
    c.arm("L", 75, hold_club_down(V(0.3, -0.35, -0.89), flex=13.0))
    c.arm("R", 13, {"dir": arm_dir(70, 25, "R"), "reach": 0.62, "pole": V(-0.8, 0.3, -0.5), "wrist": (10, 0, 0)})
    for f in (20, 32):
        c.arm("R", f, {"pos_bone": ("Head", V(-0.03, -0.12, 0.05)), "pole": V(-0.9, 0.3, -0.4),
                       "wrist": (25, -8, -10)})
    c.arm("R", 46, {"pos_bone": ("Hips", V(-0.21, -0.02, 0.03)), "pole": V(-0.6, 0.8, 0.0), "wrist": (-30, 10, 0)})
    c.arm("R", 62, {"pos_bone": ("Hips", V(-0.21, -0.02, 0.03)), "pole": V(-0.6, 0.8, 0.0), "wrist": (-30, 10, 0)})
    c.arm("R", 75, {"pos_bone": ("Hips", V(-0.21, -0.02, 0.03)), "pole": V(-0.6, 0.8, 0.0), "wrist": (-30, 10, 0)})
    keyseq(c, "fistR", [(14, 0.0), (20, 0.0), (75, 0.0)])
    return c


def clip_celebrate(rm, clips):
    src = clips["Putt"]
    c = Clip("Celebrate", 90, club="Putter", ball=src.ball,
             notes="From Putt final pose: stands up, right fist pump x2, small hop, putter raised in left hand, relaxed")
    c.inherit(src, src.length)
    p0, s0, h0 = src.val("pelvis", src.length), src.val("spine", src.length), src.val("hips", src.length)
    keyseq(c, "pelvis", [(3, p0), (16, V(10, 7, 0)), (34, V(18, 6, 0)), (60, V(14, 5, 0)), (90, V(12, 5, 0))])
    keyseq(c, "spine", [(3, s0), (16, V(6, 0, 0)), (19, V(6, 5, 0)), (24, V(8, 0, 0)), (29, V(8, 5, 0)),
                        (36, V(8, -2, 0)), (60, V(5, 2, 0)), (90, V(4, 3, 0))])
    keyseq(c, "hips", [(3, h0), (6, h0 + V(0, 0, -0.006)), (16, V(0.0, 0.05, 0.965)), (19, V(0.0, 0.05, 0.955)),
                       (24, V(0.0, 0.05, 0.962)), (29, V(0.0, 0.05, 0.953)), (36, V(0.0, 0.05, 0.935)),
                       (41, V(0.0, 0.05, 1.015)), (46, V(0.0, 0.05, 0.94)), (54, V(0.0, 0.05, 0.968)),
                       (90, V(0.0, 0.05, 0.972))])
    for s in "LR":
        keyseq(c, "lift" + s, [(37, 0.0), (41, 0.055), (45, 0.0)])
    keyseq(c, "rclub", [(0, 1.0), (2, 1.0), (9, 0.0)])
    keyseq(c, "fistR", [(4, 0.0), (10, 1.0), (52, 1.0), (62, 0.0)])
    keyseq(c, "look", [(4, HOLE), (20, HOLE + V(0, 0, 1.0)), (44, HOLE + V(-1.0, -1.5, 1.6)), (70, V(3.0, -4.0, 1.5)),
                       (90, V(3.0, -4.0, 1.5))])
    c.arm("L", 2, src.arm_keys["L"][-1][1])
    c.arm("L", 14, {"dir": arm_dir(70, 5), "reach": 0.9, "pole": "auto", "shaft": V(0.3, -0.1, 0.95)})
    c.arm("L", 26, {"dir": arm_dir(140, -6), "reach": 0.95, "pole": V(0.2, 0.6, -1), "shaft": V(0.1, 0.05, 1)})
    c.arm("L", 48, {"dir": arm_dir(136, -6), "reach": 0.95, "pole": V(0.2, 0.6, -1), "shaft": V(0.15, 0.05, 1)})
    c.arm("L", 64, {"dir": arm_dir(40, 10), "reach": 0.92, "pole": "auto", "shaft": V(0.45, -0.3, -0.84)})
    c.arm("L", 78, hold_club_down(V(0.3, -0.35, -0.89), flex=14.0))
    c.arm("L", 90, hold_club_down(V(0.3, -0.35, -0.89), flex=13.0))
    pump_up = {"dir": arm_dir(95, 22, "R"), "reach": 0.55, "pole": V(-0.7, 0.3, -0.6), "wrist": (0, 0, 0)}
    pump_dn = {"dir": arm_dir(22, 8, "R"), "reach": 0.62, "pole": V(-0.3, 1, -0.3), "wrist": (10, 0, 0)}
    c.arm("R", 10, pump_up)
    c.arm("R", 17, pump_dn)
    c.arm("R", 22, dict(pump_up, dir=arm_dir(70, 18, "R")))
    c.arm("R", 28, pump_dn)
    c.arm("R", 40, dict(pump_dn, dir=arm_dir(30, 10, "R"), reach=0.7))
    c.arm("R", 64, hang("R", flex=12.0, reach=0.95))
    c.arm("R", 78, hang("R", flex=7.0))
    c.arm("R", 90, hang("R", flex=6.0))
    return c


CLIP_BUILDERS = None


def build_all(rm, only=None):
    clips = {}
    for name, fn in CLIP_BUILDERS:
        clip = fn(rm, clips)
        clips[name] = clip
        bake_clip(rm, clip, actions=True)
    return clips


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"stages": [], "clips": None, "frames": None, "quick": False}
    for a in argv:
        if a.startswith("clips="):
            opts["clips"] = a.split("=", 1)[1].split(",")
        elif a.startswith("frames="):
            opts["frames"] = [int(x) for x in a.split("=", 1)[1].split(",") if x]
        elif a == "quick":
            opts["quick"] = True
        else:
            opts["stages"].append(a)
    if not opts["stages"]:
        opts["stages"] = ["build", "render"]
    return opts


def prepare_rig():
    ob = bpy.data.objects[RIG_NAME]
    ob.animation_data_clear()
    for pb in ob.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    bpy.context.scene.render.fps = FPS
    with open(RIG_JSON) as fh:
        data = json.load(fh)
    return RigModel(ob, data)


def stage_test(rm, opts):
    name = (opts["clips"] or ["Address"])[0]
    builders = dict(CLIP_BUILDERS)
    clips = {}
    need = {name} | set(CLIP_DEPS.get(name, ()))
    for n, fn in CLIP_BUILDERS:
        if n in need:
            clips[n] = fn(rm, clips)
            bake_clip(rm, clips[n])
    clip = clips[name]
    if clip.club:
        low = [(f, club_points(rm, clip.club, p.G)[1].z) for f, p in enumerate(clip.poses)]
        bad = [(f, round(z, 3)) for f, z in low if z < -0.004]
        log("club sole below ground:", bad[:12] if bad else "none")
    frames = opts["frames"] or [0]
    for f in range(0, clip.length + 1):
        if f in frames or "all" in opts["stages"]:
            log(describe(rm, clip, f))
    log("fk check", "%.2e" % verify_fk(rm, clip, frames))
    log("foot slide", foot_slide(rm, clip))
    rv = Review(rm)
    rv.setup_clip(clip)
    if "sheet" in opts["stages"]:
        contact_sheet(rv, clip, frames, "/tmp/pp_anim/%s_sheet.png" % name, res=(300, 360))
        return
    for f in frames:
        for cam in ("FaceOn", "DownLine") if not opts["quick"] else ("FaceOn",):
            rv.render(clip, cam, f, "/tmp/pp_anim/%s_%s_%03d.png" % (name, cam, f), (440, 520))
        if "hands" in opts["stages"] and clip.poses[f].G is not None:
            G = clip.poses[f].G
            c = G @ V(0, 0.05, 0)
            R = G.to_3x3()
            for cn, off in (("HandFO", V(0, -0.65, 0.05)), ("HandDTL", V(-0.65, 0.0, 0.05))):
                rv.camera(cn, c + off, c, lens=50)
                rv.render(clip, cn, f, "/tmp/pp_anim/%s_%s_%03d.png" % (name, cn, f), (360, 360))


def main():
    opts = parse_args()
    rm = prepare_rig()
    if "test" in opts["stages"]:
        stage_test(rm, opts)
        return


CLIP_BUILDERS = [("Idle_Shop", clip_idle), ("Address", clip_address), ("Swing_Full", clip_swing_full),
                 ("Swing_Driver", clip_swing_driver), ("Swing_Pitch", clip_swing_pitch),
                 ("Putt_Address", clip_putt_address), ("Putt", clip_putt), ("React_Good", clip_react_good),
                 ("React_Miss", clip_react_miss), ("Celebrate", clip_celebrate)]

CLIP_DEPS = {"React_Good": ("Swing_Full",), "React_Miss": ("Putt",), "Celebrate": ("Putt",)}

if __name__ == "__main__":
    main()
