"""Plan and profile sheets from an office sheet template (pp_sheets_plan / pp_sheets_create).

The template is one of the office's own sheet drawings (e.g. PL23-RID-ICD-PRF.01-00.dwg): its layout has a plan
viewport, a profile viewport and a key-plan viewport, a title block (often an xref), a "LIST OF PROFILES" text list,
a sheet label such as "PLAN/PROFILE ( 01 / 05 )", a scale bar, and a box on the key plan that marks the plan area.

Each generated sheet is a copy of that layout in the open drawing (layout_from_template), with:
  - the profile viewport on that sheet's profiles, at the template's scale;
  - the plan viewport zoomed to that sheet's lines, at the nearest standard scale (or the template's plan area);
  - the key-plan box moved and resized to the new plan area, the scale bar resized to the new plan scale;
  - the list of lines and the sheet label rewritten.

Where the profiles come from:
  views  Civil 3D profile views in the drawing (e.g. after a SewerGEMS / WaterGEMS import). Small views are put
         together on one sheet when they fit the profile viewport; a view longer than max_length (default 500 m of
         drawing length) is split over several sheets.
  rows   Profiles drawn as plain CAD and stacked in rows (like XR.ICD-PROFILES): sheet k shows the row
         row_spacing (default 500 m) below sheet k-1, starting at the template's profile area. The lines of each sheet
         are given in 'sheets'.
"""
from __future__ import annotations

import hashlib
import json
import math
import re
from typing import Any
from uuid import UUID, uuid5

from bridge_client import BridgeError, EDIT_TIMEOUT, call, drawing_target, edit_request_id

STANDARD_SCALES = [100, 200, 250, 300, 400, 500, 600, 750, 1000, 1250, 1500, 2000, 2500, 3000, 4000, 5000, 7500, 10000]
MAX_OPS = 500
MAX_STAGE_ATTEMPTS = 8


# --------------------------------------------------------------------------- the template

def _num(v: Any) -> float:
    return float(v)


def template(params: dict[str, Any], target: Any) -> dict[str, Any]:
    """What the template sheet holds, and which viewport is which."""
    path = str(params.get("template") or "")
    if not path:
        raise ValueError("template: full path of the office sheet drawing to copy (e.g. ...\\PL23-RID-ICD-PRF.01-00.dwg).")
    info = call("inspect_file", {"path": path, "limit": 500, "target": target}, timeout=300)
    layouts = info["layouts"]
    if not layouts:
        raise ValueError(f"{path}: has no paper-space layout.")
    name = params.get("template_layout")
    lay = next((l for l in layouts if l["name"] == name), None) if name else layouts[0]
    if lay is None:
        raise ValueError(f"template_layout '{name}' is not in the template. It has: {', '.join(l['name'] for l in layouts)}")
    vps = lay["viewports"]
    if len(vps) < 2:
        raise ValueError(f"The template layout '{lay['name']}' needs a plan and a profile viewport; it has {len(vps)}.")
    area = lambda v: v["width"] * v["height"]

    chosen = params.get("viewports") or {}
    def pick(role: str) -> dict[str, Any] | None:
        h = chosen.get(role)
        return next((v for v in vps if v["handle"] == h), None) if h else None
    key = pick("key")
    if key is None and len(vps) >= 3:
        key = min(vps, key=area)
    rest = [v for v in vps if v is not key]
    plan = pick("plan") or max(rest, key=lambda v: (v["at"][1], area(v)))
    profile = pick("profile") or min((v for v in rest if v is not plan), key=lambda v: v["at"][1])

    ents = lay["entities"]
    line_layer = params.get("line_layer") or "EN-LINE NAME"
    texts = [e for e in ents if e["type"] in ("TEXT", "MTEXT")]
    header = next((e for e in texts if "LIST OF PROFILES" in str(e["geometry"].get("text", "")).upper()), None)
    lines = sorted((e for e in texts if e["layer"] == line_layer), key=lambda e: -e["geometry"]["at"][1])
    label_contains = params.get("label_contains") or "PLAN/PROFILE"
    label = next((e for e in texts if label_contains.upper() in str(e["geometry"].get("text", "")).upper()), None)
    bar = next((e for e in ents if e["type"] == "INSERT" and "SCALE" in (e["layer"] or "").upper()), None)

    marker = []
    if key:
        kx0, ky0 = key["at"][0] - key["width"] / 2, key["at"][1] - key["height"] / 2
        kx1, ky1 = kx0 + key["width"], ky0 + key["height"]
        for e in ents:
            vs = (e.get("geometry") or {}).get("vertices") or []
            if e["type"] == "LWPOLYLINE" and vs and all(kx0 <= x <= kx1 and ky0 <= y <= ky1 for x, y in vs):
                marker.append(e)
        if marker:   # the hatch that fills the box comes with it (same area as the box)
            vs = marker[0]["geometry"]["vertices"]
            box_area = abs(sum(vs[i][0] * vs[(i + 1) % len(vs)][1] - vs[(i + 1) % len(vs)][0] * vs[i][1] for i in range(len(vs)))) / 2
            marker += [e for e in ents if e["type"] == "HATCH" and abs((e["geometry"] or {}).get("area", -1) - box_area) <= 0.01 * max(box_area, 1)]
    spacing = 6.0
    if len(lines) >= 2:
        spacing = round(lines[0]["geometry"]["at"][1] - lines[1]["geometry"]["at"][1], 3) or 6.0
    return {
        "path": path, "layout": lay["name"], "paper": lay["paper"], "plan": plan, "profile": profile, "key": key,
        "header": header, "lines": lines, "lineLayer": line_layer, "lineSpacing": spacing, "label": label, "scaleBar": bar,
        "marker": [m for m in marker if m["type"] == "LWPOLYLINE"][:1] + [m for m in marker if m["type"] == "HATCH"],
        "xrefs": [x["name"] for x in info["xrefs"]],
    }


