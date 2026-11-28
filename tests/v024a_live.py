"""Live test of 0.24 part A (run_v024a.bat): #69 gravity network edits and network_trace, #70 udp_set / parcel_set /
point_group_action, #71 property_set, #72 source tags, #73 pressure run edits.

Run it in a NEW unsaved drawing (DrawingN, after MCPALLOW). Nothing is saved. Output: tests\\results\\v024a.txt
"""
import json
import sys
import time
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
TARGET = call("get_drawing_info", {}).get("target")
name = Path(call("ping").get("activeDrawing") or "").name.lower()
if not (name.startswith("drawing") or "mcp_test" in name):
    sys.exit(f"REFUSED: the active drawing is '{name}'. Run this in a new unsaved drawing (DrawingN).")
tag = uuid.uuid4().hex[:4]
ox, oy = 40000.0, 40000.0


def check(title, ok, detail=""):
    RESULTS[title] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[title]:5} {title} {detail}", flush=True)


def show(title, out, cap=2500):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < cap else text[:cap] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, quiet=False, **params):
    try:
        out = call(method, {**params, "target": TARGET})
    except Exception as exc:     # noqa: BLE001
        out = {"_error": str(exc)}
    return out if quiet else show(method, out)


def edit(title, ops, must=True):
    try:
        out = call("edit", {"operations": ops, "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    if must:
        check(title, out.get("applied") is True, str(out.get("error") or out.get("exception") or ""))
    return out


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


def guarded(fn):
    try:
        fn()
    except Exception as exc:     # noqa: BLE001
        check(f"{fn.__name__} ran without a test-script error", False, f"{type(exc).__name__}: {exc}")


# ---------------------------------------------------------------- fixtures
eg, al = f"EG {tag}", f"AL {tag}"
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(-1, 13) for j in range(-6, 7)]
edit("fixture: surface and alignment", [
    {"op": "surface_create", "name": eg, "points": grid},
    {"op": "alignment_create", "name": al, "points": [[ox, oy + 3], [ox + 200, oy + 3]]}])
lists = read("list_gravity_parts", quiet=True).get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
key = plist.get("name") or plist["handle"]
parts = read("list_gravity_parts", quiet=True, parts_list=key)
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes") and not any(w in f["family"].lower() for w in ("box", "rect", "ellip", "egg", "arch"))]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
P, S = pf[0]["family"], sf[0]["family"]
NET, NET2 = f"Net {tag}", f"Net2 {tag}"
structs = [{"name": f"M{i}-{tag}", "at": [ox + 20 + 50 * i, oy, 12.0 + i], "family": S, "size_index": 0,
            "source_id": f"MH-{i}"} for i in range(3)]
pipes = [{"name": f"P{i}-{tag}", "start": [ox + 20 + 50 * i, oy, 8.0 + 0.5 * i], "end": [ox + 70 + 50 * i, oy, 8.5 + 0.5 * i],
          "family": P, "size_index": 0, "start_structure": f"M{i}-{tag}", "end_structure": f"M{i + 1}-{tag}",
          "source_id": f"CO-{i}"} for i in range(2)]
out = edit("fixture: gravity network (3 structures, 2 pipes, tagged) and an empty second network", [
    {"op": "pipe_network_create", "name": NET, "parts_list": key},
    {"op": "pipe_network_add", "network": NET, "structures": structs, "pipes": pipes, "source": "TEST model.xlsx", "run": f"run {tag}"},
    {"op": "pipe_network_create", "name": NET2, "parts_list": key}])
created = {c["name"]: c["handle"] for c in result(out, 1).get("created", []) if c.get("name")}
plists = [l for l in read("list_pressure_part_sizes", quiet=True).get("partsLists", []) if isinstance(l.get("pipeSizes"), int) and l["pipeSizes"] > 0]
best = max(plists, key=lambda l: l["pipeSizes"])
PNET, RUN = f"PN {tag}", f"RUN {tag}"
edit("fixture: pressure run with two bends", [
    {"op": "pressure_network_create", "name": PNET, "parts_list": best.get("name") or best["handle"], "reference_surface": eg},
    {"op": "pressure_pipe_run", "network": PNET, "name": RUN, "points": [[ox + 10, oy + 3], [ox + 80, oy + 3], [ox + 140, oy + 10], [ox + 190, oy + 10]],
     "size": 150, "match": "ductile", "cover": 1.2}])


def pipe(name):
    rows = read("list_pipes", quiet=True, network_name=NET, limit=200).get("pipes", [])
    return next((r for r in rows if r.get("name") == name), {})


# ---------------------------------------------------------------- #69
def gravity_69():
    tr = read("network_trace", **{"from": f"M0-{tag}", "to": f"M2-{tag}", "network": NET})
    check("#69 network_trace M0 -> M2: 5 parts (3 structures, 2 pipes)", tr.get("connected") is True and tr.get("count") == 5, str(tr.get("count")))
    check("#69 network_trace length about 100 m", abs((tr.get("length") or 0) - 100) < 6, str(tr.get("length")))
    tr2 = read("network_trace", quiet=True, **{"from": f"M0-{tag}", "to": f"M2-{tag}"})
    check("#69 network_trace without network (unique names)", tr2.get("connected") is True)

    o = edit("#69 pipe_break P0 at 25 m with a new structure", [{"op": "pipe_break", "pipe": f"P0-{tag}", "network": NET, "distance": 25,
                                                                  "family": S, "size_index": 0, "structure_name": f"MB-{tag}",
                                                                  "new_pipe_name": f"P0b-{tag}"}])
    r = result(o)
    check("#69 pipe_break made a structure and a second pipe", (r.get("structure") or {}).get("created") is True and (r.get("newPipe") or {}).get("name") == f"P0b-{tag}", str(r)[:300])
    s1, s2 = (r.get("pipe") or {}).get("slope"), (r.get("newPipe") or {}).get("slope")
    check("#69 both halves keep the grade", s1 is not None and s2 is not None and abs(s1 - s2) < 1e-4, f"{s1} {s2}")
    tr3 = read("network_trace", quiet=True, **{"from": f"M0-{tag}", "to": f"M2-{tag}", "network": NET})
    check("#69 trace now passes the new structure (7 parts)", tr3.get("count") == 7, str(tr3.get("count")))

    o = edit("#69 pipe_regrade P1 to 1 % holding the start", [{"op": "pipe_regrade", "pipe": f"P1-{tag}", "network": NET, "slope": 0.01, "hold": "start"}])
    r = result(o)
    b, a = r.get("before") or {}, r.get("after") or {}
    check("#69 pipe_regrade slope 0.01", a.get("slope") is not None and abs(abs(a["slope"]) - 0.01) < 1e-4, str(a.get("slope")))
    check("#69 pipe_regrade kept the start", b.get("start") == a.get("start"), f"{b.get('start')} {a.get('start')}")

    o = edit("#69 pipe_connect: disconnect the end of P1", [{"op": "pipe_connect", "pipe": f"P1-{tag}", "network": NET, "end": "end", "disconnect": True}])
    check("#69 disconnected", (result(o).get("pipe") or {}).get("endStructure") is None, str(result(o).get("pipe")))
    o = edit("#69 pipe_connect: connect it again", [{"op": "pipe_connect", "pipe": f"P1-{tag}", "network": NET, "end": "end", "structure": f"M2-{tag}"}])
    check("#69 connected again", (result(o).get("pipe") or {}).get("endStructure") == f"M2-{tag}", str(result(o).get("pipe")))

    o = edit("#69 pipe_end_adjust start of P1 to the inner wall", [{"op": "pipe_end_adjust", "pipe": f"P1-{tag}", "network": NET, "end": "start", "location": "inner_wall"}], must=False)
    check("#69 pipe_end_adjust applied", o.get("applied") is True, str(o.get("error")))
    o = edit("#69 structure_resize M1", [{"op": "structure_resize", "structures": [f"M1-{tag}", f"M2-{tag}"], "network": NET}], must=False)
    check("#69 structure_resize applied", o.get("applied") is True, str(o.get("error")))
    o = edit("#69 pipe_flow_direction by slope", [{"op": "pipe_flow_direction", "pipes": [f"P0-{tag}", f"P1-{tag}"], "network": NET, "method": "by_slope"}])
    check("#69 flow direction set", all(x.get("method") == "BySlope" for x in result(o).get("pipes", [])), str(result(o)))
    o = edit("#69 parts_move P0b and MB to the second network", [{"op": "parts_move", "network": NET, "to_network": NET2,
                                                                  "pipes": [f"P0b-{tag}"], "structures": [f"MB-{tag}"]}])
    check("#69 2 parts moved", result(o).get("moved") == 2, str(result(o)))
    nets = {n["name"]: n for n in read("list_pipe_networks", quiet=True).get("pipeNetworks", [])}
    check("#69 second network now has 1 pipe and 1 structure", (nets.get(NET2) or {}).get("pipeCount") == 1 and (nets.get(NET2) or {}).get("structureCount") == 1, str(nets.get(NET2)))
    o = edit("#69 a name in two networks is refused without network", [{"op": "pipe_regrade", "pipe": "nope-" + tag, "slope": 0.01}], must=False)
    check("#69 unknown pipe refused", o.get("applied") is False and "no gravity pipe" in str(o.get("error")), str(o.get("error")))


# ---------------------------------------------------------------- #72
def tags_72():
    t = read("get_source_tags", source="TEST model")
    ids = sorted(x.get("sourceId") for x in t.get("objects", []))
    check("#72 pipe_network_add tagged 3 structures and 2 pipes", {"MH-0", "MH-1", "MH-2", "CO-0", "CO-1"} <= set(ids), str(ids))
    one = read("get_source_tags", quiet=True, source_id="MH-1")
    check("#72 filter by source_id", one.get("total") == 1 and (one.get("objects") or [{}])[0].get("run") == f"run {tag}", str(one))
    h = created.get(f"M1-{tag}")
    edit("#72 source_tag_set changes a tag", [{"op": "source_tag_set", "source": "OTHER", "items": [{"handle": h, "source_id": "X-9"}]}])
    t2 = read("get_source_tags", quiet=True, handles=[h])
    check("#72 tag replaced", (t2.get("objects") or [{}])[0].get("sourceId") == "X-9", str(t2))
    edit("#72 clear", [{"op": "source_tag_set", "clear": True, "items": [{"handle": h}]}])
    check("#72 tag cleared", read("get_source_tags", quiet=True, handles=[h]).get("total") == 0)
    o = edit("#72 source_id without source is refused", [{"op": "source_tag_set", "items": [{"handle": h, "source_id": "Y"}]}], must=False)
    check("#72 missing source refused", o.get("applied") is False and "source" in str(o.get("error")), str(o.get("error")))


# ---------------------------------------------------------------- #71
def props_71():
    o = edit("#71 property_set on the alignment: starting chainage 1000", [{"op": "property_set", "handle": read("list_alignments", quiet=True)["alignments"][0]["handle"] if False else ALH,
                                                                          "properties": {"reference_point_station": 1000}}])
    r = (result(o).get("changed") or [{}])[0]
    check("#71 reference_point_station 1000", abs((r.get("after") or 0) - 1000) < 1e-6, str(r))
    al_row = next((a for a in read("list_alignments", quiet=True).get("alignments", []) if a.get("name") == al), {})
    check("#71 alignment now starts at 1000", abs((al_row.get("startStation") or 0) - 1000) < 1e-3, str(al_row))
    ph = created.get(f"P1-{tag}")
    o = edit("#71 property_set pipe flow_direction_method", [{"op": "property_set", "handle": ph, "properties": {"flow_direction_method": "end_to_start"}}])
    check("#71 enum by snake_case name", (result(o).get("changed") or [{}])[0].get("after") == "EndToStart", str(result(o)))
    o = edit("#71 property not on the allowlist", [{"op": "property_set", "handle": ph, "properties": {"start_point": [0, 0, 0]}}], must=False)
    check("#71 not allowed refused", o.get("applied") is False and "cannot be set" in str(o.get("error")), str(o.get("error")))
    o = edit("#71 wrong type", [{"op": "property_set", "handle": ph, "properties": {"return_period": "ten"}}], must=False)
    check("#71 wrong type refused", o.get("applied") is False and "takes" in str(o.get("error")), str(o.get("error")))
    sh = created.get(f"M0-{tag}")
    o = edit("#71 structure sump_depth", [{"op": "property_set", "handle": sh, "properties": {"sump_depth": 0.3}}], must=False)
    check("#71 structure sump_depth set", o.get("applied") is True, str(o.get("error")))


# ---------------------------------------------------------------- #70
def data_70():
    edit("#70 fixture: points, point group, point UDPs", [
        {"op": "cogo_points_add", "points": [{"x": ox + 5, "y": oy + 50, "z": 10, "description": f"T{tag}"},
                                             {"x": ox + 15, "y": oy + 50, "z": 11, "description": f"T{tag}"}]},
        {"op": "udp_create", "classification": f"C{tag}", "properties": [
            {"name": "Depth", "type": "double"}, {"name": "Code", "type": "string"},
            {"name": "Kind", "type": "enum", "values": ["MH", "GV"]}, {"name": "Ok", "type": "boolean"}]},
        {"op": "point_group_create", "name": f"G{tag}", "include_raw_descriptions": f"T{tag}"}])
    pts = [p for p in read("list_cogo_points", quiet=True, limit=5000).get("points", []) if p.get("description") == f"T{tag}"]
    nums = [p["number"] for p in pts]
    check("#70 fixture has 2 points", len(nums) == 2, str(nums))
    o = edit("#70 udp_set one value for both points", [{"op": "udp_set", "classification": f"C{tag}", "property": "Depth", "value": 1.75, "points": nums}])
    check("#70 udp_set count 2", result(o).get("count") == 2, str(result(o)))
    edit("#70 udp_set per point and enum / bool", [
        {"op": "udp_set", "classification": f"C{tag}", "property": "Code", "values": {str(nums[0]): "A1", str(nums[1]): "B2"}},
        {"op": "udp_set", "classification": f"C{tag}", "property": "Kind", "value": "gv", "points": nums},
        {"op": "udp_set", "classification": f"C{tag}", "property": "Ok", "value": True, "points": nums[:1]}])
    vals = read("get_user_property_values", quiet=True, classification=f"C{tag}", numbers=nums)
    text = json.dumps(vals)
    check("#70 values read back (1.75, A1, B2, GV)", "1.75" in text and "A1" in text and "B2" in text and "GV" in text, text[:400])
    o = edit("#70 wrong enum value refused", [{"op": "udp_set", "classification": f"C{tag}", "property": "Kind", "value": "XX", "points": nums}], must=False)
    check("#70 enum check", o.get("applied") is False and "not one of" in str(o.get("error")), str(o.get("error")))
    o = edit("#70 text into a double refused", [{"op": "udp_set", "classification": f"C{tag}", "property": "Depth", "value": "deep", "points": nums}], must=False)
    check("#70 type check", o.get("applied") is False and "does not fit" in str(o.get("error")), str(o.get("error")))
    o = edit("#70 point_group_action lock", [{"op": "point_group_action", "group": f"G{tag}", "action": "lock"}])
    check("#70 lock applied", result(o).get("pointsAfter") == 2, str(result(o)))
    edit("#70 unlock", [{"op": "point_group_action", "group": f"G{tag}", "action": "unlock"}])
    o = edit("#70 delete_points without confirm refused", [{"op": "point_group_action", "group": f"G{tag}", "action": "delete_points"}], must=False)
    check("#70 confirm needed", o.get("applied") is False and "confirm" in str(o.get("error")), str(o.get("error")))
    o = edit("#70 delete_points", [{"op": "point_group_action", "group": f"G{tag}", "action": "delete_points", "confirm": True}])
    check("#70 points deleted", result(o).get("pointsAfter") == 0, str(result(o)))
    parcels = read("list_parcels", quiet=True).get("parcels", [])
    if parcels:
        pc = parcels[0]
        o = edit("#70 parcel_set address / tax id", [{"op": "parcel_set", "parcel": pc.get("handle") or pc.get("name"), "address": f"1 Test St {tag}", "tax_id": 4242}])
        check("#70 parcel_set", result(o).get("taxId") == 4242, str(result(o)))
    else:
        print("no parcels in this drawing: parcel_set not tested (creating parcels is not in the API)", flush=True)


# ---------------------------------------------------------------- #73
def pressure_73():
    runs = read("list_pipe_runs", quiet=True, network_name=PNET)
    show("pipe runs", runs, 1200)
    o = edit("#73 remove the bend at the second PI", [{"op": "pressure_run_edit", "network": PNET, "run": RUN, "remove_bends": [[ox + 140, oy + 10]]}], must=False)
    check("#73 remove_bends applied", o.get("applied") is True and result(o).get("bendsRemoved") == 1, str(o.get("error") or result(o)))
    o = edit("#73 vertical offset type", [{"op": "pressure_run_edit", "network": PNET, "run": RUN, "offset_type": "offset_at_bends"}], must=False)
    check("#73 offset_type applied", o.get("applied") is True and result(o).get("verticalOffsetType") == "OffsetAtBends", str(o.get("error") or result(o)))
    o = edit("#73 break the run", [{"op": "pressure_run_edit", "network": PNET, "run": RUN, "break_at": [ox + 50, oy + 3], "new_run_name": f"RUN2 {tag}"}], must=False)
    check("#73 break_at made a second run", o.get("applied") is True and result(o).get("newRun"), str(o.get("error") or result(o)))
    runs2 = read("list_pipe_runs", quiet=True, network_name=PNET)
    check("#73 network has two runs", len(runs2.get("pipeRuns", [])) == 2, str([r.get("name") for r in runs2.get("pipeRuns", [])]))


ALH = next((a["handle"] for a in read("list_alignments", quiet=True).get("alignments", []) if a.get("name") == al), None)
for fn in (gravity_69, tags_72, props_71, data_70, pressure_73):
    try:
        fn()
    except Exception as exc:     # noqa: BLE001
        check(f"{fn.__name__} ran without a test-script error", False, f"{type(exc).__name__}: {exc}")

print()
fails = [k for k, v in RESULTS.items() if v != "PASS"]
print(f"{len(RESULTS) - len(fails)} / {len(RESULTS)} PASS")
print("ALL PASS" if not fails else "FAILED: " + "; ".join(fails))
