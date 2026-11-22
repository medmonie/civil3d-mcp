"""Live test of the sheet and AutoCAD set-up operations (run_sheets.bat).

Works on a copy: the open drawing (the water network after the WaterGEMS test, or any drawing whose name contains
'mcp_test') is first saved as 'PL15 - sheets mcp_test.dwg' next to it. Uses Civil 3D's own metric templates for the
layout-from-template and title block steps. Output: tests\\results\\sheets.txt; PDFs in tests\\results.
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
OUT = Path(__file__).resolve().parent / "results"
OUT.mkdir(exist_ok=True)
TARGET = None


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def skip(name, why):
    RESULTS[name] = "SKIP"
    print(f"SKIP  {name}: {why}", flush=True)


def show(title, out):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < 4000 else text[:4000] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, **params):
    try:
        return show(method, call(method, {**params, "target": TARGET}))
    except Exception as exc:     # noqa: BLE001
        print(f"===== {method}\nFAILED: {exc}", flush=True)
        return {}


def edit(title, ops, expect=True):
    try:
        out = call("edit", {"operations": ops, "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    check(title, (out.get("applied") is True) == expect, "" if out.get("applied") else str(out.get("error") or out.get("exception")))
    return out


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


def pdf_sizes(path):
    """(width, height) in points of every page's MediaBox."""
    import re
    data = Path(path).read_bytes()
    return [(float(m[2]) - float(m[0]), float(m[3]) - float(m[1]))
            for m in re.findall(rb"/MediaBox\s*\[\s*([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)", data)]


def pdf_pages(path):
    data = Path(path).read_bytes()
    return data.count(b"/Type /Page") - data.count(b"/Type /Pages")


# ------------------------------------------------------------------ the drawing
info = call("get_drawing_info")
name = info["name"].lower()
if "mcp_test" not in name and "watergems import" not in name:
    raise SystemExit(f"Refusing to run on '{info['name']}': open the WaterGEMS import copy or a drawing named *mcp_test*.")
if "mcp_test" not in name:
    copy = str(Path(info["path"]).with_name("PL15 - sheets mcp_test.dwg"))
    show("save copy", call("edit", {"operations": [{"op": "drawing_save", "path": copy, "overwrite": True}],
                                    "target": info["target"], "request_id": str(uuid.uuid4())}, timeout=300))
    info = call("get_drawing_info")
    check("working on the mcp_test copy", "mcp_test" in info["name"].lower())
TARGET = info["target"]