# --------------------------------------------------------------------------- sheets

def _model_size(vp: dict[str, Any]) -> tuple[float, float]:
    """Model area a viewport shows (drawing units)."""
    return vp["width"] / vp["customScale"], vp["height"] / vp["customScale"]


def _unit_mm(target: Any) -> float:
    units = call("get_drawing_info", {"target": target}).get("insertionUnits", "Meters")
    return {"Millimeters": 1, "Centimeters": 10, "Meters": 1000, "Feet": 304.8, "Inches": 25.4}.get(units, 1000)


def _fit_scale(window: list[float], vp: dict[str, Any], unit_mm: float, scales: list[int]) -> int:
    """Smallest standard 1:N at which the window fits the viewport."""
    w, h = window[2] - window[0], window[3] - window[1]
    twist = math.radians(vp.get("twistDeg", 0))
    c, s = abs(math.cos(twist)), abs(math.sin(twist))
    need = max((w*c + h*s) * unit_mm / vp["width"], (w*s + h*c) * unit_mm / vp["height"])
    return next((n for n in scales if n >= need), int(math.ceil(need)))


def _natural(s: str) -> list[Any]:
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", s)]


def _union(boxes: list[list[float]]) -> list[float]:
    return [min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes)]


def _box(ext: Any) -> list[float] | None:
    if not isinstance(ext, dict):
        return None
    (x0, y0, *_), (x1, y1, *_) = ext["min"], ext["max"]
    return [x0, y0, x1, y1]


