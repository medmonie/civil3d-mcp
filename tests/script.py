"""Development aid: put any live calls here and run run_script.bat (output: results\\script.txt).

Now: reads the Ridge plan & profile drawings from disk (inspect_file, nothing is opened or changed) and saves
results\\inspect - <file>.json for each."""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import call  # noqa: E402

FOLDER = Path.home() / "Desktop" / "CIVIL 3D MCP" / "Ridge plan&Profile"
RESULTS = Path(__file__).resolve().parent / "results"
for f in [FOLDER / "XR.ICD-PROFILES.dwg", FOLDER / "XR.BASE.dwg"]:
    try:
        r = call("inspect_file", {"path": str(f), "limit": 20}, timeout=300)
    except Exception as exc:  # noqa: BLE001
        print(f"{f.name}: FAILED {exc}")
        continue
    (RESULTS / f"inspect - {f.stem}.json").write_text(json.dumps(r, indent=1, default=str), encoding="utf-8")
    print(f"{f.name}: layouts={[l['name'] for l in r['layouts']]} xrefs={[x['name'] for x in r['xrefs']]} model={r['modelSpace']['count']}")
print("DONE")
