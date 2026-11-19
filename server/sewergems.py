"""Bentley SewerGEMS -> Civil 3D gravity network, alignments and profile views.

Input: the SewerGEMS export as one Excel workbook (sheets "Manhole", "Outfall", "Conduit" - the "full output"), or
two CSV FlexTable exports (manholes, conduits). Columns are matched by name with the units stripped, so
"Elevation (Ground) (m)" and "Elevation (Ground)" are the same column.

    check(...)   reads and validates; nothing is drawn. Returns the lines, the parts it would use and any problems.
    build(...)   check, then draws in two edit batches:
                   1. the pipe network (manholes named after SewerGEMS, pipes with SewerGEMS inverts),
                   2. per line: an alignment from the downstream end, a ground profile through the manhole rims,
                      a profile view showing that line's pipes and manholes.

Levels: a Civil 3D pipe is placed by its centreline, so each end is put at invert + inner diameter / 2. The pipe
then reports exactly the SewerGEMS invert. Manholes get rim = ground level and sump = their invert; the default
manhole is a flat-topped one, because Civil 3D's coned manholes cannot be shallower than about 1.7 m. Any manhole
whose levels Civil 3D still changed is listed in the reply (levelMismatches).
"""
from __future__ import annotations

import csv
import math
import re
from collections import Counter
from pathlib import Path
from typing import Any

from bridge_client import EDIT_TIMEOUT, call

INVERT_TOLERANCE = 0.005      # m; level differences below this are rounding, not drops or reversals
LENGTH_TOLERANCE = 0.05       # m; table length vs plan distance

# --------------------------------------------------------------------------- reading


def _key(header: Any) -> str:
    """'Elevation (Ground) (m)' -> 'elevation (ground)'; units in the last brackets are dropped."""
    text = str(header or "").strip().lstrip("﻿")
    text = re.sub(r"\s*\((m|mm|m/m|m/km|%|ft|in|m/s|l/s|m³/s)\)$", "", text, flags=re.I)
    return text.lower()


def _num(value: Any) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if math.isfinite(number) else None


def _rows_xlsx(path: Path, sheet: str) -> list[dict[str, Any]]:
    import openpyxl   # imported here so the rest of the server does not need it

    book = openpyxl.load_workbook(path, read_only=True, data_only=True)
    try:
        if sheet not in book.sheetnames:
            return []
        rows = list(book[sheet].iter_rows(values_only=True))
    finally:
        book.close()
    if not rows:
        return []
    keys = [_key(h) for h in rows[0]]
    return [dict(zip(keys, r)) for r in rows[1:] if any(v not in (None, "") for v in r)]


def _rows_csv(path: Path) -> list[dict[str, Any]]:
    with open(path, encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader, [])
        keys = [_key(h) for h in header]
        return [dict(zip(keys, r)) for r in reader if any(c.strip() for c in r)]


def _pick(row: dict[str, Any], *names: str) -> Any:
    for name in names:
        if name in row and row[name] not in (None, "", "(N/A)"):
            return row[name]
    return None


def _active(row: dict[str, Any]) -> bool:
    return str(_pick(row, "is active?") if _pick(row, "is active?") is not None else True).lower() not in ("false", "0")


def _need_path(text: str | None, what: str) -> Path:
    if not text:
        raise ValueError(f"Give {what}.")
    path = Path(text).expanduser()
    if not path.is_file():
        raise ValueError(f"{what}: file not found: {path}")
    return path


