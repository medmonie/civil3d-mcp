"""Live test of the 0.15 Civil 3D additions (run_civil.bat).

Run it in a NEW drawing from the Civil 3D metric template (Drawing1, after MCPALLOW); nothing is saved. It builds its
own data: a point file -> points_import, point groups and a description key, a TIN surface with elevation / slope
analysis and waterdrops, an alignment from a polyline and an offset alignment, two gravity networks that cross, their
pipe and structure tables, HGL values with the flood / surcharge flags, and the interference check.
Output: tests\\results\\civil.txt
"""
import json
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
OUT = Path(__file__).resolve().parent / "results"
OUT.mkdir(exist_ok=True)
TARGET = None


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def show(title, out):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < 3000 else text[:3000] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, **params):
    try:
        return show(method, call(method, {**params, "target": TARGET}))
    except Exception as exc:     # noqa: BLE001
        print(f"===== {method}\nFAILED: {exc}", flush=True)
        check(f"read {method}", False, str(exc))
        return {}


def edit(title, ops, expect=True):
    try:
        out = call("edit", {"operations": ops, "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    bad = [f"{r.get('op')}: {r.get('error')}" for r in out.get("results", []) if r.get("error")]
    check(title, (out.get("applied") is True) == expect, "" if out.get("applied") else "; ".join(bad) or str(out.get("error") or out.get("exception")))
    return out


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


info = call("get_drawing_info", {})
TARGET = info.get("target")
print("drawing:", info.get("name") or info.get("drawing"), flush=True)
tag = uuid.uuid4().hex[:4]

# ---------------------------------------------------------------- points
pts = [(1, 1000, 1000, 10.0, "MH1"), (2, 1100, 1000, 11.0, "MH2"), (3, 1100, 1100, 12.5, "TREE"),
       (4, 1000, 1100, 11.5, "MH3"), (5, 1050, 1050, 13.0, "SPOT"), (6, 1150, 1050, 12.0, "SPOT"),
       (7, 950, 1050, 9.5, "SPOT"), (8, 1050, 1150, 14.0, "SPOT"), (9, 1050, 950, 9.0, "SPOT")]
pfile = OUT / f"civil_points_{tag}.csv"
pfile.write_text("\n".join(f"{n},{e},{nn},{z},{d}" for n, e, nn, z, d in pts) + "\n", encoding="ascii")
formats = read("list_point_file_formats").get("formats", [])
fmt = next((f["name"] for f in formats if f["name"].upper().startswith("PENZD") and "comma" in f["name"].lower()), None)
check("PENZD comma format available", fmt is not None, str([f["name"] for f in formats][:10]))
before = len(read("list_cogo_points", limit=5000).get("cogoPoints", []))
if fmt:
    out = edit("points import + description key + point group", [
        {"op": "description_key_create", "key_set": f"MCP {tag}", "code": "MH*", "if_exists": "replace"},
        {"op": "points_import", "path": str(pfile), "format": fmt, "point_group": f"Survey {tag}"},
        {"op": "point_group_create", "name": f"Manholes {tag}", "include_raw_descriptions": "MH*"},
    ])
    check("points imported", result(out, 1).get("imported") == len(pts), str(result(out, 1)))
    check("manhole group has 3 points", result(out, 2).get("points") == 3, str(result(out, 2)))
groups = read("list_point_groups").get("pointGroups", [])
check("point groups listed", any(g["name"] == f"Manholes {tag}" for g in groups))
keys = read("list_description_keys").get("keySets", [])
check("description key listed", any(k.get("keySet") == f"MCP {tag}" and any(x["code"] == "MH*" for x in k["keys"]) for k in keys), str(keys)[:300])

# ---------------------------------------------------------------- surface analysis
surf = f"EG {tag}"
out = edit("surface + analysis + waterdrop", [
    {"op": "surface_create", "name": surf, "points": [[e, n, z] for _, e, n, z, _ in pts]},
    {"op": "surface_analysis_set", "surface": surf, "type": "elevations", "count": 5},
    {"op": "surface_analysis_set", "surface": surf, "type": "slopes",
     "ranges": [{"min": 0, "max": 2, "color": 3}, {"min": 2, "max": 5, "color": 2}, {"min": 5, "max": 100, "color": 1}]},
    {"op": "surface_waterdrop", "surface": surf, "points": [[1080, 1120], [1020, 1080]]},
])
check("5 elevation ranges", len(result(out, 1).get("ranges", [])) == 5, str(result(out, 1).get("ranges")))
check("waterdrops drawn", result(out, 3).get("paths", 0) >= 1, str(result(out, 3)))
ana = read("get_surface_analysis", surface=surf)
check("analysis read back", len(ana.get("elevations") or []) == 5 and len(ana.get("slopesPercent") or []) == 3,
      f"{len(ana.get('elevations') or [])} elevation, {len(ana.get('slopesPercent') or [])} slope ranges")

# ---------------------------------------------------------------- alignments
out = edit("polyline", [{"op": "draw_polyline", "points": [[900, 900], [1000, 950], [1100, 900], [1200, 980]]}])
pl = result(out).get("handle")
al = f"Road {tag}"
out = edit("alignment from polyline + offset", [
    {"op": "alignment_from_polyline", "name": al, "handle": pl, "add_curves": True},
    {"op": "alignment_offset_create", "name": f"{al} L5", "parent": al, "offset": -5},
])
check("alignment has curves", (result(out).get("entities") or 0) > 3, str(result(out)))
check("offset alignment made", result(out, 1).get("offset") == -5, str(result(out, 1)))
se = read("get_superelevation", alignment=al)
check("superelevation read", "criticalStations" in se, str(se.get("type")))
ix = read("list_intersections")
check("intersections listed", "intersections" in ix)

# ---------------------------------------------------------------- networks, tables, HGL, interference
lists = read("list_gravity_parts").get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
key = plist.get("name") or plist["handle"]
parts = read("list_gravity_parts", parts_list=key)
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes")]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
a_net, b_net = f"San {tag}", f"Storm {tag}"
if pf and sf:
    P, S = pf[0]["family"], sf[0]["family"]
    out = edit("two crossing networks", [
        {"op": "pipe_network_create", "name": a_net, "parts_list": key},
        {"op": "pipe_network_add", "network": a_net,
         "structures": [{"id": "A1", "name": "A1", "at": [1000, 1000, 10.0], "family": S, "size_index": 0},
                        {"id": "A2", "name": "A2", "at": [1100, 1000, 10.5], "family": S, "size_index": 0}],
         "pipes": [{"name": "PA1", "start": [1000, 1000, 8.0], "end": [1100, 1000, 7.5], "family": P, "size_index": 0,
                    "start_structure": "A1", "end_structure": "A2"}]},
        {"op": "pipe_network_create", "name": b_net, "parts_list": key},
        {"op": "pipe_network_add", "network": b_net,
         "structures": [{"id": "B1", "name": "B1", "at": [1050, 950, 10.0], "family": S, "size_index": 0},
                        {"id": "B2", "name": "B2", "at": [1050, 1050, 10.2], "family": S, "size_index": 0}],
         "pipes": [{"name": "PB1", "start": [1050, 950, 8.25], "end": [1050, 1050, 8.05], "family": P, "size_index": 0,
                    "start_structure": "B1", "end_structure": "B2"}]},
    ])
    out = edit("pipe and structure tables + HGL", [
        {"op": "network_table", "network": a_net, "kind": "pipes", "at": [1300, 1100]},
        {"op": "network_table", "network": a_net, "kind": "structures", "at": [1300, 1000]},
        {"op": "hgl_set", "network": a_net, "pipes": [{"name": "PA1", "hgl_up": 9.0, "hgl_down": 8.0, "flow": 0.05}],
         "structures": [{"name": "A1", "hgl": 10.4}, {"name": "A2", "hgl": 8.0}]},
    ])
    check("pipe table rows", result(out).get("rows") == 1, str(result(out)))
    check("structure table rows", result(out, 1).get("rows") == 2, str(result(out, 1)))
    hgl = read("network_hgl", network=a_net)
    issues = {f["part"]: f["issue"] for f in hgl.get("flags", [])}
    check("A1 flagged as flooding", "A1" in issues and "rim" in issues["A1"], str(issues))
    check("PA1 flagged as surcharged", issues.get("PA1") == "surcharged", str(issues))
    check("A2 not flagged", "A2" not in issues, str(issues))
    ic = read("network_interference", network1=a_net, network2=b_net, clearance=0.5)
    found = ic.get("conflicts", [])
    check("crossing found", ic.get("found") == 1 and found and found[0]["a"]["pipe"] == "PA1", str(found)[:300])
    if found:
        check("gap below clearance", found[0]["gap"] < 0.5, str(found[0]))
    if found:
        gap = found[0]["gap"]
        check("pipe above is the storm pipe", found[0]["above"] == "PB1", str(found[0]))
        ic0 = read("network_interference", network1=a_net, network2=b_net, clearance=0.0)
        check("clearance 0 reports only clashes", ic0.get("found") == (1 if gap < 0 else 0), f"gap {gap}: {ic0.get('found')}")
else:
    check("gravity families available", False)

print("\nSUMMARY")
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
