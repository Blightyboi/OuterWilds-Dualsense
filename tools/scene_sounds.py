"""Lists the sounds placed in the game's scenes, grouped by the planet (root body) they sit under.

Usage: python tools/scene_sounds.py <output file> [<part of a root object's name> ...]
Reads every OWAudioSource component in the scene files (clip chosen by AudioType, plus the plain Unity clip if any) and
the full path of its GameObject. This answers "which sounds belong to this planet", which the code alone cannot say because
the loops are assigned in the scenes. Needs UnityPy and the same setup as tools/dump_audio_library.py. Do not commit the output.
"""
import os, sys, struct, collections
import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

game = r"D:\Steam\steamapps\common\Outer Wilds"
env = UnityPy.load(os.path.join(game, "OuterWilds_Data"))
gen = TypeTreeGenerator(env.assets[0].unity_version)
gen.load_local_game(game)
src = r"D:\OuterWilds-Dualsense\audio-library.txt"

type_name = {}
for line in open(src, encoding="utf-8"):
    if "=" in line and "|" in line:
        n, rest = line.split("=", 1)
        first = rest.split("|")[0].strip()
        if first.isdigit():
            type_name[int(first)] = n.strip()

out = open(sys.argv[1], "w", encoding="utf-8")
filters = [f.lower() for f in sys.argv[2:]]
nodes_cache = {}
script_cache = {}
rows = []
for sf in env.assets:
    if sf.name not in ("level1", "level2"):
        continue
    objs = list(sf.objects.values())
    gos, trs, mbs, srcs = {}, {}, [], {}
    for o in objs:
        t = o.type.name
        if t == "GameObject":
            gos[o.path_id] = o
        elif t == "Transform":
            trs[o.path_id] = o
        elif t == "MonoBehaviour":
            mbs.append(o)
        elif t == "AudioSource":
            srcs[o.path_id] = o
    if not gos:
        continue
    go_name = {}
    go_tr = {}
    for pid, o in gos.items():
        try:
            d = o.read()
            go_name[pid] = d.m_Name
        except Exception:
            pass
    tr_of_go, parent = {}, {}
    for pid, o in trs.items():
        try:
            d = o.read()
            g = d.m_GameObject.path_id if d.m_GameObject else 0
            f = d.m_Father.path_id if d.m_Father else 0
            tr_of_go[g] = pid
            parent[pid] = f
        except Exception:
            pass
    go_of_tr = {v: k for k, v in tr_of_go.items()}
    plain = {}
    for pid, o in srcs.items():
        try:
            d = o.read()
            c = d.m_audioClip.read().m_Name if d.m_audioClip and d.m_audioClip.path_id else None
            plain[d.m_GameObject.path_id] = c
        except Exception:
            pass

    def path_of(g):
        parts = []
        tr = tr_of_go.get(g)
        while tr:
            parts.append(go_name.get(go_of_tr.get(tr), "?"))
            tr = parent.get(tr)
        return " < ".join(parts)

    for o in mbs:
        raw = o.get_raw_data()
        if len(raw) < 28:
            continue
        g_pid = struct.unpack("<q", raw[4:12])[0]
        s_fid, s_pid = struct.unpack("<iq", raw[16:28])
        key = (s_fid, s_pid)
        if key not in script_cache:
            try:
                script_cache[key] = PPtr(m_FileID=s_fid, m_PathID=s_pid, assetsfile=sf).read().m_ClassName
            except Exception:
                script_cache[key] = "?"
        if script_cache[key] != "OWAudioSource":
            continue
        try:
            if "OWAudioSource" not in nodes_cache:
                nodes_cache["OWAudioSource"] = gen.get_nodes_up("Assembly-CSharp", "OWAudioSource")
            tree = o.read_typetree(nodes_cache["OWAudioSource"])
        except Exception as e:
            continue
        t = tree.get("_audioLibraryClip")
        rows.append((sf.name, path_of(g_pid), type_name.get(t, str(t)) if t else "(plain clip: %s)" % plain.get(g_pid)))
    print(sf.name, "sources so far", len(rows), file=sys.stderr)

for fn, path, t in rows:
    root = path.split(" < ")[-1] if path else ""
    if filters and not any(f in path.lower() for f in filters):
        continue
    out.write("%s | %s | %s\n" % (t, path, fn))
out.close()