def read(params: dict[str, Any]) -> dict[str, Any]:
    """Nodes and conduits from the export, oriented in the direction of flow."""
    if params.get("path"):
        book = _need_path(params["path"], "path (the SewerGEMS workbook)")
        manholes = _rows_xlsx(book, "Manhole")
        outfalls = _rows_xlsx(book, "Outfall")
        conduits = _rows_xlsx(book, "Conduit")
        source = str(book)
    else:
        manholes = _rows_csv(_need_path(params.get("manholes_csv"), "manholes_csv (or path to the workbook)"))
        outfalls = _rows_csv(_need_path(params["outfalls_csv"], "outfalls_csv")) if params.get("outfalls_csv") else []
        conduits = _rows_csv(_need_path(params.get("conduits_csv"), "conduits_csv"))
        source = f"{params['manholes_csv']} + {params['conduits_csv']}"

    nodes: dict[str, dict[str, Any]] = {}
    for kind, rows in (("manhole", manholes), ("outfall", outfalls)):
        for r in rows:
            if not _active(r):
                continue
            label = str(_pick(r, "label") or "").strip()
            node = {
                "label": label, "kind": kind,
                "x": _num(_pick(r, "x")), "y": _num(_pick(r, "y")),
                "ground": _num(_pick(r, "elevation (ground)", "elevation (rim)")),
                "invert": _num(_pick(r, "elevation (invert)")),
                "diameter_mm": _num(_pick(r, "diameter")),
            }
            if not label:
                raise ValueError(f"A {kind} row has no Label.")
            if node["x"] is None or node["y"] is None:
                raise ValueError(f"{kind} {label} has no X / Y. Export the FlexTable with the X and Y columns, "
                                 "or use the full-output workbook.")
            nodes[label] = node

    pipes: list[dict[str, Any]] = []
    for r in conduits:
        if not _active(r):
            continue
        pipe = {
            "label": str(_pick(r, "label") or "").strip(),
            "start": str(_pick(r, "start node") or "").strip(), "stop": str(_pick(r, "stop node") or "").strip(),
            "inv_start": _num(_pick(r, "invert (start)")), "inv_stop": _num(_pick(r, "invert (stop)")),
            "diameter_mm": _num(_pick(r, "diameter")), "material": _pick(r, "material"),
            "length": _num(_pick(r, "length (scaled)", "length (unified)", "length")),
            "slope_permille": _num(_pick(r, "slope (calculated)")),   # SewerGEMS reports it in m/km = per mille
        }
        # SewerGEMS keeps the drawn direction and flags "Is Reversed?" when flow runs stop -> start.
        if str(_pick(r, "is reversed?")).lower() == "true":
            pipe["start"], pipe["stop"] = pipe["stop"], pipe["start"]
            pipe["inv_start"], pipe["inv_stop"] = pipe["inv_stop"], pipe["inv_start"]
        pipes.append(pipe)
    return {"source": source, "nodes": nodes, "pipes": pipes}


def read_rename(path_text: str | None) -> dict[str, str]:
    """Optional two-column CSV: SewerGEMS label, drawing label (e.g. MH-19,MH-1-19)."""
    if not path_text:
        return {}
    rename = {}
    with open(_need_path(path_text, "rename_csv"), encoding="utf-8-sig", newline="") as f:
        for row in csv.reader(f):
            if len(row) >= 2 and row[0].strip() and row[1].strip():
                rename[row[0].strip()] = row[1].strip()
    return rename


# --------------------------------------------------------------------------- checking


def validate(nodes: dict[str, Any], pipes: list[dict[str, Any]]) -> list[str]:
    problems: list[str] = []
    for p in pipes:
        for end in ("start", "stop"):
            if p[end] not in nodes:
                problems.append(f"{p['label']}: {end} node '{p[end]}' is not a manhole or outfall in the export")
        if p["inv_start"] is None or p["inv_stop"] is None:
            problems.append(f"{p['label']}: missing invert(s)")
        elif p["inv_stop"] > p["inv_start"] + INVERT_TOLERANCE:
            problems.append(f"{p['label']}: flows uphill ({p['inv_start']} -> {p['inv_stop']})")
        if p["diameter_mm"] is None:
            problems.append(f"{p['label']}: no diameter")
        a, b = nodes.get(p["start"]), nodes.get(p["stop"])
        if a and b:
            plan = math.hypot(a["x"] - b["x"], a["y"] - b["y"])
            if plan < 0.01:
                problems.append(f"{p['label']}: start and stop manholes are at the same point")
            elif p["length"] is not None and abs(plan - p["length"]) > LENGTH_TOLERANCE:
                problems.append(f"{p['label']}: table length {p['length']} but {plan:.2f} m between the manholes")
            for node, level in ((a, p["inv_start"]), (b, p["inv_stop"])):
                if level is not None and node["ground"] is not None and level + (p["diameter_mm"] or 0) / 1000 > node["ground"]:
                    problems.append(f"{p['label']}: pipe crown above ground at {node['label']}")
                if level is not None and node["invert"] is not None and level < node["invert"] - INVERT_TOLERANCE:
                    problems.append(f"{p['label']}: invert {level} below the invert of {node['label']} ({node['invert']})")
    outgoing = Counter(p["start"] for p in pipes)
    incoming = Counter(p["stop"] for p in pipes)
    for label, node in nodes.items():
        if node["ground"] is None or node["invert"] is None:
            problems.append(f"{label}: missing ground or invert level")
        if outgoing[label] + incoming[label] == 0:
            problems.append(f"{label}: no pipe connected")
        elif outgoing[label] > 1:
            problems.append(f"{label}: {outgoing[label]} outgoing pipes (a sewer manhole should have one)")
        elif outgoing[label] == 0 and node["kind"] != "outfall":
            problems.append(f"{label}: no outgoing pipe and not an outfall")
    if not any(n["kind"] == "outfall" for n in nodes.values()):
        problems.append("no outfall in the export")
    return problems


