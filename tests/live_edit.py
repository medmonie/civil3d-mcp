r"""Exercises the edit operations against the LIVE Civil 3D, one small batch per stage, and prints every result.

SAFETY: refuses to run unless the active drawing is an unsaved 'DrawingN.dwg' or its name contains 'mcp_test'.
The first edit shows the one-time permission dialog in Civil 3D (or run MCPALLOW first).

    python live_edit.py [stage ...]      stages: see STAGES below; default = all except 'cleanup'
Started by run_live_edit.bat, which saves the output to results\live_edit.txt."""
import json
import re
import sys
from pathlib import Path
from uuid import uuid4

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS = {}
TARGET = None


def show(title, out):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < 5000 else text[:5000] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, params=None):
    try:
        return show(method, call(method, {**(params or {}), **({"target": TARGET} if TARGET else {})}))
    except Exception as exc:
        print(f"===== {method}\nFAILED: {exc}", flush=True)
        return None


def edit(title, ops, dry_run=False, expect=True):
    """expect: True = must apply, False = must be rejected/rolled back."""
    try:
        out = call("edit", {"operations": ops, "dry_run": dry_run, "target": TARGET, "request_id": str(uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:
        out = {"applied": False, "exception": str(exc)}
    show(("DRY " if dry_run else "") + "edit: " + title, out)
    ok = (out.get("applied") is True or (dry_run and out.get("dryRun") is True and "failedAt" not in out)) == expect
    RESULTS[title + (" (dry)" if dry_run else "")] = "PASS" if ok else "FAIL"
    return out


def created_handles(out, kind=None):
    hs = []
    for r in (out or {}).get("results", []):
        res = r.get("result") or {}
        if "handle" in res and kind is None:
            hs.append(res["handle"])
        for c in res.get("created", []):
            if kind is None or c.get("kind") == kind:
                hs.append(c["handle"])
    return hs


def op_result(out, index):
    """The result of operation number 'index' (0-based) of an edit reply; {} when it is missing."""
    results = (out or {}).get("results") or []
    return (results[index].get("result") or {}) if index < len(results) else {}


def check(title, condition):
    RESULTS[title] = "PASS" if condition else "FAIL"
    print(f"----- check: {title}: {RESULTS[title]}", flush=True)


# ----------------------------------------------------------------------------- stages

def st_dryrun():
    edit("dry run layer", [{"op": "layer_create", "name": "C-MCP-DRY", "color": 1}], dry_run=True)
    layers = call("list_layers", {"limit": 20000})["layers"]
    check("dry run left no layer behind", not any(l["name"] == "C-MCP-DRY" for l in layers))
    edit("failing batch is rolled back", [{"op": "layer_create", "name": "C-MCP-ROLLBACK"},
                                           {"op": "entity_erase", "handles": ["FFFFFFF"]}], expect=False)
    layers = call("list_layers", {"limit": 20000})["layers"]
    check("failed batch left no layer behind", not any(l["name"] == "C-MCP-ROLLBACK" for l in layers))


def st_layers():
    edit("layers", [{"op": "layer_create", "name": "C-MCP-TEST", "color": 3},
                    {"op": "layer_create", "name": "C-MCP-TEST", "if_exists": "skip"},
                    {"op": "layer_create", "name": "C-MCP-TMP", "color": 1},
                    {"op": "layer_set", "name": "C-MCP-TMP", "new_name": "C-MCP-PIPE", "color": 5, "plot": False}])


def st_draft():
    out = edit("drafting", [
        {"op": "draw_polyline", "points": [[0, 0], [100, 0], [100, 100], [0, 100]], "closed": True, "layer": "C-MCP-TEST"},
        {"op": "draw_polyline", "points": [[0, 0, 1], [50, 50, 2], [100, 100, 4]], "layer": "C-MCP-TEST"},
        {"op": "draw_line", "start": [0, 100, 0], "end": [100, 0, 0], "layer": "C-MCP-TEST"},
        {"op": "draw_point", "at": [50, 50, 0]},
        {"op": "draw_text", "text": "MCP edit test", "at": [5, 105, 0], "height": 3}])
    hs = created_handles(out)
    if hs:
        edit("entity set + move", [{"op": "entity_set", "handles": hs[-2:], "layer": "C-MCP-TEST", "color": 2},
                                   {"op": "entity_move", "handles": [hs[-1]], "dy": 5}])
        read("get_objects", {"handles": hs})
        edit("entity erase", [{"op": "entity_erase", "handles": [hs[2]]}])


def st_cogo():
    out = edit("cogo add", [{"op": "cogo_points_add", "points": [
        {"x": 10, "y": 10, "z": 5.0, "description": "MCP", "name": "MCP-1"},
        {"x": 90, "y": 10, "z": 5.5, "description": "MCP"},
        {"x": 90, "y": 90, "z": 6.0}]}])
    res = ((out or {}).get("results") or [{}])[0].get("result") or {}
    if "firstNumber" in res:
        edit("cogo set + remove", [{"op": "cogo_point_set", "number": res["firstNumber"], "z": 7.25, "description": "MCP moved", "x": 12},
                                   {"op": "cogo_points_remove", "numbers": [res["lastNumber"]]}])
    read("list_cogo_points", {"limit": 10})


def st_surface():
    pts = [[x, y, 10 + 0.02 * x + 0.01 * y] for x in range(0, 101, 20) for y in range(0, 101, 20)]
    edit("surface create", [{"op": "surface_create", "name": "MCP-EG", "points": pts, "description": "MCP test surface"}])
    edit("surface add points + rebuild", [{"op": "surface_add_points", "name": "MCP-EG", "points": [[50, 50, 13.0], [120, 50, 13.0]]},
                                          {"op": "surface_rebuild", "name": "MCP-EG"}])
    read("list_surfaces")
    el = read("get_surface_elevation", {"surface_name": "MCP-EG", "x": 30, "y": 30})
    check("surface elevation at (30,30) = 10.9", bool(el) and abs((el.get("elevation") or 0) - 10.9) < 0.01)


def st_alignment():
    edit("alignment create", [{"op": "alignment_create", "name": "MCP-AL", "points": [[5, 10], [55, 10], [95, 60]], "radius": 30,
                               "description": "MCP test alignment"},
                              {"op": "alignment_create", "name": "MCP-AL2", "points": [[5, 90], [95, 90]]}])
    read("list_alignments")
    read("alignment_station_to_point", {"alignment_name": "MCP-AL", "station": 20, "offset": 0})
    edit("object_set rename", [{"op": "object_set", "kind": "alignment", "name": "MCP-AL2", "new_name": "MCP-AL-B", "description": "renamed"}])


def st_profile():
    edit("profile from surface", [{"op": "profile_from_surface", "alignment": "MCP-AL", "surface": "MCP-EG", "name": "MCP-EGP"}])
    edit("profile layout", [{"op": "profile_create", "alignment": "MCP-AL", "name": "MCP-FG", "pvis": [[0, 9.0], [40, 9.6], [90, 9.2]]}])
    read("list_profiles", {"alignment_name": "MCP-AL"})
    read("get_profile_elevation", {"alignment_name": "MCP-AL", "profile_name": "MCP-FG", "station": 20})


def pressure_list():
    lists = (call("list_pressure_part_sizes") or {}).get("partsLists", [])
    lists = [l for l in lists if isinstance(l.get("pipeSizes"), int) and l["pipeSizes"] > 0]
    best = max(lists, key=lambda l: l["pipeSizes"]) if lists else None
    return (best.get("name") or best["handle"]) if best else None


def st_pressure():
    pl = pressure_list()
    read("list_pressure_part_sizes", {"parts_list": pl, "part_type": "Pipe"})
    read("list_pressure_part_sizes", {"parts_list": pl, "part_type": "Fitting", "contains": "150"})
    read("list_pressure_part_sizes", {"parts_list": pl, "part_type": "Appurtenance"})
    out = edit("pressure network + parts", [
        {"op": "pressure_network_create", "name": "MCP-PN", "parts_list": pl, "description": "MCP test"},
        {"op": "pressure_add", "network": "MCP-PN",
         "pipes": [{"id": "P1", "start": [10, 90, 8.5], "end": [45, 90, 8.5], "size": 150, "match": "ductile"},
                   {"id": "P2", "start": [45, 90, 8.5], "end": [45, 40, 8.5], "size": 150, "match": "ductile"}],
         "fittings": [{"id": "F1", "at": [45, 90, 8.5], "type": "Elbow", "size": 150, "match": "90 degree"}]}])
    pipes = created_handles(out, "pipe")
    check("elbow connected to both pipes", op_result(out, 1).get("connections") == 2)
    edit("duplicate pressure network is refused", [{"op": "pressure_network_create", "name": "mcp-pn", "parts_list": pl}], expect=False)
    read("list_pressure_networks")
    read("list_pressure_pipes", {"network_name": "MCP-PN"})
    read("list_pressure_fittings", {"network_name": "MCP-PN"})
    if pipes:
        edit("pressure part set", [{"op": "pressure_part_set", "handle": pipes[1], "end": [45, 30, 8.5], "description": "moved by MCP"}])
        edit("pressure extend existing", [{"op": "pressure_add", "network": "MCP-PN",
                                           "pipes": [{"start": [45, 30, 8.5], "end": [80, 30, 8.5], "size": 150, "match": "ductile"}]},
                                          {"op": "pressure_network_set", "name": "MCP-PN", "reference_surface": "MCP-EG"}])
        read("list_pressure_pipes", {"network_name": "MCP-PN"})


def st_piperun():
    pl = pressure_list()
    edit("pipe run", [{"op": "pressure_network_create", "name": "MCP-RUN", "parts_list": pl, "reference_surface": "MCP-EG"},
                      {"op": "pressure_pipe_run", "network": "MCP-RUN", "name": "RUN-1", "points": [[10, 20], [60, 20], [60, 70]],
                       "size": 150, "match": "ductile", "cover": 1.2}])
    read("list_pipe_runs")
    read("list_pressure_pipes", {"network_name": "MCP-RUN"})
    read("list_pressure_fittings", {"network_name": "MCP-RUN"})


def st_gravity():
    lists = (read("list_gravity_parts") or {}).get("partsLists", [])
    if not lists:
        check("gravity parts list available", False)
        return
    pl = max(lists, key=lambda l: l.get("partFamilies") or 0)
    key = pl.get("name") or pl["handle"]
    parts = read("list_gravity_parts", {"parts_list": key}) or {}
    pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes")]
    sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]   # a real manhole, not the Null Structure
    if not pf or not sf:
        check("gravity families available", False)
        return
    out = edit("gravity network", [
        {"op": "pipe_network_create", "name": "MCP-GN", "parts_list": key, "reference_surface": "MCP-EG"},
        {"op": "pipe_network_add", "network": "MCP-GN",
         "structures": [{"id": "S1", "at": [20, 60, 11.0], "family": sf[0]["family"], "size_index": 0},
                        {"id": "S2", "at": [70, 60, 11.5], "family": sf[0]["family"], "size_index": 0}],
         "pipes": [{"id": "G1", "start": [20, 60, 9.0], "end": [70, 60, 8.8], "family": pf[0]["family"], "size_index": 0}]}])
    check("pipe connected to both structures", op_result(out, 1).get("connections") == 2)
    # A longer chain in one batch: every pipe end must find its structure, and the part is resolved once and reused.
    n = 25
    chain = edit("gravity chain of 25 structures", [
        {"op": "pipe_network_create", "name": "MCP-CHAIN", "parts_list": key},
        {"op": "pipe_network_add", "network": "MCP-CHAIN",
         "structures": [{"at": [200 + 10 * i, 200, 11.0], "family": sf[0]["family"], "size_index": 0} for i in range(n)],
         "pipes": [{"start": [200 + 10 * i, 200, 9.0], "end": [210 + 10 * i, 200, 8.95], "family": pf[0]["family"], "size_index": 0}
                   for i in range(n - 1)]}])
    check("chain: every pipe end connected", op_result(chain, 1).get("connections") == 2 * (n - 1))
    read("list_pipe_networks")
    read("list_pipes", {"network_name": "MCP-GN"})
    read("list_structures", {"network_name": "MCP-GN"})
    gp = created_handles(out, "pipe")
    gs = created_handles(out, "structure")
    if gp and gs:
        edit("gravity pipe/structure set", [{"op": "pipe_set", "handle": gp[0], "description": "set by MCP"},
                                            {"op": "structure_set", "handle": gs[0], "rim": 11.2, "description": "set by MCP"}])
        st = (call("list_structures", {"network_name": "MCP-GN"}) or {}).get("structures", [])
        check("structure rim took the given level", any(x["handle"] == gs[0] and x["rimElevation"] == 11.2 for x in st))


def st_acad():
    edit("layer for acad", [{"op": "layer_create", "name": "C-MCP-ACAD", "color": 4, "if_exists": "skip"}])
    out = edit("acad drafting", [
        {"op": "draw_circle", "center": [150, 50, 0], "radius": 10, "layer": "C-MCP-ACAD"},
        {"op": "draw_arc", "center": [150, 50, 0], "radius": 15, "start_deg": 0, "end_deg": 90, "color": 1},
        {"op": "draw_mtext", "text": "MCP mtext\\Pline two", "at": [140, 80, 0], "height": 2, "width": 30},
        {"op": "draw_hatch", "points": [[170, 40], [190, 40], [190, 60], [170, 60]], "pattern": "ANSI31", "scale": 5, "layer": "C-MCP-ACAD"},
        {"op": "draw_hatch", "points": [[170, 65], [190, 65], [180, 80]], "color": 3},
        {"op": "draw_dimension", "from": [170, 40], "to": [190, 40], "line_at": [180, 35]},
        {"op": "draw_text", "text": "old text", "at": [140, 30, 0], "height": 2, "color": 6},
        {"op": "draw_line", "start": [140, 20, 0], "end": [160, 20, 0], "color": 2}])
    hs = created_handles(out)
    if len(hs) == 8:
        cp = edit("copy rotate scale text_set", [
            {"op": "entity_copy", "handles": [hs[0], hs[7]], "dx": 60},
            {"op": "entity_rotate", "handles": [hs[7]], "base": [140, 20], "angle_deg": 90},
            {"op": "entity_scale", "handles": [hs[0]], "base": [150, 50], "factor": 0.5},
            {"op": "text_set", "handle": hs[6], "text": "new text", "height": 3},
            {"op": "text_set", "handle": hs[2], "text": "changed mtext"}])
        ents = read("list_entities", {"window": [130, 0, 260, 100], "limit": 50}) or {}
        byh = {e["handle"]: e for e in ents.get("entities", [])}
        g = lambda h: (byh.get(h) or {}).get("geometry") or {}
        check("circle scaled to radius 5", g(hs[0]).get("radius") == 5)
        check("line rotated to vertical", g(hs[7]).get("end") == [140, 40, 0])
        check("text changed", g(hs[6]).get("text") == "new text" and g(hs[6]).get("height") == 3)
        check("hatch area 400", ((out["results"][3].get("result") or {}).get("area")) == 400)
        check("dimension measures 20", g(hs[5]).get("measurement") == 20)
    blocks = (read("list_blocks") or {}).get("blocks", [])
    if blocks:
        edit("block insert", [{"op": "draw_block", "name": blocks[0]["name"], "at": [150, 90, 0], "scale": 1}])
    edit("copy of a civil object is refused", [{"op": "entity_copy", "handles": [(call("list_alignments")["alignments"] or [{"handle": "0"}])[0]["handle"]], "dx": 5}], expect=False)
    read("list_entities", {"type": "INSERT", "limit": 5})


def st_civil2():
    out = edit("profile view + parts", [
        {"op": "profile_view_create", "alignment": "MCP-AL", "at": [0, -120], "name": "MCP-PV"},
        {"op": "pipe_network_create", "name": "MCP-GN2", "parts_list": (call("list_pipe_networks")["pipeNetworks"] or [{}])[0].get("partsList") or "Standard", "reference_surface": "MCP-EG"},
    ])
    read("list_profile_views")
    edit("sample lines", [{"op": "sample_lines_create", "alignment": "MCP-AL", "group": "MCP-SLG", "stations": [10, 30], "interval": 50}])
    read("list_sample_lines", {"alignment_name": "MCP-AL"})
    edit("labels", [{"op": "labels_add", "pipe_network": "MCP-GN"}])
    edit("profile view add parts", [{"op": "profile_view_add_parts", "profile_view": "MCP-PV", "pipe_network": "MCP-GN", "pressure_network": "MCP-RUN"}])
    edit("breaklines + boundary", [
        {"op": "surface_add_breaklines", "name": "MCP-EG", "lines": [[[12, 33, 10.6], [88, 47, 11.9]]]},
        {"op": "surface_add_boundary", "name": "MCP-EG", "points": [[5, 5], [95, 5], [95, 95], [5, 95]], "type": "outer"}])
    pts = [[x, y, 11 + 0.01 * x] for x in range(0, 101, 25) for y in range(0, 101, 25)]
    vol = edit("volume surface", [{"op": "surface_create", "name": "MCP-FG", "points": pts},
                                  {"op": "surface_volume_create", "name": "MCP-VOL", "base": "MCP-EG", "comparison": "MCP-FG"}])
    v = read("get_surface_volume", {"surface_name": "MCP-VOL"}) or {}
    check("volume surface reports volumes", isinstance(v.get("cut"), (int, float)) and isinstance(v.get("fill"), (int, float)) and (v["cut"] + v["fill"]) > 0)
    edit("feature line", [{"op": "feature_line_create", "name": "MCP-FL", "points": [[110, 0, 10], [110, 50, 11], [130, 80, 12]]}])
    fl = read("list_feature_lines") or {}
    check("feature line listed", any(f.get("name") == "MCP-FL" for f in fl.get("featureLines", [])))
    edit("curved pressure pipe", [{"op": "pressure_add", "network": "MCP-PN",
                                   "pipes": [{"start": [80, 30, 8.5], "end": [100, 50, 8.5], "through": [95, 35], "size": 150, "match": "ductile"}]}])
    pp = (call("list_pressure_pipes", {"network_name": "MCP-PN"}) or {}).get("pipes", [])
    check("a curved pipe exists", any(x.get("isCurve") is True for x in pp))
    read("list_corridors")


def st_civil3():
    import os
    import tempfile
    labels = edit("pressure labels", [{"op": "pressure_labels_add", "pressure_network": "MCP-PN"}])
    check("labels report the style they used", bool((op_result(labels, 0).get("stylesUsed") or {}).get("pipe")))
    styles = (read("list_styles", {"kind": "alignment"}) or {}).get("styles", [])
    check("style names are readable", bool(styles) and all(s.get("name") for s in styles))
    edit("unknown style name is refused", [{"op": "alignment_create", "name": "MCP-NOSTYLE", "points": [[0, 0], [50, 0]],
                                             "style": "No Such Style"}], expect=False)
    nets = (read("list_pressure_networks") or {}).get("pressureNetworks", [])
    check("unset reference alignment reads as null", bool(nets) and all(n.get("referenceAlignment") is None for n in nets))
    read("list_styles", {"kind": "pressure_pipe_label"})
    edit("section views", [{"op": "section_views_create", "alignment": "MCP-AL", "group": "MCP-SLG", "at": [300, 0], "columns": 3}])
    sv = read("list_section_views") or {}
    check("section views listed", sv.get("total", 0) >= 2)
    fl = edit("feature line as breakline", [{"op": "feature_line_create", "name": "MCP-PAD", "closed": True,
                                             "points": [[30, 30, 12.5], [50, 30, 12.5], [50, 50, 12.5], [30, 50, 12.5]]}])
    hs = created_handles(fl)
    if hs:
        edit("breaklines from handles", [{"op": "surface_add_breaklines", "name": "MCP-FG", "handles": hs[:1]}])
    asm = edit("assembly", [{"op": "assembly_create", "name": "MCP-ASM", "at": [300, -150], "subassemblies": [
        {"class": "LaneSuperelevationAOR", "side": "Right"}, {"class": "LaneSuperelevationAOR", "side": "Left"}]}])
    read("list_assemblies")
    edit("object_set on an assembly", [{"op": "object_set", "kind": "assembly", "name": "MCP-ASM", "description": "MCP test assembly"}])
    edit("object_set rejects an unknown kind", [{"op": "object_set", "kind": "parcel", "name": "x", "description": "y"}], expect=False)
    profs = [p for p in (call("list_profiles", {"alignment_name": "MCP-AL"}) or {}).get("profiles", [])]
    print("profiles:", profs, flush=True)
    design = next((p["name"] for p in profs if "surface" not in str(p.get("type", "")).lower() and "eg" not in p["name"].lower()), profs[0]["name"] if profs else "")
    edit("corridor", [{"op": "corridor_create", "name": "MCP-COR", "alignment": "MCP-AL", "profile": design, "assembly": "MCP-ASM"}])
    edit("corridor surface", [{"op": "corridor_surface_add", "corridor": "MCP-COR", "name": "MCP-COR-TOP"}])
    cor = read("list_corridors") or {}
    check("corridor listed", any(c.get("name") == "MCP-COR" for c in cor.get("corridors", [])))
    edit("corridor rebuild", [{"op": "corridor_rebuild", "name": "MCP-COR"}])
    read("list_parcels")
    pdf = os.path.join(tempfile.gettempdir(), "mcp_test_plot.pdf")
    edit("plot pdf", [{"op": "plot_pdf", "path": pdf, "overwrite": True, "paper": "A3"}])
    check("pdf written", os.path.isfile(pdf) and os.path.getsize(pdf) > 1000)
    edit("plot refuses non-pdf path", [{"op": "plot_pdf", "path": pdf + ".txt"}], expect=False)


def st_acad2():
    import os
    import tempfile
    out = edit("leader table dims linetype", [
        {"op": "draw_leader", "arrow_at": [150, 50], "text_at": [165, 70], "text": "MCP leader", "height": 2},
        {"op": "draw_table", "at": [200, 100], "title": "MCP table", "rows": [["Item", "Qty"], ["Pipe DN150", 84.6], ["Elbow", 1]], "text_height": 2},
        {"op": "draw_dimension", "from": [200, 0], "to": [230, 10], "line_at": [215, -6], "type": "horizontal"},
        {"op": "draw_dimension", "from": [200, 0], "to": [230, 10], "line_at": [236, 5], "type": "vertical"},
        {"op": "draw_line", "start": [200, 20, 0], "end": [240, 20, 0]},
        {"op": "draw_circle", "center": [210, 40, 0], "radius": 4},
        {"op": "draw_text", "text": "TAG", "at": [208, 39, 0], "height": 1.5}])
    hs = created_handles(out)
    if len(hs) == 7:
        res = (out.get("results") or [])
        check("horizontal dim = 30, vertical dim = 10", res[2]["result"]["measurement"] == 30 and res[3]["result"]["measurement"] == 10)
        edit("linetype", [{"op": "entity_set", "handles": [hs[4]], "linetype": "DASHED"}])
        blk = edit("block define + insert", [
            {"op": "block_define", "name": "MCP-BLK", "handles": [hs[5], hs[6]], "base": [210, 40]},
            {"op": "draw_block", "name": "MCP-BLK", "at": [250, 40, 0], "scale": 2, "rotation_deg": 45}])
        check("defined block is listed", any(b["name"] == "MCP-BLK" for b in (call("list_blocks") or {}).get("blocks", [])))
    edit("layout", [{"op": "layout_create", "name": "MCP-Layout"}])
    check("layout listed", any(l["name"] == "MCP-Layout" for l in (read("list_layouts") or {}).get("layouts", [])))
    folder = tempfile.gettempdir()
    named = os.path.join(folder, "mcp_test_saveas.dwg")
    dwg, dxf = os.path.join(folder, "mcp_test_copy.dwg"), os.path.join(folder, "mcp_test_copy.dxf")
    for f in (dwg, dxf):
        if os.path.exists(f):
            os.remove(f)
    before = Path(call("ping")["activeDrawing"]).name
    if before.lower() != "mcp_test_saveas.dwg":
        edit("unsaved drawing cannot save in place", [{"op": "drawing_save"}], expect=False)
        edit("save as", [{"op": "drawing_save", "path": named, "overwrite": True}])
        check("save as renamed the open drawing", Path(call("ping")["activeDrawing"]).name.lower() == "mcp_test_saveas.dwg")
    sv = edit("copy + dxf", [{"op": "drawing_save", "path": dwg, "copy": True}, {"op": "drawing_save", "path": dxf, "format": "dxf"}])
    check("dwg copy written", os.path.exists(dwg) and os.path.getsize(dwg) > 10000)
    check("dxf written", os.path.exists(dxf) and os.path.getsize(dxf) > 10000)
    check("save reported no failure", not (sv.get("warnings") or []))
    check("copy did not rename the open drawing", Path(call("ping")["activeDrawing"]).name.lower() == "mcp_test_saveas.dwg")
    edit("copy without overwrite is refused", [{"op": "drawing_save", "path": dwg, "copy": True}], expect=False)
    edit("save in place", [{"op": "drawing_save"}])
    if os.path.exists(dwg):
        edit("xref attach", [{"op": "xref_attach", "path": dwg, "name": "MCP-XREF", "at": [400, 0, 0]}])


def st_cleanup():
    edit("cleanup networks", [{"op": "pressure_network_erase", "name": "MCP-PN"}], expect=True)
    edit("cleanup pipe run network", [{"op": "pressure_network_erase", "name": "MCP-RUN"}], expect=True)


STAGES = {"dryrun": st_dryrun, "layers": st_layers, "draft": st_draft, "cogo": st_cogo, "surface": st_surface,
          "alignment": st_alignment, "profile": st_profile, "pressure": st_pressure, "piperun": st_piperun,
          "gravity": st_gravity, "acad": st_acad, "civil2": st_civil2, "civil3": st_civil3, "acad2": st_acad2, "cleanup": st_cleanup}

if __name__ == "__main__":
    info = read("ping")
    name = Path((info or {}).get("activeDrawing") or "").name
    if not (re.fullmatch(r"Drawing\d+\.dwg", name, re.IGNORECASE) or "mcp_test" in name.lower()):
        raise SystemExit(f"Refusing to run: the active drawing '{name}' is not a scratch drawing "
                         "(needs an unsaved DrawingN.dwg or a name containing 'mcp_test').")
    TARGET = info.get("target")
    if not TARGET:
        raise SystemExit("Update the loaded bridge before running the edit tests.")
    read("get_edit_permission")
    wanted = sys.argv[1:] or [s for s in STAGES if s != "cleanup"]   # 'acad' needs the alignment stage for its last check only
    for stage in wanted:
        print(f"\n############### stage {stage}", flush=True)
        try:
            STAGES[stage]()
        except Exception as exc:
            RESULTS[f"stage {stage}"] = f"FAIL ({exc})"
            print(f"STAGE FAILED: {exc}", flush=True)
    read("get_drawing_info")
    print("\n############### SUMMARY")
    for k, v in RESULTS.items():
        print(f"{v:5} {k}")
    print("ALL PASS" if all(v == "PASS" for v in RESULTS.values()) else "SOME FAILED")
    sys.exit(int(any(v != "PASS" for v in RESULTS.values())))
