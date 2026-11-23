"""Offline tests of the plan and profile sheet generator (no Civil 3D). The fake template copies the real numbers of
PL23-RID-ICD-PRF.01-00.dwg: A1, plan 675 x 279 mm at 1:600, profile 675 x 279 mm at 1:1000, key plan 130 x 98 mm.

    python tests/sheets_test.py
"""
from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "server"))

import sheets  # noqa: E402

TARGET = {"session_id": "11111111-1111-1111-1111-111111111111", "drawing_id": "22222222-2222-2222-2222-222222222222"}
REQUEST = "33333333-3333-3333-3333-333333333333"
PLAN = {"handle": "1B19", "at": [355.5, 436.5], "width": 675, "height": 279, "customScale": 1000 / 600, "viewCenter": [677616.477, 815275.783]}
PROFILE = {"handle": "1B1C", "at": [355.5, 157.5], "width": 675, "height": 279, "customScale": 1.0, "viewCenter": [679388.187, 815550.034]}
KEY = {"handle": "1E9B", "at": [761, 521.956], "width": 130, "height": 98.088, "customScale": 0.0891741649, "viewCenter": [677750.01, 815601.112]}
BOX = [[731.035, 485.481], [767.15, 485.481], [767.15, 500.409], [731.035, 500.409]]


def text(h, layer, s, at):
    return {"handle": h, "type": "TEXT", "layer": layer, "geometry": {"text": s, "at": [*at, 0], "height": 3}}


ENTITIES = [
    {"handle": "14B2", "type": "INSERT", "layer": "0", "block": "XR.FRAME", "geometry": {"at": [0, 0, 0]}},
    {"handle": "1B48", "type": "INSERT", "layer": "EN-SCALE BAR", "block": "SCALE_MOD", "geometry": {"at": [20.697, 304.904, 0]}},
    {"handle": "1E9E", "type": "LWPOLYLINE", "layer": "0", "geometry": {"closed": True, "vertices": BOX}},
    {"handle": "1EA2", "type": "HATCH", "layer": "0", "geometry": {"pattern": "SOLID", "area": 539.124}},
    {"handle": "9999", "type": "HATCH", "layer": "0", "geometry": {"pattern": "SOLID", "area": 12.0}},   # not the box
    text("2254", "EN-LIST", "%%ULIST OF PROFILES", [700.131, 274.54]),
    text("2255", "EN-LINE NAME", "%%UICD Line 1", [700.186, 268.804]),
    text("2256", "EN-LINE NAME", "%%UICD Line 1-1", [700.186, 262.804]),
    text("2477", "FR-Anno-Ttlb-Text", "PLAN/PROFILE ( 01 / 05 ) ", [735.233, 44.199]),
]


class Fake:
    def __init__(self, views: list[dict[str, Any]], alignments: list[dict[str, Any]]):
        self.views, self.alignments, self.edits = views, alignments, []
        self.ledger, self.layouts = {}, {"Model"}
        self.missing = None
        self.drop_reply = False
        self.rollback = False

    def __call__(self, method: str, params: dict[str, Any] | None = None, timeout: float | None = None) -> Any:
        params = params or {}
        if method == "inspect_file":
            return {"layouts": [{"name": "01", "paper": "A1", "viewports": [PLAN, PROFILE, KEY], "entities": ENTITIES}], "xrefs": [{"name": "XR.FRAME"}]}
        if method == "get_drawing_info":
            return {"target": TARGET, "insertionUnits": "Meters"}
        if method == "list_profile_views":
            return {"profileViews": self.views}
        if method == "list_alignments":
            return {"alignments": self.alignments}
        if method == "profile_sheet_window":
            lo, hi = params["x_min"], params["x_max"]
            return {"stationRange": [1000 + lo, 1000 + hi], "window": [2000 + lo, 2000, 2000 + hi, 2050]}
        if method == "get_edit_status":
            return self.ledger.get(params["request_id"], {"state": "unknown"})
        if method == "list_layouts":
            return {"layouts": [{"name": n} for n in self.layouts]}
        if method == "list_viewports":   # the copied layout: same positions, new handles
            if params["layout"] == self.missing:
                return {"viewports": []}
            return {"viewports": [{**v, "handle": "N" + v["handle"]} for v in (PLAN, PROFILE, KEY)]}
        if method == "list_entities":   # copied justified text moves a little (its insertion point is recomputed)
            moved = lambda e: {**e, "geometry": {**e["geometry"], "at": [e["geometry"]["at"][0] + 1.0, e["geometry"]["at"][1], 0]}} if e["type"] == "TEXT" else e
            return {"entities": [{**moved(e), "handle": "N" + e["handle"]} for e in ENTITIES]}
        if method == "edit":
            self.edits.append(params["operations"])
            ops = params["operations"]
            assert len(ops) <= 500 and params["request_id"] not in self.ledger
            for op in ops:
                if op["op"] == "layout_from_template":
                    self.layouts.add(op["name"])
            result = {"applied": True, "results": []}
            if self.rollback and ops[0]["op"] == "viewport_set":
                result = {"applied": False, "error": "content rolled back"}
                self.rollback = False
            self.ledger[params["request_id"]] = {"state": "completed", "applied": result["applied"], "result": result,
                                                  "workflow": params["workflow"]}
            if self.drop_reply and ops[0]["op"] == "viewport_set":
                self.drop_reply = False
                raise sheets.BridgeError("Lost the content reply")
            return result
        raise AssertionError(method)


