"""Stand-in for the Civil 3D plugin, with canned data, so the MCP server can be tested without Civil 3D.

    python fake_bridge.py [path-to-bridge.json]

Point the MCP server at the same file with the C3D_BRIDGE_FILE environment variable. Speaks the same
newline-delimited JSON protocol as the real plugin, including the token check and the one-time edit permission
(FAKE_EDIT=allow grants it, FAKE_EDIT=decline refuses; default allow).
"""
from __future__ import annotations

import json
import os
import secrets
import socketserver
import sys
from uuid import uuid4
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
import catalog  # noqa: E402

TOKEN = secrets.token_hex(16)
STATE = {"editingAllowed": False, "edits": 0}
TARGET = {"session_id": str(uuid4()), "drawing_id": str(uuid4())}
EDIT_REQUESTS: dict[str, tuple[dict, dict]] = {}

LAYERS = [
    {"name": "0", "on": True, "frozen": False, "locked": False, "plot": True, "color": "7", "xref": False},
    {"name": "Defpoints", "on": True, "frozen": False, "locked": False, "plot": False, "color": "7", "xref": False},
    {"name": "C-ROAD-CL", "on": True, "frozen": False, "locked": False, "plot": True, "color": "1", "xref": False},
    {"name": "C-TOPO-SURF", "on": True, "frozen": False, "locked": False, "plot": True, "color": "3", "xref": False},
    {"name": "layer1", "on": True, "frozen": False, "locked": False, "plot": True, "color": "7", "xref": False},
    {"name": "Arch|A-WALL", "on": True, "frozen": False, "locked": True, "plot": True, "color": "2", "xref": True},
]

SURFACES = [
    {"name": "EG-Ground", "type": "TinSurface", "style": "Contours 1m and 5m", "layer": "C-TOPO-SURF",
     "description": "", "minElevation": 12.4, "maxElevation": 31.9, "meanElevation": 20.1},
    {"name": "Surface1", "type": "TinSurface", "style": "_No Display", "layer": "0",
     "description": "", "minElevation": 11.0, "maxElevation": 30.0, "meanElevation": 19.0},
]
ALIGNMENTS = [
    {"name": "RD-01", "type": "Centerline", "site": "", "style": "Proposed", "layer": "C-ROAD-CL",
     "length": 1250.0, "startStation": 0.0, "endStation": 1250.0, "profileCount": 2},
    {"name": "Alignment - (2)", "type": "Offset", "site": "", "style": "Proposed", "layer": "C-ROAD-CL",
     "length": 1250.0, "startStation": 0.0, "endStation": 1250.0, "profileCount": 0},
]
NETWORKS = [{"name": "SS-Main", "pipeCount": 2, "structureCount": 3},
            {"name": "Pipe Network - (1)", "pipeCount": 0, "structureCount": 0}]
PIPES = [
    {"network": "SS-Main", "name": "Pipe - (1)", "innerDiameterOrWidth": 0.3, "slope": 0.005,
     "length3d": 42.1, "start": [0.0, 0.0, 10.0], "end": [42.0, 0.0, 9.79]},
    {"network": "SS-Main", "name": "Pipe - (2)", "innerDiameterOrWidth": 0.3, "slope": 0.004,
     "length3d": 38.0, "start": [42.0, 0.0, 9.79], "end": [80.0, 0.0, 9.64]},
]


PRESSURE_NETWORKS = [{"name": "PN-TEST", "handle": "3F1", "partsList": "Water", "referenceSurface": "TEST-EG",
                      "referenceAlignment": "", "pipeCount": 2, "fittingCount": 1, "appurtenanceCount": 0,
                      "minElevation": 7.1, "maxElevation": 12.4}]
