"""Prints a loudness timeline of game clips: overall level and the 25-350 Hz band level per 250 ms.

Usage: python tools/analyze_clip.py <clip name> [<clip name> ...]
Use it before designing a haptic for a sound: it shows when the sound builds up, where its blow is, how long it
rings and whether it has any energy in the band the actuators reproduce. Find clip names with
tools/extract_haptics.py --list <part of the name>. Needs UnityPy.
"""
import io
import math
import os
import struct
import sys
import wave

import UnityPy

names = sys.argv[1:]
env = UnityPy.load(r"D:\Steam\steamapps\common\Outer Wilds\OuterWilds_Data")
clips = {}
for obj in env.objects:
    if obj.type.name == "AudioClip":
        c = obj.read()
        clips.setdefault(c.m_Name, c)


def mono_of(data):
    with wave.open(io.BytesIO(data)) as w:
        ch, rate, n = w.getnchannels(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    vals = struct.unpack("<%dh" % (len(raw) // 2), raw)
    return [sum(vals[i * ch:(i + 1) * ch]) / (ch * 32768.0) for i in range(n)], rate


def band(x, rate):
    lp = 1 - math.exp(-2 * math.pi * 350 / rate)
    hp = 1 - math.exp(-2 * math.pi * 25 / rate)
    l1 = l2 = h = 0.0
    out = []
    for v in x:
        l1 += lp * (v - l1)
        l2 += lp * (l1 - l2)
        h += hp * (l2 - h)
        out.append(l2 - h)
    return out


for name in names:
    c = clips.get(name)
    if c is None:
        print(name, "missing")
        continue
    x, rate = mono_of(next(iter(c.samples.values())))
    b = band(x, rate)
    step = rate // 4
    print("== %s: %.2f s, peak %.2f" % (name, len(x) / rate, max(abs(v) for v in x)))
    line_all, line_band = [], []
    for i in range(0, len(x), step):
        seg = x[i:i + step]
        sb = b[i:i + step]
        line_all.append(math.sqrt(sum(v * v for v in seg) / len(seg)))
        line_band.append(math.sqrt(sum(v * v for v in sb) / len(sb)))
    print("  t(s)   : " + " ".join("%5.2f" % (i * 0.25) for i in range(len(line_all))))
    print("  overall: " + " ".join("%5.2f" % v for v in line_all))
    print("  25-350 : " + " ".join("%5.2f" % v for v in line_band))
