"""Builds the release archive of the mod.

Usage: python tools/make_release.py [<output folder>]
Run `dotnet build OuterWilds-Dualsense -c Release` first: the files are taken from the OWML mod folder that the build
deploys into. The archive holds one folder `Blightyboi.OuterWilds-Dualsense/` with DualSenseMod.dll, manifest.json,
default-config.json and LICENSE (forward slashes in the entry names, which PowerShell's Compress-Archive does not give).
config.json and effects.json (local settings) are never included. Default output folder: out/ (ignored by git).
"""
import json
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = r"D:\OWML\Mods\Blightyboi.OuterWilds-Dualsense"
NAME = "Blightyboi.OuterWilds-Dualsense"

out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out")
os.makedirs(out_dir, exist_ok=True)
out = os.path.join(out_dir, NAME + ".zip")

files = [(os.path.join(MOD, n), n) for n in ("DualSenseMod.dll", "manifest.json", "default-config.json")]
files.append((os.path.join(ROOT, "LICENSE"), "LICENSE"))
for src, _ in files:
    if not os.path.exists(src):
        sys.exit("missing: " + src)

if os.path.exists(out):
    os.remove(out)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for src, name in files:
        z.write(src, NAME + "/" + name)

with zipfile.ZipFile(out) as z:
    manifest = json.loads(z.read(NAME + "/manifest.json").decode("utf-8"))
    print("version", manifest["version"])
    for info in z.infolist():
        print("%8d  %s" % (info.file_size, info.filename))
print("saved", out)
