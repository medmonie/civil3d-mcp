"""Offline tests of the WaterGEMS importer (no Civil 3D): a synthetic export and a fake bridge.

    python tests/watergems_test.py
"""
from __future__ import annotations

import sys
import tempfile
import time
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "server"))

import openpyxl  # noqa: E402

import watergems as w  # noqa: E402

TARGET = {"session_id": "11111111-1111-1111-1111-111111111111", "drawing_id": "22222222-2222-2222-2222-222222222222"}
REQUEST = "33333333-3333-3333-3333-333333333333"
PIPES = [f"pipe-{d} mm-push on-ductile iron-25 bar" for d in (150, 250, 300)]
FITTINGS = [f"tee-{a} mm x {b} mm-ductile iron" for a, b in ((150, 150), (250, 150), (250, 250))]
FITTINGS += [f"elbow-{d} mm-{a} degree-ductile iron" for d in (150, 250) for a in ("11.25", "22.5", "45", "90")]


def workbook(folder: Path, junctions: list[tuple], pipes: list[tuple], prvs: list[tuple] = ()) -> str:
    book = openpyxl.Workbook()
    book.remove(book.active)
    js = book.create_sheet("Junction")
    js.append(["Label", "X", "Y", "Elevation"])
    for row in junctions:
        js.append(list(row))
    if prvs:
        ps = book.create_sheet("PRV")
        ps.append(["Label", "X", "Y", "Elevation", "Pressure Setting (Initial)"])
        for row in prvs:
            ps.append(list(row))
    sheet = book.create_sheet("Pipe")
    sheet.append(["Label", "Start Node", "Stop Node", "Material", "Diameter", "Length"])
    for row in pipes:
        sheet.append(list(row))
    path = folder / f"export{len(list(folder.iterdir()))}.xlsx"
    book.save(path)
    return str(path)


class Fake:
    """Answers the bridge calls the importer makes; records the edits."""

    def __init__(self, appurtenances: list[str], fail_stage: str | None = None):
        self.apps, self.fail_stage, self.edits = appurtenances, fail_stage, []

    def __call__(self, method: str, params: dict[str, Any] | None = None, timeout: float | None = None) -> Any:
        params = params or {}
        if method == "get_drawing_info":
            return {"target": TARGET, "civilDrawingUnits": "Meters"}
        if method == "list_pressure_part_sizes":
            kinds = {"Pipe": PIPES, "Fitting": FITTINGS, "Appurtenance": self.apps}
            return {"sizes": [{"description": d} for d in kinds[params["part_type"]]]}
        if method == "list_entities":
            return {"entities": [], "truncated": False}
        if method == "get_edit_status":
            raise AssertionError("the importer must not answer retries itself (the bridge checks the request)")
        if method == "edit":
            self.edits.append(params)
            ops = params["operations"]
            if any(o["op"] == "pressure_add" for o in ops):
                add = next(o for o in ops if o["op"] == "pressure_add")
                created = [{"kind": k, "id": x["id"], "handle": f"H{i}{k[0]}"} for k in ("pipe", "fitting", "appurtenance")
                           for i, x in enumerate(add.get(k + "s", []))]
                return {"applied": True, "results": [{"op": "pressure_add", "result": {"created": created, "connections": 0}}]}
            if self.fail_stage == "profiles" and any(o["op"] == "alignment_create" for o in ops):
                return {"applied": False, "results": [{"op": "alignment_create", "error": "simulated failure"}]}
            return {"applied": True, "results": []}
        if method == "list_profile_views":
            return {"profileViews": []}
        if method == "list_pressure_pipes":
            return {"pipes": []}
        raise AssertionError(f"unexpected call {method}")


def params(path: str, **extra: Any) -> dict[str, Any]:
    return {"path": path, "target": TARGET, "request_id": REQUEST, **extra}


def line_network(folder: Path, prv: bool = False) -> str:
    js = [("J-1", 0, 0, 10), ("J-2", 100, 0, 10), ("J-3", 200, 0, 10)]
    ps = [("P-1", "J-1", "J-2", "PVC", 180.0, 100), ("P-2", "J-2", "J-3", "PVC", 99.4, 100)]
    if prv:
        return workbook(folder, js[:2] + [("J-4", 300, 0, 10)],
                        [("P-1", "J-1", "J-2", "Ductile Iron", 150, 100), ("P-2", "J-2", "PRV-1", "Ductile Iron", 150, 100),
                         ("P-3", "PRV-1", "J-4", "Ductile Iron", 150, 100)],
                        prvs=[("PRV-1", 200, 0, 10, 3.5)])
    return workbook(folder, js, ps)