# --------------------------------------------------------------------------- lines (one profile each)


def split_lines(nodes: dict[str, Any], pipes: list[dict[str, Any]], rename: dict[str, str], prefix: str) -> list[dict[str, Any]]:
    """The trunk from each outfall upstream, then each branch from the manhole it joins. At a junction the trunk
    continues along the pipe whose upstream manhole has the same naming level (MH-4 after MH-3, not MH-3-1),
    then along the longest upstream path. Branch names: <trunk>-<last number of the junction manhole>."""
    arriving: dict[str, list[dict[str, Any]]] = {}
    for p in pipes:
        arriving.setdefault(p["stop"], []).append(p)
    reach_cache: dict[str, float] = {}

    def reach(label: str) -> float:
        if label not in reach_cache:
            reach_cache[label] = max((p["length"] or 0) + reach(p["start"]) for p in arriving.get(label, [])) \
                if arriving.get(label) else 0.0
        return reach_cache[label]

    def level(label: str) -> int:
        return len(rename.get(label, label).split("-"))

    lines: list[dict[str, Any]] = []
    queue: list[tuple[str, dict[str, Any] | None, str]] = []
    for i, outfall in enumerate(sorted(k for k, n in nodes.items() if n["kind"] == "outfall")):
        queue.append((outfall, None, f"{prefix}{i + 1}"))
    while queue:
        start, first, name = queue.pop(0)
        chain, used, here, step = [start], [], start, first
        while True:
            options = [step] if step else arriving.get(here, [])
            if not options:
                break
            if step is None:
                same = level(here) if nodes[here]["kind"] != "outfall" else None
                options = sorted(options, key=lambda p: (same is not None and level(p["start"]) != same,
                                                         -((p["length"] or 0) + reach(p["start"]))))
                for side in options[1:]:
                    junction = rename.get(here, here).split("-")[-1]
                    queue.append((here, side, f"{name}-{junction}"))
            pipe = options[0]
            used.append(pipe)
            here = pipe["start"]
            chain.append(here)
            step = None
        if used:
            lines.append({"name": name, "nodes": chain, "pipes": used})
    return lines


def stations(nodes: dict[str, Any], chain: list[str]) -> list[float]:
    """Chainage of each manhole along straight tangents from the first one."""
    out = [0.0]
    for a, b in zip(chain, chain[1:]):
        out.append(out[-1] + math.hypot(nodes[a]["x"] - nodes[b]["x"], nodes[a]["y"] - nodes[b]["y"]))
    return out


# --------------------------------------------------------------------------- parts


