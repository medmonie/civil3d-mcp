"""Bentley WaterGEMS -> Civil 3D pressure network, with valves, service taps and profiles of the mains.

Input: the WaterGEMS "full output" workbook (sheets Pipe, Junction and, when present, PRV, Tank, Reservoir,
Hydrant). Rows repeat per time step; the first row of each element is used. Geometry must be in metres and
diameters in millimetres. WaterGEMS gives one elevation per node: it is taken as ground level, and every pipe is
laid with 'cover' (default 1.0 m) to its top.

    check(...)   reads the export and the drawing, and reports what would be built and how it differs from the CAD.
    build(...)   creates the pressure network (one edit), then alignments, ground profiles and profile views of the
                 mains (a second edit), then lines the views up below the plan (a third edit).

Parts: the network uses the sizes in the pressure parts list (default 'Water'). When it has no part of the real
material and size (e.g. a ductile-iron-only list for a PVC network), the nearest listed size stands in and the
part's description - shown by the plan labels - keeps the real material and size ("PVC Ø110"). Running the import
again with a parts list that has the real parts gives the exact parts under the same names.

Optional CAD layers of the drawing add what the model does not have:
    valve_layer       closed polylines / blocks of valve chambers -> gate valves on the pipe they sit on
    service_layers    polylines of service connections -> small pipes teed off the main
    hydrant_layer     hydrant blocks -> listed in the report (kept as CAD blocks; no hydrant parts are placed)
    cad_pipe_layers   the drawn pipes -> difference report (model vs drawing)
"""
from __future__ import annotations

import math
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any
from uuid import UUID, uuid5

from bridge_client import EDIT_TIMEOUT, BridgeError, call, drawing_target, edit_request_id

MAX_BATCH_OPERATIONS = 500
END_CLEARANCE = 2.0
STAND_IN_OD = 1.15        # outside diameter / DN allowance for a stand-in pipe (ductile iron DN150 is 170 mm)          # m; a valve or tap is kept this far from a pipe end or another valve/tap
ELBOW_MIN_DEFLECTION = 5.0   # degrees; below this two pipes simply meet
ELBOW_ANGLE_TOLERANCE = 6.0  # degrees; a bend further than this from a catalogue elbow gets no elbow (reported)
PLASTIC = ("pvc", "upvc", "pe", "hdpe", "mdpe", "ppr")
PLASTIC_OD = [16, 20, 25, 32, 40, 50, 63, 75, 90, 110, 125, 140, 160, 180, 200, 225, 250, 280, 315, 355, 400, 450,
              500, 560, 630, 710, 800, 900, 1000]
DN = [50, 63, 80, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600, 700, 800, 900, 1000, 1200, 1400, 1500, 1600]

DEFAULTS: dict[str, Any] = {
    "network": "Water", "parts_list": "Water", "cover": 1.0, "main_min_mm": 160, "line_prefix": "WL",
    "min_line_length": 50.0, "valve_tolerance": 3.0, "tap_tolerance": 1.5, "service_layers": [],
    "valve_layer": None, "hydrant_layer": None, "cad_pipe_layers": [], "profile_view_gap": 30.0,
    "pipe_label_style": "WaterGEMS Pipe", "valve_label_style": "WaterGEMS Valve", "label_height_mm": 1.8,
    "alignment_layer": "WATER-ALIGNMENT", "alignment_label_set": "_No Labels", "profile_view_style": "Profile View",
    "band_set": "auto",       # profile view band set; auto = Standard, else an Elevations band set
}

# --------------------------------------------------------------------------- reading the export


def _num(value: Any) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if math.isfinite(number) else None


STATIC_COLUMNS = ("x", "y", "elevation", "start node", "stop node", "diameter", "material", "length", "is active?")


def _same(a: Any, b: Any) -> bool:
    na, nb = _num(a), _num(b)
    if na is not None and nb is not None:
        return abs(na - nb) <= 1e-6 * max(1.0, abs(na))
    return str(a).strip().casefold() == str(b).strip().casefold()


def _sheet(book: Any, name: str, duplicates: list[str] | None = None) -> dict[str, dict[str, Any]]:
    """First row of every element (by label) of one sheet, with lower-case column names. A label that appears again is
    listed in duplicates (with whether its values differ), never merged silently."""
    if name not in book.sheetnames:
        return {}
    rows = book[name].iter_rows(values_only=True)
    header = next(rows, None)
    if not header:
        return {}
    keys: list[str] = []
    for h in header:
        key = str(h or "").strip().lower()
        keys.append(key if key not in keys else "")      # the first of duplicate columns wins
    out: dict[str, dict[str, Any]] = {}
    reported: set[str] = set()
    for r in rows:
        row = {k: v for k, v in zip(keys, r) if k}
        label = row.get("label")
        if label in (None, ""):
            continue
        if str(label) in out:
            # A WaterGEMS extended-period export repeats every element once per time step with different results;
            # only a row that disagrees on what is drawn (position, elevation, nodes, size, material) is a problem.
            first = out[str(label)]
            clash = sorted(k for k in STATIC_COLUMNS if k in row and k in first and not _same(first[k], row[k]))
            if clash and duplicates is not None and str(label) not in reported:
                reported.add(str(label))
                duplicates.append(f"{name} '{label}' has rows that disagree on {', '.join(clash)}")
            continue
        out[str(label)] = row
    return out


def _active(row: dict[str, Any]) -> bool:
    return str(row.get("is active?", True)).lower() not in ("false", "0")