def main() -> None:
    folder = Path(tempfile.mkdtemp())

    # 1. stand-ins never smaller than the bore; PRV part used when the list has one; depth allows for the stand-in
    w.call = Fake(["gate valve-150 mm-ductile iron"])
    report = w.check(params(line_network(folder)))
    assert any("Ø200 -> DN250" in k for k in report["standIns"]), report["standIns"]
    assert not any("UNDERSIZED" in k for k in report["standIns"]), report["standIns"]
    fake = Fake(["gate valve-150 mm-ductile iron", "pressure reducing valve-150 mm-ductile iron"])
    w.call = fake
    o = w._options(params(line_network(folder, prv=True)))
    plan = w._plan(params(line_network(folder, prv=True)), o, TARGET)
    content, _ = w._network_ops(plan, o)
    prv = next(a for a in content[0]["appurtenances"] if a["id"] == "PRV-1")
    assert prv["part"].startswith("pressure reducing valve") and prv["type"] == "Valve", prv
    w.call = Fake(["gate valve-150 mm-ductile iron"])
    report = w.check(params(line_network(folder, prv=True)))
    assert any("PRV PRV-1 -> gate valve" in k for k in report["standIns"]), report["standIns"]
    z = plan["nodes"]["J-1"]["z"]
    assert abs((10 - z) - (o["cover"] + 150 * w.STAND_IN_OD / 2000)) < 1e-9, z
    print("parts ok: no undersized stand-ins, PRV part preferred, gate-valve stand-in reported, depth uses the stand-in size")

    # 2. a bad profile_views_at is refused before anything is drawn; a failed stage says the network is committed
    fake = Fake(["gate valve-150 mm-ductile iron"])
    w.call = fake
    try:
        w.build(params(line_network(folder), profile_views_at=[1]))
        raise AssertionError("expected a ValueError")
    except ValueError as exc:
        assert "profile_views_at" in str(exc)
    assert not fake.edits, "nothing may be edited before the parameters are checked"
    w.call = Fake(["gate valve-150 mm-ductile iron"], fail_stage="profiles")
    reply = w.build(params(line_network(folder), main_min_mm=100, min_line_length=1))
    assert reply["built"] is False and reply["networkCommitted"] is True, reply
    assert reply["stages"]["network"] == "committed" and "simulated failure" in reply["stages"]["labels and profiles"], reply["stages"]
    print("stages ok: input checked before drawing; a rolled-back stage reports the committed network and the error")

    # 3. retries go to the bridge (the fake fails the test if the importer asks get_edit_status)
    w.call = Fake(["gate valve-150 mm-ductile iron"])
    w.build(params(line_network(folder), main_min_mm=100, min_line_length=1))
    print("retry ok: no local shortcut, the bridge compares the request")

    # 5. conflicting duplicate rows are a problem; time-step repeats are not
    js = [("J-1", 0, 0, 10), ("J-2", 100, 0, 10), ("J-1", 5, 0, 10)]
    w.call = Fake([])
    report = w.check(params(workbook(folder, js, [("P-1", "J-1", "J-2", "PVC", 180.0, 100)])))
    assert report["ready"] is False and any("J-1" in p and "x" in p for p in report["problems"]), report
    js = [("J-1", 0, 0, 10), ("J-2", 100, 0, 10), ("J-1", 0, 0, 10)]
    report = w.check(params(workbook(folder, js, [("P-1", "J-1", "J-2", "PVC", 180.0, 100)])))
    assert report["ready"] is True, report

    class Truncating(Fake):
        def __call__(self, method: str, p: dict[str, Any] | None = None, timeout: float | None = None) -> Any:
            return {"entities": [], "truncated": True} if method == "list_entities" else super().__call__(method, p, timeout)

    w.call = Truncating([])
    try:
        w.check(params(line_network(folder), cad_pipe_layers=["PIPES"]))
        raise AssertionError("expected a ValueError")
    except ValueError as exc:
        assert "more than" in str(exc)
    print("input ok: conflicting duplicates refused, repeated time steps accepted, truncated CAD layers refused")

    # 6. mains of a looped grid are found in linear time
    n = 30
    nodes = {f"N{i}-{j}": {"x": i * 50.0, "y": j * 50.0} for i in range(n) for j in range(n)}
    pipes = [{"label": f"{a}|{b}", "start": a, "stop": b, "nominal": 200}
             for i in range(n) for j in range(n) for a, b in
             ((f"N{i}-{j}", f"N{i + 1}-{j}"), (f"N{i}-{j}", f"N{i}-{j + 1}")) if b in nodes]
    started = time.perf_counter()
    lines = w.mains({"nodes": nodes, "pipes": pipes}, {"main_min_mm": 150, "min_line_length": 0, "line_prefix": "WL"})
    took = time.perf_counter() - started
    assert lines and took < 10, took
    print(f"mains ok: {len(pipes)}-pipe looped grid planned in {took:.2f} s ({len(lines)} lines)")
    from unittest.mock import patch
    from tempfile import TemporaryDirectory
    import watergems_live as live
    with TemporaryDirectory() as tmp, patch.object(live, "WATER", Path(tmp)):
        original = Path(tmp) / "PL15-21-WYTE-WTR-PLN.dwg"
        copy = original.with_name(original.stem + " - WaterGEMS import.dwg")
        copy.touch()
        src = {"path": str(original), "target": {"drawing_id": "one"}}
        dest = {**src, "path": str(copy)}
        cases = ([src, {"applied": False}], [src, {"applied": True, "warnings": ["save failed"]}],
                 [src, {"applied": True, "afterCommitSucceeded": False}], [src, {"applied": True}, src],
                 [{**src, "path": str(Path(tmp) / "other WaterGEMS.dwg")}])
        for responses in cases:
            with patch.object(live, "call", side_effect=responses), patch.object(live.watergems, "build") as build:
                try:
                    live.main()
                except SystemExit:
                    pass
                else:
                    raise AssertionError("Expected an unverified copy to stop the import")
                build.assert_not_called()
        with patch.object(live, "call", side_effect=[src, {"applied": True}, dest]):
            assert live.working_target() == src["target"]
    print("live import guard ok: only the verified working copy passes")
    print("\nALL OK")


if __name__ == "__main__":
    main()
