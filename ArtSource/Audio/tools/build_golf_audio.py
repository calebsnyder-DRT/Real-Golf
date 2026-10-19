#!/usr/bin/env python3
"""
Builds the in-game audio set under Assets/_Game/Resources/Audio/ and its manifest.

Two kinds of output:
  * Real recordings, trimmed / looped / filtered from CC0 or public-domain sources (Kenney audio packs, Wikimedia
    Commons). Written as .ogg.
  * Procedural placeholders synthesised here (ball strikes, whooshes, cup rattle, splash, roll loops, wind, cart hum).
    Written as .wav and named *_ph_* so they are easy to find and replace.

Every written file gets an entry in ArtSource/Audio/audio_manifest.json (source, license, author, processing) and the
human-readable Assets/_Game/Resources/Audio/CREDITS.txt.

File names are the contract with GolfSoundBank: a cue loads every clip in its folder whose name starts with its key
(e.g. Ball/strike_driver_ph_01.wav belongs to the "Ball/strike_driver" cue). Keep the prefixes when replacing sounds.

Needs numpy, scipy, soundfile (libsndfile with Vorbis) and ffmpeg on PATH:
    python3 -m venv /tmp/golfaudio/venv && /tmp/golfaudio/venv/bin/pip install numpy scipy soundfile
    /tmp/golfaudio/venv/bin/python ArtSource/Audio/tools/build_golf_audio.py
Sources are downloaded once into $GOLF_AUDIO_CACHE (default /tmp/golfaudio).
"""
import fractions
import json
import os
import re
import subprocess
import time
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import butter, resample_poly, sosfilt

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Assets" / "_Game" / "Resources" / "Audio"
MANIFEST = ROOT / "ArtSource" / "Audio" / "audio_manifest.json"
CREDITS = OUT / "CREDITS.txt"
CACHE = Path(os.environ.get("GOLF_AUDIO_CACHE", "/tmp/golfaudio"))
SR = 44100
UA = {"User-Agent": "RealGolfAudioBuild/1.0 (local game dev; low volume)"}

CC0 = "CC0 1.0 (https://creativecommons.org/publicdomain/zero/1.0/)"
PD = "Public domain (pdsounds.org dedication, as recorded on Wikimedia Commons)"

KENNEY = {
    "impact-sounds": "Impact Sounds",
    "music-jingles": "Music Jingles",
    "interface-sounds": "Interface Sounds",
}

# Wikimedia Commons files, licence as stated on each file page (checked via the Commons API, Oct 2026).
COMMONS = {
    "Ocean Waves on a Tropical Beach.ogg": ("CC0", "J.D. Savanyu (User:Jarrod_stanley)"),
    "On a pebble beach.ogg": ("PD", "earthcalling"),
    "Gull 1.ogg": ("PD", "avphillips"),
    "Gull 2.ogg": ("PD", "avphillips"),
    "XC707075 - European Herring Gull - Larus argentatus.mp3": ("CC0", "xeno-canto contributor (XC707075)"),
    "Gentle breeze and birds singing.ogg": ("PD", "ezwa"),
    "Sound Effects - Applause after a concert.ogg": ("CC0", "Amada44"),
    "277021 sandermotions applause-2.wav": ("CC0", "Sandermotions"),
    "Applause i.ogg": ("PD", "thore"),
    "Applause ii.ogg": ("PD", "thore"),
    "Clapping hurray.ogg": ("PD", "starlite"),
    "Slow starting applause.ogg": ("PD", "stephan"),
    "Ohhh ahhh.ogg": ("PD", "starlite"),
    "Festival concert people crowd.ogg": ("PD", "stephan"),
}

manifest = []
rng = np.random.default_rng(20261005)


# ------------------------------------------------------------------------------------------------ Sources

def fetch(url, dst):
    if dst.exists() and dst.stat().st_size > 1000:
        return dst
    dst.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(5):
        try:
            data = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180).read()
            dst.write_bytes(data)
            return dst
        except Exception as e:  # Commons rate-limits bursts (HTTP 429); back off and retry.
            print(f"  retry {dst.name}: {e}")
            time.sleep(30 * (attempt + 1))
    raise RuntimeError(f"could not download {url}")


def kenney(pack):
    folder = CACHE / f"kenney_{pack}"
    if not folder.exists():
        page = urllib.request.urlopen(urllib.request.Request(f"https://kenney.nl/assets/{pack}", headers=UA)).read().decode()
        url = re.search(r'https://kenney\.nl/media/pages/assets/[^"]+\.zip', page).group(0)
        z = fetch(url, CACHE / f"kenney_{pack}.zip")
        zipfile.ZipFile(z).extractall(folder)
    return folder


def kenney_file(pack, name):
    hits = list(kenney(pack).rglob(name))
    if not hits:
        raise FileNotFoundError(f"{name} not in Kenney {pack}")
    return hits[0]