def plan_sheets(params: dict[str, Any], target: Any) -> dict[str, Any]:
    t = template(params, target)
    unit_mm = _unit_mm(target)
    profile_w, profile_h = _model_size(t["profile"])
    max_length = float(params.get("max_length", 500))
    if not math.isfinite(max_length) or max_length <= 0:
        raise ValueError("max_length must be positive and finite.")
    twist = math.radians(t["profile"].get("twistDeg", 0))
    cosine, sine = abs(math.cos(twist)), abs(math.sin(twist))

    def available_span(height):
        widths = [max_length]
        if cosine > 1e-9:
            widths.append((profile_w - height * sine) / cosine)
        if sine > 1e-9:
            widths.append((profile_h - height * cosine) / sine)
        return min(widths)

    mode = params.get("mode") or "auto"
    notes: list[str] = []
    views = [v for v in call("list_profile_views", {"target": target}).get("profileViews", []) if _box(v.get("extents"))]
    if params.get("profile_views"):
        wanted = set(params["profile_views"])
        views = [v for v in views if v["name"] in wanted]
    if mode == "auto":
        mode = "rows" if params.get("sheets") or not views else "views"
    alignments = {a["name"]: _box(a.get("extents")) for a in call("list_alignments", {"target": target}).get("alignments", [])}

    sheets: list[dict[str, Any]] = []
    if mode == "views":
        if not views:
            raise ValueError("No profile views in the drawing: use mode 'rows' with 'sheets', or create profile views first.")
        views.sort(key=lambda v: _natural(v["name"]))
        group: list[dict[str, Any]] = []

        def flush() -> None:
            if group:
                box = _union([_box(v["extents"]) for v in group])
                sheets.append({"profileCenter": [(box[0] + box[2]) / 2, (box[1] + box[3]) / 2],
                               "lines": [v["alignment"] for v in group], "profileViews": [v["name"] for v in group]})
                group.clear()

        for v in views:
            b = _box(v["extents"])
            w, h = b[2] - b[0], b[3] - b[1]
            span = available_span(h)
            if span <= 0:
                raise ValueError(f"{v['name']}: the rotated profile viewport cannot fit this view at its template scale.")
            if h > profile_h and sine < 1e-9:
                notes.append(f"{v['name']}: {h:.0f} high, the profile viewport shows {profile_h:.0f}; its top or bottom is cut off.")
            if w > span:
                flush()
                parts = math.ceil(w / span)
                for k in range(parts):
                    x0 = b[0] + k * span
                    x1 = min(b[2], x0 + span)
                    segment = call("profile_sheet_window", {"alignment_name": v["alignment"], "profile_view": v["name"],
                                                            "x_min": x0, "x_max": x1, "target": target})
                    sheets.append({"stationRange": segment["stationRange"], "planWindow": segment["window"],
                                   "profileCenter": [(x0 + x1) / 2, (b[1] + b[3]) / 2], "lines": [v["alignment"]],
                                   "profileViews": [f"{v['name']} ({k + 1}/{parts})"]})
                continue
            trial = _union([_box(g["extents"]) for g in group] + [b]) if group else b
            if group and (trial[2] - trial[0] > available_span(trial[3] - trial[1]) or trial[3] - trial[1] > profile_h):
                flush()
            group.append(v)
        flush()
    else:
        given = params.get("sheets") or []
        if not given:
            raise ValueError("mode 'rows' needs 'sheets': [{\"lines\": [...], \"plan_window\"?: [xmin, ymin, xmax, ymax]}, ...].")
        spacing = float(params.get("row_spacing") or 500)
        first = params.get("first_row_center") or t["profile"]["viewCenter"]
        for k, s in enumerate(given):
            sheets.append({"profileCenter": [first[0], first[1] - k * spacing], "lines": list(s.get("lines") or []),
                           "planWindow": s.get("plan_window")})

    # plan area of each sheet: the sheet's alignments (or the window given), fitted at a standard scale
    scales = sorted(int(x) for x in (params.get("plan_scales") or STANDARD_SCALES))
    margin = float(params.get("plan_margin") or 0.1)
    plan_mode = params.get("plan") or "fit"
    for s in sheets:
        window = s.pop("planWindow", None)
        if window is None and plan_mode == "fit":
            boxes = [alignments[n] for n in s["lines"] if alignments.get(n)]
            if boxes:
                window = _union(boxes)
        if window and plan_mode == "fit":
            w, h = window[2] - window[0], window[3] - window[1]
            window = [window[0] - w * margin, window[1] - h * margin, window[2] + w * margin, window[3] + h * margin]
            s["planCenter"] = [(window[0] + window[2]) / 2, (window[1] + window[3]) / 2]
            s["planScale"] = _fit_scale(window, t["plan"], unit_mm, scales)
        else:
            s["planCenter"] = t["plan"]["viewCenter"]
            s["planScale"] = round(unit_mm / t["plan"]["customScale"])
            if plan_mode == "fit":
                notes.append(f"sheet with {', '.join(s['lines']) or 'no lines'}: no alignment found for the plan; the template's plan area is kept.")

    first_no = int(params.get("first_sheet") or 1)
    total = int(params.get("total_sheets") or (first_no - 1 + len(sheets)))
    width = max(2, len(str(total)))
    prefix = params.get("layout_prefix") or ""
    for i, s in enumerate(sheets):
        s["number"] = first_no + i
        s["layout"] = f"{prefix}{s['number']:0{width}d}"
        s["label"] = f"( {s['number']:0{width}d} / {total:0{width}d} )"
    return {"mode": mode, "template": t, "unitMm": unit_mm, "sheets": sheets, "total": total, "notes": notes,
            "profileViewport": {"scale": f"1:{round(unit_mm / t['profile']['customScale'])}", "shows": [round(profile_w, 1), round(profile_h, 1)]}}


