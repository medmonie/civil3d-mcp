"""Live test of 0.24 part B (run_v024b.bat): #74 labels, #75 TIN editing, #77 alignment extras, #78 plan production /
Civil tables reads, #79 AutoCAD extras.

Run it in a NEW unsaved drawing (DrawingN, after MCPALLOW). Nothing is saved. Output: tests\\results\\v024b.txt
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
grid = [[ox + i * 10, oy + j * 10, 10 + 0.01 * i * 10 + 0.05 * ((i * 7 + j * 3) % 5)] for i in range(0, 21) for j in range(-5, 6)]
edit("fixture: surface, alignment with a curve, design profile", [
    {"op": "surface_create", "name": eg, "points": grid},
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 100, oy], [ox + 200, oy + 50]], "radius": 60},
    {"op": "profile_create", "alignment": al, "name": fg, "pvis": [[0, 10.5], {"station": 100, "elevation": 12.5, "length": 30}, [200, 11.5]]}])
lists = read("list_gravity_parts", quiet=True).get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
key = plist.get("name") or plist["handle"]
parts = read("list_gravity_parts", quiet=True, parts_list=key)
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes") and not any(w in f["family"].lower() for w in ("box", "rect", "ellip", "egg", "arch"))]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
P, S = pf[0]["family"], sf[0]["family"]
NET, PV = f"Net {tag}", f"PV {tag}"
structs = [{"name": f"M{i}-{tag}", "at": [ox + 10 + 40 * i, oy, 11.0 + 0.3 * i], "family": S, "size_index": 0} for i in range(3)]
pipes = [{"name": f"P{i}-{tag}", "start": [ox + 10 + 40 * i, oy, 8.0 + 0.3 * i], "end": [ox + 50 + 40 * i, oy, 8.3 + 0.3 * i],
          "family": P, "size_index": 0, "start_structure": f"M{i}-{tag}", "end_structure": f"M{i + 1}-{tag}"} for i in range(2)]
edit("fixture: network along the alignment and a profile view showing it", [
    {"op": "pipe_network_create", "name": NET, "parts_list": key},
    {"op": "pipe_network_add", "network": NET, "structures": structs, "pipes": pipes},
    {"op": "profile_view_create", "alignment": al, "name": PV, "at": [ox, oy - 300]},
    {"op": "profile_view_add_parts", "profile_view": PV, "pipe_network": NET}])


def tin_75():
    tri = read("tin_triangles", surface=eg, window=[[ox + 40, oy - 10], [ox + 60, oy + 10]], limit=50)
    check("#75 tin_triangles in a window", (tri.get("total") or 0) > 0 and len((tri.get("triangles") or [[0]])[0]) == 3, str(tri.get("total")))
    v = read("tin_vertices", quiet=True, surface=eg, polygon=[[ox + 35, oy - 15], [ox + 65, oy - 15], [ox + 65, oy + 15], [ox + 35, oy + 15]])
    check("#75 tin_vertices inside a polygon (9 = 3 x 3 grid points)", v.get("total") == 9, str(v.get("total")))
    z0 = read("get_surface_elevation", quiet=True, surface_name=eg, x=ox + 50, y=oy).get("elevation")
    o = edit("#75 surface_tin_edit set one vertex 2 m higher, add a vertex, swap an edge", [{"op": "surface_tin_edit", "surface": eg,
         "set_elevations": [[ox + 50, oy, (z0 or 10) + 2]], "add_vertices": [[ox + 55, oy + 5, 13.0]], "swap_edges": [[ox + 75, oy + 5]]}], must=False)
    check("#75 surface_tin_edit applied", o.get("applied") is True, str(o.get("error")))
    z1 = read("get_surface_elevation", quiet=True, surface_name=eg, x=ox + 50, y=oy).get("elevation")
    check("#75 vertex elevation changed by 2 m", z0 is not None and z1 is not None and abs(z1 - z0 - 2) < 0.01, f"{z0} -> {z1}")
    o = edit("#75 raise inside a polygon by 0.5", [{"op": "surface_tin_edit", "surface": eg, "raise_inside": [[ox + 135, oy - 15], [ox + 165, oy - 15], [ox + 165, oy + 15], [ox + 135, oy + 15]], "raise_by": 0.5}], must=False)
    check("#75 raise_inside raised 9 vertices", o.get("applied") is True and ((result(o).get("done") or {}).get("verticesRaised") == 9), str(o.get("error") or result(o).get("done")))
    o = edit("#75 delete a vertex far from any", [{"op": "surface_tin_edit", "surface": eg, "delete_vertices": [[ox + 3, oy + 3]]}], must=False)
    check("#75 a point with no vertex nearby is refused", o.get("applied") is False and "no TIN vertex" in str(o.get("error")), str(o.get("error")))
    n0 = read("get_drawing_info", quiet=True)
    o = edit("#75 surface_simplify 30 %", [{"op": "surface_simplify", "surface": eg, "percent": 30}], must=False)
    r = result(o)
    print("simplify:", r, flush=True)
    b, a = (r.get("before") or {}).get("triangles"), (r.get("after") or {}).get("triangles")
    check("#75 surface_simplify removed triangles", o.get("applied") is True and b and a and a < b, f"{b} -> {a} {o.get('error')}")
    o = edit("#75 surface_smooth on a grid", [{"op": "surface_smooth", "surface": eg, "region": [[ox + 20, oy - 30], [ox + 80, oy - 30], [ox + 80, oy + 30], [ox + 20, oy + 30]], "spacing": 5}], must=False)
    check("#75 surface_smooth applied", o.get("applied") is True, str(o.get("error") or result(o)))
    o = edit("#75 minimize flat areas", [{"op": "surface_minimize_flat_areas", "surface": eg}], must=False)
    check("#75 minimize flat areas applied", o.get("applied") is True, str(o.get("error")))
    pl = edit("#75 fixture: closed polyline", [{"op": "draw_polyline", "points": [[ox + 100, oy - 40], [ox + 140, oy - 40], [ox + 140, oy - 10], [ox + 100, oy - 10]], "closed": True}])
    h = result(pl).get("handle")
    o = edit("#75 surface_mask_add", [{"op": "surface_mask_add", "surface": eg, "name": f"Mask {tag}", "polyline": h}], must=False)
    check("#75 mask added", o.get("applied") is True and result(o).get("masks") == 1, str(o.get("error") or result(o)))


def alignment_77():
    st = read("alignment_stations", alignment=al)
    check("#77 geometry stations (start, PC, PT, end at least 4)", (st.get("total") or 0) >= 4, str(st.get("total")))
    iv = read("alignment_stations", quiet=True, alignment=al, kind="interval", interval=20)
    check("#77 stations every 20 m", (iv.get("total") or 0) >= 10, str(iv.get("total")))
    pe = read("get_profile_elevation", quiet=True, alignment_name=al, profile_name=fg, station=50)
    check("#77 grade at station 50 is 2 %", pe.get("gradePercent") is not None and abs(pe["gradePercent"] - 2.0) < 0.01, str(pe.get("gradePercent")))
    edit("#77 fixture: offset alignment", [{"op": "alignment_offset_create", "parent": al, "offset": 10, "name": f"OFF {tag}"}], must=False)
    d = read("alignment_distance", quiet=True, alignment=al, other=f"OFF {tag}", stations=[20, 60])
    dists = [r.get("distance") for r in d.get("rows", [])]
    check("#77 alignment_distance about 10 m", len(dists) == 2 and all(x is not None and abs(abs(x) - 10) < 0.05 for x in dists), str(d.get("rows") or d))
    o = edit("#77 alignment_to_polyline", [{"op": "layer_create", "name": f"AL-PL {tag}"},
                                            {"op": "alignment_to_polyline", "alignment": al, "layer": f"AL-PL {tag}"}], must=False)
    check("#77 polyline drawn", o.get("applied") is True and result(o, 1).get("handle"), str(o.get("error") or result(o, 1)))
    o = edit("#77 reverse refused while profiles exist", [{"op": "alignment_reverse", "alignment": al}], must=False)
    check("#77 reverse needs confirm", o.get("applied") is False and "confirm" in str(o.get("error")), str(o.get("error")))
    edit("#77 fixture: plain alignment", [{"op": "alignment_create", "name": f"R {tag}", "points": [[ox, oy + 100], [ox + 50, oy + 100]]}])
    o = edit("#77 reverse a plain alignment", [{"op": "alignment_reverse", "alignment": f"R {tag}"}], must=False)
    r = result(o)
    check("#77 start and end swapped", o.get("applied") is True and r.get("start", [0])[0:2] == r.get("endBefore", [1])[0:2], str(o.get("error") or r))


def labels_74():
    o = edit("#74 alignment station and geometry labels", [{"op": "alignment_labels_add", "alignment": al, "major_interval": 50, "minor_interval": 10, "geometry_points": True}], must=False)
    check("#74 three label groups", o.get("applied") is True and len(result(o).get("labelGroups", [])) == 3, str(o.get("error") or result(o)))
    o = edit("#74 profile grade labels", [{"op": "profile_grade_labels_add", "profile_view": PV, "profile": fg}], must=False)
    check("#74 profile grade labels applied", o.get("applied") is True, str(o.get("error")))
    o = edit("#74 spanning pipe label in plan", [{"op": "spanning_pipe_label_add", "network": NET, "pipes": [f"P0-{tag}", f"P1-{tag}"]}], must=False)
    check("#74 spanning label in plan", o.get("applied") is True and result(o).get("handle"), str(o.get("error")))
    o = edit("#74 spanning pipe label in the profile view", [{"op": "spanning_pipe_label_add", "network": NET, "pipes": [f"P0-{tag}", f"P1-{tag}"], "profile_view": PV}], must=False)
    check("#74 spanning label in profile", o.get("applied") is True and result(o).get("handle"), str(o.get("error")))
    pvs = [v for v in read("list_profile_views", quiet=True).get("profileViews", []) if v.get("name") == PV]
    ext = (pvs[0].get("extents") if pvs else None) or {"min": [ox, oy - 300]}
    x0, y0 = ext["min"][0], ext["min"][1]
    o = edit("#74 profile view depth label", [{"op": "profile_view_depth_label_add", "profile_view": PV, "from": [x0 + 50, y0 + 10], "to": [x0 + 50, y0 + 30]}], must=False)
    check("#74 depth label applied", o.get("applied") is True, str(o.get("error")))


def reads_78():
    v = read("list_view_frames")
    check("#78 list_view_frames answers (none in a new drawing)", v.get("count") == 0 and "viewFrameGroups" in v, str(v)[:200])
    t = read("list_civil_tables")
    check("#78 list_civil_tables answers", "pointTables" in t and "_error" not in t, str(t)[:200])


def cad_79():
    o = edit("#79 spline, ellipse, wipeout", [
        {"op": "draw_spline", "points": [[ox, oy + 200, 0], [ox + 10, oy + 210, 0], [ox + 20, oy + 200, 0], [ox + 30, oy + 210, 0]]},
        {"op": "draw_ellipse", "center": [ox + 50, oy + 200, 0], "major_axis": [10, 0], "ratio": 0.5},
        {"op": "draw_wipeout", "points": [[ox + 70, oy + 195], [ox + 80, oy + 195], [ox + 80, oy + 205], [ox + 70, oy + 205]]}], must=False)
    check("#79 three entities drawn", o.get("applied") is True and len(o.get("results", [])) == 3, str(o.get("error")))
    hs = [r["result"]["handle"] for r in o.get("results", [])] if o.get("applied") else []
    types = sorted(x.get("type") for x in read("get_objects", quiet=True, handles=hs).get("objects", [])) if hs else []
    check("#79 types SPLINE, ELLIPSE, WIPEOUT", types == ["ELLIPSE", "SPLINE", "WIPEOUT"], str(types))
    o = edit("#79 field text: file name and date", [
        {"op": "draw_field_text", "at": [ox, oy + 230, 0], "field": "filename", "prefix": "File: "},
        {"op": "draw_field_text", "at": [ox, oy + 240, 0], "field": "date", "format": "yyyy"}], must=False)
    vals = [r["result"].get("value") for r in o.get("results", [])] if o.get("applied") else []
    print("field values:", vals, flush=True)
    check("#79 field values evaluated (file name, year)", len(vals) == 2 and "Drawing" in str(vals[0]) and str(vals[1]).strip().startswith("20"), str(vals) + str(o.get("error")))
    if hs:
        o = edit("#79 group_create", [{"op": "group_create", "name": f"G{tag}", "handles": hs[:2]}])
        check("#79 group with 2 members", result(o).get("members") == 2, str(result(o)))
        edit("#79 group_edit add", [{"op": "group_edit", "name": f"G{tag}", "add": hs[2:]}])
        g = [x for x in read("list_groups", quiet=True).get("groups", []) if x.get("name") == f"G{tag}"]
        check("#79 list_groups shows 3 members", bool(g) and g[0].get("members") == 3, str(g))
        edit("#79 ungroup", [{"op": "group_edit", "name": f"G{tag}", "ungroup": True}])
        check("#79 group gone, objects stay", not [x for x in read("list_groups", quiet=True).get("groups", []) if x.get("name") == f"G{tag}"]
              and len(read("get_objects", quiet=True, handles=hs).get("objects", [])) == 3)


def command_76():
    o = edit("#76 command_run regenall and audit (report)", [{"op": "command_run", "command": "regenall"}, {"op": "command_run", "command": "audit"}], must=False)
    check("#76 commands queued", o.get("applied") is True and all(r["result"].get("queued") for r in o.get("results", [])), str(o.get("error")))
    time.sleep(4)
    check("#76 Civil 3D answers after the commands ran", "_error" not in read("ping", quiet=True))
    o = edit("#76 a command not on the list is refused", [{"op": "command_run", "command": "erase all"}], must=False)
    check("#76 not allowed", o.get("applied") is False and "not allowed" in str(o.get("error")), str(o.get("error")))
    o = call("edit", {"operations": [{"op": "command_run", "command": "zoom_extents"}], "target": TARGET, "request_id": str(uuid.uuid4()), "dry_run": True})
    check("#76 dry run queues nothing (validated only)", o.get("dryRun") is True, str(o)[:200])


for fn in (reads_78, cad_79, alignment_77, labels_74, tin_75, command_76):
    try:
        fn()
    except Exception as exc:     # noqa: BLE001
        check(f"{fn.__name__} ran without a test-script error", False, f"{type(exc).__name__}: {exc}")

print()
fails = [k for k, v in RESULTS.items() if v != "PASS"]
print(f"{len(RESULTS) - len(fails)} / {len(RESULTS)} PASS")
print("ALL PASS" if not fails else "FAILED: " + "; ".join(fails))
