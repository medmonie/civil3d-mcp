"""Bentley SewerGEMS -> Civil 3D gravity network, alignments and profile views.

Input: the SewerGEMS export as one Excel workbook (sheets "Manhole", "Outfall", "Conduit" - the "full output"), or
CSV FlexTable exports with manholes, outfalls, and conduits. Geometry must use metres and diameters millimetres.

    check(...)   validates the export and returns the proposed lines and any problems.
    build(...)   creates the network, profiles, and styles in one edit, then positions views and draws band tables
                 in a separately tracked edit.

Levels: a Civil 3D pipe is placed by its centreline, so each end is put at invert + inner diameter / 2. The pipe
then reports exactly the SewerGEMS invert. Manholes get rim = ground level and sump = their invert; the default
manhole is a flat-topped one, because Civil 3D's coned manholes cannot be shallower than about 1.7 m. Any manhole
whose levels Civil 3D still changed is listed in the reply (levelMismatches).
"""
from __future__ import annotations

import csv
import json
import math
import re
from collections import Counter, deque
from pathlib import Path
from typing import Any
from uuid import UUID, uuid5

from bridge_client import EDIT_TIMEOUT, BridgeError, DrawingTarget, call, drawing_target, edit_request_id

INVERT_TOLERANCE = 0.005      # m; level differences below this are rounding, not drops or reversals
LENGTH_TOLERANCE = 0.05       # m; table length vs plan distance
MAX_BATCH_OPERATIONS = 500

# --------------------------------------------------------------------------- reading


_UNITS = re.compile(r"\s*\((m|mm|m/m|m/km|%|ft|in|m/s|l/s|m³/s)\)$", re.I)
_METRIC_COLUMNS = {
    "x": "m", "y": "m", "elevation (ground)": "m", "elevation (rim)": "m", "elevation (invert)": "m",
    "invert (start)": "m", "invert (stop)": "m", "diameter": "mm", "length (scaled)": "m",
    "length (unified)": "m", "length": "m", "slope (calculated)": "m/km",
}
_USED_COLUMNS = _METRIC_COLUMNS.keys() | {"label", "material", "start node", "stop node", "is active?", "is reversed?"}


def _keys(headers: Any, source: str) -> list[str]:
    keys: list[str] = []
    used: set[str] = set()
    for header in headers:
        text = str(header or "").strip().lstrip("\ufeff").lower()
        units = _UNITS.search(text)
        key = text[:units.start()] if units else text
        expected = _METRIC_COLUMNS.get(key)
        if units and expected and units[1] != expected:
            raise ValueError(f"{source}: '{header}' must use {expected}. Export in metric units (m, mm, m/km).")
        if key in _USED_COLUMNS and key in used:
            raise ValueError(f"{source}: duplicate column '{key}'.")
        keys.append(key)
        used.add(key)
    return keys


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
    keys = _keys(rows[0], sheet)
    return [dict(zip(keys, r)) for r in rows[1:] if any(v not in (None, "") for v in r)]


def _rows_csv(path: Path) -> list[dict[str, Any]]:
    with open(path, encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader, [])
        keys = _keys(header, path.name)
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
    node_labels: set[str] = set()
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
            if label.casefold() in node_labels:
                raise ValueError(f"Duplicate manhole or outfall label '{label}'.")
            if node["x"] is None or node["y"] is None:
                raise ValueError(f"{kind} {label} has no X / Y. Export the FlexTable with the X and Y columns, "
                                 "or use the full-output workbook.")
            nodes[label] = node
            node_labels.add(label.casefold())

    pipes: list[dict[str, Any]] = []
    pipe_labels: set[str] = set()
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
        label = pipe["label"]
        if not label or label.casefold() in pipe_labels:
            raise ValueError(f"Conduit labels must be present and unique. Duplicate or empty label '{label}'.")
        pipe_labels.add(label.casefold())
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
                label = row[0].strip()
                if label in rename:
                    raise ValueError(f"Rename CSV repeats the source label '{label}'.")
                rename[label] = row[1].strip()
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
        if p["diameter_mm"] is None or p["diameter_mm"] <= 0:
            problems.append(f"{p['label']}: diameter must be positive")
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
        if node["kind"] == "outfall" and outgoing[label] > 0:
            problems.append(f"{label}: an outfall must be the end of a flow path")
    if not any(n["kind"] == "outfall" for n in nodes.values()):
        problems.append("no outfall in the export")
    if not problems:
        upstream: dict[str, list[str]] = {}
        for p in pipes:
            upstream.setdefault(p["stop"], []).append(p["start"])
        reachable = {k for k, n in nodes.items() if n["kind"] == "outfall"}
        frontier = deque(reachable)
        while frontier:
            for label in upstream.get(frontier.popleft(), []):
                if label not in reachable:
                    reachable.add(label)
                    frontier.append(label)
        missing = sorted(nodes.keys() - reachable)
        if missing:
            problems.append(f"{len(missing)} nodes do not drain to an outfall (disconnected flow or cycle): {', '.join(missing[:20])}")
    return problems