def read(params: dict[str, Any]) -> dict[str, Any]:
    import openpyxl   # only needed for imports

    path = Path(str(params.get("path") or "")).expanduser()
    if not params.get("path") or not path.is_file():
        raise ValueError(f"path: WaterGEMS workbook not found: {params.get('path')}")
    book = openpyxl.load_workbook(path, read_only=True, data_only=True)
    try:
        duplicates: list[str] = []
        sheets = {n: _sheet(book, n, duplicates) for n in ("Pipe", "Junction", "PRV", "Tank", "Reservoir", "Hydrant")}
    finally:
        book.close()
    if not sheets["Pipe"] or not sheets["Junction"]:
        raise ValueError(f"{path.name}: needs the sheets 'Pipe' and 'Junction' (WaterGEMS full output).")

    nodes: dict[str, dict[str, Any]] = {}
    for label in set(sheets["Junction"]) & (set(sheets["PRV"]) | set(sheets["Tank"]) | set(sheets["Reservoir"]) | set(sheets["Hydrant"])):
        duplicates.append(f"node '{label}' is in more than one sheet")
    kinds = {"Junction": "junction", "PRV": "prv", "Tank": "tank", "Reservoir": "reservoir", "Hydrant": "hydrant"}
    for sheet, kind in kinds.items():
        for label, r in sheets[sheet].items():
            x, y, z = _num(r.get("x")), _num(r.get("y")), _num(r.get("elevation"))
            if x is None or y is None or not _active(r):
                continue
            nodes[label] = {"kind": kind, "x": x, "y": y, "ground": z,
                            "setting_bar": _num(r.get("pressure setting (initial)")) if kind == "prv" else None}
    pipes = []
    for label, r in sheets["Pipe"].items():
        if not _active(r):
            continue
        pipes.append({"label": label, "start": str(r.get("start node") or ""), "stop": str(r.get("stop node") or ""),
                      "material": str(r.get("material") or "").strip() or "?", "diameter_mm": _num(r.get("diameter")),
                      "length": _num(r.get("length"))})
    return {"source": str(path), "nodes": nodes, "pipes": pipes, "duplicates": duplicates}


def nominal_mm(material: str, inner_mm: float, overrides: dict[str, Any] | None = None) -> int:
    """Size the drawing shows: the outside diameter for plastic pipes (99.4 bore -> 110), otherwise the DN."""
    if overrides:
        for key in (f"{material}:{inner_mm:g}", f"{inner_mm:g}"):
            if key in overrides:
                return int(overrides[key])
    if material.lower() in PLASTIC:
        fits = [od for od in PLASTIC_OD if inner_mm < od and inner_mm / od >= 0.7]
        if fits:
            return min(fits)
    return min(DN, key=lambda d: (abs(d - inner_mm), -d))


def validate(nodes: dict[str, Any], pipes: list[dict[str, Any]]) -> tuple[list[str], list[str]]:
    problems, notes = [], []
    labels = Counter(p["label"].casefold() for p in pipes)
    problems += [f"Duplicate pipe label {k}" for k, n in labels.items() if n > 1]
    used = set()
    for p in pipes:
        for end in ("start", "stop"):
            if p[end] not in nodes:
                problems.append(f"{p['label']}: {end} node '{p[end]}' has no X/Y in the export")
            used.add(p[end])
        if p["diameter_mm"] is None or not 10 <= p["diameter_mm"] <= 3000:
            problems.append(f"{p['label']}: diameter {p['diameter_mm']} is not a millimetre value")
        if p["start"] == p["stop"]:
            problems.append(f"{p['label']}: starts and ends at the same node")
    for k, n in nodes.items():
        if n["ground"] is None:
            problems.append(f"{k}: no elevation")
    unused = [k for k in nodes if k not in used]
    if unused:
        notes.append(f"Nodes without pipes, not drawn: {', '.join(unused)}")
    return problems, notes


# --------------------------------------------------------------------------- geometry helpers


def _xy(n: dict[str, Any]) -> tuple[float, float]:
    return n["x"], n["y"]