def commons(title):
    dst = CACHE / "commons" / title
    if not dst.exists():
        q = urllib.parse.urlencode({"action": "query", "titles": "File:" + title, "prop": "imageinfo", "iiprop": "url", "format": "json"})
        r = json.load(urllib.request.urlopen(urllib.request.Request("https://commons.wikimedia.org/w/api.php?" + q, headers=UA)))
        url = next(iter(r["query"]["pages"].values()))["imageinfo"][0]["url"]
        fetch(url, dst)
        time.sleep(5)
    return dst


def commons_source(title):
    lic, author = COMMONS[title]
    return {
        "source": f"Wikimedia Commons: File:{title}",
        "url": "https://commons.wikimedia.org/wiki/File:" + urllib.parse.quote(title.replace(" ", "_")),
        "license": CC0 if lic == "CC0" else PD,
        "author": author,
    }


def kenney_source(pack, name):
    return {
        "source": f"Kenney {KENNEY[pack]} pack: {name}",
        "url": f"https://kenney.nl/assets/{pack}",
        "license": CC0,
        "author": "Kenney (www.kenney.nl)",
    }


PROCEDURAL = {"source": "Procedural (ArtSource/Audio/tools/build_golf_audio.py)", "url": "", "license": CC0 + " - generated for this project", "author": "Real-Golf"}


# ------------------------------------------------------------------------------------------------ DSP helpers

def load(path, channels=1, start=None, length=None):
    window = (["-ss", str(start)] if start is not None else []) + (["-t", str(length)] if length is not None else [])
    raw = subprocess.run(["ffmpeg", "-v", "error", *window, "-i", str(path), "-f", "f32le", "-ac", str(channels), "-ar", str(SR), "-"],
                         check=True, capture_output=True).stdout
    x = np.frombuffer(raw, dtype=np.float32).astype(np.float64)
    return x.reshape(-1, channels) if channels > 1 else x


def sec(s):
    return int(round(s * SR))


def filt(x, kind, freq, order=2):
    sos = butter(order, freq, btype=kind, fs=SR, output="sos")
    return sosfilt(sos, x, axis=0)


def fade(x, fin=0.005, fout=0.02):
    x = x.copy()
    n = len(x)
    a, b = min(sec(fin), n // 2), min(sec(fout), n // 2)
    shape = (lambda m: 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, m)))
    if a > 0:
        g = shape(a)
        x[:a] *= g if x.ndim == 1 else g[:, None]
    if b > 0:
        g = shape(b)[::-1]
        x[-b:] *= g if x.ndim == 1 else g[:, None]
    return x


def loopify(x, xfade):
    """Seamless loop: the tail is equal-power crossfaded into the head; output is xfade shorter than the input."""
    n, f = len(x), sec(xfade)
    out = x[: n - f].copy()
    t = np.linspace(0, np.pi / 2, f)
    fi, fo = np.sin(t), np.cos(t)
    if x.ndim > 1:
        fi, fo = fi[:, None], fo[:, None]
    out[:f] = x[:f] * fi + x[n - f:] * fo
    return out


def peak(x, db=-1.0):
    m = np.max(np.abs(x))
    return x if m < 1e-9 else x * (10 ** (db / 20) / m)


def rms(x, db):
    r = np.sqrt(np.mean(x ** 2))
    y = x * (10 ** (db / 20) / max(r, 1e-9))
    m = np.max(np.abs(y))
    return y * (0.98 / m) if m > 0.98 else y


def varispeed(x, factor):
    """Tape-style speed change: factor > 1 raises pitch and shortens."""
    fr = fractions.Fraction(factor).limit_denominator(64)
    return resample_poly(x, fr.denominator, fr.numerator, axis=0)


def glide(x, start, end):
    """Varispeed whose rate moves from start to end across the clip (a falling 'aww' from a neutral 'ooh')."""
    n = len(x)
    rate = np.linspace(start, end, int(n / ((start + end) / 2)))
    pos = np.cumsum(rate)
    pos = pos[pos < n - 1]
    return np.interp(pos, np.arange(n), x) if x.ndim == 1 else np.stack([np.interp(pos, np.arange(n), x[:, c]) for c in range(x.shape[1])], 1)


def envelope_db(x, win=0.02):
    m = x if x.ndim == 1 else x.mean(1)
    w = sec(win)
    k = len(m) // w
    e = np.sqrt(np.mean(m[: k * w].reshape(k, w) ** 2, 1) + 1e-12)
    return 20 * np.log10(e / e.max()), w


def segments(x, thresh_db=-28, min_len=0.2, max_len=2.5, pad=0.06, gap=0.12):
    e, w = envelope_db(x)
    on = e > thresh_db
    segs, i = [], 0
    while i < len(on):
        if not on[i]:
            i += 1
            continue
        j = i
        quiet = 0
        while j < len(on) and quiet * w < sec(gap):
            quiet = quiet + 1 if not on[j] else 0
            j += 1
        a, b = max(0, i * w - sec(pad)), min(len(x), (j - quiet) * w + sec(pad))
        if (b - a) >= sec(min_len):
            segs.append((a, min(b, a + sec(max_len))))
        i = j
    return segs