def check(params: dict[str, Any]) -> dict[str, Any]:
    target = drawing_target(params) if params.get("target") else call("get_drawing_info")["target"]
    p = plan_sheets(params, target)
    t = p["template"]
    return {
        "mode": p["mode"], "template": f"{t['path']} / {t['layout']}", "profileViewport": p["profileViewport"],
        "found": {k: bool(t[k]) for k in ("plan", "profile", "key", "header", "label", "scaleBar")} | {"listLines": len(t["lines"]), "keyBox": bool(t["marker"])},
        "sheets": [{k: s[k] for k in ("layout", "label", "lines", "planScale")} | {k: s[k] for k in ("profileViews", "stationRange") if k in s} for s in p["sheets"]],
        "notes": p["notes"],
    }


# --------------------------------------------------------------------------- build

def _failed(reply: dict[str, Any]) -> str:
    return str(reply.get("error") or reply.get("reason") or "rolled back")


def _near(a: list[float], b: list[float], tol: float = 0.5) -> bool:
    return abs(a[0] - b[0]) <= tol and abs(a[1] - b[1]) <= tol


# Leave room for viewport, label, key-plan and scale-bar operations in the first transaction.
LABEL_BATCH = 400


def _line_ops(t, s, start, stop):
    if not t["lines"]:
        return []
    g = t["lines"][0]["geometry"]
    return [{"op": "draw_text", "layout": s["layout"], "layer": t["lineLayer"], "text": f"%%U{name}",
             "at": [g["at"][0], g["at"][1] - i * t["lineSpacing"], 0], "height": g.get("height", 3)}
            for i, name in enumerate(s["lines"][start:stop], start)]


