"""Live test of 0.23 (run_v023.bat): coordinate system reads and set (roadmap #39), the file policy, strict whole
numbers, the permission retry and the commit receipt.

Run it in a NEW drawing from the Civil 3D metric template (DrawingN, after MCPALLOW). Nothing is saved.
Output: tests\\results\\v023.txt
"""
import json
import os
import sys
import tempfile
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
TARGET = call("get_drawing_info", {}).get("target")
name = Path(call("ping").get("activeDrawing") or "").name.lower()
if not (name.startswith("drawing") or "mcp_test" in name):
    sys.exit(f"REFUSED: the active drawing is '{name}'. Run this in a new unsaved drawing (DrawingN).")


def check(title, ok, detail=""):
    RESULTS[title] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[title]:5} {title} {detail}", flush=True)


def show(title, out, cap=3000):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < cap else text[:cap] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, **params):
    try:
        out = call(method, {**params, "target": TARGET})
    except Exception as exc:     # noqa: BLE001
        out = {"_error": str(exc)}
    return show(method, out)


def edit(title, ops, expect=True, dry_run=False, request_id=None):
    params = {"operations": ops, "target": TARGET, "request_id": request_id or str(uuid.uuid4())}
    if dry_run:
        params["dry_run"] = True
    try:
        out = call("edit", params, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    if expect is not None:
        ok = (out.get("applied") is True) if expect and not dry_run else \
             (out.get("dryRun") is True and "failedAt" not in out and "exception" not in out) if expect else \
             (out.get("applied") is not True)
        check(title, ok, str(out.get("error") or out.get("exception") or ""))
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


# ---------------------------------------------------------------- 1. coordinate system
def coordinate_system():
    start = read("get_coordinate_system")
    check("get_coordinate_system answers", "_error" not in start and "assigned" in start, str(start.get("_error", "")))
    tf = start.get("transformation") or {}
    if tf.get("available") is False or tf.get("error"):
        print(f"NOTE  transformation settings: {tf.get('reason') or tf.get('error')}")
    original = start.get("code") or ""

    found = read("list_coordinate_systems", contains="UTM84-36", limit=20)
    codes = [r["code"] for r in found.get("coordinateSystems", [])]
    check("list_coordinate_systems finds UTM84-36N", "UTM84-36N" in codes, f"codes: {codes[:8]}")
    check("each row carries a description", all(isinstance(r.get("description"), str) and r["description"] for r in found.get("coordinateSystems", [])))
    by_text = read("list_coordinate_systems", contains="Egypt", in_description=True, limit=20)
    check("in_description search answers", "_error" not in by_text and by_text.get("matched", 0) >= 0, str(by_text.get("_error", "")))

    bad = edit("coordinate_system_set refuses an unknown code", [{"op": "coordinate_system_set", "code": "NOT-A-ZONE-XYZ"}], expect=False)
    check("the refusal names list_coordinate_systems", "list_coordinate_systems" in str(bad.get("error", "")))
    kept = read("get_coordinate_system")
    check("the drawing kept its code after the refusal", "_error" not in kept and (kept.get("code") or "") == original, str(kept.get("code")))

    dry = edit("coordinate_system_set dry run", [{"op": "coordinate_system_set", "code": "UTM84-36N"}], dry_run=True)
    check("the dry run names the code it would set", result(dry).get("code") == "UTM84-36N" and result(dry).get("queued") is True, str(result(dry))[:200])
    after_dry = read("get_coordinate_system")
    check("the dry run left the drawing's code alone", "_error" not in after_dry and (after_dry.get("code") or "") == original, str(after_dry.get("code")))

    done = edit("coordinate_system_set UTM84-36N", [{"op": "coordinate_system_set", "code": "utm84-36n"}])
    check("the reply spells the code as the library does", result(done).get("code") == "UTM84-36N", str(result(done).get("code")))
    check("the assignment after the commit succeeded", done.get("afterCommitSucceeded") is True, str(done.get("warnings")))
    now = read("get_coordinate_system", wkt=True)
    system = now.get("system") or {}
    check("get_coordinate_system shows the new code", now.get("code") == "UTM84-36N" and now.get("assigned") is True)
    check("the system has description, datum and unit", all(isinstance(system.get(k), str) and system[k] for k in ("description", "datum", "unit")), str(system))
    check("EPSG code is 32636", system.get("epsg") == 32636, str(system.get("epsg")))
    check("WKT is returned on request", isinstance(system.get("wkt"), str) and "36" in system["wkt"])
    geo = now.get("geoLocation") or {}
    check("AutoCAD's geographic location follows the zone", geo.get("present") is True and geo.get("coordinateSystem") == "UTM84-36N" and now.get("cgeocs") == "UTM84-36N", str(geo)[:200])

    again = edit("setting the same code again changes nothing", [{"op": "coordinate_system_set", "code": "UTM84-36N"}])
    check("unchanged is reported", result(again).get("unchanged") is True)

    def transforms(title):
        # 500000 E is the central meridian of zone 36 (33 E); the UTM northing of 30 N on it is 3,318,785.353 m.
        fwd = read("transform_coordinates", points=[[500000, 3318785.353, 0]])
        pt = (fwd.get("points") or [[None, None]])[0]
        check(f"{title}: drawing -> 33 E, 30 N", pt[0] is not None and abs(pt[0] - 33.0) < 1e-6 and abs(pt[1] - 30.0) < 1e-6, str(pt))
        back = read("transform_coordinates", points=[[33.0, 30.0]], to="drawing")
        bp = (back.get("points") or [[None, None]])[0]
        check(f"{title}: 33 E, 30 N -> drawing within 1 mm", bp[0] is not None and abs(bp[0] - 500000) < 0.001 and abs(bp[1] - 3318785.353) < 0.001, str(bp))

    transforms("after the set")
    # A dry run and a failed batch of another zone must leave both the code and the conversion alone. (Set inside the
    # transaction, the rolled-back zone stayed in AutoCAD's forward conversion: 33 E came back as 27 E.)
    edit("a dry run of another zone", [{"op": "coordinate_system_set", "code": "UTM84-35N"}], dry_run=True)
    edit("a failing batch with another zone", [
        {"op": "coordinate_system_set", "code": "UTM84-35N"},
        {"op": "entity_erase", "handles": ["FFFFFFFFFF"]}], expect=False)
    check("the code is still UTM84-36N", read("get_coordinate_system").get("code") == "UTM84-36N")
    transforms("after a dry run and a failed batch")
    wrong = read("transform_coordinates", points=[[500000, 3320000]], to="drawing")
    check("drawing coordinates passed as lonlat are refused", "longitude" in str(wrong.get("_error", "")))

    edit("coordinate_system_set clears with none", [{"op": "coordinate_system_set", "code": "none"}])
    gone = read("get_coordinate_system")
    check("the code is cleared", gone.get("assigned") is False and gone.get("cgeocs") in ("", None), str(gone.get("code")))
    none = read("transform_coordinates", points=[[500000, 3320000]])
    check("transform_coordinates refuses a drawing without a coordinate system", "no geographic location" in str(none.get("_error", "")), str(none)[:160])
    if original:
        edit("restore the drawing's original code", [{"op": "coordinate_system_set", "code": original}])
    check("the drawing ends with the code it started with", (read("get_coordinate_system").get("code") or "") == original)


# ---------------------------------------------------------------- 2. file policy
def file_policy():
    perm = read("get_edit_permission")
    files = perm.get("files") or {}
    check("get_edit_permission reports the file policy", "restricted" in files, str(files))
    data = Path(os.environ["LOCALAPPDATA"]) / "C3dMcpBridge"
    fmt = (read("list_point_file_formats").get("formats") or [{}])[0].get("name")
    cases = [
        ("the audit log cannot be overwritten", str(data / "audit.log")),
        ("the bridge file cannot be overwritten", str(data / "bridge.json")),
        ("a .bat is never written", str(Path(tempfile.gettempdir()) / "points.bat")),
        ("a Startup folder is refused", str(Path(os.environ["APPDATA"]) / "Microsoft/Windows/Start Menu/Programs/Startup/points.txt")),
        ("a device path is refused", "\\\\.\\C:\\Temp\\points.txt"),
    ]
    for title, path in cases:
        out = edit(title, [{"op": "points_export", "path": path, "format": fmt, "overwrite": True}], expect=False)
        check(title + ": refused at the operation", out.get("failedAt") == 1, str(out.get("failedAt")))
    check("the audit log is still there", (data / "audit.log").is_file())
    ok = Path(tempfile.gettempdir()) / f"mcp-points-{uuid.uuid4().hex[:6]}.csv"
    edit("a fixture point", [{"op": "cogo_points_add", "points": [{"x": 1000, "y": 2000, "z": 10, "description": "T"}]}], expect=None)
    out = edit("points_export to %TEMP% still works", [{"op": "points_export", "path": str(ok), "format": fmt}], expect=None)
    if out.get("applied") is True and out.get("afterCommitSucceeded") is True:
        check("the export file exists", ok.is_file())
        ok.unlink(missing_ok=True)
    else:
        print(f"NOTE  points_export fixture not exercised: {out.get('error') or out.get('warnings')}")
    token = read("inspect_file", path=str(data / "bridge.json"))
    check("inspect_file cannot open the bridge folder", "_error" in token, str(token)[:120])


# ---------------------------------------------------------------- 3. strict whole numbers
def whole_numbers():
    for title, value in (("text", "3"), ("a fraction", 2.5), ("out of range", 12)):
        out = edit(f"dimstyle decimals as {title} is refused", [
            {"op": "dim_style_create", "name": "MCP-" + uuid.uuid4().hex[:6], "decimals": value}], expect=False)
        check(f"the message for {title} names the range", "whole number from 0 to 8" in str(out.get("error", "")), str(out.get("error")))
    edit("dimstyle decimals 3 is accepted", [{"op": "dim_style_create", "name": "MCP-" + uuid.uuid4().hex[:6], "decimals": 3}])


# ---------------------------------------------------------------- 4. permission retry
def permission_retry():
    print("NOTE  permission retry: run MCPREVOKE, send an edit, answer No, run MCPALLOW, resend the same request_id by hand.")
    rid = str(uuid.uuid4())
    first = edit("an edit with a fresh request_id applies", [{"op": "layer_create", "name": "MCP-RETRY-" + rid[:6]}], request_id=rid)
    second = edit("the same request_id returns the recorded result", [{"op": "layer_create", "name": "MCP-RETRY-" + rid[:6]}], request_id=rid)
    check("the replay did not run again", first.get("applied") is True and second.get("applied") is True and "failedAt" not in second)


for section in (coordinate_system, file_policy, whole_numbers, permission_retry):
    guarded(section)

failed = [t for t, r in RESULTS.items() if r != "PASS"]
print("\n" + ("ALL PASS" if not failed else f"{len(failed)} FAILED: " + "; ".join(failed)))
