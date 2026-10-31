#!/usr/bin/env python3
"""Synthesises the game's placeholder-free sound effects into Assets/_Project/Audio/SFX/Clips/Generated.

Usage: python Tools/generate-sfx.py
Every sound is made from noise, tones and struck-object resonances, so the project owns them outright. The output is
deterministic: the same script gives the same files. Only numpy is required. GeneratedSfxBuilder.cs turns them into cues.
"""
import os
import wave

import numpy as np

SR = 44100
PEAK = 0.7
OUT = os.path.join("Assets", "_Project", "Audio", "SFX", "Clips", "Generated")

rng = np.random.default_rng(7301)


def times(d):
    return np.arange(int(d * SR)) / SR


def noise(d):
    return rng.uniform(-1.0, 1.0, int(d * SR))


def band(x, lo, hi):
    """Band-pass by FFT with soft edges, so short sounds do not ring at the cut."""
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    gain = np.clip((freqs - lo * 0.7) / (lo * 0.3 + 1e-9), 0, 1) * np.clip((hi * 1.3 - freqs) / (hi * 0.3), 0, 1)
    return np.fft.irfft(spectrum * gain, len(x))


def decay(d, tau):
    return np.exp(-times(d) / tau)


def attack(x, seconds):
    n = min(len(x), max(1, int(seconds * SR)))
    out = x.copy()
    out[:n] *= np.linspace(0.0, 1.0, n)
    return out


def tone(d, f0, f1=None, curve=1.0):
    """Sine gliding from f0 to f1."""
    f1 = f0 if f1 is None else f1
    n = int(d * SR)
    freq = f0 + (f1 - f0) * np.linspace(0, 1, n) ** curve
    return np.sin(2 * np.pi * np.cumsum(freq) / SR)


def modal(d, modes):
    """Struck object: decaying sine resonances given as (frequency, decay seconds, level)."""
    t = times(d)
    out = np.zeros(len(t))
    for freq, tau, level in modes:
        out += level * np.sin(2 * np.pi * freq * t) * np.exp(-t / tau)
    return out


def hit(d, lo, hi, tau, level=1.0):
    """Short filtered noise burst: the contact part of an impact."""
    return band(noise(d), lo, hi) * decay(d, tau) * level