def _choose_parts(parts_list: str, pipe_family: str, structure_family: str, structure_diameter: float,
                  pipe_diameters: set[float]) -> dict[str, Any]:
    data = call("list_gravity_parts", {"parts_list": parts_list})

    def matching(groups: list[dict[str, Any]], words: str) -> list[dict[str, Any]]:
        exact = [f for f in groups if f["family"].lower() == words.lower()]
        return exact or [f for f in groups if all(w.lower() in f["family"].lower() for w in words.split())]

    pipes = matching(data["pipeFamilies"], pipe_family)
    if len(pipes) != 1:
        names = ", ".join(f["family"] for f in data["pipeFamilies"])
        raise ValueError(f"pipe_family: {len(pipes)} pipe families in '{parts_list}' match '{pipe_family}'. Available: {names}")
    sizes = [x for x in pipes[0]["sizes"] if x["inner_diameter"]]
    pipe_map = {}
    for d in sorted(pipe_diameters):
        # SewerGEMS gives the true bore (191 mm for a 200 mm PVC pipe); the catalogue lists nominal sizes,
        # so take the smallest catalogue size not below it, else the largest.
        bigger = [x for x in sizes if x["inner_diameter"] >= d - 0.5]
        pipe_map[d] = min(bigger, key=lambda x: x["inner_diameter"]) if bigger else max(sizes, key=lambda x: x["inner_diameter"])

    # The manhole: taken from the parts list when it has the family in that size, otherwise added to the list
    # from the catalogue (structure_family must then be the exact family description).
    found = matching(data["structureFamilies"], structure_family)
    in_list = len(found) == 1 and any(x["inner_diameter"] and abs(x["inner_diameter"] - structure_diameter) < 0.5
                                      for x in found[0]["sizes"])
    if len(found) > 1:
        raise ValueError(f"structure_family: {len(found)} families in '{parts_list}' match '{structure_family}': "
                         + ", ".join(f["family"] for f in found))
    return {"pipe_family": pipes[0]["family"], "pipe_sizes": pipe_map,
            "structure_family": found[0]["family"] if found else structure_family,
            "structure_diameter": structure_diameter, "add_structure_family": not in_list}


# --------------------------------------------------------------------------- actions


# A frameless, flat-topped manhole: Civil 3D's coned manholes cannot be shallower than about 1.7 m (frame + cone),
# and sewer heads are often 1.2 m deep. Its rim then stays at the SewerGEMS ground level.
DEFAULTS = {
    "network": "Sewer", "parts_list": "Sanitary Sewer", "pipe_family": "PVC",
    "structure_family": "Cylindrical Junction Structure NF SI", "structure_diameter": 1200.0, "line_prefix": "LINE",
}


def _plan(params: dict[str, Any]) -> dict[str, Any]:
    data = read(params)
    rename = read_rename(params.get("rename_csv"))
    nodes, pipes = data["nodes"], data["pipes"]
    problems = validate(nodes, pipes)
    lines = split_lines(nodes, pipes, rename, params.get("line_prefix", DEFAULTS["line_prefix"])) if not problems else []
    return {"source": data["source"], "nodes": nodes, "pipes": pipes, "rename": rename, "problems": problems, "lines": lines}


def _name(rename: dict[str, str], label: str) -> str:
    return rename.get(label, label)


def check(params: dict[str, Any]) -> dict[str, Any]:
    plan = _plan(params)
    nodes, pipes, rename = plan["nodes"], plan["pipes"], plan["rename"]
    drops = [f"{_name(rename, p['stop'])}: {p['inv_stop']} into {nodes[p['stop']]['invert']}"
             for p in pipes if p["stop"] in nodes and p["inv_stop"] is not None and nodes[p["stop"]]["invert"] is not None
             and p["inv_stop"] - nodes[p["stop"]]["invert"] > INVERT_TOLERANCE]
    return {
        "source": plan["source"],
        "manholes": sum(n["kind"] == "manhole" for n in nodes.values()),
        "outfalls": [k for k, n in nodes.items() if n["kind"] == "outfall"],
        "conduits": len(pipes),
        "materials": dict(Counter(str(p["material"]) for p in pipes)),
        "diametersMm": dict(Counter(p["diameter_mm"] for p in pipes)),
        "renamed": f"{sum(1 for k in nodes if k in rename)} of {len(nodes)} nodes" if rename else "no rename table",
        "dropManholes": drops,
        "problems": plan["problems"],
        "lines": [{"name": l["name"], "pipes": len(l["pipes"]), "from": _name(rename, l["nodes"][0]),
                   "to": _name(rename, l["nodes"][-1])} for l in plan["lines"]],
        "ready": not plan["problems"],
    }