def _sheet_content(t, s, target, unit_mm, notes_out):
    edits = []
    tplan_scale = unit_mm / t["plan"]["customScale"]
    vps = call("list_viewports", {"layout": s["layout"], "target": target}).get("viewports", [])
    ents = call("list_entities", {"layout": s["layout"], "limit": 2000, "target": target}).get("entities", [])
    role = {}
    for r in ("plan", "profile", "key"):
        if t.get(r):
            matches = [v for v in vps if _near(v["at"], t[r]["at"])]
            if len(matches) != 1:
                raise ValueError(f"Expected one {r} viewport on the copied layout, found {len(matches)}.")
            role[r] = matches[0]
    if len({v["handle"] for v in role.values()}) != len(role):
        raise ValueError("Template viewport roles resolve to the same viewport.")
    edits.append({"op": "viewport_set", "handle": role["profile"]["handle"], "view_center": s["profileCenter"], "on": True})
    edits.append({"op": "viewport_set", "handle": role["plan"]["handle"], "view_center": s["planCenter"], "scale": f"1:{s['planScale']}", "on": True})

    def find(sample: dict[str, Any] | None) -> list[dict[str, Any]]:
        if not sample:
            return []
        g = sample["geometry"] or {}
        if sample["type"] in ("TEXT", "MTEXT"):
            # justified text gets its insertion point recomputed when copied: match layer and content, then nearest
            same = [e for e in ents if e["type"] == sample["type"] and e["layer"] == sample["layer"]
                    and (e.get("geometry") or {}).get("text") == g.get("text")]
            at = g.get("at") or [0, 0]
            return sorted(same, key=lambda e: abs(e["geometry"]["at"][0] - at[0]) + abs(e["geometry"]["at"][1] - at[1]))[:1] \
                if same and _near(min(same, key=lambda e: abs(e["geometry"]["at"][0] - at[0]) + abs(e["geometry"]["at"][1] - at[1]))["geometry"]["at"], at, 5.0) else []
        at = g.get("at") or (g.get("vertices") or [[None, None]])[0]
        return [e for e in ents if e["type"] == sample["type"] and e["layer"] == sample["layer"]
                and at[0] is not None and _near((e.get("geometry") or {}).get("at") or ((e.get("geometry") or {}).get("vertices") or [[1e99, 1e99]])[0], at, 0.01)]

    # list of lines: erase the template's, write this sheet's under the header
    old = [h["handle"] for tl in t["lines"] for h in find(tl)]
    if old:
        edits.append({"op": "entity_erase", "handles": old})
    edits.extend(_line_ops(t, s, 0, LABEL_BATCH))
    # sheet label
    for lab in find(t["label"]):
        text = re.sub(r"\(\s*\d+\s*/\s*\d+\s*\)", s["label"], lab["geometry"]["text"])
        edits.append({"op": "text_set", "handle": lab["handle"], "text": text})
    # key-plan box: map the new plan area onto the key plan
    if t.get("key") and t["marker"] and role.get("key"):
        k = t["key"]
        to_paper = lambda x, y: [k["at"][0] + (x - k["viewCenter"][0]) * k["customScale"], k["at"][1] + (y - k["viewCenter"][1]) * k["customScale"]]
        pw, ph = t["plan"]["width"] * s["planScale"] / unit_mm, t["plan"]["height"] * s["planScale"] / unit_mm
        new0 = to_paper(s["planCenter"][0] - pw / 2, s["planCenter"][1] - ph / 2)
        vs = t["marker"][0]["geometry"]["vertices"]
        old0 = [min(v[0] for v in vs), min(v[1] for v in vs)]
        old_w = max(v[0] for v in vs) - old0[0]
        new_w = pw * k["customScale"]
        handles = [h["handle"] for m in t["marker"] for h in (find(m) if m["type"] == "LWPOLYLINE" else
                   [e for e in ents if e["type"] == "HATCH" and e["layer"] == m["layer"]
                    and abs((e["geometry"] or {}).get("area", -1) - m["geometry"]["area"]) <= 0.01])]
        new_h = ph * k["customScale"]
        kx0, ky0 = k["at"][0] - k["width"] / 2, k["at"][1] - k["height"] / 2
        inside = kx0 <= new0[0] and ky0 <= new0[1] and new0[0] + new_w <= kx0 + k["width"] and new0[1] + new_h <= ky0 + k["height"]
        if handles and old_w > 0 and inside:
            edits.append({"op": "entity_scale", "handles": handles, "base": old0, "factor": new_w / old_w})
            edits.append({"op": "entity_move", "handles": handles, "dx": new0[0] - old0[0], "dy": new0[1] - old0[1]})
        elif handles:
            notes_out.append(f"{s['layout']}: the plan area is outside the key plan (the key plan shows another area), so the key-plan box was left where it is.")
    # scale bar drawn for the template's plan scale
    if t["scaleBar"] and abs(s["planScale"] - tplan_scale) > 0.5:
        for b in find(t["scaleBar"]):
            edits.append({"op": "entity_scale", "handles": [b["handle"]], "base": b["geometry"]["at"][:2],
                          "factor": tplan_scale / s["planScale"]})
    return edits


