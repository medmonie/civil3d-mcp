"""Live WaterGEMS import test (run_watergems.bat). Works on a copy: the open drawing is first saved under a new name."""
import json
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import call  # noqa: E402
import watergems  # noqa: E402

WATER = Path(__file__).resolve().parents[2] / "water network"
OPTIONS = {
    "path": str(WATER / "Output from water model.xlsx"),
    "valve_layer": "Gate-Valve", "service_layers": ["WATER-63", "WATER-25mm"], "hydrant_layer": "FIRE FITTING",
    "cad_pipe_layers": ["WE. P. DI 110mm", "WE. P. DI 160mm", "WE. P. DI 200mm", "WE. P. DI 250mm", "WATER-63", "WAER PIPE 300MM"],
}
RESULTS = {}


def check(name, ok):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name}", flush=True)


def working_target():
    source = (WATER / "PL15-21-WYTE-WTR-PLN.dwg").resolve()
    copy = source.with_name(source.stem + " - WaterGEMS import.dwg")
    info = call("get_drawing_info")
    active = Path(info["path"]).resolve()
    if active == source:
        saved = call("edit", {"operations": [{"op": "drawing_save", "path": str(copy), "overwrite": True}],
                              "target": info["target"], "request_id": str(uuid.uuid4())}, timeout=300)
        if not saved.get("applied") or saved.get("afterCommitSucceeded") is False or saved.get("warnings"):
            raise SystemExit(f"Save As failed. Import stopped: {saved}")
        info = call("get_drawing_info", {"target": info["target"]})
    if Path(info["path"]).resolve() != copy or not copy.is_file():
        raise SystemExit(f"Import stopped. The active drawing must be the test copy at {copy}.")
    return info["target"]


def main():
    target = working_target()
    report = watergems.check({**OPTIONS, "target": target})
    print(json.dumps(report, indent=1), flush=True)
    check("export is ready", report.get("ready") is True)

    if not report.get("ready"):
        raise SystemExit("Input checks failed. Import stopped.")

    result = watergems.build({**OPTIONS, "target": target, "request_id": str(uuid.uuid4())})
    print(json.dumps(result, indent=1), flush=True)
    check("built", result.get("built") is True)
    nets = call("list_pressure_networks", {"target": target}).get("pressureNetworks")
    print(json.dumps(nets, indent=1)[:3000])
    pipes = call("list_pressure_pipes", {"network_name": "Water", "limit": 1000, "target": target})
    pipe_list = pipes.get("pipes", [])
    check("all planned pipes exist", len(pipe_list) == result.get("partsPlaced", {}).get("pipe"))
    named = {p.get("name") for p in pipe_list}
    check("WaterGEMS pipe names kept", all(p["label"] in named or f"{p['label']}.1" in named for p in watergems.read(OPTIONS)["pipes"]))
    views = [v["name"] for v in call("list_profile_views", {"target": target})["profileViews"]]
    check("a profile view per main", all(f"{m['name']} - Profile" in views for m in result.get("mains", [])) and result.get("mains"))
    print("\nSUMMARY")
    for k, v in RESULTS.items():
        print(f"{v:5} {k}")
    print("ALL PASS" if all(v == "PASS" for v in RESULTS.values()) else "SOME FAILED")

    if "FAIL" in RESULTS.values():
        raise SystemExit(1)


if __name__ == "__main__":
    main()
