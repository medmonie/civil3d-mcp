"""Live test of the import retries (docs/AUDIT_TEST_PLAN.md B1-B3).

  imports_live.py sewer   B2. In a NEW drawing (DrawingN, MCPALLOW): a SewerGEMS build, and the same call (same
                          request_id) again while the first still runs.
  imports_live.py water   B3 then B1. Open "water network\\PL15-21-WYTE-WTR-PLN.dwg" (or its import copy); the script
                          saves it as the copy first, like watergems_live.py. A pressure pipe named like the first
                          WaterGEMS pipe label is drawn before the import (B3), then the import is called twice with the
                          same request_id (B1).
Output: tests\\results\\imports_<mode>.txt
"""
import json
import sys
import threading
import time
import traceback
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "server"))
sys.path.insert(0, str(HERE))
from bridge_client import call  # noqa: E402
import sewergems  # noqa: E402
import watergems  # noqa: E402

RESULTS: dict[str, str] = {}
T0 = time.monotonic()


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"[{time.monotonic() - T0:6.1f}s] {RESULTS[name]:5} {name} {detail}", flush=True)


def dump(title, data, cap=3000):
    text = json.dumps(data, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < cap else text[:cap] + chr(10) + '... (truncated)'}", flush=True)


def guarded(fn, *args):
    try:
        return fn(*args), None
    except Exception as exc:     # noqa: BLE001
        return None, f"{type(exc).__name__}: {exc}\n{traceback.format_exc()}"


def sewer():
    folder = HERE.parent.parent / "sewer network"
    src = {"path": str(folder / "Full output from sewergems.xlsx"), "rename_csv": str(folder / "Output Of Lisp.csv")}
    info = call("get_drawing_info")
    if not (info["name"].lower().startswith("drawing") or "mcp_test" in info["name"].lower()):
        sys.exit(f"Refusing to draw in '{info['name']}': open a new drawing first.")
    params = {**src, "network": "MCP-SEWER-B2", "target": info["target"], "request_id": str(uuid.uuid4())}
    first: dict = {}

    def run_first():
        first["result"], first["error"] = guarded(sewergems.build, params)
        first["at"] = time.monotonic() - T0

    t = threading.Thread(target=run_first)
    t.start()
    time.sleep(float(sys.argv[2]) if len(sys.argv) > 2 else 0.7)
    second, err = guarded(sewergems.build, params)
    at2 = time.monotonic() - T0
    t.join()
    dump("B2 second call (same request_id, while the first runs)", second or err)
    dump("B2 first call", first.get("result") or first.get("error"), cap=1500)
    print(f"first finished at {first.get('at', 0):.1f}s, second at {at2:.1f}s", flush=True)
    check("B2 the first build succeeded", bool(first.get("result") and first["result"].get("built")))
    ok = isinstance(second, dict) and second.get("pending") is True
    finish_pending = isinstance(second, dict) and (second.get("finish") or {}).get("state") == "pending"
    print("second call: top-level pending" if ok else "second call: finish phase pending" if finish_pending else "second call: other", flush=True)
    check("B2 the second call says pending: true (not networkCommitted: false)", ok,
          "" if ok else json.dumps(second if second is not None else err, default=str)[:400])


def water():
    import watergems_live as wl   # noqa: E402 - reuses its options and the save-as-a-copy step
    target = wl.working_target()
    opts = {**wl.OPTIONS, "target": target}
    o = watergems._options(opts)
    label = watergems.read(opts)["pipes"][0]["label"]
    print("first WaterGEMS pipe label:", label, flush=True)

    # B3: a pressure pipe already named like a WaterGEMS pipe label
    sizes = call("list_pressure_part_sizes", {"target": target, "parts_list": o["parts_list"], "part_type": "Pipe", "limit": 5})
    first = (sizes.get("sizes") or [None])[0]
    desc = first.get("description") if isinstance(first, dict) else first
    nets = {n["name"] for n in call("list_pressure_networks", {"target": target}).get("pressureNetworks", [])}
    if "B3 CONFLICT" not in nets:
        prep = call("edit", {"target": target, "request_id": str(uuid.uuid4()), "operations": [
            {"op": "pressure_network_create", "name": "B3 CONFLICT", "parts_list": o["parts_list"]},
            {"op": "pressure_add", "network": "B3 CONFLICT", "pipes": [{"name": label, "start": [0, 0, 0], "end": [5, 0, 0], "part": desc}]}]},
            timeout=300)
        dump("B3 conflicting pipe", prep)
        check("B3 conflicting pipe drawn", prep.get("applied") is True, str(prep.get("error") or ""))

    rid = str(uuid.uuid4())
    r1, e1 = guarded(watergems.build, {**opts, "request_id": rid})
    dump("B1 first build", r1 or e1)
    check("B1 first build done", isinstance(r1, dict) and r1.get("built") is True, "" if r1 else e1[:300])
    text = json.dumps(r1, default=str)
    hits = [w for w in (r1 or {}).get("warnings", []) if label in w] if isinstance(r1, dict) else []
    check("B3 a warning names the part Civil 3D renamed", label in text and ("instead of" in text or "renamed" in text.lower()),
          json.dumps(hits, default=str)[:300])

    r2, e2 = guarded(watergems.build, {**opts, "request_id": rid})
    dump("B1 second build, same request_id", r2 or e2)
    check("B1 second call returns the recorded result or the expired note, never a KeyError",
          e2 is None and isinstance(r2, dict) and "KeyError" not in json.dumps(r2, default=str), (e2 or "")[:300])
    nets = [n["name"] for n in call("list_pressure_networks", {"target": target}).get("pressureNetworks", [])]
    check("B1 no second network built", nets.count(o["network"]) == 1 and not any(n.startswith(o["network"] + " ") for n in nets), str(nets))


def water_b3():
    """B3 again in the import copy that already holds the 'Water' network: a second import as 'Water B3' meets every
    unsplit pipe name already used in the drawing."""
    import watergems_live as wl   # noqa: E402
    target = wl.working_target()
    opts = {**wl.OPTIONS, "target": target, "network": "Water B3"}
    used = {p["name"] for p in call("list_pressure_pipes", {"target": target, "network_name": "Water", "limit": 2000}).get("pipes", [])}
    print(f"{len(used)} pipe names already used, e.g. {sorted(used)[:5]}", flush=True)
    r, e = guarded(watergems.build, {**opts, "request_id": str(uuid.uuid4())})
    dump("B3 import as 'Water B3'", r or e, cap=6000)
    text = json.dumps(r, default=str)
    warns = [w for w in (r or {}).get("warnings", [])] if isinstance(r, dict) else []
    named = [w for w in warns if "instead of" in w]
    print("rename warnings:", len(named), named[:5], flush=True)
    check("B3 a warning names the part Civil 3D renamed", bool(named) or "instead of" in text, (e or "")[:300])


if __name__ == "__main__":
    {"sewer": sewer, "water": water, "water_b3": water_b3}[sys.argv[1] if len(sys.argv) > 1 else "sewer"]()
    for k, v in RESULTS.items():
        print(f"{v:5} {k}")
    print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
