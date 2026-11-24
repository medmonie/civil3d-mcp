"""Live test of the 0.19 additions (run_model.bat): corridor feature lines over a station range (#53), feature-line
exports, corridor COGO points, targets, slope patterns and solids; surface definitions, surfaces from LandXML / DEM /
cropping / corridor surfaces, snapshots, surface solids; catchments (#68).

Run it in a NEW drawing from the Civil 3D metric template (Drawing1, after MCPALLOW); nothing is saved.
Writes small LandXML / DEM / FLT input files to tests\\results. Output: tests\\results\\model.txt

Hand-calculated values: EG is the plane z = 10 + 0.02 x' (x' = x - ox). The road runs east along y = oy for 200 m with
FG 10.5 -> 14.5 (2 %), lanes 3.6 m at -2 %, so ETW = crown - 0.072 (12.428 at station 100).
"""
import json
import math
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
TARGET = None
OUT_DIR = Path(__file__).resolve().parent / "results"
OUT_DIR.mkdir(exist_ok=True)


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


def near(a, b, tol):
    return isinstance(a, (int, float)) and isinstance(b, (int, float)) and abs(a - b) <= tol


def z_at(surface, x, y):
    return read("get_surface_elevation", surface_name=surface, x=x, y=y).get("elevation")


TARGET = call("get_drawing_info", {}).get("target")
print("session", TARGET, flush=True)
tag = uuid.uuid4().hex[:4]
ox, oy = 14000.0, 14000.0          # away from other test data
eg, al, cor = f"EG {tag}", f"AL {tag}", f"COR {tag}"

# ---------------------------------------------------------------- set-up: terrain, road, corridor
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(-1, 13) for j in range(-9, 4)]
edit("terrain, road and corridor", [
    {"op": "surface_create", "name": eg, "points": grid},
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_create", "alignment": al, "name": f"{al} FG", "pvis": [[0, 10.5], [200, 14.5]]},
    {"op": "alignment_offset_create", "name": f"{al} R5", "parent": al, "offset": 5},
    {"op": "assembly_create", "name": f"ASM {tag}", "at": [ox, oy - 400], "subassemblies": [
        {"class": "LaneSuperelevationAOR", "side": "Right"}, {"class": "LaneSuperelevationAOR", "side": "Left"}]},
    {"op": "corridor_create", "name": cor, "alignment": al, "profile": f"{al} FG", "assembly": f"ASM {tag}"},
    {"op": "corridor_region_edit", "corridor": cor, "region": "RG-1", "frequency": {"tangents": 10, "curves": 10, "profile_curves": 10}},
    {"op": "corridor_surface_add", "corridor": cor, "name": f"{cor} Top"},
])

# ---------------------------------------------------------------- #53 feature lines over a station range
fl = read("get_corridor_feature_lines", corridor=cor, codes=["ETW"], from_station=50, to_station=150)
etw = fl.get("featureLines", [])
right = next((f for f in etw if f.get("side") == "right"), {})
left = next((f for f in etw if f.get("side") == "left"), {})
check("two ETW lines, left and right", len(etw) == 2 and bool(right) and bool(left), str([(f.get("side"), f.get("index")) for f in etw]))
check("range 50-150 kept (plan length 100)", near(right.get("startStation"), 50, 0.01) and near(right.get("endStation"), 150, 0.01)
      and near(right.get("length2d"), 100, 0.01), str({k: right.get(k) for k in ("startStation", "endStation", "length2d")}))
pt100 = next((q for q in right.get("points", []) if near(q[0], 100, 0.01)), None)
check("right ETW at 100: offset 3.6, elevation 12.428", pt100 is not None and near(pt100[1], 3.6, 0.01) and near(pt100[2], 12.428, 0.01), str(pt100))
codes = fl.get("codesInRange", {})
check("codes in range: ETW / Crown points, Top links", {"ETW", "Crown"} <= set(codes.get("points", [])) and "Top" in codes.get("links", []), str(codes))
full = read("get_corridor_feature_lines", corridor=cor, codes=["ETW"], points=False)
etw_points = sum(f.get("pointCount", 0) for f in full.get("featureLines", []))
check("ETW stations 0..200 every 10 m = 2 x 21 points", etw_points == 42, str(etw_points))