def _project(p: tuple[float, float], a: tuple[float, float], b: tuple[float, float]) -> tuple[float, float]:
    """(distance from p to segment ab, position t along it)."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length2 = dx * dx + dy * dy
    t = 0.0 if length2 == 0 else max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / length2))
    return math.hypot(a[0] + t * dx - p[0], a[1] + t * dy - p[1]), t


def _bearing(a: tuple[float, float], b: tuple[float, float]) -> float:
    return math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))


CAD_LIMIT = 20000


def _cad_entities(target: Any, layer: str) -> list[dict[str, Any]]:
    reply = call("list_entities", {"layer": layer, "limit": CAD_LIMIT, "target": target})
    if reply.get("truncated"):
        raise ValueError(f"Layer '{layer}' holds more than {CAD_LIMIT} entities; the import would read only part of it. "
                         "Isolate the network's objects on their own layer (or a smaller drawing) first.")
    return reply.get("entities", [])


def _segments(entity: dict[str, Any]) -> list[tuple[tuple[float, float], tuple[float, float]]]:
    """Straight segments of a drawn line or polyline (anything else gives none)."""
    if entity.get("type") not in ("LWPOLYLINE", "POLYLINE", "LINE"):
        return []
    g = entity.get("geometry") or {}
    pts = g.get("vertices") or ([g["start"], g["end"]] if "start" in g and "end" in g else [])
    try:
        pts = [(float(v[0]), float(v[1])) for v in pts]
    except (TypeError, ValueError, IndexError):
        return []
    return list(zip(pts, pts[1:]))


def _centre(entity: dict[str, Any]) -> tuple[float, float] | None:
    g = entity.get("geometry") or {}
    if "at" in g:
        return float(g["at"][0]), float(g["at"][1])
    if g.get("vertices") and g.get("closed"):
        vs = g["vertices"]
        return sum(v[0] for v in vs) / len(vs), sum(v[1] for v in vs) / len(vs)
    return None


# --------------------------------------------------------------------------- the plan


def _catalogue(target: Any, parts_list: str) -> dict[str, list[str]]:
    """The parts of the parts list. (Parts added to a list from the catalogue through the API could not be placed in
    Civil 3D 2027 - 'Fail to add line pipe' - so the import works with what the list already holds.)"""
    out = {}
    for kind in ("Pipe", "Fitting", "Appurtenance"):
        reply = call("list_pressure_part_sizes", {"parts_list": parts_list, "part_type": kind, "limit": 1000, "target": target})
        out[kind] = [s["description"] for s in reply.get("sizes", [])]
    return out


class _Parts:
    """Chooses catalogue parts by their descriptions ('pipe-150 mm-...', 'tee-150 mm x 100 mm-...')."""

    def __init__(self, catalogue: dict[str, list[str]]):
        self.catalogue = catalogue
        self.pipe_dn = sorted({int(m[1]) for d in catalogue["Pipe"] if (m := re.match(r"pipe-(\d+) mm-", d, re.I))})
        if not self.pipe_dn:
            raise ValueError("The pressure parts list has no metric pipes ('pipe-<size> mm-...'). Add pipe sizes to it in Civil 3D.")
        self.used: dict[str, str] = {}      # description -> type

    def _take(self, kind: str, ptype: str, pattern: str) -> str | None:
        rx = re.compile(pattern, re.I)
        hits = sorted(d for d in self.catalogue[kind] if rx.match(d))
        if hits:
            self.used[hits[0]] = ptype
            return hits[0]
        return None

    def stand_in(self, inner_mm: float) -> int:
        """Smallest listed DN that is not smaller than the bore (a stand-in never under-sizes the pipe); the largest
        listed DN when the bore is bigger than all of them (reported as undersized)."""
        bigger = [d for d in self.pipe_dn if d >= inner_mm - 0.5]
        return min(bigger) if bigger else max(self.pipe_dn)

    def pipe(self, dn: int) -> str:
        part = self._take("Pipe", "Pipe", rf"pipe-{dn} mm-")
        assert part
        return part

    def tee(self, run: int, branch: int) -> str | None:
        return self._take("Fitting", "Tee", rf"tee-{run} mm x {branch} mm-")

    def elbows(self, dn: int) -> dict[float, str]:
        out = {}
        for d in self.catalogue["Fitting"]:
            m = re.match(rf"elbow-{dn} mm-([\d.]+) degree", d, re.I)
            if m:
                out[float(m[1])] = d
        return out

    def elbow(self, dn: int, angle: float) -> str:
        part = self.elbows(dn)[angle]
        self.used[part] = "Elbow"
        return part

    def valve(self, dn: int) -> str | None:
        return self._take("Appurtenance", "Valve", rf"gate valve-{dn} mm-")

    def prv(self, dn: int) -> str | None:
        return self._take("Appurtenance", "Valve", rf"(?=.*(pressure[- ]reduc|\bprv\b)).*-{dn} mm")

    def hydrant(self) -> str | None:
        return self._take("Appurtenance", "Hydrant", r".*hydrant")


def _plan(params: dict[str, Any], o: dict[str, Any], target: Any) -> dict[str, Any]:
    data = read(params)
    nodes, pipes = data["nodes"], data["pipes"]
    problems, notes = validate(nodes, pipes)
    problems += [f"Conflicting rows: {d}. Fix the export so each element has one position and size." for d in data.get("duplicates", [])]
    if problems:
        return {"problems": problems, "notes": notes, "source": data["source"]}
    used = {p["start"] for p in pipes} | {p["stop"] for p in pipes}
    nodes = {k: n for k, n in nodes.items() if k in used}
    parts = _Parts(_catalogue(target, o["parts_list"]))
    overrides = o.get("sizes")
    report: dict[str, Any] = {"standIns": Counter(), "notPlaced": [], "fittingsSkipped": [], "drawnModelPipes": 0}

    for p in pipes:
        p["nominal"] = nominal_mm(p["material"], p["diameter_mm"], overrides)
        p["dn"] = parts.stand_in(p["diameter_mm"])
        p["part"] = parts.pipe(p["dn"])
        under = " UNDERSIZED (no larger pipe in the list)" if p["dn"] < p["diameter_mm"] - 0.5 else ""
        report["standIns"][f"{p['material']} Ø{p['nominal']} -> DN{p['dn']}{under}"] += 1

    # Laying depth: each node's pipes meet at one centreline level, set by the largest pipe there - the larger of the
    # real size and the stand-in part that is drawn. The built pipes' real cover is checked after the build.
    biggest: dict[str, float] = defaultdict(float)
    for p in pipes:
        for end in (p["start"], p["stop"]):
            biggest[end] = max(biggest[end], max(p["nominal"], p["dn"] * STAND_IN_OD) / 1000.0)
    for k, n in nodes.items():
        n["z"] = n["ground"] - o["cover"] - biggest[k] / 2

    # Points where a pipe is cut: CAD valves and service taps.
    cuts: dict[str, list[dict[str, Any]]] = defaultdict(list)

    def nearest_pipe(pt: tuple[float, float]) -> tuple[float, float, dict[str, Any]]:
        return min(((*_project(pt, _xy(nodes[p["start"]]), _xy(nodes[p["stop"]])), p) for p in pipes), key=lambda r: r[0])

    def add_cut(p: dict[str, Any], t: float, cut: dict[str, Any]) -> None:
        length = math.dist(_xy(nodes[p["start"]]), _xy(nodes[p["stop"]]))
        if length < 2 * END_CLEARANCE + 0.5:
            report["notPlaced"].append(f"{cut['what']}: pipe {p['label']} is too short to take it")
            return
        lo, hi = END_CLEARANCE / length, 1 - END_CLEARANCE / length
        t = min(max(t, lo), hi)
        if any(abs(t - c["t"]) * length < END_CLEARANCE for c in cuts[p["label"]]):
            report["notPlaced"].append(f"{cut['what']}: too close to another valve/tap on {p['label']}")
            return
        cuts[p["label"]].append({**cut, "t": t})

    xs = [n["x"] for n in nodes.values()]
    ys = [n["y"] for n in nodes.values()]
    near_network = lambda pt: min(xs) - 100 <= pt[0] <= max(xs) + 100 and min(ys) - 100 <= pt[1] <= max(ys) + 100

    if o.get("valve_layer"):
        chambers = [c for e in _cad_entities(target, o["valve_layer"]) if (c := _centre(e)) and near_network(c)]
        for i, c in enumerate(chambers, 1):
            d, t, p = nearest_pipe(c)
            if d > o["valve_tolerance"]:
                report["notPlaced"].append(f"valve chamber {i} at ({c[0]:.1f}, {c[1]:.1f}): no model pipe within {o['valve_tolerance']} m")
                continue
            add_cut(p, t, {"kind": "valve", "what": f"valve {i}", "source": c})

    services: list[dict[str, Any]] = []
    for layer in o.get("service_layers") or []:
        size = int(m[1]) if (m := re.search(r"(\d{2,3})", layer)) else 63
        for e in _cad_entities(target, layer):
            segs = _segments(e)
            if not segs:
                continue
            path = [segs[0][0]] + [s[1] for s in segs]
            middle = ((path[0][0] + path[-1][0]) / 2, (path[0][1] + path[-1][1]) / 2) if len(path) == 2 else path[len(path) // 2]
            if nearest_pipe(middle)[0] < 1.0 and nearest_pipe(path[0])[0] < 1.0 and nearest_pipe(path[-1])[0] < 1.0:
                report["drawnModelPipes"] += 1      # the model's own pipe drawn on a service layer, not a service
                continue
            ends = [(nearest_pipe(path[0]), path), (nearest_pipe(path[-1]), path[::-1])]
            (d, t, host), route = min(ends, key=lambda x: x[0][0])
            if d > o["tap_tolerance"]:
                report["notPlaced"].append(f"service {e['handle']} ({layer}): neither end within {o['tap_tolerance']} m of a model pipe")
                continue
            route = [route[0]] + [v for prev, v in zip(route, route[1:]) if math.dist(prev, v) >= 1.0]
            if len(route) < 2:
                report["notPlaced"].append(f"service {e['handle']} ({layer}): shorter than 1 m")
                continue
            sid = f"S-{len(services) + 1}"
            services.append({"id": sid, "layer": layer, "nominal": size, "route": route, "host": host["label"]})
            add_cut(host, t, {"kind": "tap", "what": f"service {sid}", "service": sid})

    hydrants = []
    if o.get("hydrant_layer"):
        hydrants = [c for e in _cad_entities(target, o["hydrant_layer"]) if (c := _centre(e)) and near_network(c)]

    return {"source": data["source"], "nodes": nodes, "pipes": pipes, "cuts": cuts, "services": services,
            "hydrants": hydrants, "parts": parts, "report": report, "problems": [], "notes": notes}


def _z_along(nodes: dict[str, Any], p: dict[str, Any], t: float) -> float:
    a, b = nodes[p["start"]], nodes[p["stop"]]
    return a["z"] + t * (b["z"] - a["z"])


def _network_ops(plan: dict[str, Any], o: dict[str, Any]) -> tuple[list[dict[str, Any]], dict[str, list[str]]]:
    """pressure_add content, and per WaterGEMS pipe the ids of the pieces it was cut into."""
    nodes, parts, report = plan["nodes"], plan["parts"], plan["report"]
    pipes_out, fittings, appurtenances = [], [], []
    pieces: dict[str, list[str]] = {}
    joins: dict[str, list[tuple[dict[str, Any], tuple[float, float]]]] = defaultdict(list)   # node -> (pipe, far point)

    for p in plan["pipes"]:
        a, b = nodes[p["start"]], nodes[p["stop"]]
        cuts = sorted(plan["cuts"].get(p["label"], []), key=lambda c: c["t"])
        ts = [0.0] + [c["t"] for c in cuts] + [1.0]
        pts = [(a["x"] + t * (b["x"] - a["x"]), a["y"] + t * (b["y"] - a["y"]), _z_along(nodes, p, t)) for t in ts]
        ids = []
        for i in range(len(pts) - 1):
            pid = p["label"] if len(pts) == 2 else f"{p['label']}.{i + 1}"
            length = math.dist(pts[i][:2], pts[i + 1][:2])
            pipes_out.append({"id": pid, "name": pid, "part": p["part"], "start": list(pts[i]), "end": list(pts[i + 1]),
                              "description": f"{p['material'].upper()} Ø{p['nominal']} L={p['length'] or length:.0f}m", "_length": length})
            ids.append(pid)
        pieces[p["label"]] = ids
        joins[p["start"]].append((p, _xy(b)))
        joins[p["stop"]].append((p, _xy(a)))
        for c, at in zip(cuts, pts[1:-1]):
            if c["kind"] == "valve":
                part = parts.valve(p["dn"])
                if part is None:
                    report["notPlaced"].append(f"{c['what']}: the parts list has no gate valve DN{p['dn']}")
                    continue
                appurtenances.append({"id": f"GV:{p['label']}:{len(appurtenances) + 1}", "name": f"GV-{len(appurtenances) + 1}",
                                      "part": part, "at": list(at), "description": f"GATE VALVE Ø{p['nominal']}"})
            else:
                svc = next(s for s in plan["services"] if s["id"] == c["service"])
                branch_dn = parts.stand_in(svc["nominal"])
                tee = parts.tee(p["dn"], branch_dn)
                if tee is None:
                    report["fittingsSkipped"].append(f"{svc['id']}: no tee DN{p['dn']} x DN{branch_dn}")
                else:
                    fittings.append({"id": f"T:{p['label']}:{svc['id']}", "name": f"T-{svc['id']}", "part": tee, "at": list(at),
                                     "description": f"TEE Ø{p['nominal']} x Ø{svc['nominal']}"})
                route = [list(at[:2])] + [list(v) for v in svc["route"][1:]]
                for j, (u, v) in enumerate(zip(route, route[1:]), 1):
                    sid = f"{svc['id']}.{j}" if len(route) > 2 else svc["id"]
                    pipes_out.append({"id": sid, "name": sid, "part": parts.pipe(branch_dn),
                                      "start": [u[0], u[1], at[2]], "end": [v[0], v[1], at[2]],
                                      "description": f"PVC Ø{svc['nominal']} SERVICE"})

    for label, n in nodes.items():
        here = joins[label]
        at = [n["x"], n["y"], n["z"]]
        if n["kind"] == "prv":
            dn = min(p["dn"] for p, _ in here)
            part = parts.prv(dn)
            if part is None and (part := parts.valve(dn)):
                report["standIns"][f"PRV {label} -> gate valve DN{dn} (the parts list has no PRV DN{dn})"] += 1
            if part:
                setting = f" {n['setting_bar']:g} BAR" if n.get("setting_bar") else ""
                appurtenances.append({"id": label, "name": label, "part": part, "at": at, "description": f"PRV{setting} ({label})"})
            else:
                report["notPlaced"].append(f"{label}: the parts list has no valve DN{dn}")
            continue
        if len(here) == 3:
            sizes = sorted((p["dn"] for p, _ in here), reverse=True)
            tee = parts.tee(sizes[0], sizes[2])
            if tee:
                fittings.append({"id": label, "name": label, "part": tee, "at": at, "description": f"TEE ({label})"})
            else:
                report["fittingsSkipped"].append(f"{label}: no tee DN{sizes[0]} x DN{sizes[2]}")
        elif len(here) == 2:
            (p1, f1), (p2, f2) = here
            deflection = abs((_bearing(f1, _xy(n)) - _bearing(_xy(n), f2) + 180) % 360 - 180)
            if deflection < ELBOW_MIN_DEFLECTION:
                continue
            if p1["dn"] != p2["dn"]:
                report["fittingsSkipped"].append(f"{label}: DN{p1['dn']} meets DN{p2['dn']} at {deflection:.0f}° (no reducer in the parts list)")
                continue
            options = parts.elbows(p1["dn"])
            best = min(options, key=lambda a: abs(a - deflection)) if options else None
            if best is None or abs(best - deflection) > ELBOW_ANGLE_TOLERANCE:
                report["fittingsSkipped"].append(f"{label}: {deflection:.0f}° bend has no elbow in the parts list")
                continue
            fittings.append({"id": label, "name": label, "part": parts.elbow(p1["dn"], best), "at": at,
                             "description": f"BEND {best:g}° ({label})"})
        elif len(here) > 3:
            report["fittingsSkipped"].append(f"{label}: {len(here)} pipes meet (no cross fitting placed)")

    labelled = set()
    for label, ids in pieces.items():      # one label per WaterGEMS pipe, on its longest piece
        longest = max((x for x in pipes_out if x["id"] in ids), key=lambda x: x["_length"])
        labelled.add(longest["id"])
    labelled |= {a["id"] for a in appurtenances}
    for x in pipes_out:
        x.pop("_length", None)
    plan["labelled"] = labelled
    for f in fittings + appurtenances:
        f["type"] = parts.used[f["part"]]        # the plugin picks the part among those of this type
    for i, c in enumerate(plan["hydrants"], 1):
        report["notPlaced"].append(f"hydrant {i} at ({c[0]:.1f}, {c[1]:.1f}): the parts list has no hydrant; kept as the CAD block")

    return [{"pipes": pipes_out, "fittings": fittings, "appurtenances": appurtenances}], pieces


def mains(plan: dict[str, Any], o: dict[str, Any]) -> list[dict[str, Any]]:
    """Profile lines along the mains: repeatedly the longest path through the remaining main pipes."""
    nodes = plan["nodes"]
    left = [p for p in plan["pipes"] if p["nominal"] >= o["main_min_mm"]]
    lines = []
    length = lambda p: math.dist(_xy(nodes[p["start"]]), _xy(nodes[p["stop"]]))
    while left:
        adj: dict[str, list[tuple[str, dict[str, Any]]]] = defaultdict(list)
        for p in left:
            adj[p["start"]].append((p["stop"], p))
            adj[p["stop"]].append((p["start"], p))

        # Longest-first spanning tree (Kruskal), then its longest path by two sweeps: linear, even with loops.
        root = {k: k for k in adj}

        def find(k: str) -> str:
            while root[k] != k:
                root[k] = root[root[k]]
                k = root[k]
            return k

        tree: dict[str, list[tuple[str, dict[str, Any]]]] = defaultdict(list)
        for p in sorted(left, key=length, reverse=True):
            a, b = find(p["start"]), find(p["stop"])
            if a != b:
                root[a] = b
                tree[p["start"]].append((p["stop"], p))
                tree[p["stop"]].append((p["start"], p))

        def farthest(start: str) -> tuple[float, list[str], list[dict[str, Any]]]:
            dist, back = {start: 0.0}, {start: None}
            todo = [start]
            while todo:
                node = todo.pop()
                for nxt, p in tree[node]:
                    if nxt not in dist:
                        dist[nxt], back[nxt] = dist[node] + length(p), (node, p)
                        todo.append(nxt)
            end = max(dist, key=lambda k: dist[k])
            path, used = [end], []
            while back[path[-1]]:
                prev, p = back[path[-1]]
                path.append(prev)
                used.append(p)
            return dist[end], path[::-1], used[::-1]

        component_best = []
        seen: set[str] = set()
        for k in adj:                      # one diameter per connected part; take the longest
            if find(k) in seen:
                continue
            seen.add(find(k))
            far = farthest(k)[1][-1]
            component_best.append(farthest(far))
        dist, path, used = max(component_best, key=lambda r: r[0])
        if not used:
            break
        left = [p for p in left if p not in used]
        if dist >= o["min_line_length"]:
            lines.append({"name": f"{o['line_prefix']}{len(lines) + 1}", "nodes": path, "pipes": used, "length": dist})
    return lines


def compare(plan: dict[str, Any], target: Any, layers: list[str]) -> dict[str, Any]:
    """Where the model and the drawn pipes disagree (route more than 1 m apart, or a different size)."""
    nodes = plan["nodes"]
    segs = [(layer, a, b) for layer in layers for e in _cad_entities(target, layer) for a, b in _segments(e)]
    if not segs:
        return {"note": "no drawn pipes found on " + ", ".join(layers)}
    routes, sizes = [], []
    for p in plan["pipes"]:
        a, b = _xy(nodes[p["start"]]), _xy(nodes[p["stop"]])
        far, layer_votes = 0.0, Counter()
        for t in (0.1, 0.5, 0.9):
            q = (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]))
            d, layer = min(((_project(q, s[1], s[2])[0], s[0]) for s in segs), key=lambda r: r[0])
            far = max(far, d)
            layer_votes[layer] += 1
        layer = layer_votes.most_common(1)[0][0]
        if far > 1.0:
            routes.append(f"{p['label']} ({p['material']} Ø{p['nominal']}): up to {far:.1f} m from the drawn pipe")
        elif (m := re.search(r"(\d{2,4})", layer)) and int(m[1]) != p["nominal"]:
            sizes.append(f"{p['label']}: model Ø{p['nominal']}, drawn on '{layer}'")
    verts = [v for _, a, b in segs for v in (a, b)]
    off = [f"{k}: {d:.1f} m from the nearest drawn pipe vertex" for k, n in nodes.items()
           if (d := min(math.dist(_xy(n), v) for v in verts)) > 1.0]
    return {"pipesCompared": len(plan["pipes"]), "routeDifferences": routes, "sizeDifferences": sizes, "nodesOffDrawing": off}


# --------------------------------------------------------------------------- actions


def _options(params: dict[str, Any]) -> dict[str, Any]:
    o = {**DEFAULTS, **{k: v for k, v in params.items() if v is not None}}
    for key in ("cover", "main_min_mm", "min_line_length", "valve_tolerance", "tap_tolerance", "profile_view_gap"):
        value = o[key]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            raise ValueError(f"'{key}' must be a non-negative number.")
    for key in ("service_layers", "cad_pipe_layers"):
        if not isinstance(o[key], list) or not all(isinstance(x, str) for x in o[key]):
            raise ValueError(f"'{key}' must be a list of layer names.")
    return o


def _summary(plan: dict[str, Any], o: dict[str, Any]) -> dict[str, Any]:
    pipes, rep = plan["pipes"], plan["report"]
    return {
        "source": plan["source"],
        "pipes": len(pipes), "nodes": len(plan["nodes"]),
        "prvs": [k for k, n in plan["nodes"].items() if n["kind"] == "prv"],
        "lengthByMaterialAndSize": {f"{m} Ø{s}": round(sum(p["length"] or 0 for p in pipes if p["material"] == m and p["nominal"] == s), 1)
                                    for m, s in sorted({(p["material"], p["nominal"]) for p in pipes})},
        "standIns": dict(rep["standIns"]),
        "valvesFromCad": sum(1 for cs in plan["cuts"].values() for c in cs if c["kind"] == "valve"),
        "servicesFromCad": len(plan["services"]),
        "serviceLayerLinesThatAreModelPipes": rep["drawnModelPipes"],
        "hydrantsInCad": len(plan["hydrants"]),
        "notPlaced": rep["notPlaced"], "fittingsSkipped": rep["fittingsSkipped"],
        "cover": o["cover"], "notes": plan["notes"],
    }


def check(params: dict[str, Any]) -> dict[str, Any]:
    o = _options(params)
    target = drawing_target(params) if params.get("target") else call("get_drawing_info")["target"]
    plan = _plan(params, o, target)
    if plan["problems"]:
        return {"ready": False, "problems": plan["problems"], "notes": plan["notes"]}
    _network_ops(plan, o)        # fills the report (fittings that cannot be placed)
    lines = mains(plan, o)
    out = _summary(plan, o)
    out["mains"] = [{"name": l["name"], "pipes": len(l["pipes"]), "length": round(l["length"], 1),
                     "from": l["nodes"][0], "to": l["nodes"][-1]} for l in lines]
    if o["cad_pipe_layers"]:
        out["differences"] = compare(plan, target, o["cad_pipe_layers"])
    out["ready"] = True
    return out


def _stations(nodes: dict[str, Any], chain: list[str]) -> list[float]:
    out = [0.0]
    for a, b in zip(chain, chain[1:]):
        out.append(out[-1] + math.dist(_xy(nodes[a]), _xy(nodes[b])))
    return out


def _edit(target: Any, request_id: str, ops: list[dict[str, Any]], phase: str, dry_run: bool = False) -> dict[str, Any]:
    if len(ops) > MAX_BATCH_OPERATIONS:
        raise ValueError(f"WaterGEMS {phase} exceeds {MAX_BATCH_OPERATIONS} operations.")
    # A retry with the same request_id reaches the bridge, which returns the recorded result only when the operations
    # (and dry_run) are identical and refuses a changed request.
    return call("edit", {"operations": ops, "target": target, "request_id": request_id, "dry_run": dry_run}, timeout=EDIT_TIMEOUT)


def _cover_check(target: Any, network: str, wanted: float, plan: dict[str, Any], pieces: dict[str, list[str]]) -> dict[str, Any]:
    """Cover of the pipes as built in Civil 3D: the real part's outside diameter and end elevations against the ground,
    interpolated between the WaterGEMS node elevations of the pipe it belongs to."""
    try:
        reply = call("list_pressure_pipes", {"network_name": network, "limit": 5000, "target": target})
    except Exception as exc:     # noqa: BLE001 - a check, never a reason to fail the import
        return {"error": f"could not read the built pipes: {exc}"}
    nodes = plan["nodes"]
    parent = {pid: p for p in plan["pipes"] for pid in pieces.get(p["label"], [])}
    covers = []
    for r in reply.get("pipes", []):
        p = parent.get(r.get("name"))
        od = r.get("outerDiameter")
        if not p or not isinstance(od, (int, float)):
            continue
        a, b = nodes[p["start"]], nodes[p["stop"]]
        for pt, z in ((r.get("start"), r.get("startElevation")), (r.get("end"), r.get("endElevation"))):
            if not pt or not isinstance(z, (int, float)):
                continue
            _, t = _project((pt[0], pt[1]), _xy(a), _xy(b))
            ground = a["ground"] + t * (b["ground"] - a["ground"])
            covers.append((ground - (z + od / 2), r["name"]))
    if not covers:
        return {"note": "no built pipes could be checked"}
    worst: dict[str, float] = {}
    for c, n in covers:
        worst[n] = min(c, worst.get(n, c))
    low = sorted((c, n) for n, c in worst.items() if c < wanted - 0.005)
    return {"wanted": wanted, "min": round(min(worst.values()), 3), "pipesChecked": len(worst),
            "belowWantedCount": len(low), "belowWanted": [f"{n}: {c:.3f} m" for c, n in low[:50]],
            "basis": "built part outside diameter and elevations vs WaterGEMS ground, at both ends of every piece",
            **({"truncated": True} if reply.get("truncated") else {})}


def build(params: dict[str, Any]) -> dict[str, Any]:
    target = drawing_target(params)
    request_id = edit_request_id(params)
    o = _options(params)
    dry_run = params.get("dry_run", False)
    if not isinstance(dry_run, bool):
        raise ValueError("'dry_run' must be true or false.")
    at = params.get("profile_views_at")
    if at is not None and not (isinstance(at, (list, tuple)) and len(at) == 2
                               and all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in at)):
        raise ValueError("profile_views_at must be [x, y] in drawing units.")
    if call("get_drawing_info", {"target": target})["civilDrawingUnits"] != "Meters":
        raise ValueError("WaterGEMS import requires a Civil 3D drawing set to metres.")
    plan = _plan(params, o, target)
    if plan["problems"]:
        return {"built": False, "problems": plan["problems"], "note": "Fix these in WaterGEMS (or the export) first."}
    content, pieces = _network_ops(plan, o)
    differences = compare(plan, target, o["cad_pipe_layers"]) if o["cad_pipe_layers"] else None   # before the network exists
    net = o["network"]
    h = float(o["label_height_mm"])
    core_ops: list[dict[str, Any]] = [
        {"op": "pressure_network_create", "name": net, "parts_list": o["parts_list"],
         "description": f"From WaterGEMS: {Path(plan['source']).name}"},
        {"op": "pressure_add", "network": net, **content[0]},
        {"op": "label_style_create", "kind": "pressure_pipe_label", "name": o["pipe_label_style"], "if_exists": "replace",
         "components": [{"name": "Size", "text": "<[Description(CP)]>", "height": h, "attachment": "BottomCenter", "y_offset": 1.0}]},
        {"op": "label_style_create", "kind": "pressure_appurtenance_label", "name": o["valve_label_style"], "if_exists": "replace",
         "components": [{"name": "Name", "text": "<[Description(CP)]>", "height": h, "attachment": "BottomLeft", "x_offset": 2.0, "y_offset": 2.0}]},
    ]
    core = _edit(target, request_id, core_ops, "network", dry_run)
    summary = _summary(plan, o)
    if not core.get("applied"):
        return {"built": False, "dryRun": dry_run, "networkCommitted": False, "target": target, "core": core,
                **({"plan": summary} if dry_run else {})}
    # From here on the network exists in the drawing: every later failure is reported with that fact.
    stages: dict[str, Any] = {"network": "committed"}

    created = next((r["result"]["created"] for r in core["results"] if r["op"] == "pressure_add"), [])
    handle = {c["id"]: c["handle"] for c in created if c.get("id")}
    lines = mains(plan, o)
    nodes = plan["nodes"]
    label_ops = [{"op": "pressure_labels_add", "pressure_network": net, "fittings": False,
                  "pipe_style": o["pipe_label_style"], "appurtenance_style": o["valve_label_style"],
                  "handles": [handle[i] for i in plan["labelled"] if i in handle]}]
    origin = params.get("profile_views_at") or [min(n["x"] for n in nodes.values()), min(n["y"] for n in nodes.values()) - 50.0]
    profile_ops: list[dict[str, Any]] = label_ops + [{"op": "layer_create", "name": o["alignment_layer"], "on": False, "if_exists": "skip"}]
    for i, line in enumerate(lines):
        chain, name = line["nodes"], line["name"]
        st = _stations(nodes, chain)
        part_handles = [handle[pid] for p in line["pipes"] for pid in pieces[p["label"]] if pid in handle]
        part_handles += [handle[k] for k in chain if k in handle]                   # tees, bends, PRVs on the line
        part_handles += [h for pid, h in handle.items() if pid.split(":")[1:2] and pid.split(":")[1] in {p["label"] for p in line["pipes"]}]
        profile_ops += [
            {"op": "alignment_create", "name": name, "points": [[nodes[k]["x"], nodes[k]["y"]] for k in chain],
             "description": f"Water main {chain[0]} to {chain[-1]}", "layer": o["alignment_layer"]},
            {"op": "profile_create", "alignment": name, "name": f"{name} - Ground", "pvis": [[s, nodes[k]["ground"]] for s, k in zip(st, chain)]},
            {"op": "profile_view_create", "alignment": name, "name": f"{name} - Profile", "at": [origin[0], origin[1] - i * 10.0],
             **({"style": o["profile_view_style"]} if o["profile_view_style"] else {}),
             **({"band_set": o["band_set"]} if o.get("band_set") else {})},
            {"op": "profile_view_add_parts", "profile_view": f"{name} - Profile", "handles": part_handles},
        ]
    if lines:
        profile_ops.append({"op": "alignment_label_set", "alignments": [l["name"] for l in lines],
                            "label_set": o["alignment_label_set"], "layer": o["alignment_layer"]})
    profiles: dict[str, Any] = {}
    layout: dict[str, Any] = {"applied": True}

    def failed(stage: str, reply: dict[str, Any]) -> str:
        bad = [f"{r.get('op')}: {r.get('error')}" for r in reply.get("results", []) if r.get("error")]
        return "; ".join(bad) or str(reply.get("error") or "rolled back")

    try:
        profiles = _edit(target, str(uuid5(UUID(request_id), "watergems-profiles")), profile_ops, "labels and profiles")
        stages["labels and profiles"] = "committed" if profiles.get("applied") else "rolled back: " + failed("profiles", profiles)
        if lines:
            if profiles.get("applied"):
                views = {v["name"]: v for v in call("list_profile_views", {"target": target})["profileViews"]}
                moves, top = [], origin[1]
                for line in lines:
                    v = views.get(f"{line['name']} - Profile")
                    if not v or not isinstance(v.get("extents"), dict):
                        continue
                    (x0, y0, _), (x1, y1, _) = v["extents"]["min"], v["extents"]["max"]
                    dx, dy = origin[0] - x0, top - y1
                    if abs(dx) > 1e-6 or abs(dy) > 1e-6:
                        moves.append({"op": "entity_move", "handles": [v["handle"]], "dx": dx, "dy": dy})
                    top -= y1 - y0 + o["profile_view_gap"]
                if moves:
                    layout = _edit(target, str(uuid5(UUID(request_id), "watergems-layout")), moves, "profile layout")
                    stages["profile layout"] = "committed" if layout.get("applied") else "rolled back: " + failed("layout", layout)
    except Exception as exc:       # noqa: BLE001 - whatever failed, say that the network is already in the drawing
        stages.setdefault("labels and profiles", f"failed: {type(exc).__name__}: {exc}")
        return {"built": False, "network": net, "networkCommitted": True, "target": target, "request_id": request_id,
                "stages": stages, "error": f"{type(exc).__name__}: {exc}",
                "note": f"The pressure network '{net}' was created and stays in the drawing. Fix the cause and finish the "
                        "labels/profiles by hand, or erase the network (pressure_network_erase) and import again with a new request_id.",
                "plan": summary}

    reply = {
        "built": bool(profiles.get("applied") and layout.get("applied")), "network": net, "networkCommitted": True,
        "target": target, "request_id": request_id, "stages": stages,
        **summary,
        "coverCheck": _cover_check(target, net, o["cover"], plan, pieces),
        "partsPlaced": {k: sum(1 for c in created if c["kind"] == k) for k in ("pipe", "fitting", "appurtenance")},
        "connections": next((r["result"].get("connections") for r in core["results"] if r["op"] == "pressure_add"), None),
        "mains": [{"name": l["name"], "pipes": len(l["pipes"]), "length": round(l["length"], 1)} for l in lines],
        "warnings": core.get("warnings", []) + profiles.get("warnings", []) + layout.get("warnings", []),
    }
    if differences is not None:
        reply["differences"] = differences
    return reply