def swept_noise(d, lo0, lo1, width):
    """Noise whose band moves over time, made from overlapping filtered blocks."""
    source = noise(d)
    block = 1024
    hop = block // 2
    window = np.hanning(block)
    out = np.zeros(len(source))
    freqs = np.fft.rfftfreq(block, 1.0 / SR)
    steps = max(1, (len(source) - block) // hop)
    for step in range(steps):
        start = step * hop
        centre = lo0 + (lo1 - lo0) * step / steps
        gain = np.exp(-(((freqs - centre) / width) ** 2))
        out[start:start + block] += np.fft.irfft(np.fft.rfft(source[start:start + block] * window) * gain, block) * window
    return out


def mix(d, *parts):
    """Adds (start seconds, signal) pairs into a buffer of d seconds."""
    out = np.zeros(int(d * SR))
    for start, signal in parts:
        i = int(start * SR)
        n = min(len(signal), len(out) - i)
        if n > 0:
            out[i:i + n] += signal[:n]
    return out


def bubbles(d, count, lo, hi, rising=False):
    """Water blips: short rising sines at scattered times."""
    out = np.zeros(int(d * SR))
    for k in range(count):
        length = rng.uniform(0.03, 0.08)
        base = rng.uniform(lo, hi) * (1.0 + (k / count if rising else 0.0))
        blip = tone(length, base, base * 1.5) * np.hanning(int(length * SR)) * rng.uniform(0.4, 1.0)
        start = rng.uniform(0, d - length) if not rising else (k / count) * (d - length)
        i = int(start * SR)
        n = min(len(blip), len(out) - i)
        out[i:i + n] += blip[:n]
    return out


def thud(d, f0, f1, tau):
    return tone(d, f0, f1, 0.4) * decay(d, tau)


# ------------------------------------------------------------------ Camera

def shutter():
    click = lambda level: (hit(0.035, 1500, 9000, 0.009, 1.0) + modal(0.035, [(3100, 0.01, 0.6), (1400, 0.012, 0.5)])) * level
    return mix(0.2, (0.0, click(1.0)), (0.06, swept_noise(0.05, 5200, 3000, 1400) * 0.3), (0.115, click(0.8)))


def photo_raise():
    return mix(0.22, (0.0, hit(0.03, 500, 3000, 0.01, 0.7)), (0.02, swept_noise(0.15, 900, 1900, 500) * 0.12),
               (0.16, modal(0.05, [(2100, 0.01, 0.5)])))


def photo_dry():
    dull = lambda: hit(0.04, 250, 1400, 0.012, 0.9) + modal(0.04, [(420, 0.012, 0.5)])
    return mix(0.22, (0.0, dull()), (0.11, dull() * 0.75))


def photo_ready():
    ratchet = sum(mix(0.3, (0.018 * k, hit(0.012, 2000, 7000, 0.003, 0.35))) for k in range(5))
    return ratchet + mix(0.3, (0.1, modal(0.2, [(1760, 0.06, 0.6), (3520, 0.03, 0.2)])))


def photo_hit():
    notes = [(0.0, 1319), (0.05, 1760), (0.1, 2637)]
    return mix(0.45, *[(s, modal(0.32, [(f, 0.09, 0.6), (f * 2.01, 0.04, 0.2)])) for s, f in notes])


def locked_tick():
    return modal(0.05, [(1500, 0.008, 0.8), (3100, 0.004, 0.3)]) + hit(0.05, 1500, 6000, 0.002, 0.5)


# ------------------------------------------------------------------ Water balloon and water

def balloon_fill():
    return bubbles(0.55, 22, 320, 620, rising=True) * 0.7 + band(noise(0.55), 300, 2200) * 0.05


def balloon_throw():
    d = 0.32
    return swept_noise(d, 500, 1700, 500) * np.hanning(int(d * SR)) ** 0.8 * 0.5


def balloon_pop():
    return hit(0.09, 700, 6000, 0.012, 1.0) + thud(0.09, 900, 300, 0.018) * 0.6 + mix(0.09, (0.0, bubbles(0.09, 3, 500, 900) * 0.3))


def splash(d, body):
    wash = band(noise(d), 500, 7000) * decay(d, d * 0.26)
    low = thud(0.14, 220, 80, 0.05)
    return mix(d, (0.0, attack(wash, 0.004)), (0.0, low * body), (0.04, bubbles(d - 0.05, int(40 * d), 350, 1100) * 0.45))


def wade():
    d = 0.3
    swish = band(noise(d), 350, 2400) * np.hanning(int(d * SR)) ** 1.5 * 0.55
    return swish + bubbles(d, 5, 300, 650) * 0.3


# ------------------------------------------------------------------ Cage

def cage_lock():
    bars = modal(0.5, [(310, 0.14, 0.7), (742, 0.08, 0.5), (1289, 0.05, 0.4), (2011, 0.03, 0.25)])
    latch = hit(0.03, 1800, 8000, 0.006, 0.8) + modal(0.03, [(2600, 0.008, 0.4)])
    return mix(0.55, (0.0, thud(0.25, 130, 55, 0.08) * 1.1), (0.0, hit(0.06, 300, 3500, 0.02, 0.9)), (0.0, bars), (0.13, latch))


def cage_open():
    d = 0.6
    creak = np.sign(tone(d, 170, 330, 0.7)) * 0.5 + tone(d, 340, 660, 0.7) * 0.5
    creak = band(creak, 300, 3500) * np.hanning(int(d * SR)) * 0.3
    latch = hit(0.04, 1500, 7000, 0.006, 0.9) + modal(0.04, [(1900, 0.012, 0.5), (820, 0.016, 0.4)])
    return mix(0.72, (0.0, latch), (0.08, creak))


# ------------------------------------------------------------------ Stone and ceramic

def ceramic(a, b):
    return modal(0.28, [(a, 0.05, 0.7), (b, 0.03, 0.4), (a * 2.7, 0.015, 0.3)]) + mix(0.28, (0.0, hit(0.02, 2000, 8000, 0.003, 0.5)))


def shatter():
    d = 0.7
    parts = [(0.0, hit(0.12, 300, 5000, 0.03, 1.0)), (0.0, thud(0.16, 170, 70, 0.05) * 0.8)]
    for _ in range(18):
        freq = rng.uniform(1500, 5200)
        parts.append((rng.uniform(0.02, 0.5) ** 1.5 + 0.01, modal(0.05, [(freq, 0.012, rng.uniform(0.15, 0.4))])))
    return mix(d, *parts)


# ------------------------------------------------------------------ Bodies

def land(f0, weight, lo, hi):
    d = 0.26
    return thud(d, f0, f0 * 0.45, 0.045) * weight + hit(d, lo, hi, 0.03, 0.55)


def strain(f0):
    d = 0.5
    creak = np.sign(tone(d, f0, f0 * 1.35, 0.6)) * 0.5 + tone(d, f0 * 2.02, f0 * 2.6) * 0.5
    return band(creak, f0, f0 * 8) * np.hanning(int(d * SR)) * 0.45 + band(noise(d), 250, 900) * np.hanning(int(d * SR)) * 0.08


def boing(f0, f1):
    d = 0.22
    return tone(d, f0, f1, 0.6) * decay(d, 0.08) * attack(np.ones(int(d * SR)), 0.005) + mix(d, (0.0, hit(0.03, 500, 2500, 0.01, 0.4)))


def ping():
    return mix(0.42, (0.0, modal(0.28, [(880, 0.08, 0.7), (2640, 0.02, 0.2)])), (0.1, modal(0.3, [(1175, 0.09, 0.7), (3525, 0.02, 0.2)])))


def respawn():
    d = 0.45
    poof = swept_noise(d, 400, 2200, 700) * decay(d, 0.16) * 0.45
    sparkle = sum(mix(d, (0.05 + 0.05 * k, modal(0.2, [(1047 * 2 ** (k / 4), 0.04, 0.3)]))) for k in range(5))
    return attack(poof, 0.01) + sparkle


# ------------------------------------------------------------------ Items

def impact_bucket():
    return modal(0.35, [(520, 0.08, 0.7), (1180, 0.06, 0.5), (2050, 0.03, 0.3)]) + mix(0.35, (0.0, hit(0.04, 400, 4000, 0.01, 0.7)))


def impact_matches():
    parts = [(0.0, hit(0.04, 400, 2500, 0.012, 0.8))]
    for _ in range(12):
        parts.append((rng.uniform(0.0, 0.14), hit(0.015, 2500, 8000, 0.004, rng.uniform(0.2, 0.45))))
    return mix(0.2, *parts)


def impact_sausage():
    return thud(0.16, 210, 90, 0.035) + mix(0.16, (0.0, hit(0.03, 300, 1400, 0.012, 0.5)))


def wood(f0, level=1.0, tail=0.05):
    return (modal(0.22, [(f0, tail, 0.8), (f0 * 2.3, tail * 0.6, 0.5), (f0 * 4.1, tail * 0.3, 0.25)])
            + mix(0.22, (0.0, hit(0.03, 300, 3000, 0.008, 0.7)))) * level


def log_snap():
    return mix(0.3, (0.0, wood(170)), (0.0, thud(0.2, 120, 60, 0.05) * 0.8), (0.07, wood(320, 0.45, 0.025)))


def log_unsnap():
    return mix(0.26, (0.0, wood(290, 0.5, 0.025)), (0.05, wood(210, 0.7, 0.035)))


def fire_ready():
    d = 0.9
    whoof = swept_noise(d, 350, 1000, 450) * np.hanning(int(d * SR)) ** 0.6 * 0.55
    return mix(1.0, (0.0, whoof), (0.22, modal(0.5, [(659, 0.14, 0.45), (1319, 0.08, 0.2)])), (0.34, modal(0.5, [(988, 0.16, 0.5), (1976, 0.08, 0.2)])))


def watched():
    pulse = lambda f: tone(0.16, f) * np.hanning(int(0.16 * SR)) + tone(0.16, f * 1.5) * np.hanning(int(0.16 * SR)) * 0.4
    return mix(0.4, (0.0, pulse(392)), (0.18, pulse(466)))


def break_press():
    return hit(0.05, 700, 5000, 0.008, 0.9) + modal(0.05, [(1150, 0.01, 0.5)])


def break_free():
    d = 0.5
    rise = tone(d, 440, 1320, 0.6) * decay(d, 0.16) * attack(np.ones(int(d * SR)), 0.01) * 0.7
    return rise + mix(d, (0.0, hit(0.05, 800, 6000, 0.012, 0.8)))


# ------------------------------------------------------------------ Leaves

def rustle(d, count, level):
    out = np.zeros(int(d * SR))
    for _ in range(count):
        length = rng.uniform(0.03, 0.09)
        burst = band(noise(length), 1800, 8500) * np.hanning(int(length * SR)) * rng.uniform(0.3, 1.0)
        i = int(rng.uniform(0, d - length) * SR)
        out[i:i + len(burst)] += burst
    return out * np.hanning(len(out)) ** 0.4 * level


# ------------------------------------------------------------------ Missions

def butterfly_catch():
    notes = [(0.0, 1568), (0.06, 2093)]
    return mix(0.36, (0.0, band(noise(0.1), 1500, 6000) * decay(0.1, 0.03) * 0.5), *[(s, modal(0.28, [(f, 0.07, 0.6)])) for s, f in notes])


def mission_step():
    return mix(0.42, (0.0, modal(0.3, [(784, 0.09, 0.6), (1568, 0.05, 0.2)])), (0.09, modal(0.3, [(1175, 0.1, 0.7), (2350, 0.05, 0.2)])))


def mission_complete():
    notes = [(0.0, 523), (0.1, 659), (0.2, 784), (0.32, 1047)]
    return mix(1.0, *[(s, modal(0.6, [(f, 0.2, 0.6), (f * 2, 0.08, 0.2)])) for s, f in notes])


def swarm():
    d = 1.4
    flutter = 0.5 + 0.5 * np.sin(2 * np.pi * 34 * times(d))
    return band(noise(d), 500, 3500) * flutter * np.hanning(int(d * SR)) ** 0.6


# ------------------------------------------------------------------ UI

def ui_click():
    return modal(0.07, [(1250, 0.012, 0.8), (2500, 0.006, 0.3)]) + hit(0.07, 1500, 6000, 0.002, 0.4)


def ui_hover():
    return modal(0.05, [(2100, 0.008, 0.5)])


CLIPS = {
    "photo_shutter": shutter, "photo_raise": photo_raise, "photo_dry": photo_dry, "photo_ready": photo_ready,
    "photo_hit": photo_hit, "locked_tick": locked_tick,
    "balloon_fill": balloon_fill, "balloon_throw": balloon_throw, "balloon_pop": balloon_pop,
    "balloon_splash_a": lambda: splash(0.6, 1.0), "balloon_splash_b": lambda: splash(0.7, 0.8),
    "water_splash_a": lambda: splash(0.45, 0.7), "water_splash_b": lambda: splash(0.5, 0.6),
    "water_wade_a": wade, "water_wade_b": wade, "water_wade_c": wade,
    "cage_lock": cage_lock, "cage_open": cage_open,
    "stone_shatter_a": shatter, "stone_shatter_b": shatter,
    "ceramic_clink_a": lambda: ceramic(2350, 3710), "ceramic_clink_b": lambda: ceramic(2780, 4230),
    "land_camper_a": lambda: land(120, 1.0, 180, 1500), "land_camper_b": lambda: land(105, 1.0, 180, 1300),
    "land_gnome_a": lambda: land(210, 0.8, 350, 2600), "land_gnome_b": lambda: land(240, 0.8, 350, 3000),
    "strain_camper": lambda: strain(95), "strain_gnome": lambda: strain(170),
    "gnome_mount": lambda: boing(260, 620), "gnome_dismount": lambda: boing(560, 210),
    "gnome_ping": ping, "item_respawn": respawn,
    "impact_bucket": impact_bucket, "impact_matches": impact_matches, "impact_sausage": impact_sausage,
    "bush_hide_a": lambda: rustle(0.42, 30, 0.8), "bush_hide_b": lambda: rustle(0.5, 36, 0.8),
    "bush_search_a": lambda: rustle(0.7, 55, 1.0), "bush_search_b": lambda: rustle(0.8, 60, 1.0),
    "ui_click": ui_click, "ui_hover": ui_hover,
    "log_handoff_a": lambda: wood(230), "log_handoff_b": lambda: wood(265),
    "log_snap": log_snap, "log_unsnap": log_unsnap, "fire_ready": fire_ready,
    "watched": watched, "break_press": break_press, "break_free": break_free,
    "butterfly_catch": butterfly_catch, "mission_step": mission_step, "mission_complete": mission_complete, "swarm": swarm,
}


def write(name, signal):
    peak = np.max(np.abs(signal))
    signal = signal / peak * PEAK
    fade = min(len(signal) // 4, int(0.01 * SR))
    signal[-fade:] *= np.linspace(1.0, 0.0, fade)
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(SR)
        out.writeframes((signal * 32767).astype("<i2").tobytes())
    return len(signal) / SR, float(np.sqrt(np.mean(signal ** 2)))


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, make in CLIPS.items():
        length, rms = write(name, make())
        print("{:20s} {:.2f} s  rms {:.3f}".format(name, length, rms))


if __name__ == "__main__":
    main()