# ---------------------------------------------------------------- #68 feature-line exports
out = edit("export right ETW as alignment, profile, 3D polyline, grading feature line", [
    {"op": "corridor_feature_line_export", "corridor": cor, "code": "ETW", "side": "right", "as": "alignment", "name": f"ETW-R {tag}"},
    {"op": "corridor_feature_line_export", "corridor": cor, "code": "ETW", "side": "right", "as": "profile", "alignment": al, "name": f"{al} ETW-R"},
    {"op": "corridor_feature_line_export", "corridor": cor, "code": "ETW", "side": "right", "as": "polyline3d"},
    {"op": "corridor_feature_line_export", "corridor": cor, "code": "ETW", "side": "right", "as": "grading_feature_line", "name": f"FL ETW {tag}"},
])
check("alignment 200 m long", near(result(out).get("length"), 200, 0.05), str(result(out)))
check("alignment in the site 'Corridor Exports'", next((x.get("site") for x in read("list_alignments").get("alignments", [])
                                                        if x.get("name") == f"ETW-R {tag}"), None) == "Corridor Exports")
ev = read("get_profile_elevation", alignment_name=al, profile_name=f"{al} ETW-R", station=100)
check("profile at 100 = 12.428", near(ev.get("elevation"), 12.428, 0.01), str(ev))
check("3D polyline length sqrt(200^2 + 4^2) = 200.04", near(result(out, 2).get("length3d"), math.hypot(200, 4), 0.05), str(result(out, 2)))
g = result(out, 3)
check("grading feature line 200 m, 10.428 to 14.428", near(g.get("length2d"), 200, 0.05) and near(g.get("minElevation"), 10.428, 0.01)
      and near(g.get("maxElevation"), 14.428, 0.01), str(g))
gf = read("get_feature_line", handle=g.get("handle"))
check("feature line read back: 10.428 to 14.428", near(gf.get("minElevation"), 10.428, 0.01) and near(gf.get("maxElevation"), 14.428, 0.01), str({k: gf.get(k) for k in ("minElevation", "maxElevation")}))

out = edit("COGO points on the ETW feature lines", [{"op": "corridor_points_export", "corridor": cor, "point_group": f"ETW {tag}", "codes": ["ETW"]}])
check("42 COGO points (2 x 21 stations)", result(out).get("pointsAdded") == 42, str(result(out)))

# ---------------------------------------------------------------- #68 targets: right lane width to the offset alignment at 5 m
tg = read("get_corridor_targets", corridor=cor)
asm = next((a for a in read("list_assemblies").get("assemblies", []) if a.get("name") == f"ASM {tag}"), {})
right_sub = next((s.rsplit(" (", 1)[0] for s in asm.get("subassemblies", []) if s and s.endswith("(Right)")), None)
targets = [t for b in tg.get("baselines", []) for r in b.get("regions", []) for t in (r.get("targets") or [])]
width = next((t for t in targets if t.get("subassembly") == right_sub and t.get("type") in ("Offset", "Alignment")), None)
check("an offset (width) target on the right lane", width is not None, f"right={right_sub}; {[(t.get('subassembly'), t.get('target'), t.get('type')) for t in targets]}")
if width:
    out = edit("right lane width to the R5 alignment", [
        {"op": "corridor_targets_set", "corridor": cor, "targets": [{"target": width["target"], "subassembly": right_sub, "alignments": [f"{al} R5"]}]}])
    check("one target set", result(out).get("set") == 1, str(result(out)))
    sec = read("get_corridor_section", corridor=cor, station=100, codes=["ETW"])
    r_etw = [q for q in sec.get("points", []) if q.get("offset", 0) > 0]
    check("right ETW now at offset 5.0, elevation 12.5 - 0.10 = 12.40",
          len(r_etw) == 1 and near(r_etw[0]["offset"], 5.0, 0.01) and near(r_etw[0]["elevation"], 12.40, 0.01), str(r_etw))