# --------------------------------------------------------------------------- lines (one profile each)


def split_lines(nodes: dict[str, Any], pipes: list[dict[str, Any]], rename: dict[str, str], prefix: str) -> list[dict[str, Any]]:
    """The trunk from each outfall upstream, then each branch from the manhole it joins. At a junction the trunk
    continues along the pipe whose upstream manhole has the same naming level (MH-4 after MH-3, not MH-3-1),
    then along the longest upstream path. Branch names: <trunk>-<last number of the junction manhole>."""
    arriving: dict[str, list[dict[str, Any]]] = {}
    for p in pipes:
        arriving.setdefault(p["stop"], []).append(p)
    departing = {p["start"]: p for p in pipes}
    remaining = {label: len(arriving.get(label, [])) for label in nodes}
    reach = dict.fromkeys(nodes, 0.0)
    upstream_ends = deque(label for label, count in remaining.items() if count == 0)
    processed = 0
    while upstream_ends:
        label = upstream_ends.popleft()
        processed += 1
        if p := departing.get(label):
            downstream = p["stop"]
            reach[downstream] = max(reach[downstream], (p["length"] or 0) + reach[label])
            remaining[downstream] -= 1
            if remaining[downstream] == 0:
                upstream_ends.append(downstream)
    if processed != len(nodes):
        raise ValueError("Conduits contain a cycle or an invalid flow path.")

    def level(label: str) -> int:
        return len(rename.get(label, label).split("-"))

    lines: list[dict[str, Any]] = []
    queue: deque[tuple[str, dict[str, Any] | None, str]] = deque()
    for i, outfall in enumerate(sorted(k for k, n in nodes.items() if n["kind"] == "outfall")):
        queue.append((outfall, None, f"{prefix}{i + 1}"))
    used_names = {name.casefold() for _, _, name in queue}
    while queue:
        start, first, name = queue.popleft()
        chain, used, here, step = [start], [], start, first
        while True:
            options = [step] if step else arriving.get(here, [])
            if not options:
                break
            if step is None:
                same = level(here) if nodes[here]["kind"] != "outfall" else None
                options = sorted(options, key=lambda p: (same is not None and level(p["start"]) != same,
                                                         -((p["length"] or 0) + reach[p["start"]]), p["label"]))
                for side in options[1:]:
                    junction = rename.get(here, here).split("-")[-1]
                    branch = base = f"{name}-{junction}"
                    index = 2
                    while branch.casefold() in used_names:
                        branch = f"{base}-{index}"
                        index += 1
                    used_names.add(branch.casefold())
                    queue.append((here, side, branch))
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


def _choose_parts(target: DrawingTarget, parts_list: str, pipe_family: str, structure_family: str, structure_diameter: float,
                  pipe_diameters: set[float]) -> dict[str, Any]:
    data = call("list_gravity_parts", {"parts_list": parts_list, "target": target})

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
        # Allow the small difference between a true bore and its nominal catalogue size (191 -> 200 mm).
        bigger = [x for x in sizes if x["Units"].casefold() in ("mm", "millimeter", "millimeters", "millimetres")
                  and d - 0.5 <= x["inner_diameter"] <= d + max(0.5, d * 0.05)]
        if not bigger:
            available = ", ".join(x["Label"] for x in sizes) or "none"
            raise ValueError(f"No suitable metric catalogue size for {d:g} mm in '{pipes[0]['family']}'. Available: {available}.")
        pipe_map[d] = min(bigger, key=lambda x: x["inner_diameter"])

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
    names = [_name(rename, label) for label in nodes] + [f"P {_name(rename, p['start'])}" for p in pipes]
    collisions = [name for name, count in Counter(n.casefold() for n in names).items() if count > 1]
    if collisions:
        problems.append(f"Duplicate drawing part names after renaming: {', '.join(collisions[:20])}")
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
    "band_set": None,                               # Civil 3D band set on the views (name or "auto"); default Standard
}


