"""Live test of 0.22 (run_v022.bat): alignment bearings, H5 (vertical curves kept on PVI elevation edits), inspect_file
budget, list_entities inside blocks and other drawings, style_copy, band-set import with per-row data.

Run it in a NEW drawing from the Civil 3D metric template (DrawingN, after MCPALLOW). Nothing is saved.
Output: tests\\results\\v022.txt
"""
import glob
import json
import os
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


# ---------------------------------------------------------------- 1. alignment directions
al = f"DIR {tag}"
edit("fixture: alignment NE then E, with a curve", [
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 100, oy + 100], [ox + 300, oy + 100]], "radius": 50}])
geo = read("get_alignment_geometry", alignment=al)
ents = [part for e in geo.get("entities", []) for part in (e.get("parts") or [e])]
lines = [e for e in ents if e.get("kind", "").lower().startswith("line")]
arcs = [e for e in ents if "arc" in e.get("kind", "").lower()]
check("1a first tangent bearing 45 (NE), angleFromEast 45", bool(lines) and abs(lines[0].get("bearing", -1) - 45) < 1e-3
      and abs(lines[0].get("angleFromEast", -1) - 45) < 1e-3, f"{lines[0] if lines else ents}")
check("1b last tangent bearing 90 (E), angleFromEast 0", len(lines) > 1 and abs(lines[-1].get("bearing", -1) - 90) < 1e-3
      and abs(lines[-1].get("angleFromEast", -1)) < 1e-3, f"{lines[-1] if lines else ''}")
check("1c quadrant bearing text", bool(lines) and lines[0].get("quadrantBearing", "").startswith("N 45") and lines[0]["quadrantBearing"].endswith("E"),
      lines[0].get("quadrantBearing") if lines else "")
check("1d curve start/end bearings match the tangents", bool(arcs) and abs(arcs[0].get("startBearing", -1) - 45) < 0.01
      and abs(arcs[0].get("endBearing", -1) - 90) < 0.01, f"{arcs[0] if arcs else ''}")
check("1e note says clockwise from north", "clockwise from north" in str(geo.get("note", "")))

# ---------------------------------------------------------------- 2. H5 vertical curves kept
pal, prof = f"H5 {tag}", f"H5 FG {tag}"
edit("fixture: profile with three curve kinds", [
    {"op": "alignment_create", "name": pal, "points": [[ox, oy - 300], [ox + 400, oy - 300]]},
    {"op": "profile_create", "alignment": pal, "name": prof, "pvis": [
        [0, 10], {"station": 100, "elevation": 14, "length": 40}, {"station": 200, "elevation": 11, "lengths": [20, 30]},
        {"station": 300, "elevation": 15, "radius": 1500}, [400, 12]]}])


def curves():
    g = read("get_profile_geometry", quiet=True, alignment=pal, profile=prof)
    out = {}
    for e in g.get("entities", []):
        if "pviStation" in e or "kind" in e and e.get("type", "").lower().find("tangent") < 0:
            st = e.get("pviStation")
            if st is not None:
                out[round(st)] = e
    return g, out


g0, c0 = curves()
show("curves before", c0)
out = edit("2a elevation-only change of the three curved PVIs", [{"op": "profile_pvis_set", "alignment": pal, "profile": prof,
           "pvis": [{"station": 100, "elevation": 14.5}, {"station": 200, "elevation": 10.5}, {"station": 300, "elevation": 15.5}]}])
g1, c1 = curves()
show("curves after", c1)
check("2b three curves reported kept", len(result(out).get("curvesKept", [])) == 3, str(result(out).get("curvesKept")))
check("2c all three curves still there", all(k in c1 for k in (100, 200, 300)), str(sorted(c1)))
check("2d curve lengths unchanged", all(k in c1 and k in c0 and abs(c1[k].get("length", 0) - c0[k].get("length", 1)) < 0.01 for k in (100, 200)),
      f"{[(k, c0.get(k, {}).get('length'), c1.get(k, {}).get('length')) for k in (100, 200, 300)]}")
pv = {round(v["station"]): v["elevation"] for v in g1.get("pvis", [])}
check("2e new elevations applied", abs(pv.get(100, 0) - 14.5) < 1e-6 and abs(pv.get(200, 0) - 10.5) < 1e-6 and abs(pv.get(300, 0) - 15.5) < 1e-6, str(pv))
bad = edit("2f impossible explicit curve fails the edit", [{"op": "profile_pvis_set", "alignment": pal, "profile": prof,
           "pvis": [{"station": 100, "elevation": 20, "length": 5000}]}], must=False)