# ---------------------------------------------------------------- #68 slope pattern
out = edit("slope pattern crown to right ETW", [
    {"op": "corridor_slope_pattern_add", "corridor": cor, "from": {"code": "Crown"}, "to": {"code": "ETW", "side": "right"}}])
check("one slope pattern", result(out).get("slopePatterns") == 1, str(result(out)))
check("slope pattern listed", len(read("get_corridor_targets", corridor=cor).get("slopePatterns", [])) == 1)

# ---------------------------------------------------------------- #68 corridor solids: volume = shape area x 200 m
sec = read("get_corridor_section", corridor=cor, station=100)
area = sum(s.get("area", 0) for s in sec.get("shapes", []))
out = edit("corridor shapes as 3D solids", [{"op": "corridor_solids_export", "corridor": cor}])
vol = result(out).get("totalVolume")
check(f"solids volume = shape area {area:.3f} x 200", area > 0 and near(vol, area * 200, 0.02 * area * 200), f"{vol} vs {area * 200:.3f}; {result(out).get('types')}")

# ---------------------------------------------------------------- #68 surfaces from other sources
crop = [[ox + 40, oy - 20], [ox + 80, oy - 20], [ox + 80, oy + 20], [ox + 40, oy + 20]]
out = edit("cropped EG and a static copy of the corridor surface", [
    {"op": "surface_create_from", "name": f"CROP {tag}", "source": "crop", "surface": eg, "points": crop},
    {"op": "surface_create_from", "name": f"CORS {tag}", "source": "corridor", "corridor": cor, "corridor_surface": f"{cor} Top"}])
check("cropped surface at (60, 0) = 11.2", near(z_at(f"CROP {tag}", ox + 60, oy), 11.2, 0.01))
check("cropped surface has nothing at x' = 100", z_at(f"CROP {tag}", ox + 100, oy) is None)
check("corridor surface copy at the crown, station 100 = 12.5", near(z_at(f"CORS {tag}", ox + 100, oy), 12.5, 0.01))

out = edit("surface solids: 1 m deep, and down to elevation 5", [
    {"op": "surface_solids_create", "surface": f"CROP {tag}", "mode": "depth", "depth": 1},
    {"op": "surface_solids_create", "surface": f"CROP {tag}", "mode": "elevation", "elevation": 5}])
check("40 x 40 x 1 = 1600 m3", near(result(out).get("totalVolume"), 1600, 16), str(result(out).get("totalVolume")))
check("40 x 40 x (11.2 - 5) = 9920 m3", near(result(out, 1).get("totalVolume"), 9920, 99), str(result(out, 1).get("totalVolume")))

# LandXML: a 20 x 20 square at z = 30 as two faces
lx = OUT_DIR / f"model_{tag}.xml"
x0, y0 = ox + 300, oy + 300
lx.write_text(f"""<?xml version="1.0" encoding="UTF-8"?>
<LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
 <Units><Metric areaUnit="squareMeter" linearUnit="meter" volumeUnit="cubicMeter" temperatureUnit="celsius" pressureUnit="milliBars"/></Units>
 <Surfaces><Surface name="LX{tag}"><Definition surfType="TIN">
  <Pnts><P id="1">{y0} {x0} 30</P><P id="2">{y0} {x0 + 20} 30</P><P id="3">{y0 + 20} {x0 + 20} 30</P><P id="4">{y0 + 20} {x0} 30</P></Pnts>
  <Faces><F>1 2 3</F><F>1 3 4</F></Faces>
 </Definition></Surface></Surfaces>
</LandXML>
""", encoding="utf-8")
# DEM: ESRI ASCII grid, 5 x 5 cells of 10 m at z = 40
dx0, dy0 = ox + 400, oy + 400
out = edit("surface from LandXML", [{"op": "surface_create_from", "name": f"LX {tag}", "source": "landxml", "path": str(lx)}])
check("LandXML surface at its centre = 30", near(z_at(f"LX {tag}", x0 + 10, y0 + 10), 30, 0.01))