PRESSURE_PIPES = [
    {"network": "PN-TEST", "name": "Pipe - (1)", "partFamily": "pipe-ductile iron", "description": "DN150",
     "nominalDiameter": 0.15, "innerDiameter": 0.159, "outerDiameter": 0.17, "wallThickness": 0.0055,
     "isCurve": False, "length2d": 49.497, "length3d": 49.6, "slope": -0.01, "startElevation": 8.0,
     "endElevation": 7.5, "minCover": 3.0, "maxCover": 3.1, "start": [10.0, 90.0, 8.0], "end": [45.0, 55.0, 7.5]},
    {"network": "PN-TEST", "name": "Pipe - (2)", "partFamily": "pipe-ductile iron", "description": "DN150",
     "nominalDiameter": 0.15, "innerDiameter": 0.159, "outerDiameter": 0.17, "wallThickness": 0.0055,
     "isCurve": False, "length2d": 57.009, "length3d": 57.1, "slope": -0.02, "startElevation": 7.5,
     "endElevation": 7.1, "minCover": 3.0, "maxCover": 3.2, "start": [45.0, 55.0, 7.5], "end": [90.0, 20.0, 7.1]},
]
PRESSURE_FITTINGS = [{"network": "PN-TEST", "name": "Bend - (1)", "partFamily": "bend-ductile iron",
                      "description": "DN150 bend", "partType": "Bend", "connections": 2,
                      "horizontalAngleRad": 0.0, "location": [45.0, 55.0, 7.5]}]


def handle(method: str, params: dict):
    limit = int(params.get("limit", 500))
    if method == "ping":
        return {"bridgeVersion": "fake", "acadVersion": "26.0s (fake)", "product": "Civil 3D (fake)",
                "activeDrawing": r"C:\Projects\demo\DEMO.dwg", "editingAllowed": STATE["editingAllowed"]}
    if method == "get_drawing_info":
        return {"name": "DEMO.dwg", "path": r"C:\Projects\demo\DEMO.dwg", "readOnly": False,
                "insertionUnits": "Meters", "civilDrawingUnits": "Meters", "layerCount": len(LAYERS), "modelSpaceEntityCount": 1234,
                "civil": {"surfaces": len(SURFACES), "alignments": len(ALIGNMENTS),
                          "pipeNetworks": len(NETWORKS), "sites": 0}}
    if method == "list_layers":
        rows = LAYERS[:limit]
        return {"total": len(LAYERS), "returned": len(rows), "truncated": len(LAYERS) > len(rows), "layers": rows}
    if method == "count_entities":
        if params.get("group_by", "type") == "layer":
            groups = [{"key": "C-ROAD-CL", "count": 900}, {"key": "C-TOPO-SURF", "count": 334}]
        else:
            groups = [{"key": "LWPOLYLINE", "count": 900}, {"key": "AECC_TIN_SURFACE", "count": 2}]
        return {"groupBy": params.get("group_by", "type"), "layerFilter": params.get("layer"),
                "total": 1234, "groups": groups, "groupsTruncated": False}
    if method == "list_surfaces":
        return {"count": len(SURFACES), "surfaces": SURFACES}
    if method == "list_alignments":
        return {"count": len(ALIGNMENTS), "alignments": ALIGNMENTS}
    if method == "list_pipe_networks":
        return {"count": len(NETWORKS), "pipeNetworks": NETWORKS}
    if method == "list_pipes":
        name = params.get("network_name")
        rows = [p for p in PIPES if name is None or p["network"].lower() == name.lower()][:limit]
        return {"total": len(rows), "returned": len(rows), "truncated": False, "pipes": rows}
    if method == "list_pressure_networks":
        return {"count": len(PRESSURE_NETWORKS), "pressureNetworks": PRESSURE_NETWORKS}
    if method == "list_pressure_pipes":
        name = params.get("network_name")
        rows = [p for p in PRESSURE_PIPES if name is None or p["network"].lower() == name.lower()][:limit]
        return {"total": len(rows), "returned": len(rows), "truncated": False, "pipes": rows}
    if method == "list_pressure_fittings":
        name = params.get("network_name")
        rows = [p for p in PRESSURE_FITTINGS if name is None or p["network"].lower() == name.lower()][:limit]
        return {"total": len(rows), "returned": len(rows), "truncated": False, "fittings": rows}
    if method == "get_surface_elevation":
        return {"surface": params["surface_name"], "x": params["x"], "y": params["y"], "elevation": 12.5}
    if method == "alignment_station_to_point":
        return {"alignment": params["alignment_name"], "station": params["station"],
                "offset": params.get("offset", 0), "easting": 100.0, "northing": 200.0, "stationRange": [0, 117.2]}
    if method == "alignment_point_to_station":
        return {"alignment": params["alignment_name"], "x": params["x"], "y": params["y"], "station": 42.0, "offset": -1.5}
    if method == "list_profiles":
        return {"count": 1, "profiles": [{"alignment": "RD-01", "name": "EG", "type": "EG",
                "startStation": 0, "endStation": 117.2, "minElevation": 10, "maxElevation": 15}]}
    if method == "get_profile_elevation":
        return {"alignment": params["alignment_name"], "profile": params["profile_name"],
                "station": params["station"], "elevation": 11.0, "stationRange": [0, 117.2]}
    if method == "list_corridors":
        return {"count": 1, "corridors": [{"name": "COR-1", "baselineCount": 1, "baselines": [{"name": "B1", "regionCount": 1}]}]}
    if method == "describe_api":
        return {"type": params["type_name"], "methods": ["static ObjectId Create(Database database, String name)"], "properties": [], "constructors": []}
    if method == "get_selection":
        return {"count": 1, "returned": 1, "truncated": False,
                "objects": [{"handle": "2A4F", "type": "AECC_ALIGNMENT", "layer": "C-ROAD-CL", "civilName": "RD-01"}]}
    if method == "list_actions":
        return {"bridgeVersion": "fake",
                "actions": [{"name": n, "kind": "read"} for n in catalog.READ_ACTIONS] + [{"name": "edit", "kind": "write"}],
                "editOperations": sorted(catalog.EDIT_OPS), "editPermission": {"editingAllowed": STATE["editingAllowed"]}}
    if method == "get_edit_permission":
        return {"editingAllowed": STATE["editingAllowed"], "appliedEdits": STATE["edits"]}
    if method == "get_edit_status":
        request_id = params["request_id"]
        entry = EDIT_REQUESTS.get(request_id)
        if entry is None:
            return {"request_id": request_id, "state": "unknown"}
        result = entry[1]
        return {"request_id": request_id, "state": "completed", "applied": result["applied"], "result": result}
    if method == "edit":
        request_id = params["request_id"]
        if request_id in EDIT_REQUESTS:
            previous, result = EDIT_REQUESTS[request_id]
            if previous != params:
                raise ValueError("This request_id already identifies a different edit.")
            return result
        ops = params.get("operations") or []
        for i, op in enumerate(ops, 1):
            if op.get("op") not in catalog.EDIT_OPS:
                raise ValueError(f"operation {i}: unknown op '{op.get('op')}'")
        if not STATE["editingAllowed"]:
            if os.environ.get("FAKE_EDIT", "allow") != "allow":
                result = {"applied": False, "permission": "not granted", "reason": "The user declined editing in Civil 3D."}
                EDIT_REQUESTS[request_id] = params, result
                return result
            STATE["editingAllowed"] = True   # the one-time dialog answered Yes
        dry = bool(params.get("dry_run"))
        if not dry:
            STATE["edits"] += 1
        result = {"applied": not dry, "dryRun": dry, "operations": len(ops), "warnings": [],
                  "results": [{"index": i, "op": op["op"], "result": {}} for i, op in enumerate(ops, 1)]}
        EDIT_REQUESTS[request_id] = params, result
        return result
    if method in catalog.READ_ACTIONS:
        return {"note": f"fake bridge has no canned data for {method}"}
    raise ValueError(f"unknown method '{method}'")