def _presentation_ops(network: str, lines: list[str], o: dict[str, Any]) -> list[dict[str, Any]]:
    h = float(o["label_height_mm"])
    return [
        {"op": "label_style_create", "kind": "pipe_label", "name": o["pipe_label_style"], "if_exists": "replace",
         "components": [{"type": "arrow", "name": "Flow"},
                        {"name": "Size", "text": o["pipe_label_text"], "height": h, "attachment": "BottomCenter", "y_offset": 1.0}]},
        {"op": "network_styles", "network": network, "pipe_style": o["pipe_style"], "structure_style": o["structure_style"],
         "pipe_label_style": o["pipe_label_style"], "structure_label_style": o["structure_label_style"]},
        {"op": "layer_create", "name": o["alignment_layer"], "on": False, "if_exists": "skip"},
        {"op": "alignment_label_set", "alignments": lines, "label_set": o["alignment_label_set"], "layer": o["alignment_layer"]},
    ]


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


def _finish_ops(origin: list[float], lines: list[dict[str, Any]], nodes: dict[str, Any],
                views: dict[str, Any], gap: float, draw_tables: bool) -> list[dict[str, Any]]:
    moves, top = [], origin[1]
    annotations: list[dict[str, Any]] = []
    for line in lines:
        name = f"{line['name']} - Profile"
        view = views.get(name)
        ext = view.get("extents") if view else None
        if not isinstance(ext, dict):
            raise ValueError(f"Cannot read the extents of profile view '{name}'.")
        (x0, y0, _), (x1, y1, _) = ext["min"], ext["max"]
        dx, dy = origin[0] - x0, top - y1
        if abs(dx) > 1e-6 or abs(dy) > 1e-6:
            moves.append({"op": "entity_move", "handles": [view["handle"]], "dx": dx, "dy": dy})
        top -= y1 - y0 + gap + (TABLE_HEIGHT if draw_tables else 0)
        if draw_tables:
            moved = {**view, "extents": {"min": [x0 + dx, y0 + dy], "max": [x1 + dx, y1 + dy]}}
            annotations.extend(band_table(line, nodes, moved))

    if annotations:
        moves.extend([
            {"op": "layer_create", "name": TEXT_LAYER, "color": 3, "if_exists": "skip"},
            {"op": "layer_create", "name": GRID_LAYER, "color": 4, "if_exists": "skip"},
        ])
        moves.extend({"op": "draw_annotations", "items": annotations[i:i + 5000]} for i in range(0, len(annotations), 5000))
    return moves


def _check_batch(batch: dict[str, Any], phase: str) -> None:
    if len(batch["operations"]) > MAX_BATCH_OPERATIONS:
        raise ValueError(f"SewerGEMS {phase} exceeds {MAX_BATCH_OPERATIONS} operations. Split the export.")
    if len(json.dumps(batch, allow_nan=False)) + 1024 > 4 * 1024 * 1024:
        raise ValueError(f"SewerGEMS {phase} exceeds the bridge request size limit. Split the export.")


