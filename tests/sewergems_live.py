r"""End-to-end test of the SewerGEMS import against the LIVE Civil 3D (run_sewergems.bat).
Builds the example network in the ACTIVE drawing (must be a new DrawingN.dwg or a name containing mcp_test), then
reads it back and compares every manhole level and pipe invert with the workbook."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
import sewergems  # noqa: E402
from bridge_client import call  # noqa: E402

FOLDER = Path(__file__).resolve().parent.parent.parent / "sewer network"
SRC = {"path": str(FOLDER / "Full output from sewergems.xlsx"), "rename_csv": str(FOLDER / "Output Of Lisp.csv")}
FAILED = []


def check(title, ok, detail=""):
    print(("PASS  " if ok else "FAIL  ") + title + (f"  ({detail})" if detail and not ok else ""), flush=True)
    if not ok:
        FAILED.append(title)


drawing = call("ping")["activeDrawing"]
if not (drawing.lower().startswith("drawing") or "mcp_test" in drawing.lower()):
    sys.exit(f"Refusing to draw in '{drawing}': open a new drawing first.")

report = sewergems.check(SRC)
print(json.dumps(report, indent=1), flush=True)
check("export is ready", report["ready"], report["problems"])

result = sewergems.build({**SRC, "network": "MCP-SEWER"})
print(json.dumps(result, indent=1, default=str), flush=True)
check("built", result.get("built") is True)
check("all pipe ends connected", result.get("connections") == 2 * result.get("pipes", -1), result.get("connections"))

plan = sewergems._plan(SRC)
nodes, rename = plan["nodes"], plan["rename"]
structures = {s["name"]: s for s in call("list_structures", {"network_name": "MCP-SEWER", "limit": 500})["structures"]}
pipes = {p["name"]: p for p in call("list_pipes", {"network_name": "MCP-SEWER", "limit": 500})["pipes"]}
check("51 manholes", len(structures) == len(nodes), len(structures))
check("50 pipes", len(pipes) == len(plan["pipes"]), len(pipes))
bad = [k for k, n in nodes.items()
       if abs(structures[sewergems._name(rename, k)]["rimElevation"] - n["ground"]) > 0.005
       or abs(structures[sewergems._name(rename, k)]["sumpElevation"] - n["invert"]) > 0.005]
check("rim = ground and sump = invert at every manhole", not bad, bad[:5])
bad = []
for p in plan["pipes"]:
    got = pipes[f"P {sewergems._name(rename, p['start'])}"]
    bore = got["innerDiameterOrWidth"]
    half = bore / 2 if bore < 5 else bore / 2000   # metres, or mm in some catalogues
    if abs(got["start"][2] - half - p["inv_start"]) > 0.005 or abs(got["end"][2] - half - p["inv_stop"]) > 0.005:
        bad.append(f"{p['label']}: {got['start'][2] - half:.3f}/{got['end'][2] - half:.3f} vs {p['inv_start']}/{p['inv_stop']}")
check("pipe inverts equal SewerGEMS at both ends", not bad, bad[:5])
views = call("list_profile_views")["profileViews"]
check("one profile view per line", sorted(v["alignment"] for v in views) == sorted(l["name"] for l in report["lines"]),
      [v["alignment"] for v in views])
check("no manhole level changed by Civil 3D", not result.get("levelMismatches"), result.get("levelMismatches"))
boxes = sorted((v["extents"]["min"][1], v["extents"]["max"][1]) for v in views if isinstance(v.get("extents"), dict))
# each view has its band table (sewergems.TABLE_HEIGHT) below it
check("profile views and their tables do not overlap",
      all(a[1] <= b[0] - sewergems.TABLE_HEIGHT + 1e-6 for a, b in zip(boxes, boxes[1:])), boxes)
plan_min_y = min(n["y"] for n in nodes.values())
check("profile views are below the plan", bool(boxes) and boxes[-1][1] < plan_min_y, (boxes[-1:], plan_min_y))
check("presentation applied", result.get("presentation") == "applied", result.get("presentation"))
check("a band table under every profile view", result.get("bandTables") == len(report["lines"]), result.get("bandTables"))
print("ALL PASS" if not FAILED else "FAILED: " + ", ".join(FAILED))