def crowdify(x, voices=7, spread=0.05, max_delay=0.09):
    """Thickens one voice into a small gallery: detuned, delayed copies panned across a stereo pair."""
    n = len(x)
    out = np.zeros((int(n * (1 + 2 * spread)) + sec(max_delay) + 10, 2))
    for v in range(voices):
        y = varispeed(x, 1 + rng.uniform(-spread, spread))
        d = sec(rng.uniform(0, max_delay))
        pan = rng.uniform(0.15, 0.85)
        g = rng.uniform(0.6, 1.0)
        out[d: d + len(y), 0] += y * g * (1 - pan)
        out[d: d + len(y), 1] += y * g * pan
    return out


def noise(n):
    return rng.standard_normal(n)


def brown(n):
    b = np.cumsum(noise(n))
    b -= filt(b, "lowpass", 2.0, 1)
    return b / (np.max(np.abs(b)) + 1e-9)


def expenv(n, tau, delay=0):
    t = np.arange(n) / SR - delay
    return np.where(t >= 0, np.exp(-np.maximum(t, 0) / tau), 0.0)


def modal(dur, freqs, decays, amps, delay=0.0, detune=0.02):
    n = sec(dur)
    t = np.arange(n) / SR - delay
    y = np.zeros(n)
    for f, d, a in zip(freqs, decays, amps):
        f *= 1 + rng.uniform(-detune, detune)
        y += a * np.sin(2 * np.pi * f * np.maximum(t, 0) + rng.uniform(0, 2 * np.pi)) * np.where(t >= 0, np.exp(-np.maximum(t, 0) / d), 0)
    return y


def lfo(n, rates, depth=1.0):
    t = np.arange(n) / SR
    y = sum(np.sin(2 * np.pi * r * t + rng.uniform(0, 2 * np.pi)) / (i + 1) for i, r in enumerate(rates))
    y /= np.max(np.abs(y)) + 1e-9
    return 1 + depth * y


# ------------------------------------------------------------------------------------------------ Output

def write(rel, x, info, cue, notes, placeholder=False):
    path = OUT / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    x = np.clip(np.nan_to_num(x), -1, 1).astype(np.float32)
    if path.suffix == ".ogg":
        sf.write(path, x, SR, format="OGG", subtype="VORBIS")
    else:
        sf.write(path, x, SR, subtype="PCM_16")
    entry = {"file": "Assets/_Game/Resources/Audio/" + rel, "cue": cue, "placeholder": placeholder,
             "seconds": round(len(x) / SR, 3), "channels": 1 if x.ndim == 1 else x.shape[1], "processing": notes}
    entry.update(info)
    manifest.append(entry)
    print(f"  {rel:48s} {entry['seconds']:7.2f}s  {'PLACEHOLDER' if placeholder else entry['license'][:14]}")


# ------------------------------------------------------------------------------------------------ Ambience

