"""Find what makes a drawing unsavable ("object cannot be saved ... no file was created") (run_save.bat).

Seen once when quitting Civil 3D after the five live suites had run in one drawing (0.15 civil, 0.16 data,
0.17 geometry, 0.18 extras, 0.19 model). This runner replays those suites in that order, in ONE new unsaved drawing,
and after EVERY edit call (applied or rolled back) asks the plugin's read-only `diagnose_save`:
  * header: do the current layer / text / dimension / multileader / table styles, linetype and annotation scale
    still point at live objects?
  * scan: every object is written through a probe copy filer. Objects whose writing fails, and
    objects that still point (hard) at an erased object, are listed by handle, type, owner and Civil 3D name.
The first edit after which the drawing stops being savable is reported with its suite, title and operations: that
edit (and the objects named) is the root cause. After each suite a full check also writes a real copy to %TEMP%.

Run it in a NEW drawing from the Civil 3D metric template (Drawing1, after MCPALLOW); nothing is saved.
Output: tests\\results\\save.txt (log) and tests\\results\\save_check.json (every diagnosis that found a problem).

Options:  --suites civil,data,geometry,extras,model   which suites, in this order (default: all five)
          --check edit|suite      after every edit (default) or only after each suite
          --copy-each-edit        also write the %TEMP% copy after every edit (slower; use when the scan is clean
                                  but the copy fails)
          --keep-going            do not stop at the first problem (to see whether later steps add more)
"""
import argparse
import json
import runpy
import sys
import time
import traceback
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "server"))
import bridge_client  # noqa: E402

SUITES = {"civil": "civil_live.py", "data": "data_live.py", "geometry": "geometry_live.py",
          "extras": "extras_live.py", "model": "model_live.py"}
OUT = HERE / "results"
OUT.mkdir(exist_ok=True)
REPORT = OUT / "save_check.json"

ap = argparse.ArgumentParser()
ap.add_argument("--suites", default=",".join(SUITES))
ap.add_argument("--check", choices=("edit", "suite"), default="edit")
ap.add_argument("--copy-each-edit", action="store_true")
ap.add_argument("--keep-going", action="store_true")
args = ap.parse_args()

real_call = bridge_client.call
state = {"suite": "baseline", "edit": 0, "target": None, "first": None, "problems": []}


class FirstProblem(BaseException):
    """Stops the running suite at the first edit that leaves the drawing unsavable. BaseException, because the suites
    catch Exception around their own edit calls and would otherwise carry on."""


def log(text):
    print(text, flush=True)


def diagnose(target, copy):
    params = {"target": target, "scan": True, "save_copy": copy, "limit": 50}
    return real_call("diagnose_save", params, timeout=bridge_client.EDIT_TIMEOUT)


def summary(d):
    """One line: what is wrong, or 'savable'."""
    if d.get("savable"):
        s = d.get("scan") or {}
        return f"savable ({s.get('objectsChecked')} objects, {s.get('seconds')} s)"
    parts = [f"header {h['variable']}={h['status']}" for h in d.get("header", []) if not h.get("ok")]
    s = d.get("scan") or {}
    if s.get("unopenable"):
        opened = [f["object"].get("type") for f in s.get("failures") or [] if f.get("stage") == "open"]
        parts.append(f"{s.get('unopenable')} object(s) cannot be opened (left open by something else) {sorted(set(opened))}")
    if s.get("failureCount"):
        parts.append(f"{s.get('failureCount')} object(s) fail to write {s.get('failuresByType')}")
    if s.get("danglingHardCount"):
        parts.append(f"{s.get('danglingHardCount')} hard reference(s) to erased objects {s.get('danglingHardByType')}")
    c = d.get("saveCopy")
    if c and not c.get("ok"):
        parts.append(f"copy to %TEMP% failed: {c.get('error')}")
    return "NOT SAVABLE: " + "; ".join(parts)


def record(where, params, out, d):
    """Keep every diagnosis that found a problem; the first one is the root cause."""
    entry = {
        "suite": state["suite"], "edit": state["edit"], "where": where,
        "operations": [o.get("op") for o in (params or {}).get("operations", []) if isinstance(o, dict)],
        "editApplied": (out or {}).get("applied"),
        "editErrors": [f"{r.get('op')}: {r.get('error')}" for r in (out or {}).get("results", []) if r.get("error")],
        "diagnosis": d,
    }
    state["problems"].append(entry)
    REPORT.write_text(json.dumps(state["problems"], indent=1, default=str), encoding="utf-8")
    if state["first"] is None:
        state["first"] = entry
        log("\n" + "#" * 100)
        log(f"FIRST SAVE PROBLEM  suite={entry['suite']}  edit #{entry['edit']}  {where}")
        log(f"  operations: {entry['operations']}")
        log(f"  edit applied: {entry['editApplied']}   errors: {entry['editErrors'] or 'none'}")
        log(f"  {summary(d)}")
        for h in d.get("header", []):
            if not h.get("ok"):
                log(f"  header {h['variable']}: {h['status']} (handle {h.get('handle')}, {h.get('name')})")
        s = d.get("scan") or {}
        for f in (s.get("failures") or [])[:15]:
            log(f"  FAILS TO WRITE: {json.dumps(f, default=str)}")
        for r in (s.get("danglingHard") or [])[:15]:
            log(f"  POINTS AT ERASED: {json.dumps(r, default=str)}")
        if d.get("saveCopy") and not d["saveCopy"].get("ok"):
            log(f"  copy: {d['saveCopy']}")
        log("#" * 100 + "\n")
        if not args.keep_going:
            raise FirstProblem()