g2, c2 = curves()
pv2 = {round(v["station"]): v["elevation"] for v in g2.get("pvis", [])}
check("2f impossible curve: not applied", bad.get("applied") is False, str(bad.get("error") or bad.get("exception") or ""))
check("2g profile unchanged after the failure", abs(pv2.get(100, 0) - 14.5) < 1e-6 and 100 in c2 and abs(c2[100].get("length", 0) - c1[100].get("length", 1)) < 0.01,
      f"{pv2.get(100)} {c2.get(100, {}).get('length')}")
state = lambda g, c: ({round(v["station"]): v["elevation"] for v in g.get("pvis", [])}, {k: c[k].get("length") for k in c})
ref = state(g2, c2)
edit("2i a later failing operation rolls the profile back", [
    {"op": "profile_pvis_set", "alignment": pal, "profile": prof, "pvis": [{"station": 100, "elevation": 30}, {"station": 200, "curve": "none"}]},
    {"op": "draw_circle", "center": [0, 0, 0], "radius": -1}], must=False)
gi, ci = curves()
check("2i profile unchanged after a later operation failed", state(gi, ci) == ref, f"{state(gi, ci)} vs {ref}")
dry = call("edit", {"operations": [{"op": "profile_pvis_set", "alignment": pal, "profile": prof, "pvis": [{"station": 300, "elevation": 40}]}],
                    "target": TARGET, "request_id": str(uuid.uuid4()), "dry_run": True}, timeout=EDIT_TIMEOUT)
gd, cd = curves()
check("2j dry run leaves the profile unchanged", dry.get("dryRun") is True and state(gd, cd) == ref, f"{state(gd, cd)} vs {ref}")
out = edit("2h curve: none removes the curve", [{"op": "profile_pvis_set", "alignment": pal, "profile": prof,
           "pvis": [{"station": 300, "curve": "none"}]}])
g3, c3 = curves()
check("2h curve at 300 removed, others kept", 300 not in c3 and 100 in c3 and 200 in c3, str(sorted(c3)))