def build_ambience():
    print("Ambience")
    t = "Ocean Waves on a Tropical Beach.ogg"
    x = load(commons(t), 2, start=120, length=66)
    x = filt(filt(x, "highpass", 50), "lowpass", 11000)
    write("Ambience/surf_bed_01.ogg", rms(loopify(x, 4.0), -22), commons_source(t), "Ambience/surf_bed",
          "120-186 s excerpt, HP 50 Hz / LP 11 kHz, 4 s equal-power loop crossfade, RMS -22 dBFS")

    t = "On a pebble beach.ogg"
    x = filt(load(commons(t), 1), "highpass", 70)
    write("Ambience/surf_shore_01.ogg", rms(loopify(x, 3.0), -20), commons_source(t), "Ambience/surf_shore",
          "mono for 3D emitters along the shoreline, HP 70 Hz, 3 s loop crossfade, RMS -20 dBFS")

    t = "Gentle breeze and birds singing.ogg"
    x = filt(load(commons(t), 1), "highpass", 250)
    write("Ambience/birds_01.ogg", rms(loopify(x, 2.5), -28), commons_source(t), "Ambience/birds",
          "HP 250 Hz (removes breeze rumble), 2.5 s loop crossfade, RMS -28 dBFS")

    gulls = []
    for t in ("Gull 1.ogg", "Gull 2.ogg", "XC707075 - European Herring Gull - Larus argentatus.mp3"):
        try:
            x = filt(load(commons(t), 1), "highpass", 450, 3)
        except Exception as e:
            print(f"  skip {t}: {e}")
            continue
        for a, b in segments(x, thresh_db=-24, min_len=0.25, max_len=2.2):
            seg = x[a:b]
            gulls.append((np.sqrt(np.mean(seg ** 2)), seg, t))
    gulls.sort(key=lambda g: -g[0])
    for i, (_, seg, t) in enumerate(gulls[:8]):
        write(f"Ambience/gull_{i + 1:02d}.ogg", peak(fade(seg, 0.01, 0.08), -2), commons_source(t), "Ambience/gull",
              "single call cut by energy segmentation, HP 450 Hz, 10/80 ms fades, peak -2 dBFS")

    # Wind: two decorrelated pink-ish noise beds, band-limited, with slow gusts and a faint whistle band.
    n = sec(42)
    gust = lfo(n, [0.045, 0.11, 0.23, 0.37], 0.45)
    chans = []
    for c in range(2):
        body = filt(brown(n) * 0.6 + filt(noise(n), "lowpass", 900) * 0.4, "bandpass", [90, 1600])
        hiss = filt(noise(n), "bandpass", [1200, 4200]) * 0.05
        whistle = filt(noise(n), "bandpass", [620 + 40 * c, 700 + 40 * c], 3) * 0.6 * lfo(n, [0.07, 0.19], 0.8)
        chans.append((body + hiss * gust + whistle) * gust)
    x = np.stack(chans, 1)
    x[:, 1] = 0.75 * x[:, 1] + 0.25 * x[:, 0]
    write("Ambience/wind_ph_01.wav", rms(loopify(x, 3.0), -24), PROCEDURAL, "Ambience/wind",
          "brown + low-passed noise 90-1600 Hz, gust LFO, faint whistle band, 3 s loop crossfade", True)

    # Electric cart: motor whine harmonics (integer cycles so the loop is clean) plus tyre noise.
    n = sec(6)
    t = np.arange(n) / SR
    whine = sum(np.sin(2 * np.pi * 180 * k * t) * a for k, a in ((1, 0.5), (2, 0.25), (3, 0.12), (13, 0.05)))
    tyres = filt(noise(n), "bandpass", [120, 900]) * 0.5 * lfo(n, [0.5, 1.5], 0.2)
    x = fade(np.concatenate([whine + tyres, whine[: sec(0.5)] + tyres[: sec(0.5)]]), 0, 0)
    write("Ambience/cart_hum_ph_01.wav", peak(loopify(x, 0.5), -6), PROCEDURAL, "Ambience/cart_hum",
          "180 Hz motor harmonics + band-passed tyre noise, loop", True)


# ------------------------------------------------------------------------------------------------ Crowd

def build_crowd():
    print("Crowd")
    t = "Festival concert people crowd.ogg"
    x = load(commons(t), 2)[sec(4): sec(56)]
    x = filt(filt(x, "highpass", 150), "lowpass", 1900)
    write("Crowd/murmur_01.ogg", rms(loopify(x, 3.0), -26), commons_source(t), "Crowd/murmur",
          "4-56 s excerpt, HP 150 Hz / LP 1.9 kHz for a distant gallery, 3 s loop crossfade, RMS -26 dBFS")

    def clip(title, name, cue, a, b, fin, fout, ch=2, lp=9000):
        x = load(commons(title), ch)[sec(a): sec(b)]
        x = filt(x, "lowpass", lp)
        write(name, peak(fade(x, fin, fout), -2), commons_source(title), cue,
              f"{a}-{b} s, LP {lp} Hz (distance), fades {fin}/{fout} s, peak -2 dBFS")

    clip("Applause ii.ogg", "Crowd/applause_small_01.ogg", "Crowd/applause_small", 0.0, 6.0, 0.05, 2.0)
    clip("277021 sandermotions applause-2.wav", "Crowd/applause_small_02.ogg", "Crowd/applause_small", 0.0, 6.3, 0.05, 1.5)
    clip("Applause i.ogg", "Crowd/applause_big_01.ogg", "Crowd/applause_big", 0.0, 9.0, 0.05, 2.5)
    try:
        clip("Sound Effects - Applause after a concert.ogg", "Crowd/applause_big_02.ogg", "Crowd/applause_big", 1.0, 11.0, 0.3, 2.5)
    except Exception as e:
        print(f"  skip concert applause: {e}")
    clip("Clapping hurray.ogg", "Crowd/cheer_01.ogg", "Crowd/cheer", 0.0, 8.0, 0.02, 2.0, ch=1)
    clip("Slow starting applause.ogg", "Crowd/ovation_01.ogg", "Crowd/ovation", 0.0, 18.0, 0.2, 3.5)

    t = "Ohhh ahhh.ogg"
    x = filt(load(commons(t), 1), "highpass", 120)
    segs = segments(x, thresh_db=-26, min_len=0.3, max_len=2.5, gap=0.15)
    ooh = x[segs[0][0]: segs[0][1]] if segs else x[: sec(1.6)]
    write("Crowd/ooh_01.ogg", peak(filt(fade(crowdify(ooh), 0.03, 0.4), "lowpass", 6000), -3), commons_source(t), "Crowd/ooh",
          "first vocal of the clip, thickened into a gallery (7 detuned/delayed/panned copies), LP 6 kHz")
    whole = x[: sec(4.3)]
    write("Crowd/ooh_02.ogg", peak(filt(fade(crowdify(whole, voices=8), 0.03, 0.6), "lowpass", 6000), -3), commons_source(t), "Crowd/ooh",
          "whole 'ohhh-ahhh', thickened into a gallery, LP 6 kHz")
    aww = glide(ooh, 1.0, 0.8)
    write("Crowd/aww_ph_01.wav", peak(filt(fade(crowdify(aww, voices=8), 0.03, 0.5), "lowpass", 4500), -3),
          dict(commons_source(t), source=commons_source(t)["source"] + " (pitch-glided down: derived placeholder)"), "Crowd/aww",
          "the 'ooh' with a falling varispeed glide (1.0 -> 0.8) to read as a disappointed 'aww', gallery-thickened", True)


