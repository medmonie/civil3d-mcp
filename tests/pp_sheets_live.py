"""Live test of the plan and profile sheet generator (run_pp_sheets.bat), on a drawing named *mcp_test*.

Uses the office template ...\\Ridge plan&Profile\\PL23-RID-ICD-PRF.01-00.dwg (read only). When the drawing has no
profile views, the WaterGEMS model is imported first so there are real Civil 3D profile views. Then:
views mode (layouts PP-01...), rows mode (layouts R-01...), and a multi-sheet PDF of the PP sheets.
Output: tests\\results\\pp_sheets.txt and pp_sheets_<time>.pdf
"""
import json
import sys
import time
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402
import sheets  # noqa: E402
import watergems  # noqa: E402

DESK = Path.home() / "Desktop" / "CIVIL 3D MCP"
TEMPLATE = DESK / "Ridge plan&Profile" / "PL23-RID-ICD-PRF.01-00.dwg"
WATER = {"path": str(DESK / "water network" / "Output from water model.xlsx"),
         "valve_layer": "Gate-Valve", "service_layers": ["WATER-63", "WATER-25mm"], "hydrant_layer": "FIRE FITTING"}
OUT = Path(__file__).resolve().parent / "results"
RESULTS: dict[str, str] = {}


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def show(title, data):
    text = json.dumps(data, indent=1, default=str)
    print(f"===== {title}\n{text[:6000]}", flush=True)
    return data


info = call("get_drawing_info")
if "mcp_test" not in info["name"].lower():
    raise SystemExit(f"Refusing to run on '{info['name']}': open a drawing whose name contains mcp_test.")
target = info["target"]

views = call("list_profile_views", {"target": target}).get("profileViews", [])
if not views:
    r = show("WaterGEMS import (for profile views)", watergems.build({**WATER, "target": target, "request_id": str(uuid.uuid4())}))
    check("profile views created", r.get("built") is True)
    views = call("list_profile_views", {"target": target}).get("profileViews", [])
print("profile views:", [v["name"] for v in views])

# ---------------------------------------------------------------- views mode
base = {"template": str(TEMPLATE), "target": target}
plan = show("pp_sheets_plan (views)", sheets.check({**base, "layout_prefix": "PP-"}))
found = plan["found"]
check("template parts found", all(found[k] for k in ("plan", "profile", "key", "header", "label", "scaleBar", "keyBox")) and found["listLines"] > 0, str(found))
check("sheets planned from the profile views", plan["mode"] == "views" and len(plan["sheets"]) >= 1, str([(s["layout"], s["lines"]) for s in plan["sheets"]]))
built = show("pp_sheets_create (views)", sheets.build({**base, "layout_prefix": "PP-", "if_exists": "replace", "request_id": str(uuid.uuid4())}))
check("views sheets built", built.get("built") is True, str(built.get("stages")))
for s in built.get("sheets", []):
    vps = call("list_viewports", {"layout": s["layout"], "target": target}).get("viewports", [])
    ents = call("list_entities", {"layout": s["layout"], "limit": 2000, "target": target}).get("entities", [])
    texts = [e["geometry"]["text"] for e in ents if e["type"] == "TEXT"]
    check(f"{s['layout']}: plan viewport at {s['planScale']}", any(v["scale"] == s["planScale"] for v in vps), str([v["scale"] for v in vps]))
    check(f"{s['layout']}: all viewports switched on", bool(vps) and all(v.get("on") for v in vps), str([(v["scale"], v.get("on")) for v in vps]))
    check(f"{s['layout']}: list of lines", all(any(n in t for t in texts) for n in s["lines"]), str([t for t in texts if "%%U" in t]))
    check(f"{s['layout']}: sheet label", any(s["label"] in t for t in texts), str([t for t in texts if "PLAN/PROFILE" in t]))
xr = call("list_xrefs", {"target": target}).get("xrefs", [])
frame = next((x for x in xr if x["name"].upper() == "XR.FRAME"), None)
check("title block xref resolved", frame is not None and frame["status"] == "Resolved", str(frame))

# ---------------------------------------------------------------- rows mode
rows = {**base, "mode": "rows", "layout_prefix": "R-", "if_exists": "replace", "plan": "template", "total_sheets": 2,
        "sheets": [{"lines": ["ICD Line 1", "ICD Line 1-1"]}, {"lines": ["ICD Line 2"]}]}
built_rows = show("pp_sheets_create (rows)", sheets.build({**rows, "request_id": str(uuid.uuid4())}))
check("rows sheets built", built_rows.get("built") is True, str(built_rows.get("stages")))
centers = []
for s in built_rows.get("sheets", []):
    vps = call("list_viewports", {"layout": s["layout"], "target": target}).get("viewports", [])
    prof = min(vps, key=lambda v: v["at"][1]) if vps else None
    centers.append(prof["viewCenter"] if prof else None)
check("rows 500 m apart", len(centers) == 2 and None not in centers and abs(centers[0][1] - centers[1][1] - 500) < 0.01, str(centers))

# ---------------------------------------------------------------- PDF of the generated sheets
pdf = OUT / f"pp_sheets_{time.strftime('%Y%m%d_%H%M%S')}.pdf"
layouts = [s["layout"] for s in built.get("sheets", [])]
if layouts:
    r = show("plot", call("edit", {"operations": [{"op": "plot_pdf", "path": str(pdf), "layouts": layouts}],
                                   "target": target, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT))
    check("sheets plotted to one PDF", pdf.exists() and not r.get("warnings"), str(r.get("warnings") or r.get("error")))
call("edit", {"operations": [{"op": "drawing_save"}], "target": target, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)

print("\nSUMMARY")
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if all(v == "PASS" for v in RESULTS.values()) else "SOME FAILED")
