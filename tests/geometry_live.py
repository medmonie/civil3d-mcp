"""Live test of the 0.17 additions (run_geometry.bat): material volumes, vertical curves, spirals / station equations /
design speeds, connected alignments, feature line editing, more labels, several profile views, user-defined property
definitions and point export.

Run it in a NEW drawing from the Civil 3D metric template (Drawing1, after MCPALLOW); nothing is saved.
Output: tests\\results\\geometry.txt
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
ox, oy = 8000.0, 8000.0          # away from other test data
OUT = Path(__file__).resolve().parent / "results"


def near(a, b, tol):
    return a is not None and b is not None and abs(a - b) <= tol


# ---------------------------------------------------------------- terrain: EG slopes 10 -> 14 over 200 m, FG flat at 12
eg, fg, al = f"EG {tag}", f"FG {tag}", f"AL {tag}"
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(-1, 12) for j in range(-3, 4)]
flat = [[ox + i * 20, oy + j * 20, 12.0] for i in range(-1, 12) for j in range(-3, 4)]
edit("surfaces", [{"op": "surface_create", "name": eg, "points": grid}, {"op": "surface_create", "name": fg, "points": flat}])

# ---------------------------------------------------------------- #2 vertical curves
edit("alignment, profile with a K curve, sample lines", [
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_create", "alignment": al, "name": f"{al} FG", "pvis": [[0, 10], {"station": 100, "elevation": 14, "k": 10}, [200, 10]]},
    {"op": "sample_lines_create", "alignment": al, "group": f"SLG {tag}", "interval": 50, "left_width": 20, "right_width": 20, "prefix": f"SL{tag}-"},
])
pg = read("get_profile_geometry", alignment=al, profile=f"{al} FG")
crest = [e for e in pg.get("entities", []) if e.get("curve")]
check("crest curve from K: length = K x A = 10 x 8 = 80", len(crest) == 1 and crest[0]["curve"] == "Crest"
      and near(crest[0]["length"], 80, 0.01) and near(crest[0]["k"], 10, 0.01), str(crest))
pv0 = (pg.get("pvis") or [{}])
check("grades +4 % / -4 %", near(pv0[0].get("gradeOut"), 4, 0.001) and near(pv0[-1].get("gradeIn"), -4, 0.001), str(pv0))
edit("PVI edit: curve length 60, new PVI at 30", [
    {"op": "profile_pvis_set", "alignment": al, "profile": f"{al} FG", "pvis": [{"station": 100, "length": 60}, [30, 11.2]]}])
pg = read("get_profile_geometry", alignment=al, profile=f"{al} FG")
crest = [e for e in pg.get("entities", []) if e.get("curve")]
check("curve now 60 m, 4 PVIs", len(pg.get("pvis", [])) == 4 and any(near(e["length"], 60, 0.01) for e in crest), str(crest))

# ---------------------------------------------------------------- #1 material volumes (EG base vs FG compare)
edit("material list", [{"op": "material_list_create", "alignment": al, "group": f"SLG {tag}", "name": f"Earthwork {tag}",
                         "materials": [{"name": "Earthwork", "type": "earthwork",
                                        "items": [{"surface": eg, "condition": "base"}, {"surface": fg, "condition": "compare"}]}]}])
mv = read("get_material_volumes", alignment=al, group=f"SLG {tag}")
ml = (mv.get("materialLists") or [{}])[0]
mat = (ml.get("materials") or [ml])[0]
tot = mat.get("totals") or {}
print("material stations:", mat.get("stations"), flush=True)
check("material list read back", ml.get("materialList") == f"Earthwork {tag}" and ml.get("source") in ("civil3d", "computed"), str(ml.get("source")))
check("cut ~4000 m3 and fill ~4000 m3 (average end area by hand)", near(tot.get("cut"), 4000, 200) and near(tot.get("fill"), 4000, 200), str(tot))

# ---------------------------------------------------------------- #3 spirals, station equations, design speeds
sp = f"SP {tag}"
edit("alignment with spirals", [
    {"op": "alignment_create", "name": sp, "points": [[ox, oy - 400], [ox + 300, oy - 400], [ox + 500, oy - 200]],
     "radius": 150, "spiral_in": 40},
    {"op": "alignment_set", "alignment": sp, "station_equations": [{"back": 100, "ahead": 1000}],
     "design_speeds": [{"station": 0, "speed": 60, "comment": "test"}]},
])
ag = read("get_alignment_geometry", alignment=sp)
parts = [x for e in ag.get("entities", []) for x in e.get("parts", [])]
spirals = [x for x in parts if x["kind"] == "Spiral"]
arcs = [x for x in parts if x["kind"] == "Arc"]
check("two 40 m spirals and a 150 m arc", len(spirals) == 2 and all(near(s["length"], 40, 0.01) for s in spirals)
      and len(arcs) == 1 and near(arcs[0]["radius"], 150, 0.01), str([(x["kind"], x["length"]) for x in parts]))
check("station equation and design speed read back", len(ag.get("stationEquations", [])) == 1
      and near(ag["stationEquations"][0]["stationAhead"], 1000, 0.001) and ag.get("designSpeeds", [{}])[0].get("speed") == 60,
      str(ag.get("stationEquations")) + str(ag.get("designSpeeds")))

# ---------------------------------------------------------------- #4 connected alignment (quarter circle, R 10)
a1, a2, cr = f"A1 {tag}", f"A2 {tag}", f"CR {tag}"
edit("two roads + curb return", [
    {"op": "alignment_create", "name": a1, "points": [[ox, oy + 600], [ox + 200, oy + 600]]},
    {"op": "alignment_create", "name": a2, "points": [[ox + 100, oy + 500], [ox + 100, oy + 700]]},
])
out = edit("connected alignment", [{"op": "alignment_connected_create", "name": cr, "incoming": a1, "incoming_station": 90,
                                     "outgoing": a2, "outgoing_station": 110, "radius": 10}])
cg = read("get_alignment_geometry", alignment=cr)
carcs = [x for e in cg.get("entities", []) for x in e.get("parts", []) if x["kind"] == "Arc"]
check("curb return has an R10 arc and both parents", any(near(a["radius"], 10, 0.01) for a in carcs)
      and (cg.get("connected") or {}).get("incoming") == a1, str(cg.get("connected")) + str(carcs)[:200])

# ---------------------------------------------------------------- #10 feature lines
fl = f"FL {tag}"
edit("feature line", [{"op": "feature_line_create", "name": fl, "points": [[ox + 20, oy + 10, 0], [ox + 100, oy + 10, 0], [ox + 180, oy + 10, 0]]}])
edit("feature line from surface, raised 1 m", [{"op": "feature_line_edit", "feature_line": fl, "from_surface": eg, "include_intermediate": False},
                                              {"op": "feature_line_edit", "feature_line": fl, "raise": 1}])
fr = read("get_feature_line", feature_line=fl)
els = [p["point"][2] for p in fr.get("points", [])]
check("elevations = EG + 1", len(els) >= 3 and near(els[0], 11.4, 0.01) and near(els[-1], 14.6, 0.01), str(els))
check("grade 2 %", near((fr.get("points") or [{}])[0].get("gradeOut"), 2, 0.01), str((fr.get("points") or [{}])[0]))
edit("feature line elevation at a vertex", [{"op": "feature_line_edit", "feature_line": fl, "elevations": [{"index": 1, "elevation": 20}]}])
fr = read("get_feature_line", feature_line=fl)
check("vertex 1 at 20", near((fr.get("points") or [{}, {}])[1].get("point", [0, 0, 0])[2], 20, 0.001), str(fr.get("points")))

# ---------------------------------------------------------------- #17 several profile views, #14 labels
out = edit("profile views, 100 m each", [{"op": "profile_views_create", "alignment": al, "at": [ox, oy + 1200], "length": 100, "per_row": 2}])
views = result(out).get("views", [])
check("two profile views", len(views) == 2, str(views))
lab = [
    {"kind": "station_offset", "alignment": al, "at": [ox + 50, oy + 5]},
    {"kind": "alignment_geometry", "alignment": sp},
    {"kind": "surface_spot", "surface": eg, "points": [[ox + 40, oy + 30], [ox + 80, oy + 30]]},
    {"kind": "surface_slope", "surface": eg, "points": [[ox + 60, oy + 40]]},
    {"kind": "surface_contours", "surface": eg, "line": [[ox + 10, oy - 30], [ox + 190, oy - 30]]},
    {"kind": "note", "at": [ox, oy + 100]},
]
if views:
    lab += [{"kind": "profile_crest", "profile_view": views[0]["name"], "profile": f"{al} FG"},
            {"kind": "profile_pvi", "profile_view": views[0]["name"], "profile": f"{al} FG"},
            {"kind": "station_elevation", "profile_view": views[0]["name"], "station": 50, "elevation": 12}]
out = edit("labels", [{"op": "labels_place", "labels": lab}])
got = {x["kind"]: x["count"] for x in result(out).get("labels", [])}
check("every label kind placed", all(got.get(k["kind"], 0) >= 1 for k in lab), str(got))
check("alignment geometry labels: one per part (5)", got.get("alignment_geometry") == 5, str(got.get("alignment_geometry")))

# ---------------------------------------------------------------- #19 UDP definitions, #20 point export
cls = f"MCP {tag}"
edit("point properties", [{"op": "udp_create", "classification": cls, "properties": [
    {"name": "Owner", "type": "string", "default": "Utility"},
    {"name": "Depth", "type": "double", "data_type": "Elevation", "min": 0, "max": 20},
    {"name": "Kind", "type": "enum", "values": ["Valve", "Hydrant"]}]}])
ud = read("list_user_properties", kind="point")
mine = next((c for c in ud.get("classifications", []) if c["classification"] == cls), {})
check("classification with 3 properties", [p["name"] for p in mine.get("properties", [])] == ["Owner", "Depth", "Kind"], str(mine))

fmts = read("list_point_file_formats").get("formats", [])
fmt = next((f["name"] for f in fmts if f["name"].upper().startswith("PENZD") and "comma" in f["name"].lower()), None)
pfile = OUT / f"export_{tag}.csv"
if fmt:
    edit("points + export", [{"op": "cogo_points_add", "points": [{"x": ox + 1, "y": oy + 1, "z": 5, "description": "EXP"},
                                                                  {"x": ox + 2, "y": oy + 2, "z": 6, "description": "EXP"}]},
                             {"op": "points_export", "path": str(pfile), "format": fmt}])
    text = pfile.read_text(encoding="utf-8", errors="replace") if pfile.exists() else ""
    check("export file written with the points", pfile.exists() and text.count("EXP") >= 2, text[:200])
else:
    check("PENZD format available", False)

for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
