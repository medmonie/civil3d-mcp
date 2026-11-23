"""Live test of profile view bands (run_bands.bat).

Read-only except for one dry run (rolled back): lists the drawing's band sets and their bands, then makes a throw-away
alignment and profile, a profile view with the default band set, one with band_set "auto", and re-bands the first with
profile_view_bands band_set "auto". Works in any drawing, including a new one from the Civil 3D template. Output: tests\\results\\bands.txt
"""
import json
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def show(title, out):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < 4000 else text[:4000] + chr(10) + '... (truncated)'}", flush=True)
    return out


target = call("get_drawing_info", {}).get("target")
print("drawing:", target, flush=True)
sets = call("list_styles", {"kind": "profile_view_band_set", "target": target})
names = [s if isinstance(s, str) else s.get("name") for s in sets.get("styles", sets.get("names", []))]
show("band sets", sets)
counts = {}
for n in names:
    try:
        d = call("get_style", {"kind": "profile_view_band_set", "name": n, "target": target})
        counts[n] = len(d.get("bottom", [])) + len(d.get("top", []))
    except Exception as exc:     # noqa: BLE001
        counts[n] = f"error: {exc}"
print("bands per set:", counts, flush=True)
check("drawing has a band set with bands", any(isinstance(v, int) and v > 0 for v in counts.values()), str(counts))

# Everything below is a dry run: a throw-away alignment and ground profile, a profile view with the drawing's default
# band set, one with band_set "auto", then the default view re-banded with profile_view_bands band_set "auto".
tag = uuid.uuid4().hex[:6]
al = f"bands test {tag}"
ops = [
    {"op": "alignment_create", "name": al, "points": [[0, -100000], [200, -100000], [400, -99950]]},
    {"op": "profile_create", "alignment": al, "name": f"{al} - Ground", "pvis": [[0, 10], [200, 12], [400, 9]]},
    {"op": "profile_view_create", "alignment": al, "name": f"{al} - default", "at": [0, -100200]},
    {"op": "profile_view_create", "alignment": al, "name": f"{al} - auto", "at": [0, -100400], "band_set": "auto"},
    {"op": "profile_view_bands", "profile_view": f"{al} - default", "band_set": "auto"},
]
out = show("dry run", call("edit", {"operations": ops, "dry_run": True, "target": target,
                                     "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT))
res = [r.get("result") or {} for r in out.get("results", [])]
for r, op in zip(out.get("results", []), ops):
    check(f"{op['op']} {op.get('name') or op.get('profile_view')} ran", not r.get("error"), str(r.get("error") or ""))
if len(res) == len(ops):
    print(f"default band set: {res[2].get('bandSet')} -> {len(res[2].get('bands', []))} bands", flush=True)
    if any("Elevation" in n and isinstance(c, int) and c > 0 for n, c in counts.items()) and not counts.get("Standard"):
        check("auto picks an Elevations band set", "Elevation" in (res[3].get("bandSet") or ""), str(res[3].get("bandSet")))
    check("profile view with band_set auto has bands", len(res[3].get("bands", [])) > 0, f"set {res[3].get('bandSet')}: {res[3].get('bands')}")
    check("profile_view_bands band_set auto adds bands", len(res[4].get("bands", [])) > 0, str(res[4].get("bands")))
check("dry run rolled back", out.get("applied") is False and out.get("dryRun") is True)

print("\nSUMMARY")
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
