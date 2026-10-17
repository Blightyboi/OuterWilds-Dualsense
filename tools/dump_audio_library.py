"""Reads the game's sound table (AudioLibrary) straight from the game files: which clips belong to each AudioType.

Usage:
    python tools/dump_audio_library.py <folder with the decompiled AudioType.cs> <output file>

The table is a Unity ScriptableObject that is only readable with type information built from Assembly-CSharp.dll,
so this needs UnityPy and TypeTreeGeneratorAPI (pip install UnityPy TypeTreeGeneratorAPI). The decompiled source
comes from: ilspycmd -p -o <folder> "<game>/OuterWilds_Data/Managed/Assembly-CSharp.dll".
The output (one line per sound: AudioType = id | volume | clips) shows which clip a sound in the game really is,
and tools/analyze_clip.py then measures that clip. Do not commit the output: it is derived from the game's data.
"""
import os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

game = r"D:\Steam\steamapps\common\Outer Wilds"
env = UnityPy.load(os.path.join(game, "OuterWilds_Data"))
gen = TypeTreeGenerator(env.assets[0].unity_version)
gen.load_local_game(game)
nodes = gen.get_nodes_up("Assembly-CSharp", "AudioLibrary")

names = {}
for line in open(os.path.join(sys.argv[1], "AudioType.cs"), encoding="utf-8"):
    line = line.strip().rstrip(",")
    if "=" in line and not line.startswith("//"):
        n, v = [p.strip() for p in line.split("=", 1)]
        if v.isdigit():
            names[int(v)] = n

hit = None
tried = 0
for obj in env.objects:
    if obj.type.name != "MonoBehaviour" or obj.byte_size < 20000:
        continue
    tried += 1
    try:
        tree = obj.read_typetree(nodes)
    except Exception:
        continue
    entries = tree.get("audioEntries") if isinstance(tree, dict) else None
    if entries and len(entries) > 100:
        hit = (obj, tree)
        print("FOUND in", obj.assets_file.name, "path", obj.path_id, "entries", len(entries))
        break
print("tried", tried)
if not hit:
    sys.exit(1)

obj, tree = hit
clip_names = {}
def clip_name(ptr):
    pid = ptr["m_PathID"]
    fid = ptr["m_FileID"]
    if pid == 0:
        return "null"
    key = (fid, pid)
    if key not in clip_names:
        try:
            if fid == 0:
                clip_names[key] = obj.assets_file.objects[pid].read().m_Name
            else:
                ext = obj.assets_file.externals[fid - 1]
                f = env.files[os.path.basename(ext.path)] if os.path.basename(ext.path) in env.files else None
                clip_names[key] = f.objects[pid].read().m_Name if f else "?ext%d:%d" % (fid, pid)
        except Exception as e:
            clip_names[key] = "?%d:%d" % (fid, pid)
    return clip_names[key]

with open(sys.argv[2], "w", encoding="utf-8") as out:
    out.write("# AudioType = id | volume | clips\n")
    for e in sorted(tree["audioEntries"], key=lambda e: e["type"]):
        t = e["type"]
        out.write("%s = %d | %s | %s\n" % (names.get(t, str(t)), t, e.get("volume", 1), ", ".join(clip_name(c) for c in e["clips"])))
print("written", sys.argv[2])