def _stage(target, stage_id, workflow, operations):
    # A lost reply or restarted MCP client can resume against the bridge's session ledger.
    # Only a confirmed, unapplied transaction gets a fresh attempt ID on a subsequent call.
    for _ in range(MAX_STAGE_ATTEMPTS):
        identity = {"target": target, "request_id": str(stage_id)}
        status = call("get_edit_status", identity)
        state = status["state"]
        if state == "unknown":
            break
        if status.get("workflow") != workflow:
            raise ValueError("This request_id belongs to a different sheet plan. Use a new ID for changed inputs.")
        if state == "pending":
            raise ValueError("The previous edit is still pending. Resume with the same request_id.")
        if state == "error":
            if status.get("retryable") is True:
                break
            raise ValueError("The previous edit failed with an uncertain outcome. Check the drawing and edit status before continuing.")
        if state != "completed" or status.get("applied") is None:
            raise ValueError("The previous edit outcome is unknown. Check the drawing before continuing.")
        if status["applied"]:
            return status.get("result") or {"applied": True, "afterCommitSucceeded": status.get("afterCommitSucceeded")}
        stage_id = uuid5(stage_id, "retry")
    else:
        raise ValueError("The sheet stage reached its retry limit. Inspect the drawing and resolve the failure before starting a new request.")
    ops = operations()
    if not 0 < len(ops) <= MAX_OPS:
        raise ValueError(f"Sheet transaction has {len(ops)} operations (limit {MAX_OPS}).")
    reply = call("edit", {**identity, "workflow": workflow, "operations": ops}, timeout=EDIT_TIMEOUT)
    if reply.get("state") == "pending":
        raise ValueError("The previous edit is still pending. Resume with the same request_id.")
    if not reply.get("applied"):
        raise ValueError(_failed(reply))
    return reply


def build(params: dict[str, Any]) -> dict[str, Any]:
    target = drawing_target(params)
    root = UUID(edit_request_id(params))
    p = plan_sheets(params, target)
    t, sheets = p["template"], p["sheets"]
    if not sheets:
        return {"built": False, "note": "No sheets to make.", "notes": p["notes"]}
    workflow = hashlib.sha256(json.dumps([params, p], sort_keys=True, allow_nan=False).encode()).hexdigest()
    replace = params.get("if_exists") == "replace"
    ids = [root if i == 0 else uuid5(root, f"layout-{i}") for i in range(len(sheets))]
    existing = {l["name"].casefold() for l in call("list_layouts", {"target": target})["layouts"]}
    for s, copy_id in zip(sheets, ids):
        if s["layout"].casefold() in existing and not replace:
            status = call("get_edit_status", {"target": target, "request_id": str(copy_id)})
            if status.get("workflow") != workflow:
                raise ValueError(f"Layout '{s['layout']}' already exists. Choose another name or if_exists: replace.")

    stages = {s["layout"]: "pending" for s in sheets}
    built, copied, warnings = [], [], []
    notes = list(p["notes"])
    failure = None
    for s, copy_id in zip(sheets, ids):
        name = s["layout"]
        try:
            copy = {"op": "layout_from_template", "path": t["path"], "layout": t["layout"], "name": name}
            if replace:
                copy["if_exists"] = "replace"
            first = _stage(target, copy_id, workflow, lambda: [copy])
            copied.append(name)
            warnings.extend(first.get("warnings", []))
            if first.get("afterCommitSucceeded") is False:
                raise ValueError("Layout copied, but its after-commit work failed. Check the layout and xrefs.")
            second = _stage(target, uuid5(copy_id, "content"), workflow,
                            lambda: _sheet_content(t, s, target, p["unitMm"], notes))
            warnings.extend(second.get("warnings", []))
            if t["lines"]:
                for start in range(LABEL_BATCH, len(s["lines"]), MAX_OPS):
                    _stage(target, uuid5(copy_id, f"labels-{start}"), workflow,
                           lambda: _line_ops(t, s, start, start + MAX_OPS))
            stages[name] = "committed"
            built.append({"layout": name, "label": s["label"], "lines": s["lines"], "planScale": f"1:{s['planScale']}",
                          "profileViews": s.get("profileViews"), **({"stationRange": s["stationRange"]} if "stationRange" in s else {})})
        except (BridgeError, ValueError) as exc:
            failure = f"{name}: {exc}"
            stages[name] = str(exc)
            break
    if t["scaleBar"]:
        warnings.append("The scale bar is resized for each plan scale; check any scale text drawn inside the scale bar block.")
    return {"built": len(built) == len(sheets), "layoutsCreated": bool(copied), "copiedLayouts": copied,
            "stages": stages, "mode": p["mode"], "template": f"{t['path']} / {t['layout']}", "sheets": built,
            "notes": notes, "warnings": warnings, **({"error": failure} if failure else {})}