# What the drawing should look like. Names refer to the styles of Civil 3D's metric template; every one can be
# overridden through build(..., presentation={...}). Band titles follow the LISP profile sheets.
PRESENTATION: dict[str, Any] = {
    "pipe_style": "Single Line (Sanitary)",
    "structure_style": "Sanitary Sewer Manhole",
    "structure_label_style": "Name Only (Sanitary)",
    "pipe_label_style": "SewerGEMS Pipe",            # created below: flow arrow + "ø200 L=22m"
    # The pipe's Description holds the nominal size (ø200); Civil 3D has no usable inner-diameter field for labels.
    "pipe_label_text": "<[Description(CP)]> L=<[2D Length(Um|P0|RN|AP|Sn|OF)]>m",
    "label_height_mm": 2.5,
    "alignment_label_set": "_No Labels",
    "alignment_layer": "SEWER-ALIGNMENT",           # turned off: the pipes already show the route
    "profile_view_style": "Profile View",
    "band_table": True,                             # the LISP-style table under each profile view
}


def present(network: str, lines: list[str], options: dict[str, Any] | None = None) -> dict[str, Any]:
    """Plan and profile presentation for an imported network, as one edit batch."""
    o = {**PRESENTATION, **(options or {})}
    h = float(o["label_height_mm"])
    ops: list[dict[str, Any]] = [
        {"op": "label_style_create", "kind": "pipe_label", "name": o["pipe_label_style"], "if_exists": "replace",
         "components": [{"type": "arrow", "name": "Flow"},
                        {"name": "Size", "text": o["pipe_label_text"], "height": h, "attachment": "BottomCenter", "y_offset": 1.0}]},
        {"op": "network_styles", "network": network, "pipe_style": o["pipe_style"], "structure_style": o["structure_style"],
         "pipe_label_style": o["pipe_label_style"], "structure_label_style": o["structure_label_style"]},
        {"op": "layer_create", "name": o["alignment_layer"], "on": False, "if_exists": "skip"},
        {"op": "alignment_label_set", "alignments": lines, "label_set": o["alignment_label_set"], "layer": o["alignment_layer"]},
    ]
    for line in lines:
        ops.append({"op": "profile_view_set", "profile_view": f"{line} - Profile", "style": o["profile_view_style"]})
    return call("edit", {"operations": ops}, timeout=EDIT_TIMEOUT)


# --------------------------------------------------------------------------- band table (drawn, like the LISP sheets)

# The per-mille sign is not in AutoCAD's SHX fonts, so that title is multiline text with an Arial override.
BAND_ROWS = ["GROUND LEVEL (m)", "INVERT LEVEL (m)", "PIPE DEPTH (m)", "DISTANCE (m)", r"SLOPE {\fArial|b0|i0;\U+2030}",
             "PIPE DIAMETER (mm)", "PIPE MATERIAL"]
ROW_H = 16.0            # row height (drawing units, as the LISP sheets)
TITLE_W = 70.0          # title column width, left of the profile view
TEXT_H = 3.0            # value text height
TITLE_H = 3.2           # row title text height
TABLE_GAP = 5.0         # between the profile view and the table
TABLE_HEIGHT = len(BAND_ROWS) * ROW_H + TABLE_GAP
TEXT_LAYER, GRID_LAYER = "EN-P-TXT", "EN-P-GRID"


def _f2(v: float) -> str:
    return f"{v:.2f}"


def _runs(values: list[Any]) -> list[tuple[int, int, Any]]:
    """Consecutive equal values as (first index, last index, value)."""
    out: list[tuple[int, int, Any]] = []
    for i, v in enumerate(values):
        if out and out[-1][2] == v:
            out[-1] = (out[-1][0], i, v)
        else:
            out.append((i, i, v))
    return out


