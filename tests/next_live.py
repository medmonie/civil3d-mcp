"""Live test of 0.21 (run_next.bat): #52 show_objects, #15 profile_view_labels_add, #16 corridor earthwork over a
station range (get_material_volumes from_station / to_station).

Run it in a NEW drawing from the Civil 3D metric template (DrawingN, after MCPALLOW) and leave Civil 3D visible: the
show_objects checks change the view and the selection. Nothing is saved.
Output: tests\\results\\next.txt
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
eg, al, fg = f"EG {tag}", f"AL {tag}", f"FG {tag}"
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20 + 0.3 * (j % 2)] for i in range(-1, 13) for j in range(-6, 7)]
edit("fixture: surface, alignment, design profile", [
    {"op": "surface_create", "name": eg, "points": grid},
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_create", "alignment": al, "name": fg, "pvis": [[0, 10.5], [100, 13.5], [200, 13.0]]}])

lists = read("list_gravity_parts", quiet=True).get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
key = plist.get("name") or plist["handle"]
parts = read("list_gravity_parts", quiet=True, parts_list=key)
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes") and not any(w in f["family"].lower() for w in ("box", "rect", "ellip", "egg", "arch"))]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
NET, PNET = f"Net {tag}", f"PN {tag}"
structs = [{"name": f"M{i}-{tag}", "at": [ox + 20 + 50 * i, oy, 10.4 + i], "family": sf[0]["family"], "size_index": 0} for i in range(4)]
pipes = [{"name": f"P{i}-{tag}", "start": [ox + 20 + 50 * i, oy, 8.0 + 0.5 * i], "end": [ox + 70 + 50 * i, oy, 8.5 + 0.5 * i],
          "family": pf[0]["family"], "size_index": 0, "start_structure": f"M{i}-{tag}", "end_structure": f"M{i + 1}-{tag}"} for i in range(3)]
out = edit("fixture: gravity network (4 structures, 3 pipes) and a second network not in the view", [
    {"op": "pipe_network_create", "name": NET, "parts_list": key},
    {"op": "pipe_network_add", "network": NET, "structures": structs, "pipes": pipes},
    {"op": "pipe_network_create", "name": NET + " B", "parts_list": key},
    {"op": "pipe_network_add", "network": NET + " B", "pipes": [{"name": f"Q-{tag}", "start": [ox + 20, oy + 40, 8], "end": [ox + 90, oy + 40, 8.5],
                                                                 "family": pf[0]["family"], "size_index": 0}]}])
created = {c["name"]: c["handle"] for c in result(out, 1).get("created", []) if c.get("name")}

plists = [l for l in read("list_pressure_part_sizes", quiet=True).get("partsLists", []) if isinstance(l.get("pipeSizes"), int) and l["pipeSizes"] > 0]
best = max(plists, key=lambda l: l["pipeSizes"])
edit("fixture: pressure run along the alignment with a bend", [
    {"op": "pressure_network_create", "name": PNET, "parts_list": best.get("name") or best["handle"], "reference_surface": eg},
    {"op": "pressure_pipe_run", "network": PNET, "name": f"RUN {tag}", "points": [[ox + 10, oy + 3], [ox + 100, oy + 3], [ox + 180, oy + 8]],
     "size": 150, "match": "ductile", "cover": 1.2}])
PV = f"PV {tag}"
edit("fixture: profile view with both networks", [
    {"op": "profile_view_create", "alignment": al, "name": PV, "at": [ox, oy - 400]},
    {"op": "profile_view_add_parts", "profile_view": PV, "pipe_network": NET},
    {"op": "profile_view_add_parts", "profile_view": PV, "pressure_network": PNET}])


# ---------------------------------------------------------------- #15
def labels_15():
    o1 = edit("#15 profile_view_labels_add (every network shown)", [{"op": "profile_view_labels_add", "profile_view": PV}])
    r1 = result(o1)
    check("#15 3 pipe and 4 structure labels", r1.get("pipeLabels") == 3 and r1.get("structureLabels") == 4, str(r1))
    check("#15 pressure pipes labelled", (r1.get("pressurePipeLabels") or 0) >= 2, str(r1))
    record_fittings = r1.get("fittingLabels")
    print("fitting labels:", record_fittings, flush=True)
    o2 = edit("#15 profile_view_labels_add again", [{"op": "profile_view_labels_add", "profile_view": PV}])
    r2 = result(o2)
    made1 = sum(r1.get(k) or 0 for k in ("pipeLabels", "structureLabels", "pressurePipeLabels", "fittingLabels", "appurtenanceLabels"))
    made2 = sum(r2.get(k) or 0 for k in ("pipeLabels", "structureLabels", "pressurePipeLabels", "fittingLabels", "appurtenanceLabels"))
    check("#15 a re-run adds nothing", made2 == 0 and r2.get("alreadyLabelled") == made1, f"first {made1}, second {r2}")
    o3 = edit("#15 a network that is not in the view", [{"op": "profile_view_labels_add", "profile_view": PV, "pipe_network": NET + " B"}])
    check("#15 warns that the network's parts are not shown", any("not shown" in w for w in o3.get("warnings") or []), str(o3.get("warnings")))
    o4 = edit("#15 bad ratio is refused", [{"op": "profile_view_labels_add", "profile_view": PV, "ratio": 2}], must=False)
    check("#15 ratio outside 0-1 refused", o4.get("applied") is False and "ratio" in str(o4.get("error")), str(o4.get("error")))


# ---------------------------------------------------------------- #52
def show_52():
    a, b = created.get(f"P1-{tag}"), created.get(f"M2-{tag}")
    out = read("show_objects", handles=[a, b, "FFFFFFF"])
    check("#52 zoomed to the objects and selected them", out.get("zoomed") is True and out.get("selected") == 2 and len(out.get("notFound") or []) == 1,
          str({k: out.get(k) for k in ("zoomed", "selected", "space", "notFound")}))
    time.sleep(1)
    sel = read("get_selection")
    got = {o.get("handle") for o in sel.get("objects", sel.get("selection", []))} if isinstance(sel, dict) else set()
    check("#52 Civil 3D's selection is those two objects", got == {a, b}, str(got))
    time.sleep(3)   # time to look at the screen
    out = read("show_objects", window=[[ox - 10, oy - 60], [ox + 210, oy + 60]], select=False)
    sel = read("get_selection", quiet=True)
    n = len(sel.get("objects", sel.get("selection", []))) if isinstance(sel, dict) else -1
    check("#52 zoom to a window and clear the selection", out.get("zoomed") is True and n == 0, f"selection {n}")
    lay = f"MCP-Next-{tag}"
    o = edit("fixture: a line on a layout", [{"op": "layout_create", "name": lay},
                                              {"op": "draw_line", "start": [20, 20, 0], "end": [120, 80, 0], "layout": lay}])
    lh = result(o, 1).get("handle")
    out = read("show_objects", handles=[lh])
    check("#52 an object on a layout makes that layout current", out.get("space") == lay and out.get("zoomed") is True, str(out.get("space")))
    time.sleep(2)
    out = read("show_objects", handles=[a])
    check("#52 back to model space", out.get("space") == "Model", str(out.get("space")))
    out = read("show_objects", handles=[a, lh])
    check("#52 objects in two spaces are refused", "_error" in out and "different spaces" in out["_error"], str(out)[:300])
    out = read("show_objects")
    check("#52 nothing to show is refused", "_error" in out, str(out)[:200])
    read("show_objects", handles=[a], select=False)


# ---------------------------------------------------------------- #16
def earthwork_16():
    asm, cor, top, grp = f"ASM {tag}", f"COR {tag}", f"COR {tag} Top", f"SLG {tag}"
    edit("#16 fixture: assembly, corridor, corridor surface, sample lines every 20 m", [
        {"op": "assembly_create", "name": asm, "at": [ox, oy - 700], "subassemblies": [
            {"class": "BasicLane", "side": "Right"}, {"class": "BasicLane", "side": "Left"}]},
        {"op": "corridor_create", "name": cor, "alignment": al, "profile": fg, "assembly": asm},
        {"op": "corridor_surface_add", "corridor": cor, "name": top},
        {"op": "sample_lines_create", "alignment": al, "group": grp, "interval": 20, "left_width": 6, "right_width": 6, "prefix": f"S{tag}-"}])
    srcs = read("list_surfaces", quiet=True)
    cs = next((x for x in srcs.get("surfaces", []) if x.get("name") == top), {})
    check("#16 the corridor surface is built (has elevations)", cs.get("minElevation") is not None, str(cs))
    edit("#16 material list: existing ground (base) vs corridor surface (compare)", [
        {"op": "material_list_create", "alignment": al, "group": grp, "name": f"EW {tag}", "materials": [
            {"name": "Earthwork", "items": [{"surface": eg, "condition": "base"}, {"surface": top, "condition": "compare"}]}]}])
    full = read("get_material_volumes", alignment=al, group=grp)
    part = read("get_material_volumes", alignment=al, group=grp, from_station=40, to_station=120)

    def mat(o):
        try:
            ml = o["materialLists"][0]
            return ml, (ml.get("materials") or [ml])[0]
        except (KeyError, IndexError, TypeError):
            return {}, {}
    _, mf = mat(full)
    _, mp = mat(part)
    rows = mp.get("stations") or []
    st = [r["station"] for r in rows]
    check("#16 full run computed", (mf.get("totals") or {}).get("cut") is not None, str(mf.get("totals") or full.get("_error")))
    check("#16 the range covers sample lines 40-120 only", st and min(st) >= 40 - 1e-6 and max(st) <= 120 + 1e-6 and len(st) == 5 and part.get("range", {}).get("sampleLines") == 5,
          f"stations {st} range {part.get('range')}")
    t = mp.get("totals") or {}
    s_cut = round(sum(r.get("cutVolume", 0) for r in rows[1:]), 3)
    s_fill = round(sum(r.get("fillVolume", 0) for r in rows[1:]), 3)
    check("#16 range totals = the volumes between the sample lines in the range", abs((t.get("cut") or 0) - s_cut) < 0.01 and abs((t.get("fill") or 0) - s_fill) < 0.01,
          f"totals {t}, sums cut {s_cut} fill {s_fill}")
    tf = mf.get("totals") or {}
    check("#16 range totals do not exceed the full run", (t.get("cut") or 0) <= (tf.get("cut") or 0) + 0.01 and (t.get("fill") or 0) <= (tf.get("fill") or 0) + 0.01
          and (t.get("cut") or 0) + (t.get("fill") or 0) > 0, f"range {t} full {tf}")
    check("#16 no empty-surface warning", not mf.get("warning"), str(mf.get("warning")))
    bad = read("get_material_volumes", alignment=al, group=grp, from_station=120, to_station=40)
    check("#16 a reversed range is refused", "_error" in bad and "greater" in bad["_error"], str(bad)[:200])
    narrow = read("get_material_volumes", alignment=al, group=grp, from_station=41, to_station=59)
    check("#16 a range with fewer than two sample lines is refused", "_error" in narrow and "fewer than two" in narrow["_error"], str(narrow)[:200])


for block in (labels_15, show_52, earthwork_16):
    guarded(block)

save = read("diagnose_save", quiet=True)
check("drawing still savable", save.get("savable") is True, str(save.get("_error") or ""))
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