# DEM: ESRI ASCII grid at z = 40 over 50 x 50 m (50 x 50 cells read; 5 x 5 cells of 10 m did not). Variants are logged.
def dem_text(upper, n, crlf):
    keys = ["ncols", "nrows", "xllcorner", "yllcorner", "cellsize", "NODATA_value"]
    if upper:
        keys = [k.upper() for k in keys]
    vals = [n, n, f"{dx0:.1f}", f"{dy0:.1f}", f"{50 / n:.1f}", "-9999"]
    nl = "\r\n" if crlf else "\n"
    return nl.join(f"{k} {v}" for k, v in zip(keys, vals)) + nl + nl.join(" ".join(["40.0"] * n) for _ in range(n)) + nl
dem = None
for i, (ext, upper, n, crlf) in enumerate([(".asc", True, 50, True), (".asc", False, 50, False), (".asc", True, 5, True)]):
    cand = OUT_DIR / f"model_{tag}_{i}{ext}"
    cand.write_bytes(dem_text(upper, n, crlf).encode("ascii"))
    try:
        o = call("edit", {"operations": [{"op": "surface_create_from", "name": f"DEM {tag}", "source": "dem", "path": str(cand)}],
                          "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        o = {"applied": False, "error": str(exc)}
    print(f"DEM variant {i} ext={ext} upper={upper} n={n} crlf={crlf}: applied={o.get('applied')} {o.get('error', '')}", flush=True)
    if o.get("applied"):
        dem = cand
        break
check("a DEM variant was read", dem is not None)
if dem is not None:
    edit("DEM into a TIN", [{"op": "surface_create", "name": f"DEMT {tag}"},
                            {"op": "surface_definition_add", "surface": f"DEMT {tag}", "kind": "dem", "path": str(dem)}])
    grid_row = next((s for s in read("list_surfaces").get("surfaces", []) if s.get("name") == f"DEM {tag}"), {})
    check("DEM grid surface is flat at 40", grid_row.get("type") == "GridSurface" and near(grid_row.get("minElevation"), 40, 0.01)
      and near(grid_row.get("maxElevation"), 40, 0.01), str(grid_row))
    check("DEM added to a TIN: 40 at its centre", near(z_at(f"DEMT {tag}", dx0 + 25, dy0 + 25), 40, 0.01))

# ---------------------------------------------------------------- #68 surface definitions
out = edit("contours 11 / 12 / 13 extracted from EG", [{"op": "surface_extract", "surface": eg, "what": "contours_at", "elevations": [11, 12, 13]}])
contours = result(out).get("handles", [])
out = edit("contour surface", [
    {"op": "surface_create", "name": f"CT {tag}"},
    {"op": "surface_definition_add", "surface": f"CT {tag}", "kind": "contours", "handles": contours}])
check("contour surface between 11 and 12 (x' = 75) = 11.5", near(z_at(f"CT {tag}", ox + 75, oy), 11.5, 0.02), str(result(out, 1)))
out = edit("snapshot of the contour surface", [{"op": "surface_snapshot", "surface": f"CT {tag}"}])
check("snapshot made", result(out).get("hasSnapshot") is True, str(result(out)))

px, py = ox + 500, oy - 100
desc = f"PG{tag}"
edit("four COGO points at z 20 and their point group", [
    {"op": "cogo_points_add", "points": [{"x": px + dx, "y": py + dy, "z": 20, "description": desc} for dx, dy in ((0, 0), (40, 0), (40, 40), (0, 40))]},
    {"op": "point_group_create", "name": f"PG {tag}", "include_raw_descriptions": desc}])
out = edit("surface from the point group", [
    {"op": "surface_create", "name": f"PGS {tag}"},
    {"op": "surface_definition_add", "surface": f"PGS {tag}", "kind": "point_group", "point_group": f"PG {tag}"}])
check("point group surface = 20", near(z_at(f"PGS {tag}", px + 20, py + 20), 20, 0.01), str(result(out, 1)))

flt = OUT_DIR / f"model_{tag}.flt"
flt.write_text(f"# breakline test\nS{px + 5} {py + 20} 25.0 MID\n {px + 35} {py + 20} 25.0\n", encoding="ascii")
out = edit("breakline file across the point group surface", [{"op": "surface_definition_add", "surface": f"PGS {tag}", "kind": "breakline_file", "path": str(flt)}])
check("breakline from file raises the middle to 25", near(z_at(f"PGS {tag}", px + 20, py + 20), 25, 0.01))

# drawing objects: AutoCAD points at z = 50 on a fresh surface
qx, qy = ox + 600, oy - 100
pts = [[qx, qy, 50], [qx + 30, qy, 50], [qx + 30, qy + 30, 50], [qx, qy + 30, 50]]
out = edit("AutoCAD points", [{"op": "draw_point", "at": q} for q in pts])
handles = [result(out, i).get("handle") for i in range(4)]
out = edit("surface from drawing points", [
    {"op": "surface_create", "name": f"DO {tag}"},
    {"op": "surface_definition_add", "surface": f"DO {tag}", "kind": "drawing_objects", "object_type": "points", "handles": handles}])
check("drawing-object surface = 50", near(z_at(f"DO {tag}", qx + 15, qy + 15), 50, 0.01), str(result(out, 1)))

# wall breakline on a flat surface at 60: which side rises and to what (absolute or relative 'elevation')
wx, wy = ox + 700, oy - 100
flat = [[wx + dx, wy + dy, 60] for dx in (0, 40) for dy in (0, 40)]
out = edit("flat surface and a wall source feature line", [
    {"op": "surface_create", "name": f"WALL {tag}", "points": flat},
    {"op": "feature_line_create", "name": f"WALL FL {tag}", "points": [[wx + 20, wy + 5, 60], [wx + 20, wy + 35, 60]]}])
wall_fl = result(out, 1).get("handle")
out = edit("wall breakline, elevation 62 on the right", [
    {"op": "surface_definition_add", "surface": f"WALL {tag}", "kind": "wall_breaklines", "walls": [{"handle": wall_fl, "elevation": 62, "right_side": True}]}])
zl, zr = z_at(f"WALL {tag}", wx + 19.9, wy + 20), z_at(f"WALL {tag}", wx + 20.1, wy + 20)
check("wall: west side 60, east (right) side at the absolute 62",
      near(zl, 60, 0.05) and near(zr, 62, 0.05),
      f"x-0.1: {zl}, x+0.1: {zr} (source runs north, so right = east)")

# ---------------------------------------------------------------- #68 catchment on EG (flows west, -x)
cb = [[ox + 120, oy - 150, 0], [ox + 160, oy - 150, 0], [ox + 160, oy - 110, 0], [ox + 120, oy - 110, 0]]
out = edit("catchment 40 x 40", [{"op": "catchment_create", "name": f"CA {tag}", "surface": eg, "boundary": cb, "group": f"CG {tag}",
                                  "flow_from": [ox + 159, oy - 130], "impervious_percent": 25, "runoff_coefficient": 0.5}])
ca = result(out).get("catchment", {})
check("catchment area 1600", near(ca.get("area2d"), 1600, 0.5), str(ca))
check("25 % impervious and C 0.5 kept", near(ca.get("imperviousPercent"), 25, 0.01) and near(ca.get("runoffCoefficient"), 0.5, 0.001), str(ca))
path = ca.get("flowPath") or []
plen = sum(math.dist(path[i][:2], path[i + 1][:2]) for i in range(len(path) - 1)) if isinstance(path, list) and len(path) > 1 else None
check("flow path runs west about 39 m to the boundary", plen is not None and 35 <= plen <= 41 and path[-1][0] < path[0][0], f"length {plen}; {path[:2]}...{path[-1:]}")
lc = read("list_catchments", group=f"CG {tag}")
check("catchment listed in its group", lc.get("total") == 1, str(lc.get("total")))

# ---------------------------------------------------------------- summary
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