# ------------------------------------------------------------------------------------------------ Ball, club, cup

def strike(kind, i):
    """Clubface impacts. Driver/wood: titanium 'ping-crack'. Irons/wedges: compressed click + turf. Putter: soft 'tock'."""
    dur = 0.7 if kind == "sand" else 0.45
    n = sec(dur)
    click = filt(noise(n), "highpass", 2500) * expenv(n, 0.0012)
    if kind in ("driver", "wood"):
        base = (2950, 4450, 6100, 8000) if kind == "driver" else (2400, 3650, 5200, 7000)
        body = modal(dur, [f * rng.uniform(0.97, 1.03) for f in base], [0.045, 0.032, 0.022, 0.015], [1, 0.7, 0.45, 0.25])
        thump = filt(noise(n), "lowpass", 380) * expenv(n, 0.01) * 2.5 + np.sin(2 * np.pi * 130 * np.arange(n) / SR) * expenv(n, 0.02) * 0.4
        air = filt(noise(n), "bandpass", [900, 5000]) * expenv(n, 0.07, 0.004) * 0.08
        y = np.tanh(2.2 * (click * 1.2 + body * (0.8 if kind == "driver" else 0.6) + thump + air))
    elif kind in ("iron", "wedge"):
        base = (1850, 3100, 4700) if kind == "iron" else (1500, 2600, 4000)
        body = modal(dur, [f * rng.uniform(0.96, 1.04) for f in base], [0.018, 0.012, 0.008], [1, 0.6, 0.35])
        turf = filt(noise(n), "bandpass", [180, 2600]) * expenv(n, 0.045 if kind == "iron" else 0.06, 0.004) * (0.7 if kind == "iron" else 0.95)
        thump = filt(noise(n), "lowpass", 300) * expenv(n, 0.008) * 2.0
        y = np.tanh(2.0 * (click * 1.3 + body * 0.7 + turf + thump))
    elif kind == "putter":
        body = modal(dur, [1100 * rng.uniform(0.95, 1.05), 2350, 3900], [0.022, 0.011, 0.007], [1, 0.45, 0.2])
        y = click * 0.35 + body * 0.8 + filt(noise(n), "lowpass", 500) * expenv(n, 0.006) * 0.6
    elif kind == "sand":
        spray = filt(noise(n), "bandpass", [700, 6500]) * expenv(n, 0.16, 0.003)
        grains = filt((rng.random(n) < 0.012) * noise(n), "bandpass", [1500, 7000]) * expenv(n, 0.22) * 3
        thud = filt(noise(n), "lowpass", 260) * expenv(n, 0.025) * 2
        y = spray + grains + thud
    else:  # rough: grass tearing as the club cuts through
        tear = filt(noise(n), "bandpass", [500, 5500]) * expenv(n, 0.09, 0.002) * lfo(n, [37, 61], 0.6)
        y = tear + filt(noise(n), "lowpass", 300) * expenv(n, 0.015)
    y = y[: sec(dur)]
    return peak(fade(y, 0.0005, 0.08), -1)


def whoosh():
    dur = rng.uniform(0.38, 0.5)
    n = sec(dur)
    t = np.arange(n) / n
    peak_at = rng.uniform(0.55, 0.7)
    amp = np.exp(-((t - peak_at) / 0.17) ** 2)
    centre = 400 + 2600 * np.exp(-((t - peak_at) / 0.22) ** 2)
    bands = [(220, 500), (450, 1000), (900, 2000), (1800, 3600), (3200, 6500)]
    y = np.zeros(n)
    src = noise(n)
    for lo, hi in bands:
        mid = np.sqrt(lo * hi)
        w = np.exp(-(np.log(centre / mid) / 0.55) ** 2)
        y += filt(src, "bandpass", [lo, hi]) * w
    return peak(fade(y * amp, 0.01, 0.03), -3)


