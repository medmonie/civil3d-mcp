"""Live test of the fixes made after the audit test (run_fixes.bat): D4 block_import replace keeps the drawing's layers,
D5 subassemblies chained, D6 connected pressure pipes not reported, D7 a failed solids file keeps the old file, D8 no
duplicate labels on a re-run. Ends with diagnose_save.

Run it in a NEW drawing from the Civil 3D metric template (DrawingN, after MCPALLOW); nothing is saved.
Output: tests\\results\\fixes.txt
"""
import json
import os
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
TARGET = call("get_drawing_info", {}).get("target")
name = Path(call("ping").get("activeDrawing") or "").name.lower()
if not (name.startswith("drawing") or "mcp_test" in name):
    sys.exit(f"REFUSED: the active drawing is '{name}'. Run this in a new unsaved drawing (DrawingN).")
tag = uuid.uuid4().hex[:4]
TMP = Path(os.environ.get("TEMP", "C:\\Temp")) / f"mcp_fixes_{tag}"
TMP.mkdir(parents=True, exist_ok=True)
ox, oy = 30000.0, 30000.0


def check(title, ok, detail=""):
    RESULTS[title] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[title]:5} {title} {detail}", flush=True)


def show(title, out, cap=2000):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < cap else text[:cap] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, **params):
    try:
        return call(method, {**params, "target": TARGET})
    except Exception as exc:     # noqa: BLE001
        print(f"===== {method} FAILED: {exc}", flush=True)
        return {"_error": str(exc)}


