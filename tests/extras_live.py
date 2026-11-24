"""Live test of the 0.18 additions (run_extras.bat): derived profiles, corridor baselines / regions / surface boundaries,
surface extraction, curved gravity pipes, part swaps, parts in section views, pressure run bends and merges, section
view volume tables.

Run it in a NEW drawing from the Civil 3D metric template (Drawing1, after MCPALLOW); nothing is saved.
Output: tests\\results\\extras.txt
"""
import json
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
TARGET = None


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def show(title, out):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < 2500 else text[:2500] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, **params):
    try:
        return show(method, call(method, {**params, "target": TARGET}))
    except Exception as exc:     # noqa: BLE001
        print(f"===== {method}\nFAILED: {exc}", flush=True)
        check(f"read {method}", False, str(exc))
        return {}


def edit(title, ops):
    try:
        out = call("edit", {"operations": ops, "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    bad = [f"{r.get('op')}: {r.get('error')}" for r in out.get("results", []) if r.get("error")]
    check(title, out.get("applied") is True, "; ".join(bad) or str(out.get("error") or out.get("exception") or ""))
    return out


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


TARGET = call("get_drawing_info", {}).get("target")
tag = uuid.uuid4().hex[:4]
ox, oy = 11000.0, 11000.0          # away from other test data


def near(a, b, tol):
    return a is not None and b is not None and abs(a - b) <= tol


def pressure_list():
    lists = (call("list_pressure_part_sizes", {"target": TARGET}) or {}).get("partsLists", [])
    lists = [l for l in lists if isinstance(l.get("pipeSizes"), int) and l["pipeSizes"] > 0]
    best = max(lists, key=lambda l: l["pipeSizes"]) if lists else None
    return (best.get("name") or best["handle"]) if best else None


# ---------------------------------------------------------------- terrain: z = 10 + 0.02 x'
eg, al = f"EG {tag}", f"AL {tag}"
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(-1, 12) for j in range(-8, 4)]
edit("surface", [{"op": "surface_create", "name": eg, "points": grid}])

# ---------------------------------------------------------------- #12 surface extraction
out = edit("contours at 11, 12, 13 and the border", [
    {"op": "surface_extract", "surface": eg, "what": "contours_at", "elevations": [11, 12, 13], "layer": "0"},
    {"op": "surface_extract", "surface": eg, "what": "border"},
    {"op": "surface_extract", "surface": eg, "what": "contours", "interval": 1, "low": 10, "high": 14}])
check("three contours at given levels", result(out).get("count", 0) >= 3, str(result(out)))
check("border extracted", result(out, 1).get("count", 0) >= 1, str(result(out, 1)))
check("contours 10..14 every 1 m", result(out, 2).get("count", 0) >= 4, str(result(out, 2)))

# ---------------------------------------------------------------- road, corridor
edit("alignment, profile, offset, assembly, corridor", [
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_create", "alignment": al, "name": f"{al} FG", "pvis": [[0, 10.5], [200, 14.5]]},
    {"op": "alignment_offset_create", "name": f"{al} L5", "parent": al, "offset": -5},
    {"op": "assembly_create", "name": f"ASM {tag}", "at": [ox, oy - 300], "subassemblies": [
        {"class": "LaneSuperelevationAOR", "side": "Right"}, {"class": "LaneSuperelevationAOR", "side": "Left"}]},
    {"op": "corridor_create", "name": f"COR {tag}", "alignment": al, "profile": f"{al} FG", "assembly": f"ASM {tag}"},
    {"op": "corridor_surface_add", "corridor": f"COR {tag}", "name": f"COR {tag} Top"},
])

# ---------------------------------------------------------------- #5 derived profiles
out = edit("corridor ETW profile", [
    {"op": "profile_derive", "kind": "corridor_feature_line", "name": f"{al} ETW", "alignment": al, "corridor": f"COR {tag}", "code": "ETW"}])
check("ETW profile made", bool(result(out).get("handle")), str(result(out)))
out = edit("static layout copy of the ETW profile", [
    {"op": "profile_derive", "kind": "static_copy", "name": f"{al} ETW copy", "alignment": al, "profile": f"{al} ETW"}])
check("static copy made", bool(result(out).get("handle")) and near(result(out).get("endStation"), 200, 0.01), str(result(out)))
out = edit("offset profile at -2 %", [
    {"op": "profile_derive", "kind": "offset_slope", "name": f"{al} L5 FG", "offset_alignment": f"{al} L5", "profile": f"{al} FG", "slope_percent": -2}])
ev = read("get_profile_elevation", alignment_name=f"{al} L5", profile_name=f"{al} L5 FG", station=100)
check("offset profile 5 m at -2 % = FG - 0.10 (12.40)", near(ev.get("elevation"), 12.4, 0.01), str(ev))
ev2 = read("get_profile_elevation", alignment_name=al, profile_name=f"{al} ETW", station=100)
check("ETW profile = crown - 2 % x 3.6 (12.428)", near(ev2.get("elevation"), 12.428, 0.01), str(ev2))

# ---------------------------------------------------------------- #6 corridor regions, baselines, surface
out = edit("split region at 100, extra station, frequency", [
    {"op": "corridor_region_edit", "corridor": f"COR {tag}", "region": "RG-1", "add_stations": [{"station": 55, "description": "test"}],
     "frequency": {"tangents": 5}, "split_at": 100}])
regs = result(out).get("regions", [])
check("two regions after the split", len(regs) == 2 and near(regs[0]["end"], 100, 0.01), str(regs))
check("extra station 55 kept", any(55 in (r.get("extraStations") or []) for r in regs), str(regs))
if len(regs) == 2:
    out = edit("merge the regions back", [{"op": "corridor_region_edit", "corridor": f"COR {tag}", "merge": [regs[0]["name"], regs[1]["name"]]}])
    check("one region after the merge", len(result(out).get("regions", [])) == 1, str(result(out).get("regions")))
out = edit("second baseline on the offset alignment", [
    {"op": "corridor_baseline_add", "corridor": f"COR {tag}", "alignment": f"{al} L5", "profile": f"{al} L5 FG", "assembly": f"ASM {tag}"}])
check("baseline with a region", (result(out).get("region") or {}).get("name") == "RG-1", str(result(out)))
out = edit("corridor surface boundary + mask + ETW breaklines", [
    {"op": "corridor_surface_set", "corridor": f"COR {tag}", "surface": f"COR {tag} Top", "feature_line_codes": ["ETW"],
     "boundaries": [{"name": "Extents", "extents": True}],
     "masks": [{"name": "Hole", "points": [[ox + 90, oy - 2], [ox + 110, oy - 2], [ox + 110, oy + 2], [ox + 90, oy + 2]]}]}])
check("boundary and mask on the corridor surface", result(out).get("boundaries") == 1 and result(out).get("masks") == 1, str(result(out)))

# ---------------------------------------------------------------- #15 gravity: curved pipe, swap, section views
lists = read("list_gravity_parts").get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
key = plist.get("name") or plist["handle"]
parts = read("list_gravity_parts", parts_list=key)
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes") and not any(w in f["family"].lower() for w in ("box", "rect", "ellip", "egg", "arch"))]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
net = f"Net {tag}"
if pf and sf:
    P, S = pf[0]["family"], sf[0]["family"]
    edit("network with two manholes", [
        {"op": "pipe_network_create", "name": net, "parts_list": key},
        {"op": "pipe_network_add", "network": net,
         "structures": [{"id": "M1", "name": "M1", "at": [ox + 20, oy + 30, 11.0], "family": S, "size_index": 0},
                        {"id": "M2", "name": "M2", "at": [ox + 120, oy + 30, 13.0], "family": S, "size_index": 0}]}])
    out = edit("curved pipe R 80", [{"op": "pipe_curved_add", "network": net, "name": "C1", "start": [ox + 20, oy + 30, 8.0],
                                     "end": [ox + 120, oy + 30, 9.0], "radius": 80, "family": P, "size_index": 0,
                                     "start_structure": "M1", "end_structure": "M2"}])
    check("curved pipe", result(out).get("type") == "Curved" and near(result(out).get("radius"), 80, 0.5), str(result(out)))
    if len(pf[0]["sizes"]) > 1:
        out = edit("swap pipe to the next size", [{"op": "part_swap", "network": net, "names": ["C1"], "family": P, "size_index": 1}])
        check("pipe swapped", result(out).get("swapped") == 1, str(result(out)))
    edit("sample lines + section views", [
        {"op": "sample_lines_create", "alignment": al, "group": f"SLG {tag}", "stations": [50, 100, 150], "left_width": 40, "right_width": 40, "prefix": f"SL{tag}-"},
        {"op": "section_views_create", "alignment": al, "group": f"SLG {tag}", "at": [ox, oy - 600]}])
    out = edit("network in the section views", [{"op": "parts_to_section_views", "network": net, "alignment": al, "group": f"SLG {tag}"}])
    check("parts added to 3 section views", result(out).get("sectionViews") == 3 and result(out).get("added", 0) >= 3, str(result(out)))
else:
    check("gravity families available", False)

# ---------------------------------------------------------------- #18 section view volume tables
edit("material list", [{"op": "material_list_create", "alignment": al, "group": f"SLG {tag}", "name": f"EW {tag}", "materials": [
    {"name": "Earthwork", "items": [{"surface": eg, "condition": "base"}, {"surface": f"COR {tag} Top", "condition": "compare"}]}]}])
out = edit("total volume tables", [{"op": "section_volume_tables", "alignment": al, "group": f"SLG {tag}", "material_list": f"EW {tag}"}])
check("a table on each section view", result(out).get("tables") == 3, str(result(out)))

# ---------------------------------------------------------------- #16 pressure run bends and merge
pl = pressure_list()
pn = f"PN {tag}"
if pl:
    edit("two pipe runs", [
        {"op": "pressure_network_create", "name": pn, "parts_list": pl, "reference_surface": eg},
        {"op": "pressure_pipe_run", "network": pn, "name": f"RUN-A {tag}", "points": [[ox, oy - 100], [ox + 100, oy - 100]], "size": 150, "match": "ductile", "cover": 1.2},
        {"op": "pressure_pipe_run", "network": pn, "name": f"RUN-B {tag}", "points": [[ox + 100, oy - 100], [ox + 160, oy - 100]], "size": 150, "match": "ductile", "cover": 1.2}])
    out = edit("bend at a PI", [{"op": "pressure_run_edit", "network": pn, "run": f"RUN-A {tag}", "bends": [[ox + 50, oy - 92]]}])
    check("bend added", len(result(out).get("bends", [])) == 1, str(result(out)))
    out = edit("vertical bend", [{"op": "pressure_run_edit", "network": pn, "run": f"RUN-A {tag}", "vertical_bends": [[30, 9.0]]}])
    check("vertical bend added", len(result(out).get("verticalBends", [])) == 1, str(result(out)))
    out = edit("merge RUN-B into RUN-A", [{"op": "pressure_run_edit", "network": pn, "run": f"RUN-A {tag}", "merge": f"RUN-B {tag}"}])
    runs = [r["name"] for r in read("list_pipe_runs", network_name=pn).get("pipeRuns", [])]
    check("one run left", runs == [f"RUN-A {tag}"], str(runs))
else:
    check("pressure parts list available", False)

for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