def band_table(line: dict[str, Any], nodes: dict[str, Any], view: dict[str, Any]) -> list[dict[str, Any]]:
    """Edit operations that draw the band table of one line under its profile view.

    view: the profile view as list_profile_views returns it (extents and stationStart): the view is drawn at 1:1
    horizontally, so station s sits at x = left edge + (s - stationStart)."""
    chain, pipes = line["nodes"], line["pipes"]
    st = stations(nodes, chain)
    left, right = view["extents"]["min"][0], view["extents"]["max"][0]
    top = view["extents"]["min"][1] - TABLE_GAP
    x = [left + (s - view["stationStart"]) for s in st]
    row_bottom = [top - ROW_H * (k + 1) for k in range(len(BAND_ROWS))]
    ops: list[dict[str, Any]] = []

    def line_op(a, b):
        ops.append({"op": "draw_line", "start": a, "end": b, "layer": GRID_LAYER})

    def text(t, at, rot=0.0, h=TEXT_H):
        ops.append({"op": "draw_text", "text": t, "at": at, "height": h, "rotation_deg": rot, "layer": TEXT_LAYER})

    def vtext(t, xc, row, dx=0.0):
        """A value written upwards, centred on x = xc + dx, near the bottom of the row."""
        text(t, [xc + dx + TEXT_H / 2, row_bottom[row] + 2.0], rot=90.0)

    def htext(t, xc, row, y=4.0):
        """A value written along the row, centred on x = xc."""
        text(t, [xc - len(t) * TEXT_H * 0.42, row_bottom[row] + y])

    # frame: row lines across the table and the title column
    for y in [top] + row_bottom:
        line_op([left - TITLE_W, y], [right, y])
    line_op([left - TITLE_W, top], [left - TITLE_W, row_bottom[-1]])
    line_op([left, top], [left, row_bottom[-1]])
    line_op([right, top], [right, row_bottom[-1]])
    for k, title in enumerate(BAND_ROWS):
        if "{" in title:   # formatted title: multiline text, placed by its top-left corner
            ops.append({"op": "draw_mtext", "text": title, "at": [left - TITLE_W + 3.0, row_bottom[k] + (ROW_H + TITLE_H) / 2],
                        "height": TITLE_H, "layer": TEXT_LAYER})
        else:
            text(title, [left - TITLE_W + 3.0, row_bottom[k] + (ROW_H - TITLE_H) / 2], h=TITLE_H)

    # pipe i runs from chain[i+1] (upstream) down to chain[i]; the line is drawn from its downstream end
    inv_down = [p["inv_stop"] for p in pipes]     # invert where pipe i leaves towards chain[i]
    inv_up = [p["inv_start"] for p in pipes]      # invert at chain[i+1]
    for i, label in enumerate(chain):
        n = nodes[label]
        xi = x[i]
        vtext(_f2(n["ground"]), xi, 0)
        # levels at this manhole: the outgoing pipe (towards the outfall) and the incoming one of this line
        out_level = inv_up[i - 1] if i > 0 else None          # pipe i-1 starts here (flows away, downstream)
        in_level = inv_down[i] if i < len(pipes) else None     # pipe i arrives here from upstream
        levels = [v for v in (out_level, in_level) if v is not None]
        if len(levels) == 2 and abs(levels[0] - levels[1]) > INVERT_TOLERANCE:
            # drop manhole: outgoing on the left, incoming on the right, as on the LISP sheets
            for v, dx in ((min(levels), -2.0), (max(levels), 2.0)):
                vtext(_f2(v), xi, 1, dx)
                vtext(_f2(n["ground"] - v), xi, 2, dx)
        else:
            v = levels[0] if levels else n["invert"]
            vtext(_f2(v), xi, 1)
            vtext(_f2(n["ground"] - v), xi, 2)
        vtext(f"{st[i]:.0f}", xi, 3)

    # pipe lengths between the stations
    for i in range(len(pipes)):
        htext(f"{st[i + 1] - st[i]:.0f}", (x[i] + x[i + 1]) / 2, 3, y=1.5)

    # slope, diameter, material: one value per run of equal pipes, with separators where they change
    for row, values in ((4, [_f2(p["slope_permille"]) if p["slope_permille"] is not None else "-" for p in pipes]),
                        (5, [f"{p['nominal_mm']:g}" for p in pipes]),
                        (6, [str(p["material"] or "") for p in pipes])):
        for first, last, value in _runs(values):
            xa, xb = x[first], x[last + 1]
            line_op([xa, row_bottom[row]], [xa, row_bottom[row] + ROW_H])
            line_op([xb, row_bottom[row]], [xb, row_bottom[row] + ROW_H])
            htext(value, (xa + xb) / 2, row)
    return ops