def caller_title():
    """The suites call `call("edit", ...)` from their own edit(title, ops): show that title."""
    try:
        return sys._getframe(2).f_locals.get("title")
    except ValueError:
        return None


def watched_call(method, params=None, *a, **kw):
    out = None
    try:
        out = real_call(method, params, *a, **kw)
        return out
    finally:
        if method == "edit" and isinstance(params, dict) and params.get("target"):
            state["edit"] += 1
            state["target"] = params["target"]
            if args.check == "edit" and sys.exc_info()[0] is not FirstProblem:
                title = caller_title() or ",".join(o.get("op", "?") for o in params.get("operations", []) if isinstance(o, dict))
                where = f"after edit '{title}'"
                try:
                    d = diagnose(params["target"], copy=args.copy_each_edit)
                except Exception as exc:     # noqa: BLE001 - a broken check is reported, not fatal
                    log(f"[save-check] {state['suite']} #{state['edit']} {where}: diagnose_save failed: {exc}")
                else:
                    log(f"[save-check] {state['suite']} #{state['edit']} {where}: {summary(d)}")
                    if not d.get("savable"):
                        record(where, params, out, d)


bridge_client.call = watched_call      # the suites do `from bridge_client import call` when they start

# ---------------------------------------------------------------- baseline
info = real_call("get_drawing_info", {})
state["target"] = info.get("target")
log(f"drawing {info.get('name')}  units {info.get('civilDrawingUnits')}  target {state['target']}")
if info.get("path") and Path(str(info.get("path"))).suffix.lower() == ".dwg" and Path(str(info["path"])).exists():
    log("WARNING: this drawing has been saved before; run the test in a NEW unsaved drawing.")
base = diagnose(state["target"], copy=True)
log(f"[save-check] baseline (empty drawing): {summary(base)}")
if not base.get("savable"):
    log("The drawing is not savable BEFORE any test ran: the template or Civil 3D itself is the cause, not the MCP.")
    state["suite"] = "baseline"
    try:
        record("baseline", None, None, base)
    except FirstProblem:
        pass

# ---------------------------------------------------------------- suites
started = time.time()
stopped = False
for name in [s.strip() for s in args.suites.split(",") if s.strip()]:
    if name not in SUITES:
        log(f"unknown suite '{name}' (use {', '.join(SUITES)})")
        continue
    state["suite"] = name
    log("\n" + "=" * 100 + f"\nSUITE {name} ({SUITES[name]})\n" + "=" * 100)
    try:
        runpy.run_path(str(HERE / SUITES[name]), run_name="__main__")
    except FirstProblem:
        stopped = True
    except SystemExit:
        pass
    except Exception:            # noqa: BLE001 - a suite's own crash must not hide the save result
        log(f"suite {name} stopped with an exception:\n{traceback.format_exc()}")
    if stopped:
        break
    d = diagnose(state["target"], copy=True)
    log(f"[save-check] END OF SUITE {name} (full check with %TEMP% copy): {summary(d)}")
    if not d.get("savable"):
        try:
            record(f"end of suite {name} (full check)", None, None, d)
        except FirstProblem:
            stopped = True
            break

# ---------------------------------------------------------------- verdict
log("\n" + "=" * 100 + "\nVERDICT")
first = state["first"]
if first is None:
    log(f"No save problem found after {state['edit']} edits ({round(time.time() - started)} s).")
    log("Next: QUIT Civil 3D and answer NO. If the 'cannot be saved' message still appears, it does not come from an object")
    log("in this drawing: note the exact text and whether Civil 3D was autosaving (settings.autosaveMinutes / autosaveFolder")
    log("in save_check.json of the last check), then rerun with --copy-each-edit.")
else:
    log(f"ROOT CAUSE: suite '{first['suite']}', edit #{first['edit']} {first['where']}")
    log(f"  operations in that edit: {first['operations']}  (applied: {first['editApplied']})")
    log(f"  {summary(first['diagnosis'])}")
    log("  The objects named under FIRST SAVE PROBLEM are the elements that break the save; the operation that made")
    log("  them is in the list above. Full details: tests\\results\\save_check.json")
    log("Next: QUIT Civil 3D and answer NO (nothing is saved). Rerun with --suites set to that suite only to confirm it")
    log("reproduces in a fresh drawing.")
print("SAVE CHECK " + ("CLEAN" if first is None else "FOUND THE CAUSE"), flush=True)