def view(name, align, x0, y0, w, h):
    return {"name": name, "alignment": align, "extents": {"min": [x0, y0, 0], "max": [x0 + w, y0 + h, 0]}}


def align(name, x0, y0, w, h):
    return {"name": name, "extents": {"min": [x0, y0, 0], "max": [x0 + w, y0 + h, 0]}}


def main() -> None:
    base = {"template": r"C:\x\PL23-RID-ICD-PRF.01-00.dwg", "target": TARGET, "request_id": REQUEST}

    # views: three short views stacked 60 m apart fit one sheet; a 1,200 m view is split into three sheets
    views = [view("L1 - Profile", "L1", 0, 1000, 300, 50), view("L2 - Profile", "L2", 0, 940, 250, 50),
             view("L3 - Profile", "L3", 0, 880, 200, 50), view("L4 - Profile", "L4", 0, 0, 1200, 60)]
    aligns = [align("L1", 1000, 1000, 120, 40), align("L2", 1100, 1000, 80, 30), align("L3", 1050, 980, 60, 20), align("L4", 2000, 2000, 900, 300)]
    sheets.call = Fake(views, aligns)
    plan = sheets.plan_sheets(base, TARGET)
    got = [s["lines"] for s in plan["sheets"]]
    assert got == [["L1", "L2", "L3"], ["L4"], ["L4"], ["L4"]], got
    assert [s["layout"] for s in plan["sheets"]] == ["01", "02", "03", "04"]
    assert plan["sheets"][0]["label"] == "( 01 / 04 )"
    assert plan["sheets"][0]["planScale"] == 400, plan["sheets"][0]["planScale"]     # 180 m wide + 2 x 10 % on 675 mm -> 1:320 -> 1:400
    assert [row["planCenter"][0] for row in plan["sheets"][1:]] == [2250, 2750, 3100]
    assert [row["stationRange"] for row in plan["sheets"][1:]] == [[1000, 1500], [1500, 2000], [2000, 2200]]
    t = plan["template"]
    assert t["plan"]["handle"] == "1B19" and t["profile"]["handle"] == "1B1C" and t["key"]["handle"] == "1E9B"
    assert len(t["lines"]) == 2 and t["lineSpacing"] == 6.0 and t["label"] and t["scaleBar"]
    assert [m["handle"] for m in t["marker"]] == ["1E9E", "1EA2"], t["marker"]
    print("views ok: short views share a sheet, a long view is split at 500 m, plan scales fitted, template parts found")

    # rows: 500 m between rows, lines as given, template plan area when no alignment matches
    sheets.call = Fake([], [])
    plan = sheets.plan_sheets({**base, "sheets": [{"lines": ["ICD Line 1"]}, {"lines": ["ICD Line 2"]}], "first_sheet": 6, "total_sheets": 7}, TARGET)
    assert plan["mode"] == "rows"
    c0, c1 = plan["sheets"][0]["profileCenter"], plan["sheets"][1]["profileCenter"]
    assert c0 == PROFILE["viewCenter"] and abs(c0[1] - c1[1] - 500) < 1e-9, (c0, c1)
    assert plan["sheets"][1]["label"] == "( 07 / 07 )" and plan["sheets"][1]["layout"] == "07"
    assert plan["sheets"][0]["planScale"] == 600
    print("rows ok: 500 m row spacing, numbering, template plan kept")

    # build: viewports moved, list rewritten, label renumbered, box mapped onto the key plan, scale bar resized
    fake = Fake(views[:1], [align("L1", 677600, 815500, 120, 40)])     # inside the key plan
    sheets.call = fake
    reply = sheets.build({**base, "total_sheets": 5})
    assert reply["built"] and len(fake.edits) == 2, reply
    ops = fake.edits[1]
    names = [o["op"] for o in ops]
    assert names.count("viewport_set") == 2 and "entity_erase" in names, names
    erase = next(o for o in ops if o["op"] == "entity_erase")
    assert set(erase["handles"]) == {"N2255", "N2256"}, erase
    label = next(o for o in ops if o["op"] == "text_set")
    assert label["handle"] == "N2477" and "( 01 / 05 )" in label["text"], label
    texts = [o for o in ops if o["op"] == "draw_text"]
    assert [x["text"] for x in texts] == ["%%UL1"] and texts[0]["layout"] == "01"
    box_ops = [o for o in ops if o["op"] in ("entity_scale", "entity_move") and "N1E9E" in o["handles"]]
    assert len(box_ops) == 2 and set(box_ops[0]["handles"]) == {"N1E9E", "N1EA2"}, box_ops
    bar = [o for o in ops if o["op"] == "entity_scale" and o["handles"] == ["N1B48"]]
    assert bar and abs(bar[0]["factor"] - 600 / 250) < 1e-9, bar        # L1: 120 x 40 m -> 1:250
    print("build ok: viewports, list of lines, label, key-plan box and scale bar")

    # a plan area outside the key plan: the box is not moved off the sheet
    far = Fake([view("L9 - Profile", "L9", 0, 0, 300, 50)], [align("L9", 10, 10, 100, 40)])
    sheets.call = far
    reply = sheets.build({**base})
    ops = far.edits[1]
    assert not [o for o in ops if o["op"] in ("entity_scale", "entity_move") and "N1E9E" in o["handles"]], ops
    assert any("outside the key plan" in n for n in reply["notes"]), reply["notes"]
    print("build ok: a plan outside the key plan leaves the key-plan box in place")
    from unittest.mock import patch
    sheets.call = Fake(views[-1:], aligns[-1:])
    with patch.dict(PROFILE, customScale=3):
        narrow = sheets.plan_sheets(base, TARGET)["sheets"]
    end = 0
    for row in narrow:
        left, right = row["stationRange"]
        assert left == end + 1000 and right - left <= 225
        x = row["profileCenter"][0]
        assert x - 112.5 <= left - 1000 and x + 112.5 >= right - 1000
        end = right - 1000
    assert end == 1200

    fake = Fake([], [])
    sheets.call = fake
    many = {**base, "sheets": [{"lines": ["Water"]} for _ in range(100)]}
    fake.missing = "001"
    missing = sheets.build(many)
    assert not missing["built"] and missing["sheets"] == [] and missing["copiedLayouts"] == ["001"]
    fake.missing = "042"
    partial = sheets.build(many)
    assert not partial["built"] and len(partial["sheets"]) == 41 and "042" in partial["error"]
    assert partial["stages"]["043"] == "pending" and "043" not in fake.layouts
    fake.missing = None
    fake.drop_reply = True
    interrupted = sheets.build(many)
    assert not interrupted["built"] and len(interrupted["sheets"]) == 41
    done = sheets.build(many)
    assert done["built"] and len(done["sheets"]) == 100 and len(fake.edits) == 200
    assert sheets.build(many)["built"] and len(fake.edits) == 200
    changed = {**many, "plan_margin": 0.2, "if_exists": "replace"}
    assert not sheets.build(changed)["built"] and len(fake.edits) == 200
    sheets.call = Fake([], [])
    large = sheets.build({**base, "sheets": [{"lines": [str(i) for i in range(1050)]}]})
    assert large["built"] and sum(op["op"] == "draw_text" for ops in sheets.call.edits for op in ops) == 1050
    fake = Fake([], [])
    fake.rollback = True
    sheets.call = fake
    one = {**base, "sheets": [{"lines": ["A"]}]}
    assert not sheets.build(one)["built"]
    assert sheets.build(one)["built"] and len(fake.edits) == 3
    print("coverage, station-specific plans, missing viewports, bounded edits and lost-reply resume ok")
    print("\nALL OK")


if __name__ == "__main__":
    main()