def _stack_views(origin: list[float], names: list[str], gap: float, below: float = 0.0) -> None:
    """A profile view's height depends on its level range and the style's exaggeration, so the views are created
    first and then moved: left edges at origin x, the first top edge at origin y, each next one below the last."""
    views = {v["name"]: v for v in call("list_profile_views")["profileViews"]}
    moves, top = [], origin[1]
    for name in names:
        ext = views.get(name, {}).get("extents")
        if not isinstance(ext, dict):
            continue
        (x0, y0, _), (_, y1, _) = ext["min"], ext["max"]
        dx, dy = origin[0] - x0, top - y1
        if abs(dx) > 1e-6 or abs(dy) > 1e-6:
            moves.append({"op": "entity_move", "handles": [views[name]["handle"]], "dx": dx, "dy": dy})
        top -= (y1 - y0) + below + gap
    if moves:
        call("edit", {"operations": moves}, timeout=EDIT_TIMEOUT)


def build(params: dict[str, Any]) -> dict[str, Any]:
    opts = {**DEFAULTS, **{k: v for k, v in params.items() if v is not None}}
    plan = _plan(params)
    if plan["problems"]:
        return {"built": False, "problems": plan["problems"], "note": "Fix these in SewerGEMS (or the export) first."}
    nodes, pipes, rename, lines = plan["nodes"], plan["pipes"], plan["rename"], plan["lines"]
    parts = _choose_parts(opts["parts_list"], opts["pipe_family"], opts["structure_family"],
                          float(opts["structure_diameter"]), {p["diameter_mm"] for p in pipes})
    dry_run = bool(params.get("dry_run", False))
    net = opts["network"]

    # ---- batch 1: the network
    structures = [{
        "name": _name(rename, label), "description": f"SewerGEMS {label}" + (" (outfall)" if n["kind"] == "outfall" else ""),
        "at": [n["x"], n["y"], n["ground"]], "family": parts["structure_family"],
        "inner_diameter": parts["structure_diameter"], "rim": n["ground"], "sump": n["invert"],
    } for label, n in nodes.items()]
    pipe_ops = []
    for p in pipes:
        size = parts["pipe_sizes"][p["diameter_mm"]]
        half = size["inner_diameter"] / 2000.0
        a, b = nodes[p["start"]], nodes[p["stop"]]
        pipe_ops.append({
            "name": f"P {_name(rename, p['start'])}", "description": f"%%c{size['inner_diameter']:g}",
            "start": [a["x"], a["y"], p["inv_start"] + half], "end": [b["x"], b["y"], p["inv_stop"] + half],
            "family": parts["pipe_family"], "size_index": size["size_index"],
        })
    network_ops: list[dict[str, Any]] = []
    if parts["add_structure_family"]:
        network_ops.append({"op": "parts_list_add_family", "parts_list": opts["parts_list"], "domain": "structure",
                            "family": parts["structure_family"], "sizes": [parts["structure_diameter"]]})
    network_ops += [
        {"op": "pipe_network_create", "name": net, "parts_list": opts["parts_list"],
         "description": f"From SewerGEMS: {Path(plan['source']).name}"},
        {"op": "pipe_network_add", "network": net, "structures": structures, "pipes": pipe_ops,
         "connect_tolerance": 0.05},
    ]
    first = call("edit", {"dry_run": dry_run, "operations": network_ops}, timeout=EDIT_TIMEOUT)
    if not first.get("applied") or dry_run:
        return {"built": False, "dryRun": dry_run, "network": first}

    added = first["results"][-1]["result"]
    handle = {c["name"]: c["handle"] for c in added["created"]}
    connections = added["connections"]

    # Civil 3D may hold a rim above the given level when the structure part cannot be that shallow.
    levels = call("list_structures", {"network_name": net, "limit": 5000})["structures"]
    mismatches = []
    for st in levels:
        label = next((k for k in nodes if _name(rename, k) == st["name"]), None)
        if label and (abs(st["rimElevation"] - nodes[label]["ground"]) > INVERT_TOLERANCE
                      or abs(st["sumpElevation"] - nodes[label]["invert"]) > INVERT_TOLERANCE):
            mismatches.append(f"{st['name']}: rim {st['rimElevation']} (ground {nodes[label]['ground']}), "
                              f"sump {st['sumpElevation']} (invert {nodes[label]['invert']})")

    # ---- batch 2: one alignment, ground profile and profile view per line
    origin = params.get("profile_views_at")
    if not origin:
        xs, ys = [n["x"] for n in nodes.values()], [n["y"] for n in nodes.values()]
        origin = [min(xs), min(ys) - 50.0]   # top-left of the first view, below the plan
    spacing = 10.0   # provisional; _stack_views places the views once their sizes are known
    ops: list[dict[str, Any]] = []
    for i, line in enumerate(lines):
        chain = line["nodes"]
        st = stations(nodes, chain)
        name = line["name"]
        ops.append({"op": "alignment_create", "name": name, "points": [[nodes[k]["x"], nodes[k]["y"]] for k in chain],
                    "description": f"{_name(rename, chain[0])} to {_name(rename, chain[-1])}"})
        ops.append({"op": "profile_create", "alignment": name, "name": f"{name} - Ground",
                    "pvis": [[s, nodes[k]["ground"]] for s, k in zip(st, chain)]})
        ops.append({"op": "profile_view_create", "alignment": name, "name": f"{name} - Profile",
                    "at": [origin[0], origin[1] - i * spacing], **({"style": params["profile_view_style"]} if params.get("profile_view_style") else {})})
        part_handles = [handle[_name(rename, k)] for k in chain] + [handle[f"P {_name(rename, p['start'])}"] for p in line["pipes"]]
        ops.append({"op": "profile_view_add_parts", "profile_view": f"{name} - Profile", "handles": part_handles})
    second = call("edit", {"operations": ops}, timeout=EDIT_TIMEOUT)
    looks, tables = None, 0
    options = {**PRESENTATION, **(params["presentation"] if isinstance(params.get("presentation"), dict) else {})}
    styled = params.get("presentation", True) is not False
    if second.get("applied"):
        if styled:
            looks = present(net, [l["name"] for l in lines], options)
        draw_tables = styled and options["band_table"]
        view_names = [f"{l['name']} - Profile" for l in lines]
        _stack_views(origin, view_names, gap=float(params.get("profile_view_gap", 30.0)), below=TABLE_HEIGHT if draw_tables else 0.0)
        if draw_tables:
            for p in pipes:
                p["nominal_mm"] = parts["pipe_sizes"][p["diameter_mm"]]["inner_diameter"]
            views = {v["name"]: v for v in call("list_profile_views")["profileViews"]}
            layer_ops = [{"op": "layer_create", "name": TEXT_LAYER, "color": 3, "if_exists": "skip"},
                         {"op": "layer_create", "name": GRID_LAYER, "color": 4, "if_exists": "skip"}]
            for i, line in enumerate(lines):
                view = views.get(f"{line['name']} - Profile")
                if not view or not isinstance(view.get("extents"), dict):
                    continue
                ops = (layer_ops if i == 0 else []) + band_table(line, nodes, view)
                done = call("edit", {"operations": ops}, timeout=EDIT_TIMEOUT)
                tables += 1 if done.get("applied") else 0

    return {
        "built": bool(second.get("applied")),
        "network": net, "manholes": len(nodes), "pipes": len(pipes), "connections": connections,
        "parts": {"pipe": parts["pipe_family"], "pipeSizes": {str(k): v["Label"] for k, v in parts["pipe_sizes"].items()},
                  "structure": f"{parts['structure_family']} {parts['structure_diameter']:g} mm"
                               + (f" (added to '{opts['parts_list']}')" if parts["add_structure_family"] else "")},
        "levelMismatches": mismatches,
        "lines": [{"name": l["name"], "pipes": len(l["pipes"]), "length": round(stations(nodes, l["nodes"])[-1], 2)} for l in lines],
        "profiles": second if not second.get("applied") else "created",
        "presentation": "applied" if looks and looks.get("applied") else looks,
        "bandTables": tables,
        "note": "Pipe ends are at invert + catalogue bore / 2, so Civil 3D reports the SewerGEMS inverts. "
                "Laterals and property connections are not imported.",
    }