# ---------------------------------------------------------------- 3. inspect_file budget, other-DWG and block reads
templates = sorted(glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\Autodesk\C3D *\enu\Template\**\*.dwt"), recursive=True))
pp = [t for t in templates if "metric" in t.lower() and "plan and profile" in t.lower()] or [t for t in templates if "metric" in t.lower()] or templates
template = pp[0] if pp else None
print("template:", template)
if template:
    small = read("inspect_file", quiet=True, path=template, limit=5)
    listed = sum(len(l.get("entities", [])) for l in small.get("layouts", [])) + len(small.get("blocks", [])) \
        + len(small.get("views", [])) + len(small.get("pageSetups", []))
    big = read("inspect_file", quiet=True, path=template, limit=2000)
    show("inspect_file limit 5 (truncated)", {"listed": listed, "truncated": small.get("truncated"), "bytes": len(json.dumps(small)),
                                             "bytes at 2000": len(json.dumps(big))})
    check("3a inspect_file limit 5 lists at most 5 items", listed <= 5, f"listed {listed}")
    check("3b inspect_file says what was cut", bool(small.get("truncated")) or listed == len(json.dumps(big)) and False or bool(small.get("truncated")),
          str(small.get("truncated")))
    lay = next((l["name"] for l in big.get("layouts", []) if l.get("entityCount")), None)
    if lay:
        one = read("inspect_file", quiet=True, path=template, layout=lay, limit=200)
        check("3c inspect_file layout reads one layout", [l["name"] for l in one.get("layouts", [])] == [lay], lay)
        fe = read("list_entities", quiet=True, path=template, layout=lay, limit=20)
        show("list_entities in the template layout", {k: fe.get(k) for k in ("file", "total", "returned")})
        check("3d list_entities reads a layout of a file on disk", fe.get("file") == template and fe.get("total", 0) > 0, str(fe.get("_error") or fe.get("total")))
    blk = next((b["name"] for b in big.get("blocks", []) if b.get("name")), None)
    if blk:
        fb = read("list_entities", quiet=True, path=template, block=blk, limit=20)
        show("list_entities in a block of the template", {k: fb.get(k) for k in ("block", "total", "returned")})
        check("3e list_entities reads a block definition in a file on disk", fb.get("block", {}).get("name") == blk, str(fb.get("_error") or ""))
else:
    check("3 a Civil 3D template was found for the file reads", False)

bname = f"MH_{tag}"
mk = edit("fixture: two lines and a circle as block " + bname, [
    {"op": "draw_line", "start": [ox, oy + 500, 0], "end": [ox + 2, oy + 500, 0]},
    {"op": "draw_line", "start": [ox, oy + 500, 0], "end": [ox, oy + 502, 0]},
    {"op": "draw_circle", "center": [ox + 1, oy + 501, 0], "radius": 0.6}])
handles = [r.get("result", {}).get("handle") for r in mk.get("results", [])]
edit("define the block", [{"op": "block_define", "name": bname, "handles": handles, "base": [ox, oy + 500]}])
inb = read("list_entities", block=bname)
types = sorted(e["type"] for e in inb.get("entities", []))
check("3f list_entities inside a block of the open drawing", types == ["CIRCLE", "LINE", "LINE"], str(types))
circ = next((e for e in inb.get("entities", []) if e["type"] == "CIRCLE"), {})
org = inb.get("block", {}).get("origin", [0, 0, 0])
cx = circ.get("geometry", {}).get("center", [9e9, 9e9])
check("3g circle 1,1 from the block base point", abs(cx[0] - org[0] - 1) < 1e-6 and abs(cx[1] - org[1] - 1) < 1e-6, f"center {cx} origin {org}")
check("3h block and layout together refused", "_error" in read("list_entities", quiet=True, block=bname, layout="Layout1"))

# ---------------------------------------------------------------- 4. style_copy and band set import
styles = read("list_styles", quiet=True, kind="profile_view").get("styles") or read("list_styles", quiet=True, kind="profile_view")
show("profile view styles", styles, 800)
src_style = (styles[0] if isinstance(styles, list) and styles and isinstance(styles[0], str) else
             (styles[0].get("name") if isinstance(styles, list) and styles else None))
if src_style:
    new = f"{src_style} copy {tag}"
    cp = edit("4a style_copy a profile view style", [{"op": "style_copy", "kind": "profile_view", "name": src_style, "new_name": new}])
    after = json.dumps(read("list_styles", quiet=True, kind="profile_view"))
    check("4b the copy is listed and the original kept", new in after and src_style in after)
    again = edit("4c copying onto an existing name fails", [{"op": "style_copy", "kind": "profile_view", "name": src_style, "new_name": new}], must=False)
    check("4c copying onto an existing name fails", again.get("applied") is False)
    skip = edit("4d if_exists skip", [{"op": "style_copy", "kind": "profile_view", "name": src_style, "new_name": new, "if_exists": "skip"}])
    check("4d skip reports copied false", result(skip).get("copied") is False)
else:
    check("4 a profile view style to copy", False, str(styles))

edit("fixture: EG surface and profile view", [
    {"op": "surface_create", "name": f"EG {tag}", "points": [[ox + i * 50, oy - 400 + j * 50, 10 + 0.01 * i * 50] for i in range(-1, 10) for j in range(-1, 4)]},
    {"op": "profile_from_surface", "alignment": pal, "surface": f"EG {tag}", "name": f"EG {tag}"},
    {"op": "profile_view_create", "alignment": pal, "at": [ox, oy - 1500], "band_set": "auto"}])
pvs = [v for v in read("list_profile_views", quiet=True).get("profileViews", []) if v.get("alignment") == pal]
pvname = pvs[0]["name"] if pvs else f"{pal} - Profile View"
sets = read("list_styles", quiet=True, kind="profile_view_band_set")
setnames = sets.get("styles") if isinstance(sets, dict) else sets
show("band sets", setnames, 800)
bs = None
for s in setnames or []:
    nm = s if isinstance(s, str) else s.get("name")
    gs = read("get_style", quiet=True, kind="profile_view_band_set", name=nm)
    if any("profile" in json.dumps(b).lower() for b in gs.get("bottom", [])):
        bs = nm
        break
if bs:
    imp = edit("4e band set imported with Civil 3D's import", [{"op": "profile_view_bands", "profile_view": pvname, "band_set": bs,
                "rows": [{"location": "bottom", "index": 0, "profile1": f"EG {tag}", "profile2": prof}]}])
    check("4f method is import", result(imp).get("method") == "import", str(result(imp).get("method")))
    bands = result(imp).get("bands", [])
    show("bands", bands, 1500)
    check("4g row 0 got the requested profiles", prof in json.dumps(bands) and f"EG {tag}" in json.dumps(bands), "")
    bad = edit("4h a row that does not exist fails", [{"op": "profile_view_bands", "profile_view": pvname, "band_set": bs,
               "rows": [{"index": 99}]}], must=False)
    check("4h a row that does not exist fails", bad.get("applied") is False)
else:
    check("4e a band set with a profile data band", False)

print()
fails = [k for k, v in RESULTS.items() if v != "PASS"]
print(f"{len(RESULTS) - len(fails)} / {len(RESULTS)} PASS")
print("ALL PASS" if not fails else "FAILED: " + "; ".join(fails))