def thud(surface):
    dur = 0.3
    n = sec(dur)
    if surface == "green":
        y = filt(noise(n), "lowpass", 1300) * expenv(n, 0.018) + np.sin(2 * np.pi * 230 * np.arange(n) / SR) * expenv(n, 0.025) * 0.5
    elif surface == "fairway":
        y = filt(noise(n), "lowpass", 900) * expenv(n, 0.03) + filt(noise(n), "bandpass", [1500, 5000]) * expenv(n, 0.02) * 0.15
    elif surface == "rough":
        y = filt(noise(n), "bandpass", [400, 4000]) * expenv(n, 0.06) * lfo(n, [45, 77], 0.7) * 0.6 + filt(noise(n), "lowpass", 500) * expenv(n, 0.03)
    else:  # cart path: small hard ball on concrete
        y = modal(dur, [2600, 4300, 5900], [0.011, 0.008, 0.006], [1, 0.6, 0.4]) + filt(noise(n), "highpass", 3000) * expenv(n, 0.002) * 0.8
        y += filt((rng.random(n) < 0.004) * noise(n), "highpass", 2000) * expenv(n, 0.05) * 0.6
    return peak(fade(y, 0.0005, 0.05), -1)


def cup_drop(i):
    """Rim tick, a drop, the bottom clunk and a few settling bounces, then a short rattle."""
    dur = 1.0
    n = sec(dur)
    y = modal(dur, [3300, 5200], [0.008, 0.005], [0.35, 0.2]) * rng.uniform(0.6, 1.0)
    t0 = rng.uniform(0.07, 0.1)
    gap, amp, t = rng.uniform(0.11, 0.14), 1.0, t0
    for k in range(5):
        y += modal(dur, [920 * rng.uniform(0.97, 1.03), 1750, 2950], [0.03, 0.018, 0.01], [amp, amp * 0.55, amp * 0.3], delay=t)
        y += filt(noise(n), "lowpass", 260) * expenv(n, 0.012, t) * amp * 1.4
        t += gap
        gap *= rng.uniform(0.55, 0.68)
        amp *= rng.uniform(0.38, 0.5)
    y += filt(noise(n), "bandpass", [1800, 5200]) * expenv(n, 0.12, t) * lfo(n, [28], 0.9) * 0.05
    return peak(fade(y, 0.0005, 0.1), -1)


def lip_out(i):
    dur = 0.6
    n = sec(dur)
    y = np.zeros(n)
    t = 0.0
    for k in range(rng.integers(3, 6)):
        y += modal(dur, [3400 * rng.uniform(0.95, 1.05), 5600, 7300], [0.01, 0.007, 0.005], [1, 0.5, 0.3], delay=t) * (0.8 ** k)
        t += rng.uniform(0.025, 0.045)
    y += filt(noise(n), "bandpass", [2000, 6000]) * expenv(n, 0.12) * lfo(n, [33], 0.8) * 0.08
    return peak(fade(y, 0.0005, 0.08), -2)


def splash(i):
    dur = 1.0
    n = sec(dur)
    tt = np.arange(n) / SR
    f = 420 * np.exp(-tt / 0.03) + 140
    plop = np.sin(2 * np.pi * np.cumsum(f) / SR) * expenv(n, 0.05) * 0.8
    burst = filt(noise(n), "lowpass", 3200) * expenv(n, 0.14, 0.005)
    spray = filt(noise(n), "bandpass", [2500, 9000]) * expenv(n, 0.09, 0.01) * 0.4
    bubbles = np.zeros(n)
    for _ in range(rng.integers(10, 18)):
        start = rng.uniform(0.04, 0.55)
        length = rng.uniform(0.01, 0.03)
        f0 = rng.uniform(600, 1800)
        m = sec(length)
        k = sec(start)
        ft = f0 * (1 + 1.5 * np.linspace(0, 1, m))
        bubbles[k: k + m] += np.sin(2 * np.pi * np.cumsum(ft) / SR) * np.hanning(m) * rng.uniform(0.1, 0.3)
    return peak(fade(plop + burst + spray + bubbles, 0.0005, 0.15), -1)


def roll_loop(surface):
    n = sec(4.5)
    if surface == "green":
        y = brown(n) * 0.5 + filt(noise(n), "lowpass", 450) * 0.6 + filt(noise(n), "bandpass", [1500, 4000]) * 0.04
        y *= lfo(n, [6.0, 9.0], 0.15)
    elif surface == "fairway":
        ticks = filt((rng.random(n) < 0.02) * noise(n), "bandpass", [1500, 7000]) * 1.5
        y = filt(noise(n), "bandpass", [300, 3000]) * 0.4 + ticks + brown(n) * 0.3
    else:
        crackle = filt((rng.random(n) < 0.05) * noise(n), "highpass", 1800) * 1.2
        y = filt(noise(n), "bandpass", [700, 6000]) * 0.35 + crackle + brown(n) * 0.25
    return peak(loopify(y, 0.5), -3)