def build(params: dict[str, Any]) -> dict[str, Any]:
    target = drawing_target(params)
    request_id = edit_request_id(params)
    attempt = params.get("finish_attempt", 1)
    if type(attempt) is not int or not 1 <= attempt <= 20:
        raise ValueError("'finish_attempt' must be an integer from 1 to 20.")

    def finish_id_for(number: int) -> str:
        return str(uuid5(UUID(request_id), f"sewergems-finish:{number}"))

    finish_id = finish_id_for(attempt)
    opts = {**DEFAULTS, **{k: v for k, v in params.items() if v is not None}}
    dry_run = params.get("dry_run", False)
    if not isinstance(dry_run, bool):
        raise ValueError("'dry_run' must be true or false.")
    presentation = params.get("presentation", True)
    if not isinstance(presentation, (bool, dict)):
        raise ValueError("'presentation' must be true, false, or an object of style overrides.")
    styled = presentation is not False
    options = {**PRESENTATION, **(presentation if isinstance(presentation, dict) else {})}
    draw_tables = styled and options["band_table"]
    if not isinstance(draw_tables, bool):
        raise ValueError("'band_table' must be true or false.")
    gap = float(params.get("profile_view_gap", 30.0))
    if not math.isfinite(gap) or gap < 0:
        raise ValueError("'profile_view_gap' must be a finite, non-negative distance.")

    plan = _plan(params)
    if plan["problems"]:
        return {"built": False, "problems": plan["problems"], "note": "Fix these in SewerGEMS (or the export) first."}
    nodes, pipes, rename, lines = plan["nodes"], plan["pipes"], plan["rename"], plan["lines"]
    if len(nodes) > 5000 or len(pipes) > 5000:
        raise ValueError("An import supports up to 5,000 structures and 5,000 pipes. Split the export.")
    if call("get_drawing_info", {"target": target})["civilDrawingUnits"] != "Meters":
        raise ValueError("SewerGEMS import requires a Civil 3D drawing set to metres.")

    parts = _choose_parts(target, opts["parts_list"], opts["pipe_family"], opts["structure_family"],
                          float(opts["structure_diameter"]), {p["diameter_mm"] for p in pipes})
    net = opts["network"]
    origin = params.get("profile_views_at")
    if origin is None:
        origin = [min(n["x"] for n in nodes.values()), min(n["y"] for n in nodes.values()) - 50.0]
    if (not isinstance(origin, list) or len(origin) != 2 or
            not all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in origin)):
        raise ValueError("'profile_views_at' must be a finite [x, y] coordinate.")

    structures = [{
        "name": _name(rename, label), "description": f"SewerGEMS {label}" + (" (outfall)" if n["kind"] == "outfall" else ""),
        "at": [n["x"], n["y"], n["ground"]], "family": parts["structure_family"],
        "inner_diameter": parts["structure_diameter"], "rim": n["ground"], "sump": n["invert"],
    } for label, n in nodes.items()]
    pipe_ops = []
    for p in pipes:
        size = parts["pipe_sizes"][p["diameter_mm"]]
        p["nominal_mm"] = size["inner_diameter"]
        half = p["nominal_mm"] / 2000.0
        a, b = nodes[p["start"]], nodes[p["stop"]]
        pipe_ops.append({
            "name": f"P {_name(rename, p['start'])}", "description": f"%%c{p['nominal_mm']:g}",
            "start": [a["x"], a["y"], p["inv_start"] + half], "end": [b["x"], b["y"], p["inv_stop"] + half],
            "family": parts["pipe_family"], "size_index": size["size_index"], "verify_bore_mm": p["nominal_mm"],
            "start_structure": _name(rename, p["start"]), "end_structure": _name(rename, p["stop"]),
        })

    core_ops: list[dict[str, Any]] = [
        {"op": "parts_list_add_family", "parts_list": opts["parts_list"], "domain": "structure",
         "family": parts["structure_family"], "sizes": [parts["structure_diameter"]]},
        {"op": "pipe_network_create", "name": net, "parts_list": opts["parts_list"],
         "description": f"From SewerGEMS: {Path(plan['source']).name}"},
        {"op": "pipe_network_add", "network": net, "structures": structures, "pipes": pipe_ops},
    ]
    style = params.get("profile_view_style") or (options["profile_view_style"] if styled else None)
    band_set = params.get("band_set") or options.get("band_set")
    for i, line in enumerate(lines):
        chain = line["nodes"]
        st = stations(nodes, chain)
        name = line["name"]
        core_ops.extend([
            {"op": "alignment_create", "name": name, "points": [[nodes[k]["x"], nodes[k]["y"]] for k in chain],
             "description": f"{_name(rename, chain[0])} to {_name(rename, chain[-1])}"},
            {"op": "profile_create", "alignment": name, "name": f"{name} - Ground",
             "pvis": [[s, nodes[k]["ground"]] for s, k in zip(st, chain)]},
            {"op": "profile_view_create", "alignment": name, "name": f"{name} - Profile",
             "at": [origin[0], origin[1] - i * 10.0], **({"style": style} if style else {}),
             **({"band_set": band_set} if band_set else {})},
            {"op": "profile_view_add_parts", "profile_view": f"{name} - Profile", "pipe_network": net,
             "structure_names": [_name(rename, k) for k in chain],
             "pipe_names": [f"P {_name(rename, p['start'])}" for p in line["pipes"]]},
        ])
    if styled:
        core_ops.extend(_presentation_ops(net, [line["name"] for line in lines], options))

    # Bind finish inputs to the core request so the same import ID cannot change the planned output.
    finish_plan = {"origin": origin, "gap": gap, "draw_tables": draw_tables}
    core_batch = {"operations": core_ops, "target": target, "request_id": request_id,
                  "dry_run": dry_run, "sewergems_finish": finish_plan}
    _check_batch(core_batch, "network and profiles")
    core = call("edit", core_batch, timeout=EDIT_TIMEOUT)
    if core.get("state") == "pending":
        # The bridge returns the ledger entry, not a result, while the same request_id is still running.
        return {"built": False, "pending": True, "target": target, "request_id": request_id,
                "note": "This import is still running in Civil 3D. Call again later with the same request_id."}
    if not core.get("applied"):
        return {"built": False, "dryRun": dry_run, "target": target, "core": core,
                "note": "Dry run covers the network, profile objects, and styles." if dry_run else None}

    finish = {"request_id": finish_id}
    mismatches = []
    try:
        if attempt > 1:
            previous = call("get_edit_status", {"request_id": finish_id_for(attempt - 1), "target": target})
            if previous["state"] != "completed" or previous.get("applied") is not False:
                raise ValueError("A new finish attempt requires a confirmed rollback of the preceding finish attempt.")
        status = call("get_edit_status", {"request_id": finish_id, "target": target})
        retryable = status["state"] == "error" and status.get("retryable") is True
        if status["state"] != "unknown" and not retryable:
            finish = {**finish, **(status.get("result") or status)}
        else:
            views = {v["name"]: v for v in call("list_profile_views", {"target": target})["profileViews"]}
            finish_ops = _finish_ops(origin, lines, nodes, views, gap, draw_tables)
            if finish_ops:
                batch = {"operations": finish_ops, "target": target, "request_id": finish_id}
                _check_batch(batch, "profile layout and bands")
                finish = call("edit", batch, timeout=EDIT_TIMEOUT)
            else:
                finish = {**finish, "applied": True, "state": "already_positioned"}

        if finish.get("state") == "pending":
            # The first call with this request_id is still laying out the profiles (seen through get_edit_status, or
            # as the ledger entry the bridge returns for a running request): say so, so the caller asks again later.
            return {"built": False, "pending": True, "network": net, "coreApplied": True, "target": target,
                    "request_id": request_id, "finish": finish,
                    "note": "This import is still running in Civil 3D (profile layout). Call again later with the same request_id."}
        by_name = {_name(rename, label): node for label, node in nodes.items()}
        levels = call("list_structures", {"network_name": net, "limit": 5000, "target": target})["structures"]
        for st in levels:
            node = by_name.get(st["name"])
            if not node:
                continue
            rim, sump = st["rimElevation"], st["sumpElevation"]
            if (not isinstance(rim, (int, float)) or not isinstance(sump, (int, float)) or
                    abs(rim - node["ground"]) > INVERT_TOLERANCE or abs(sump - node["invert"]) > INVERT_TOLERANCE):
                mismatches.append(f"{st['name']}: rim {rim} (ground {node['ground']}), sump {sump} (invert {node['invert']})")
    except (BridgeError, ValueError) as exc:
        return {"built": False, "network": net, "coreApplied": True, "target": target,
                "request_id": request_id, "finish": finish, "error": str(exc)}

    built = finish.get("applied") is True
    return {
        "built": built, "network": net, "coreApplied": True, "target": target, "request_id": request_id,
        "finish": finish, "manholes": len(nodes), "pipes": len(pipes), "connections": 2 * len(pipes),
        "parts": {"pipe": parts["pipe_family"], "pipeSizes": {str(k): v["Label"] for k, v in parts["pipe_sizes"].items()},
                  "structure": f"{parts['structure_family']} {parts['structure_diameter']:g} mm"},
        "levelMismatches": mismatches,
        "lines": [{"name": l["name"], "pipes": len(l["pipes"]), "length": round(stations(nodes, l["nodes"])[-1], 2)} for l in lines],
        "profiles": "created",
        "presentation": "applied" if styled else None,
        "bandTables": len(lines) if draw_tables and built else 0,
        "warnings": core.get("warnings", []) + finish.get("warnings", []),
    }