templates = sorted(glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\Autodesk\C3D *\enu\Template\**\*.dwt"), recursive=True))
print("templates found:", len(templates))
pp = [t for t in templates if "metric" in t.lower() and "plan and profile" in t.lower()] or \
     [t for t in templates if "metric" in t.lower() and "plan" in t.lower()]
ncs = [t for t in templates if "metric" in t.lower() and "ncs" in t.lower()] or [t for t in templates if "metric" in t.lower()]
template = (pp or ncs or templates or [None])[0]
print("template:", template)

pipes = call("list_pressure_pipes", {"network_name": "Water", "limit": 5000, "target": TARGET}).get("pipes", [])
pts = [p[k] for p in pipes for k in ("start", "end") if p.get(k)]
if pts:
    window = [min(p[0] for p in pts) - 10, min(p[1] for p in pts) - 10, max(p[0] for p in pts) + 10, max(p[1] for p in pts) + 10]
else:
    ents = call("list_entities", {"limit": 2000, "target": TARGET}).get("entities", [])
    xs = [v[0] for e in ents for v in (e.get("geometry") or {}).get("vertices", [])]
    ys = [v[1] for e in ents for v in (e.get("geometry") or {}).get("vertices", [])]
    window = [min(xs), min(ys), max(xs), max(ys)]
print("model window:", window)
mid = [(window[0] + window[2]) / 2, (window[1] + window[3]) / 2]

# ------------------------------------------------------------------ AutoCAD styles and set-up
for kind in ("text_style", "dim_style", "mleader_style", "linetype", "layer_state", "annotation_scale", "page_setup"):
    read("list_cad_styles", kind=kind)
out = edit("styles, linetypes, annotation scale, layer state", [
    {"op": "text_style_create", "name": "MCP-Arial", "font": "arial.ttf", "if_exists": "replace"},
    {"op": "dim_style_create", "name": "MCP-500", "text_style": "MCP-Arial", "text_height": 1.25, "arrow_size": 1.0,
     "overall_scale": 0.5, "decimals": 2, "text_above": True, "if_exists": "replace"},
    {"op": "mleader_style_create", "name": "MCP-Note", "text_style": "MCP-Arial", "text_height": 1.25, "arrow_size": 1.0, "if_exists": "replace"},
    {"op": "linetype_load", "names": ["DASHED", "CENTER"]},
    {"op": "annotation_scale_add", "scale": "1:500", "set_current": True},
    {"op": "layer_state_save", "name": "MCP-before", "description": "before the sheets test", "if_exists": "replace"},
])
styles = {k: [i["name"] for i in read("list_cad_styles", kind=k).get("items", [])] for k in ("text_style", "dim_style", "mleader_style", "linetype", "layer_state", "annotation_scale")}
check("styles exist", "MCP-Arial" in styles["text_style"] and "MCP-500" in styles["dim_style"] and "MCP-Note" in styles["mleader_style"]
      and {"DASHED", "CENTER"} <= set(styles["linetype"]) and "MCP-before" in styles["layer_state"] and "1:500" in styles["annotation_scale"])
edit("text, dimension, leader with the new styles", [
    {"op": "draw_text", "text": "MCP sheets test", "at": [mid[0], mid[1], 0], "height": 2.5, "style": "MCP-Arial"},
    {"op": "draw_dimension", "from": [window[0], window[1]], "to": [window[0] + 50, window[1]], "line_at": [window[0] + 25, window[1] - 5], "style": "MCP-500"},
    {"op": "draw_leader", "arrow_at": mid, "text_at": [mid[0] + 20, mid[1] + 20], "text": "MCP note", "style": "MCP-Note"},
])

# ------------------------------------------------------------------ views, layouts, viewports
edit("named view", [{"op": "view_create", "name": "MCP-Main", "window": window, "if_exists": "replace"}])
check("view listed", any(v["name"] == "MCP-Main" for v in read("list_views").get("views", [])))

layouts = [l["name"] for l in read("list_layouts").get("layouts", [])]
setup = []
for name in ("MCP-Sheet-1", "MCP-Temp"):
    if name in layouts:
        setup.append({"op": "layout_delete", "name": name})
setup += [
    {"op": "layout_create", "name": "MCP-Sheet-1"},
    {"op": "layout_create", "name": "MCP-Temp"},
    {"op": "page_setup_set", "layout": "MCP-Sheet-1", "device": "DWG To PDF.pc3", "paper": "ISO full bleed A1 (841", "plot_style": "monochrome.ctb", "scale": "1:1"},
]
out = edit("layouts and page setup", setup)
edit("delete a layout", [{"op": "layout_delete", "name": "MCP-Temp"}])
check("layout deleted", "MCP-Temp" not in [l["name"] for l in read("list_layouts").get("layouts", [])])

out = edit("viewports: window fitted, named view at 1:2000, locked, layer frozen", [
    {"op": "viewport_create", "layout": "MCP-Sheet-1", "at": [330, 320], "width": 600, "height": 480, "window": window,
     "lock": True, "freeze_layers": ["Gate-Valve"]},
    {"op": "viewport_create", "layout": "MCP-Sheet-1", "at": [740, 160], "width": 160, "height": 120, "view": "MCP-Main", "scale": "1:2000"},
    {"op": "draw_mtext", "layout": "MCP-Sheet-1", "text": "WATER NETWORK\\PGENERAL LAYOUT", "at": [660, 560], "height": 5, "style": "MCP-Arial"},
])
vps = read("list_viewports", layout="MCP-Sheet-1").get("viewports", [])
check("two viewports on the sheet", len(vps) == 2, str([(v["scale"], v["locked"]) for v in vps]))
check("second viewport at 1:2000", any(v["scale"] == "1:2000" for v in vps))
check("first viewport locked, layer frozen", any(v["locked"] and "Gate-Valve" in (v.get("frozenLayers") or []) for v in vps))
if vps:
    out = edit("viewport_set: scale 1:1000 and twist", [{"op": "viewport_set", "handle": vps[-1]["handle"], "scale": "1:1000", "twist_deg": 30}])
    r = result(out)
    check("viewport changed", r.get("scale") == "1:1000" and abs(r.get("twistDeg", 0) - 30) < 0.01, str(r))

if template:
    out = edit("layout from template", [{"op": "layout_from_template", "path": template, "name": "MCP-Sheet-T", "if_exists": "replace"}])
    r = result(out)
    tvps = read("list_viewports", layout="MCP-Sheet-T").get("viewports", [])
    check("template layout has its objects", r.get("objects", 0) > 0, f"objects={r.get('objects')} viewports={r.get('viewports')} listed={len(tvps)}")
    out = edit("title block from file (whole template as a block)", [
        {"op": "title_block_insert", "layout": "MCP-Sheet-1", "path": template, "as_block": "MCP-TB", "at": [0, 0], "replace": True}])
    check("title block inserted on the layout", result(out).get("layout") == "MCP-Sheet-1")
else:
    skip("layout from template", "no Civil 3D template found")

# ------------------------------------------------------------------ blocks
inserts = call("list_entities", {"type": "INSERT", "limit": 200, "target": TARGET}).get("entities", [])
props = [read("get_block_properties", handle=e["handle"]) for e in inserts[:20]]
check("block properties read", bool(props) and all("block" in p for p in props) if inserts else True)
dyn = next((p for p in props if p.get("dynamic") and any(not x["readOnly"] for x in p.get("properties", []))), None)
if dyn:
    prop = next(x for x in dyn["properties"] if not x["readOnly"])
    value = prop["allowed"][-1] if prop["allowed"] else prop["value"]
    edit("dynamic block property", [{"op": "dynamic_block_set", "handle": dyn["handle"], "properties": {prop["name"]: value}}])
else:
    skip("dynamic block property", "no dynamic block in this drawing")

# ------------------------------------------------------------------ PDF
# a new folder per run: only fresh files count (an earlier PDF may be attached as an underlay and locked)
PDF = OUT / f"sheets_pdf_{time.strftime('%Y%m%d_%H%M%S')}"
PDF.mkdir()
single, multi = PDF / "sheets_single.pdf", PDF / "sheets_multi.pdf"
sheets = ["MCP-Sheet-1"] + (["MCP-Sheet-T"] if template else [])
if template:
    edit("multi-sheet PDF with mixed paper is refused", [{"op": "plot_pdf", "path": str(multi), "layouts": sheets, "overwrite": True}], expect=False)
    edit("same paper for both sheets", [{"op": "page_setup_set", "layout": "MCP-Sheet-T", "device": "DWG To PDF.pc3",
                                         "paper": "ISO full bleed A1 (841", "rotation": "0", "scale": "fit"}])
    edit("same orientation for both sheets", [{"op": "page_setup_set", "layout": "MCP-Sheet-1", "rotation": "0"}])
out = edit("PDF: one layout, multi-sheet, one per layout", [
    {"op": "plot_pdf", "path": str(single), "layout": "MCP-Sheet-1", "overwrite": True},
    {"op": "plot_pdf", "path": str(multi), "layouts": sheets, "overwrite": True},
    {"op": "plot_pdf", "folder": str(PDF), "layouts": sheets, "overwrite": True},
])
print("warnings:", out.get("warnings"))
check("PDFs plotted without warnings", not out.get("warnings"), str(out.get("warnings")))
check("single PDF written", single.exists() and single.stat().st_size > 1000)
check("multi-sheet PDF has one page per layout", multi.exists() and pdf_pages(multi) == len(sheets), f"pages={pdf_pages(multi) if multi.exists() else 0}")
sizes = pdf_sizes(multi) if multi.exists() else []
check("pages keep the layouts' paper (A1/A0, not Letter)", sizes and all(max(w, h) > 1500 for w, h in sizes), str(sizes))
per = [Path(f) for f in result(out, 2).get("files", [])]
check("one PDF per layout", per and all(f.exists() for f in per), str([f.name for f in per]))

# ------------------------------------------------------------------ underlays and xrefs
if single.exists():
    out = edit("PDF underlay", [{"op": "underlay_attach", "path": str(single), "at": [window[2] + 50, window[1]], "scale": 1.0}])
xref_src = OUT / "xref_source.dwg"
out = edit("write a drawing to attach as xref", [{"op": "drawing_save", "path": str(xref_src), "copy": True, "overwrite": True}])
if xref_src.exists():
    edit("xref attach", [{"op": "xref_attach", "path": str(xref_src), "name": "MCP-XREF", "at": [window[2] + 2000, window[1], 0]}])
    xr = read("list_xrefs").get("xrefs", [])
    check("xref listed", any(x["name"] == "MCP-XREF" for x in xr), str([(x["name"], x["status"]) for x in xr]))
    out = edit("xref reload", [{"op": "xref_reload", "names": ["MCP-XREF"]}])
    check("reload ran without warnings", not out.get("warnings"), str(out.get("warnings")))
    out = edit("xref detach", [{"op": "xref_detach", "names": ["MCP-XREF"]}])
    check("xref detached", not any(x["name"] == "MCP-XREF" for x in read("list_xrefs").get("xrefs", [])), str(out.get("warnings")))

# ------------------------------------------------------------------ purge and layer state
out = edit("purge list only", [{"op": "purge", "list_only": True}])
listed = result(out).get("purged", {})
out = edit("load unused linetypes", [{"op": "linetype_load", "names": ["BATTING", "ZIGZAG", "TRACKS"]}])
fresh = set(result(out).get("loaded", []))
out = edit("purge linetypes", [{"op": "purge", "kinds": ["linetypes"]}])
gone = result(out).get("purged", {}).get("linetypes", [])
check("unused linetypes purged", fresh <= set(gone), f"loaded {sorted(fresh)}, purged {gone}")
edit("layer state restore", [{"op": "layer_state_restore", "name": "MCP-before"}])

edit("save the test drawing", [{"op": "drawing_save"}])

print("\nSUMMARY")
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if all(v in ("PASS", "SKIP") for v in RESULTS.values()) else "SOME FAILED")
