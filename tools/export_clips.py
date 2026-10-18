"""Exports game clips as WAV files.

Usage: python tools/export_clips.py <output folder> <clip name> [<clip name> ...]
Needs UnityPy. The files are derived from the game's assets: do not commit them (*.wav is in .gitignore).
"""
import os
import sys

import UnityPy

out = sys.argv[1]
names = set(sys.argv[2:])
os.makedirs(out, exist_ok=True)
env = UnityPy.load(r"D:\Steam\steamapps\common\Outer Wilds\OuterWilds_Data")
done = set()
for obj in env.objects:
    if obj.type.name != "AudioClip":
        continue
    c = obj.read()
    if c.m_Name in names and c.m_Name not in done:
        for fname, data in c.samples.items():
            path = os.path.join(out, fname if fname.lower().endswith(".wav") else c.m_Name + ".wav")
            with open(path, "wb") as f:
                f.write(data)
            print("saved", path, len(data) // 1024, "KB")
        done.add(c.m_Name)
for n in sorted(names - done):
    print("missing", n)