def build_ball():
    print("Ball / club / cup")
    for kind, count in (("driver", 3), ("wood", 3), ("iron", 4), ("wedge", 3), ("putter", 4), ("sand", 3), ("rough", 3)):
        for i in range(count):
            write(f"Ball/strike_{kind}_ph_{i + 1:02d}.wav", strike(kind, i), PROCEDURAL, f"Ball/strike_{kind}",
                  f"synth {kind} impact (noise click + modal body + turf/thump layers)", True)
    for i in range(4):
        write(f"Ball/whoosh_ph_{i + 1:02d}.wav", whoosh(), PROCEDURAL, "Ball/whoosh", "band-swept noise club whoosh", True)

    def kenney_set(pack, names, out, cue, speed, lp=None, hp=120, length=None, gain_db=-2):
        for i, name in enumerate(names):
            x = load(kenney_file(pack, name), 1)
            x = filt(varispeed(x, speed), "highpass", hp)
            if lp:
                x = filt(x, "lowpass", lp)
            if length:
                x = x[: sec(length)]
            write(f"{out}_{i + 1:02d}.ogg", peak(fade(x, 0.0005, 0.05), gain_db), kenney_source(pack, name), cue,
                  f"varispeed x{speed} (smaller object), HP {hp} Hz" + (f", LP {lp} Hz" if lp else "") + (f", first {length} s" if length else ""))

    kenney_set("impact-sounds", [f"footstep_grass_00{k}.ogg" for k in range(4)], "Ball/land_fairway", "Ball/land_fairway", 1.5, length=0.3)
    kenney_set("impact-sounds", [f"footstep_grass_00{k}.ogg" for k in (1, 3, 4)], "Ball/land_rough", "Ball/land_rough", 1.15, lp=3200, length=0.35)
    kenney_set("impact-sounds", [f"footstep_snow_00{k}.ogg" for k in range(4)], "Ball/land_bunker", "Ball/land_bunker", 1.3, length=0.4)
    kenney_set("impact-sounds", [f"impactGeneric_light_00{k}.ogg" for k in range(3)], "Ball/land_cartpath", "Ball/land_cartpath", 1.6, hp=300)
    kenney_set("impact-sounds", [f"impactSoft_medium_00{k}.ogg" for k in range(2)], "Ball/land_green", "Ball/land_green", 1.8, lp=4000, length=0.25)
    kenney_set("impact-sounds", [f"impactWood_light_00{k}.ogg" for k in range(4)], "Ball/tree_hit", "Ball/tree_hit", 1.2)
    kenney_set("impact-sounds", [f"impactMetal_light_00{k}.ogg" for k in range(3)], "Ball/club_change", "Ball/club_change", 1.35, gain_db=-4)

    for surface, count in (("green", 3), ("fairway", 2), ("rough", 2), ("cartpath", 2)):
        for i in range(count):
            write(f"Ball/land_{surface}_ph_{i + 1:02d}.wav", thud(surface), PROCEDURAL, f"Ball/land_{surface}",
                  f"synth {surface} landing", True)
    for i in range(3):
        write(f"Ball/cup_drop_ph_{i + 1:02d}.wav", cup_drop(i), PROCEDURAL, "Ball/cup_drop",
              "rim tick, drop, bottom clunk + settling bounces, rattle tail", True)
    for i in range(2):
        write(f"Ball/lip_out_ph_{i + 1:02d}.wav", lip_out(i), PROCEDURAL, "Ball/lip_out", "rapid rim ticks + scrape", True)
    for i in range(2):
        write(f"Ball/splash_ph_{i + 1:02d}.wav", splash(i), PROCEDURAL, "Ball/splash", "plop chirp + burst + spray + bubbles", True)
    for surface in ("green", "fairway", "cartpath"):
        write(f"Ball/roll_{surface}_ph_01.wav", roll_loop(surface), PROCEDURAL, f"Ball/roll_{surface}",
              f"seamless {surface} roll loop (pitch/volume driven by ball speed at runtime)", True)


# ------------------------------------------------------------------------------------------------ Stingers + UI

MAJOR = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MINOR = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def analyse_jingle(x):
    """Krumhansl key-profile correlation (major vs minor) and the pitch contour slope (rising > 0)."""
    frame, hop = 4096, 2048
    win = np.hanning(frame)
    freqs = np.fft.rfftfreq(frame, 1 / SR)
    band = (freqs > 80) & (freqs < 2500)
    pcs = (np.round(12 * np.log2(freqs[band] / 440.0)) % 12).astype(int)
    chroma = np.zeros(12)
    contour, weights = [], []
    for k in range(0, len(x) - frame, hop):
        mag = np.abs(np.fft.rfft(x[k: k + frame] * win))[band]
        np.add.at(chroma, pcs, mag)
        e = mag.sum()
        if e > 1e-6:
            top = np.argsort(mag)[-6:]
            contour.append(np.average(np.log2(freqs[band][top]), weights=mag[top]))
            weights.append(e)
    corr = lambda prof: max(np.corrcoef(np.roll(prof, s), chroma)[0, 1] for s in range(12))
    major, minor = corr(MAJOR), corr(MINOR)
    slope = 0.0
    if len(contour) > 3:
        tt = np.arange(len(contour))
        slope = np.polyfit(tt / len(tt), contour, 1, w=np.sqrt(weights))[0]
    return major - minor, slope, len(x) / SR


