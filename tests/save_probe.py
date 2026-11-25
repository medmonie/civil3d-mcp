"""Find which stage of diagnose_save crashes Civil 3D (run_save_probe.bat).

Runs diagnose_save in steps of rising risk, in a NEW unsaved drawing (Drawing1 after MCPALLOW). Each step is written to
tests\\results\\save_probe.txt BEFORE it runs, so after a crash the last line names the step that took Civil 3D down.
"""
import json
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

OUT = HERE / "results" / "save_probe.txt"
OUT.parent.mkdir(exist_ok=True)
log = OUT.open("w", encoding="utf-8")


def say(text):
    print(text, flush=True)
    log.write(text + "\n")
    log.flush()


info = call("get_drawing_info", {})
target = info.get("target")
say(f"drawing {info.get('name')} insertionUnits {info.get('insertionUnits')} civilUnits {info.get('civilDrawingUnits')}")

STEPS = [
    ("1 header only", {"scan": False, "save_copy": False}),
    ("2 copy to %TEMP% only (Wblock + SaveAs)", {"scan": False, "save_copy": True}),
    ("3 scan, copy filer, first 5 objects", {"scan": True, "save_copy": False, "filer": "copy", "max_objects": 5}),
    ("5 scan, copy filer, all objects", {"scan": True, "save_copy": False, "filer": "copy"}),
    ("6 full check (scan + copy)", {"scan": True, "save_copy": True}),
]
only = set(sys.argv[1:])
for name, params in STEPS:
    if only and name.split()[0] not in only:
        continue
    say(f"RUNNING {name} {json.dumps(params)}")
    t = time.time()
    try:
        out = call("diagnose_save", {**params, "target": target, "limit": 20}, timeout=EDIT_TIMEOUT)
        scan = out.get("scan") or {}
        say(f"  OK in {time.time() - t:.1f} s: savable={out.get('savable')} headerOk={out.get('headerOk')} "
            f"scan={ {k: scan.get(k) for k in ('clean', 'objectsChecked', 'failureCount', 'danglingHardCount', 'seconds')} } "
            f"copy={out.get('saveCopy')}")
        bad = [h for h in out.get("header", []) if not h.get("ok")]
        if bad:
            say(f"  header problems: {bad}")
        for f in (scan.get("failures") or [])[:5]:
            say(f"  failure: {json.dumps(f, default=str)}")
    except Exception as exc:  # noqa: BLE001
        say(f"  FAILED after {time.time() - t:.1f} s: {exc}")
        break
say("PROBE DONE")