class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        for raw in self.rfile:
            req = json.loads(raw)
            if req.get("token") != TOKEN:
                reply = {"id": req.get("id"), "ok": False, "error": "unauthorized"}
            else:
                try:
                    method, params = req.get("method", ""), req.get("params") or {}
                    if (method in ("edit", "get_edit_status") or "target" in params) and params.get("target") != TARGET:
                        raise ValueError("The active drawing is different from the request target.")
                    reply = {"id": req.get("id"), "ok": True,
                             "result": handle(method, params),
                             "target": TARGET if method not in ("edit", "get_edit_status") else None}
                except Exception as exc:  # mirror the plugin: errors go back as ok=false
                    reply = {"id": req.get("id"), "ok": False, "error": f"{type(exc).__name__}: {exc}"}
            self.wfile.write((json.dumps(reply) + "\n").encode("utf-8"))


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main() -> None:
    target = Path(sys.argv[1] if len(sys.argv) > 1 else os.environ.get("C3D_BRIDGE_FILE", "bridge.json"))
    with Server(("127.0.0.1", 0), Handler) as srv:
        port = srv.server_address[1]
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps({"port": port, "token": TOKEN, "pid": os.getpid(),
                                      "session_id": TARGET["session_id"]}), encoding="utf-8")
        print(f"fake bridge on 127.0.0.1:{port}, connection file {target}", flush=True)
        try:
            srv.serve_forever()
        finally:
            target.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