def edit(title, ops, must=True):
    try:
        out = call("edit", {"operations": ops, "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    if must:
        check(title, out.get("applied") is True, str(out.get("error") or out.get("exception") or ""))
    return out


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


def counts():
    return {g["key"]: g["count"] for g in read("count_entities", group_by="type").get("groups", [])}


def guarded(fn):
    try:
        fn()
    except Exception as exc:     # noqa: BLE001
        check(f"{fn.__name__} ran without a test-script error", False, f"{type(exc).__name__}: {exc}")


def dxf(path, line_len):
    blk = f"MCPATT{tag}".upper()
    t = ["0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1009", "0", "ENDSEC",
         "0", "SECTION", "2", "TABLES", "0", "TABLE", "2", "LTYPE", "70", "1",
         "0", "LTYPE", "2", "CONTINUOUS", "70", "0", "3", "Solid line", "72", "65", "73", "0", "40", "0.0", "0", "ENDTAB",
         "0", "TABLE", "2", "LAYER", "70", "2",
         "0", "LAYER", "2", "0", "70", "0", "62", "6", "6", "CONTINUOUS",
         "0", "LAYER", "2", "MCP-SHARED", "70", "0", "62", "5", "6", "CONTINUOUS", "0", "ENDTAB",
         "0", "ENDSEC", "0", "SECTION", "2", "BLOCKS",
         "0", "BLOCK", "8", "0", "2", blk, "70", "2", "10", "0.0", "20", "0.0", "30", "0.0", "3", blk,
         "0", "LINE", "8", "MCP-SHARED", "10", "0.0", "20", "0.0", "30", "0.0", "11", str(float(line_len)), "21", "0.0", "31", "0.0",
         "0", "ATTDEF", "8", "0", "10", "0.0", "20", "2.0", "30", "0.0", "40", "1.0", "1", "A", "3", "Tag one", "2", "TAG1", "70", "0",
         "0", "ENDBLK", "8", "0", "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES", "0", "ENDSEC", "0", "EOF"]
    Path(path).write_text("\n".join(t) + "\n", encoding="ascii")
    return blk


def layers():
    return {l["name"]: l.get("color") for l in read("list_layers", limit=5000).get("layers", [])}


def width(handle):
    o = next(iter(read("get_objects", handles=[handle]).get("objects", [])), {})
    e = o.get("extents") or {}
    try:
        return round(e["max"][0] - e["min"][0], 3)
    except (KeyError, TypeError, IndexError):
        return None


# ---------------------------------------------------------------- D4
def d4():
    blk = dxf(TMP / "v1.dxf", 10)
    dxf(TMP / "v2.dxf", 20)
    edit("D4 fixture: layer MCP-SHARED yellow, import v1 (line 10 m)", [
        {"op": "layer_create", "name": "MCP-SHARED", "color": 2, "if_exists": "skip"},
        {"op": "block_import", "path": str(TMP / "v1.dxf"), "blocks": [blk]}])
    out = edit("D4 insert v1", [{"op": "draw_block", "name": blk, "at": [ox, oy, 0], "attributes": {"TAG1": "x"}}])
    h = result(out).get("handle")
    w1 = width(h)
    before = layers()
    out = edit("D4 block_import replace true (v2: line 20 m, MCP-SHARED blue, layer 0 magenta in the file)", [
        {"op": "block_import", "path": str(TMP / "v2.dxf"), "blocks": [blk], "replace": True}])
    after = layers()
    w2 = width(h)
    check("D4 the block was redefined (reference 10 m -> 20 m wide)", w1 is not None and w2 is not None and w2 > w1 + 5, f"{w1} -> {w2}")
    changed = {k: (before.get(k), after.get(k)) for k in set(before) | set(after) if before.get(k) != after.get(k)}
    check("D4 the drawing's layers kept their colours", not changed and after.get("MCP-SHARED") == before.get("MCP-SHARED"), str(changed))


# ---------------------------------------------------------------- D5, D6 need terrain + alignment
eg, al = f"EG {tag}", f"AL {tag}"
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(-1, 12) for j in range(-8, 4)]
edit("fixture: surface, alignment, profile", [
    {"op": "surface_create", "name": eg, "points": grid},
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_create", "alignment": al, "name": f"{al} FG", "pvis": [[0, 10.5], [200, 14.5]]}])


def d5():
    asm, cor = f"ASM {tag}", f"COR {tag}"
    out = edit("D5 assembly BasicLane + BasicShoulder (right), corridor", [
        {"op": "assembly_create", "name": asm, "at": [ox, oy - 300], "subassemblies": [
            {"class": "BasicLane", "side": "Right"}, {"class": "BasicShoulder", "side": "Right"}]},
        {"op": "corridor_create", "name": cor, "alignment": al, "profile": f"{al} FG", "assembly": asm}])
    show("D5 subassemblies", result(out).get("subassemblies"))
    fl = read("get_corridor_feature_lines", corridor=cor, points=False).get("featureLines", [])
    off = {r.get("code"): r.get("maxOffset") for r in fl}
    print("D5 offsets:", off, flush=True)
    etw, eps = off.get("ETW"), off.get("EPS")
    check("D5 the shoulder starts at the lane edge (EPS beyond ETW 3.6 m)", etw is not None and eps is not None and eps > etw + 0.1, f"ETW {etw}, EPS {eps}")
    check("D5 the reply says what the shoulder is attached to", "baseline" not in str((result(out).get("subassemblies") or [{}, {}])[-1].get("attachedTo")),
          str(result(out).get("subassemblies")))


def d6_d8_pressure():
    lists_ = [l for l in read("list_pressure_part_sizes").get("partsLists", []) if isinstance(l.get("pipeSizes"), int) and l["pipeSizes"] > 0]
    if not lists_:
        check("pressure parts list available", False)
        return
    best = max(lists_, key=lambda l: l["pipeSizes"])
    pn = f"PN {tag}"
    edit("fixture: pressure run with a bend", [
        {"op": "pressure_network_create", "name": pn, "parts_list": best.get("name") or best["handle"], "reference_surface": eg},
        {"op": "pressure_pipe_run", "network": pn, "name": f"RUN {tag}", "points": [[ox, oy - 100], [ox + 60, oy - 100], [ox + 100, oy - 60]],
         "size": 150, "match": "ductile", "cover": 1.2}])
    out = show("D6 network_interference", read("network_interference", network1=pn, within=1.0))
    check("D6 pipes joined at the bend are not reported", out.get("found") == 0 and (out.get("connectedPairsSkipped") or 0) >= 1,
          f"found {out.get('found')}, skipped {out.get('connectedPairsSkipped')}")

    c0 = counts()
    o1 = edit("D8 pressure_labels_add (1)", [{"op": "pressure_labels_add", "pressure_network": pn}])
    c1 = counts()
    o2 = edit("D8 pressure_labels_add (2)", [{"op": "pressure_labels_add", "pressure_network": pn}])
    c2 = counts()
    keys = ("AECC_PRESSURE_PIPE_LABEL", "AECC_FITTING_LABEL")
    check("D8 pressure_labels_add twice: no second set", all(c1.get(k) == c2.get(k) for k in keys) and (result(o2).get("alreadyLabelled") or 0) > 0,
          f"{[(k, c0.get(k), c1.get(k), c2.get(k)) for k in keys]} second {result(o2)}")


def d8_gravity():
    lists = read("list_gravity_parts").get("partsLists", [])
    plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
    key = plist.get("name") or plist["handle"]
    parts = read("list_gravity_parts", parts_list=key)
    pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes") and not any(w in f["family"].lower() for w in ("box", "rect", "ellip", "egg", "arch"))]
    sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
    net = f"Net {tag}"
    edit("fixture: gravity network (2 structures, 1 pipe)", [
        {"op": "pipe_network_create", "name": net, "parts_list": key},
        {"op": "pipe_network_add", "network": net,
         "structures": [{"name": f"M1-{tag}", "at": [ox + 20, oy + 30, 10.4], "family": sf[0]["family"], "size_index": 0},
                        {"name": f"M2-{tag}", "at": [ox + 120, oy + 30, 12.4], "family": sf[0]["family"], "size_index": 0}],
         "pipes": [{"name": f"P1-{tag}", "start": [ox + 20, oy + 30, 8.0], "end": [ox + 120, oy + 30, 8.5], "family": pf[0]["family"],
                    "size_index": 0, "start_structure": f"M1-{tag}", "end_structure": f"M2-{tag}"}]}])
    keys = ("AECC_PIPE_LABEL", "AECC_STRUCTURE_LABEL")
    c0 = counts()
    edit("D8 labels_add (1)", [{"op": "labels_add", "pipe_network": net}])
    c1 = counts()
    o2 = edit("D8 labels_add (2)", [{"op": "labels_add", "pipe_network": net}])
    c2 = counts()
    check("D8 labels_add twice: no second set", c1.get("AECC_PIPE_LABEL") == 1 and c1.get("AECC_STRUCTURE_LABEL") == 2
          and all(c1.get(k) == c2.get(k) for k in keys) and result(o2).get("alreadyLabelled") == 3,
          f"{[(k, c0.get(k), c1.get(k), c2.get(k)) for k in keys]} second {result(o2)}")

    pls = [s["name"] for s in read("list_styles", kind="pipe_label").get("styles", [])]
    sls = [s["name"] for s in read("list_styles", kind="structure_label").get("styles", [])]
    op = {"op": "network_styles", "network": net, "pipe_label_style": pls[-1], "structure_label_style": sls[-1]}
    o3 = edit("D8 network_styles with label styles (1)", [op])
    o4 = edit("D8 network_styles with label styles (2)", [op])
    c4 = counts()
    check("D8 network_styles restyles the existing labels instead of adding", all(c4.get(k) == c2.get(k) for k in keys)
          and result(o3).get("labelsAdded") == 0 and result(o4).get("labelsRestyled") == 3,
          f"{[(k, c2.get(k), c4.get(k)) for k in keys]} {result(o3)} {result(o4)}")


def d7():
    empty = f"EMPTY {tag}"
    edit("fixture: empty surface", [{"op": "surface_create", "name": empty}])
    f = TMP / "solids.dwg"
    f.write_bytes(b"old file - must survive a failed creation")
    out = edit("D7 solids on an empty surface into an existing file (fails after commit)", [
        {"op": "surface_solids_create", "surface": empty, "mode": "depth", "depth": 1.0, "file": str(f), "overwrite": True}], must=False)
    print("warnings:", out.get("warnings"), flush=True)
    leftovers = [p.name for p in TMP.glob("solids.mcp-*")]
    check("D7 the old file survives a failed creation", f.exists() and f.read_bytes().startswith(b"old file") and not leftovers,
          f"exists {f.exists()} leftovers {leftovers}")
    out = edit("D7 solids on the real surface into the same file", [
        {"op": "surface_solids_create", "surface": eg, "mode": "depth", "depth": 1.0, "file": str(f), "overwrite": True}], must=False)
    print("warnings:", out.get("warnings"), flush=True)
    check("D7 a successful creation replaces it", f.exists() and not f.read_bytes().startswith(b"old file") and f.stat().st_size > 1000,
          f"{f.stat().st_size if f.exists() else 'missing'} bytes")


for block in (d4, d5, d6_d8_pressure, d8_gravity, d7):
    guarded(block)

save = read("diagnose_save")
show("diagnose_save", {k: save.get(k) for k in ("savable", "objectsScanned", "unwritable", "copy", "_error")})
check("drawing still savable", save.get("savable") is True)

for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