def build_stingers_and_ui():
    print("Stingers")
    rows = []
    for k in range(17):
        name = f"jingles_PIZZI{k:02d}.ogg"
        x = load(kenney_file("music-jingles", name), 1)
        mode, slope, dur = analyse_jingle(x)
        rows.append((mode + 0.35 * np.sign(slope), mode, slope, dur, name))
    rows.sort(key=lambda r: -r[0])
    for r in rows:
        print(f"    {r[4]:24s} score {r[0]:+.2f} mode {r[1]:+.2f} slope {r[2]:+.2f} {r[3]:.1f}s")
    # Brightest third for the good holes (longest two for an ace and the round), darkest third for the bad ones.
    happy = rows[:6]
    happy_long = sorted(happy, key=lambda r: -r[3])
    sad = list(reversed(rows))[:5]
    pick = {
        "stinger_ace": happy_long[0],
        "stinger_round": happy_long[1],
        "stinger_eagle": next(r for r in happy if r not in happy_long[:2]),
        "stinger_birdie": [r for r in happy if r not in happy_long[:2]][1],
        "stinger_par": min([r for r in happy if r not in happy_long[:2]][2:], key=lambda r: r[3]),
        "stinger_bogey": min(sad[1:], key=lambda r: r[3]),
        "stinger_double": sad[0],
    }
    for out, r in pick.items():
        x = load(kenney_file("music-jingles", r[4]), 2)
        write(f"Stingers/{out}_01.ogg", peak(fade(x, 0.002, 0.15), -3), kenney_source("music-jingles", r[4]), f"Stingers/{out}",
              f"auto-picked by mode/contour analysis (score {r[0]:+.2f}, {r[3]:.1f} s) - audition and swap freely")

    print("UI")
    for out, names in (("ui_select", ["select_002.ogg", "select_005.ogg"]), ("ui_confirm", ["confirmation_002.ogg"]),
                       ("ui_back", ["back_002.ogg"]), ("ui_title", ["maximize_006.ogg"])):
        for i, name in enumerate(names):
            x = load(kenney_file("interface-sounds", name), 1)
            write(f"UI/{out}_{i + 1:02d}.ogg", peak(fade(x, 0.001, 0.03), -4), kenney_source("interface-sounds", name), f"UI/{out}", "peak -4 dBFS")


# ------------------------------------------------------------------------------------------------ Main

def write_credits():
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps({"generated_by": "ArtSource/Audio/tools/build_golf_audio.py", "files": manifest}, indent=1))
    lines = [
        "Real-Golf - in-game audio",
        "=========================",
        "",
        "Built by ArtSource/Audio/tools/build_golf_audio.py; the machine-readable manifest (with processing notes) is",
        "ArtSource/Audio/audio_manifest.json. Everything here is CC0 or public domain: attribution is not required and",
        "is given as a courtesy. Files named *_ph_* are procedural PLACEHOLDERS synthesised by the script - replace them",
        "with recorded sounds when available, keeping the filename prefix (it is the GolfSoundBank cue key).",
        "",
        "Sources",
        "-------",
        "  Kenney (https://kenney.nl): Impact Sounds, Music Jingles, Interface Sounds packs - CC0 1.0",
        "  Wikimedia Commons (licence per file page, checked Oct 2026):",
    ]
    for title, (lic, author) in COMMONS.items():
        if any(e["source"].endswith(title) or title in e["source"] for e in manifest):
            lines.append(f"    [{'CC0' if lic == 'CC0' else 'PD '}] {title} - {author}")
    lines += ["", "Files", "-----"]
    for e in manifest:
        tag = "PLACEHOLDER" if e["placeholder"] else ("CC0" if e["license"].startswith("CC0") else "PD")
        lines.append(f"  {e['file'].replace('Assets/_Game/Resources/Audio/', ''):44s} {tag:11s} {e['source']}")
    CREDITS.write_text("\n".join(lines) + "\n")


if __name__ == "__main__":
    CACHE.mkdir(parents=True, exist_ok=True)
    for sub in ("Ambience", "Ball", "Crowd", "Stingers", "UI"):
        d = OUT / sub
        if d.exists():
            for f in d.iterdir():
                if f.suffix in (".wav", ".ogg"):
                    f.unlink()
    build_ambience()
    build_crowd()
    build_ball()
    build_stingers_and_ui()
    write_credits()
    print(f"{len(manifest)} files -> {OUT}")
