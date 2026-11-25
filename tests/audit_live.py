"""Live test of the audit fixes (run_audit.bat): docs/AUDIT_TEST_PLAN.md lines C1-C13, A4, A7 and the D records.

Run it in a NEW drawing from the Civil 3D metric template (DrawingN, after MCPALLOW); nothing is saved. Files it writes
(DXF fixtures, a DXF copy of the drawing, a dummy solids file) go to %TEMP%\\mcp_audit_<tag>. The office drawing
XR.BASE.dwg is copied there first and only the copy is read.
Output: tests\\results\\audit.txt   (PASS/FAIL lines for the C and A checks, RECORD lines for the D observations)
"""
import json
import os
import shutil
import sys
import time
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402
import qa  # noqa: E402

RESULTS: dict[str, str] = {}
RECORDS: dict[str, str] = {}
TARGET = None


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def record(name, text):
    RECORDS[name] = str(text)
    print(f"RECORD {name}: {text}", flush=True)


def show(title, out, cap=2500):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < cap else text[:cap] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, quiet=False, **params):
    try:
        out = call(method, {**params, "target": TARGET})
        return out if quiet else show(method, out)
    except Exception as exc:     # noqa: BLE001
        print(f"===== {method}\nFAILED: {exc}", flush=True)
        return {"_error": str(exc)}


def raw_edit(ops, request_id=None, dry_run=False):
    params = {"operations": ops, "target": TARGET, "request_id": request_id or str(uuid.uuid4())}
    if dry_run:
        params["dry_run"] = True
    try:
        return call("edit", params, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        return {"applied": False, "exception": str(exc)}


def edit(title, ops, must=True, dry_run=False, cap=2500):
    out = show("edit: " + title, raw_edit(ops, dry_run=dry_run), cap)
    if must:
        ok = out.get("applied") is True or (dry_run and out.get("dryRun") is True and not out.get("error"))
        check(title, ok, str(out.get("error") or out.get("exception") or ""))
    return out


def refused(title, ops, *needles):
    """The edit must be refused and its error must contain every needle."""
    out = show("edit (expect refusal): " + title, raw_edit(ops))
    err = str(out.get("error") or out.get("exception") or "")
    check(title, out.get("applied") is False and all(n.lower() in err.lower() for n in needles), err[:300])
    return out


def read_refused(title, method, needle, **params):
    try:
        out = call(method, {**params, "target": TARGET})
        check(title, False, "not refused: " + json.dumps(out, default=str)[:300])
    except Exception as exc:     # noqa: BLE001
        check(title, needle.lower() in str(exc).lower(), str(exc)[:300])


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


def warnings(out):
    return out.get("warnings") or []


def section(title):
    print(f"\n#################### {title}", flush=True)


def guarded(fn):
    """Run one block of checks; an unexpected exception fails that block only."""
    try:
        fn()
    except Exception as exc:     # noqa: BLE001
        check(f"{fn.__name__} ran without a test-script error", False, f"{type(exc).__name__}: {exc}")


def entities(layout=None, type_=None, limit=2000):
    params = {"limit": limit}
    if layout:
        params["layout"] = layout
    if type_:
        params["type"] = type_
    return read("list_entities", quiet=True, **params).get("entities", [])


def find(ents, handle):
    return next((e for e in ents if e.get("handle") == handle), None)


def dxf_blocks(path):
    """BLOCKS section of an ASCII DXF: {block name: [entity type, ...]} with VERTEX / ATTRIB counted under their owner."""
    lines = Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    pairs = [(lines[i].strip(), lines[i + 1].rstrip("\r")) for i in range(0, len(lines) - 1, 2)]
    blocks, in_blocks, cur, last = {}, False, None, None
    for i, (code, val) in enumerate(pairs):
        if code == "2" and i > 0 and pairs[i - 1] == ("0", "SECTION"):
            in_blocks = val == "BLOCKS"
            continue
        if not in_blocks:
            continue
        if code == "0":
            if val == "BLOCK":
                cur = next((v for c, v in pairs[i + 1:i + 30] if c == "2"), None)
                blocks[cur] = []
            elif val == "ENDBLK":
                cur = None
            elif cur is not None and val in ("VERTEX", "ATTRIB") and last is not None:
                last[val] = last.get(val, 0) + 1
            elif cur is not None and val not in ("SEQEND",):
                last = {"type": val}
                blocks[cur].append(last)
    return blocks


# ---------------------------------------------------------------- fixtures
TARGET = call("get_drawing_info", {}).get("target")
name = Path(call("ping").get("activeDrawing") or "").name.lower()
if not (name.startswith("drawing") or "mcp_test" in name):
    print(f"REFUSED: the active drawing is '{name}'. Run this in a new unsaved drawing (DrawingN).")
    sys.exit(1)

tag = uuid.uuid4().hex[:4]
TMP = Path(os.environ.get("TEMP", "C:\\Temp")) / f"mcp_audit_{tag}"
TMP.mkdir(parents=True, exist_ok=True)
ox, oy = 20000.0, 20000.0
LAY = f"MCP-Audit-{tag}"


def dxf_fixture(path, line_len, layer0_color):
    """R12 DXF with block MCPATT{tag}: a line on MCP-SHARED and two attribute definitions TAG1/TAG2.
    Layer 0 / Defpoints / MCP-SHARED carry other colours than the drawing's (for D4); STANDARD uses romans.shx."""
    blk = f"MCPATT{tag}"
    t = ["0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1009", "0", "ENDSEC",
         "0", "SECTION", "2", "TABLES",
         "0", "TABLE", "2", "LTYPE", "70", "1",
         "0", "LTYPE", "2", "CONTINUOUS", "70", "0", "3", "Solid line", "72", "65", "73", "0", "40", "0.0",
         "0", "ENDTAB",
         "0", "TABLE", "2", "LAYER", "70", "3",
         "0", "LAYER", "2", "0", "70", "0", "62", str(layer0_color), "6", "CONTINUOUS",
         "0", "LAYER", "2", "Defpoints", "70", "0", "62", "3", "6", "CONTINUOUS",
         "0", "LAYER", "2", "MCP-SHARED", "70", "0", "62", "5", "6", "CONTINUOUS",
         "0", "ENDTAB",
         "0", "TABLE", "2", "STYLE", "70", "1",
         "0", "STYLE", "2", "STANDARD", "70", "0", "40", "0.0", "41", "1.0", "50", "0.0", "71", "0", "42", "2.5",
         "3", "romans.shx", "4", "",
         "0", "ENDTAB",
         "0", "ENDSEC",
         "0", "SECTION", "2", "BLOCKS",
         "0", "BLOCK", "8", "0", "2", blk, "70", "2", "10", "0.0", "20", "0.0", "30", "0.0", "3", blk,
         "0", "LINE", "8", "MCP-SHARED", "10", "0.0", "20", "0.0", "30", "0.0", "11", str(float(line_len)), "21", "0.0", "31", "0.0",
         "0", "ATTDEF", "8", "0", "10", "0.0", "20", "2.0", "30", "0.0", "40", "1.0", "1", "A", "3", "Tag one", "2", "TAG1", "70", "0",
         "0", "ATTDEF", "8", "0", "10", "0.0", "20", "4.0", "30", "0.0", "40", "1.0", "1", "B", "3", "Tag two", "2", "TAG2", "70", "0",
         "0", "ENDBLK", "8", "0",
         "0", "ENDSEC",
         "0", "SECTION", "2", "ENTITIES", "0", "ENDSEC", "0", "EOF"]
    Path(path).write_text("\n".join(t) + "\n", encoding="ascii")
    return blk


BLK = dxf_fixture(TMP / "attblock.dxf", 10, 1)
dxf_fixture(TMP / "attblock_v2.dxf", 20, 6)


def count_types():
    return {g["key"]: g["count"] for g in read("count_entities", quiet=True, group_by="type").get("groups", [])}


def layers_by_name():
    return {l["name"]: l.get("color") for l in read("list_layers", quiet=True, limit=5000).get("layers", [])}


def standard_font():
    rows = read("list_cad_styles", quiet=True, kind="text_style").get("items") or []
    return next((r.get("font") for r in rows if str(r.get("name")).lower() == "standard"), None)


# terrain z = 10 + 0.02 (x - ox), alignment, gravity network with 2 structures + 1 pipe, a second network, a layout
section("fixtures")
eg, al = f"EG {tag}", f"AL {tag}"
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(-1, 14) for j in range(-8, 6)]
edit("fixture: surface, alignment, profile, layer, layout", [
    {"op": "surface_create", "name": eg, "points": grid},
    {"op": "surface_create", "name": f"FG {tag}", "points": [[x, y, z + 0.5] for x, y, z in grid]},
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_create", "alignment": al, "name": f"{al} FG", "pvis": [[0, 10.5], [200, 14.5]]},
    {"op": "layer_create", "name": "MCP-SHARED", "color": 2, "if_exists": "skip"},
    {"op": "layout_create", "name": LAY}])

lists = read("list_gravity_parts").get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0) if lists else {}
key = plist.get("name") or plist.get("handle")
parts = read("list_gravity_parts", parts_list=key) if key else {}
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes") and not any(w in f["family"].lower() for w in ("box", "rect", "ellip", "egg", "arch"))]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
NET, NET2 = f"Net {tag}", f"Net2 {tag}"
P = S = None
pipe_handle = pipe2_handle = None
if pf and sf:
    P, S = pf[0]["family"], sf[0]["family"]
    print("pipe family sizes:", pf[0]["sizes"], flush=True)
    out = edit("fixture: two gravity networks", [
        {"op": "pipe_network_create", "name": NET, "parts_list": key},
        {"op": "pipe_network_add", "network": NET,
         "structures": [{"id": "M1", "name": f"M1-{tag}", "at": [ox + 20, oy + 30, 10.4], "family": S, "size_index": 0},
                        {"id": "M2", "name": f"M2-{tag}", "at": [ox + 120, oy + 30, 12.4], "family": S, "size_index": 0}],
         "pipes": [{"name": f"P1-{tag}", "start": [ox + 20, oy + 30, 8.0], "end": [ox + 120, oy + 30, 8.5], "family": P,
                    "size_index": 0, "start_structure": f"M1-{tag}", "end_structure": f"M2-{tag}"}]},
        {"op": "pipe_network_create", "name": NET2, "parts_list": key},
        {"op": "pipe_network_add", "network": NET2,
         "pipes": [{"name": f"Q1-{tag}", "start": [ox + 20, oy + 60, 8.0], "end": [ox + 120, oy + 60, 8.5], "family": P, "size_index": 0}]}])
    created = result(out, 1).get("created", []) + result(out, 3).get("created", [])
    pipe_handle = next((c["handle"] for c in created if c.get("name") == f"P1-{tag}"), None)
    pipe2_handle = next((c["handle"] for c in created if c.get("name") == f"Q1-{tag}"), None)
else:
    check("gravity pipe and structure families available", False)

# attribute block from the DXF fixture
edit("fixture: import the attribute block from a DXF", [{"op": "block_import", "path": str(TMP / "attblock.dxf"), "blocks": [BLK]}])


# ---------------------------------------------------------------- C1 C2 C3
def c1_c2_c3():
    section("C1 entity_copy keeps attributes and 3D polyline vertices")
    out = edit("C1 fixtures: attributed block + 3D polyline (model space)", [
        {"op": "draw_block", "name": BLK, "at": [ox + 300, oy, 0], "attributes": {"TAG1": "one", "TAG2": "two"}},
        {"op": "draw_polyline", "points": [[ox + 320, oy, 1], [ox + 330, oy + 5, 2], [ox + 340, oy, 3], [ox + 350, oy + 5, 4]]}])
    br, p3 = result(out, 0).get("handle"), result(out, 1).get("handle")
    out = edit("C1 entity_copy", [{"op": "entity_copy", "handles": [br, p3], "dy": 50}])
    copies = result(out).get("handles", [])
    ents = entities()
    cb = find(ents, copies[0]) if copies else None
    cp = find(ents, copies[1]) if len(copies) > 1 else None
    orig_p3 = find(ents, p3)
    attrs = ((cb or {}).get("geometry") or {}).get("attributes")
    check("C1 copied block reference keeps its attributes", attrs == {"TAG1": "one", "TAG2": "two"}, str(attrs))
    gl, go = ((cp or {}).get("geometry") or {}), ((orig_p3 or {}).get("geometry") or {})
    check("C1 copied 3D polyline keeps its vertices (same length, moved 50)",
          gl.get("length") is not None and abs(gl.get("length", 0) - go.get("length", -1)) < 1e-3
          and gl.get("start") and abs(gl["start"][1] - (oy + 50)) < 1e-3 and abs(gl["start"][2] - 1) < 1e-3, f"{cp} vs {orig_p3}")

    section("C2 entity_copy on a paper-space layout")
    out = edit("C2 fixture: a line on the layout", [{"op": "draw_line", "start": [10, 10, 0], "end": [60, 10, 0], "layout": LAY}])
    ln = result(out).get("handle")
    out = edit("C2 entity_copy", [{"op": "entity_copy", "handles": [ln], "dy": 20}])
    cl = (result(out).get("handles") or [None])[0]
    on_layout, in_model = find(entities(layout=LAY), cl), find(entities(), cl)
    check("C2 the copy is on the same layout, not in model space", on_layout is not None and in_model is None, f"layout={on_layout} model={in_model}")

    section("C3 block_define from an attributed block reference + heavy 2D polyline")
    out = edit("C3 fixture: waterdrop path (heavy 2D polyline) + attributed block", [
        {"op": "surface_waterdrop", "surface": eg, "points": [[ox + 150, oy + 10]], "kind": "polyline2d"},
        {"op": "draw_block", "name": BLK, "at": [ox + 400, oy, 0], "attributes": {"TAG1": "x1", "TAG2": "x2"}}])
    wd = (result(out, 0).get("handles") or [None])[0]
    br2 = result(out, 1).get("handle")
    objs = read("get_objects", handles=[h for h in (wd, br2) if h])
    wd_type = next((o.get("type") for o in objs.get("objects", []) if o.get("handle") == wd), None)
    record("C3 waterdrop polyline2d object type", wd_type)
    model_block = f"AUD-M-{tag}"
    out = edit("C3 block_define (model space, replace_originals)", [
        {"op": "block_define", "name": model_block, "handles": [h for h in (br2, wd) if h], "base": [ox + 400, oy], "replace_originals": True}])
    ins = result(out).get("insertedHandle")
    m = find(entities(), ins)
    check("C3 the new reference is in model space", m is not None and (m.get("geometry") or {}).get("block") == model_block, str(m))

    out = edit("C3 fixtures on the layout: attributed block + polyline + text", [
        {"op": "draw_block", "name": BLK, "at": [100, 100, 0], "attributes": {"TAG1": "L1", "TAG2": "L2"}, "layout": LAY},
        {"op": "draw_polyline", "points": [[110, 100], [130, 110], [150, 100]], "layout": LAY},
        {"op": "draw_text", "text": "on layout", "at": [100, 90, 0], "layout": LAY}])
    hs = [result(out, i).get("handle") for i in range(3)]
    layout_block = f"AUD-L-{tag}"
    out = edit("C3 block_define (layout, replace_originals)", [
        {"op": "block_define", "name": layout_block, "handles": hs, "base": [100, 100], "replace_originals": True}])
    ins = result(out).get("insertedHandle")
    lay_ents = entities(layout=LAY)
    on_l = find(lay_ents, ins)
    check("C3 the new reference is on that layout", on_l is not None and find(entities(), ins) is None, str(on_l))
    check("C3 the originals on the layout are gone", all(find(lay_ents, h) is None for h in hs))

    # the block definitions, read from a DXF copy of the drawing
    dxf = TMP / f"audit_copy_{tag}.dxf"
    edit("C3 save a DXF copy to read the block definitions", [{"op": "drawing_save", "path": str(dxf), "copy": True, "format": "dxf"}])
    try:
        blocks = {k.upper(): v for k, v in dxf_blocks(dxf).items() if k}
        mb, lb = blocks.get(model_block.upper(), []), blocks.get(layout_block.upper(), [])
        print("DXF block", model_block, mb, "\nDXF block", layout_block, lb, flush=True)
        ins_m = next((e for e in mb if e["type"] == "INSERT"), {})
        poly_m = next((e for e in mb if e["type"] in ("POLYLINE", "LWPOLYLINE")), {})
        check("C3 model block: the nested reference keeps its 2 attributes", ins_m.get("ATTRIB") == 2, str(ins_m))
        check("C3 model block: the polyline keeps its vertices",
              poly_m.get("type") == "LWPOLYLINE" or poly_m.get("VERTEX", 0) >= 2, str(poly_m))
        record("C3 model block polyline in the definition", poly_m)
        ins_l = next((e for e in lb if e["type"] == "INSERT"), {})
        check("C3 layout block: nested reference keeps 2 attributes, polyline and text present",
              ins_l.get("ATTRIB") == 2 and any(e["type"] in ("LWPOLYLINE", "POLYLINE") for e in lb) and any(e["type"] == "TEXT" for e in lb), str(lb))
    except Exception as exc:     # noqa: BLE001
        check("C3 DXF copy readable", False, str(exc))


# ---------------------------------------------------------------- C4 C5 D1
def c4_c5_d1():
    if not pipe_handle:
        check("C4/C5/D1 network fixture", False)
        return
    section("C4 C5 part_swap guards")
    before = read("list_pipes", quiet=True, limit=500).get("pipes", [])
    refused("C4 part_swap with only network + family", [{"op": "part_swap", "network": NET, "family": P}], "give 'names' or 'handles'")
    after = read("list_pipes", quiet=True, limit=500).get("pipes", [])
    check("C4 nothing changed", json.dumps(before, sort_keys=True) == json.dumps(after, sort_keys=True))
    refused("C5 part_swap with a handle from another network", [{"op": "part_swap", "network": NET, "handles": [pipe2_handle], "family": P}],
            "is not a part of network")

    section("D1 pipe_set inner_diameter 300 in a metric drawing")
    p0 = next((x for x in after if x.get("handle") == pipe_handle), {})
    out = edit("D1 pipe_set inner_diameter 300", [{"op": "pipe_set", "handle": pipe_handle, "inner_diameter": 300}], must=False)
    p1 = next((x for x in read("list_pipes", quiet=True, limit=500).get("pipes", []) if x.get("handle") == pipe_handle), {})
    record("D1 pipe before", {k: p0.get(k) for k in ("partSize", "innerDiameterOrWidth")})
    record("D1 pipe after", {k: p1.get(k) for k in ("partSize", "innerDiameterOrWidth")})
    record("D1 edit reply", {"applied": out.get("applied"), "error": out.get("error"), "result": result(out), "warnings": warnings(out)})
    record("D1 catalogue sizes", pf[0]["sizes"])


# ---------------------------------------------------------------- C6 C7 D5
def c6_c7_d5():
    section("C6 feature_line_edit index checks")
    fl = f"FL {tag}"
    edit("C6 fixture: feature line", [{"op": "feature_line_create", "name": fl, "points": [[ox + 20, oy - 20, 10], [ox + 60, oy - 20, 11], [ox + 100, oy - 20, 12]]}])
    refused("C6 elevations index given as a string", [{"op": "feature_line_edit", "feature_line": fl, "elevations": [{"index": "2", "elevation": 10}]}],
            "'index' must be a whole number")
    refused("C6 elevations index missing", [{"op": "feature_line_edit", "feature_line": fl, "elevations": [{"elevation": 10}]}],
            "Missing whole-number parameter 'index'")
    refused("C6 elevations index 1.5", [{"op": "feature_line_edit", "feature_line": fl, "elevations": [{"index": 1.5, "elevation": 10}]}],
            "must be a whole number")

    section("C7 corridor_feature_line_export index 1.5 + D5 assembly chaining")
    asm, cor = f"ASM {tag}", f"COR {tag}"
    out = edit("D5 assembly BasicLane + BasicShoulder (right) and a corridor", [
        {"op": "assembly_create", "name": asm, "at": [ox, oy - 300], "subassemblies": [
            {"class": "BasicLane", "side": "Right"}, {"class": "BasicShoulder", "side": "Right"}]},
        {"op": "corridor_create", "name": cor, "alignment": al, "profile": f"{al} FG", "assembly": asm}])
    record("D5 assembly_create reply", result(out, 0))
    fls = read("get_corridor_feature_lines", corridor=cor, points=False)
    rows = fls.get("featureLines", []) if isinstance(fls, dict) else []
    record("D5 feature lines (code, side, offset range)",
           [(r.get("code"), r.get("side"), r.get("offset") or r.get("offsetRange") or [r.get("minOffset"), r.get("maxOffset")]) for r in rows])
    record("D5 codes in the sections", {k: fls.get(k) for k in ("pointCodes", "linkCodes", "shapeCodes") if k in fls})
    record("D5 assemblies", read("list_assemblies", quiet=True))
    code = next((r.get("code") for r in rows if r.get("code")), "ETW")
    refused("C7 corridor_feature_line_export index 1.5",
            [{"op": "corridor_feature_line_export", "corridor": cor, "code": code, "as": "polyline3d", "index": 1.5}], "must be a whole number")


# ---------------------------------------------------------------- C8 C9 D4
def c8_c9_d4():
    section("C8 relative paths refused")
    refused("C8 points_export relative path", [{"op": "points_export", "path": "out.txt", "format": "PENZ (comma delimited)"}], "must be a full path")
    refused("C8 points_import relative path", [{"op": "points_import", "path": "out.txt", "format": "PENZD (comma delimited)"}], "must be a full path")
    refused("C8 block_import relative path", [{"op": "block_import", "path": "out.dwg"}], "must be a full path")
    read_refused("C8 inspect_file relative path", "inspect_file", "must be a full path", path="out.dwg")

    section("C9 inspect_file with duplicate attribute tags")
    src = Path.home() / "Desktop" / "CIVIL 3D MCP" / "Ridge plan&Profile" / "XR.BASE.dwg"
    if src.exists():
        copy = TMP / "XR.BASE copy.dwg"
        shutil.copy2(src, copy)
        out = read("inspect_file", quiet=True, path=str(copy), limit=5)
        b = next((x for x in out.get("blocks", []) if x.get("name") == "BBAL2"), None)
        check("C9 inspect_file succeeds on a drawing with duplicate attribute tags (BBAL2)", "_error" not in out and b is not None,
              json.dumps(b, default=str)[:300] if b else out.get("_error", "BBAL2 not listed"))
    else:
        check("C9 XR.BASE.dwg available", False, str(src))

    section("D4 block_import replace:true and the drawing's layers / text styles")
    lay0, font0 = layers_by_name(), standard_font()
    out = edit("D4 block_import replace true (layer 0 colour 6, Defpoints 3, MCP-SHARED 5, STANDARD romans.shx)",
               [{"op": "block_import", "path": str(TMP / "attblock_v2.dxf"), "blocks": [BLK], "replace": True}], must=False)
    lay1, font1 = layers_by_name(), standard_font()
    changed = {k: (lay0.get(k), lay1.get(k)) for k in set(lay0) | set(lay1) if lay0.get(k) != lay1.get(k)}
    record("D4 edit reply", {"applied": out.get("applied"), "error": out.get("error"), "result": result(out), "warnings": warnings(out)})
    record("D4 layer colours before -> after (0, Defpoints, MCP-SHARED)", {k: (lay0.get(k), lay1.get(k)) for k in ("0", "Defpoints", "MCP-SHARED")})
    record("D4 layers that changed", changed)
    record("D4 STANDARD font before -> after", (font0, font1))
    blk = next((b for b in read("list_blocks", quiet=True, limit=2000).get("blocks", []) if str(b.get("name")).upper() == BLK.upper()), None)
    record("D4 block after replace", blk)


# ---------------------------------------------------------------- C10 C11 C12 C13 D8 (section views)
def c10_to_c13_d8():
    section("C11 pipe_ground_profile interval 0.001")
    t0 = time.time()
    out = read("pipe_ground_profile", quiet=True, network=NET, surface=eg, interval=0.001)
    dt = time.time() - t0
    pipes = out.get("pipes", []) if isinstance(out, dict) else []
    counts = [len(p.get("samples", [])) for p in pipes]
    check("C11 returns quickly with at most ~2000 samples per pipe", "_error" not in out and dt < 60 and counts and max(counts) <= 2002,
          f"{dt:.1f}s samples per pipe {counts} {out.get('_error', '')}")

    section("C12 sample_surface tiny interval")
    read_refused("C12 sample_surface interval 0.0001 refused", "sample_surface", "use a larger interval",
                 surface=eg, line=[[ox, oy], [ox + 200, oy]], interval=0.0001)

    section("C13 sample_lines_create over 5000 lines (dry run)")
    out = edit("C13 sample lines every 0.03 m on 200 m (dry run)", [
        {"op": "sample_lines_create", "alignment": al, "group": f"BIG {tag}", "interval": 0.03, "left_width": 5, "right_width": 5, "prefix": f"B{tag}-"}],
        dry_run=True, cap=1500)
    w = [x for x in warnings(out) if "stopped at 5000" in x]
    check("C13 a warning says where it stopped", bool(w), str(warnings(out))[:300])

    section("D8 repeat runs + C10 warning cap")
    grp = f"SLG {tag}"
    edit("fixture: 12 sample lines + section views", [
        {"op": "sample_lines_create", "alignment": al, "group": grp, "interval": 15, "left_width": 40, "right_width": 40, "prefix": f"SL{tag}-"},
        {"op": "section_views_create", "alignment": al, "group": grp, "at": [ox, oy - 700]}])
    views0 = len([v for v in read("list_section_views", quiet=True).get("sectionViews", []) if v.get("group") == grp])
    edit("D8 section_views_create again", [{"op": "section_views_create", "alignment": al, "group": grp, "at": [ox, oy - 1400]}], must=False)
    views1 = len([v for v in read("list_section_views", quiet=True).get("sectionViews", []) if v.get("group") == grp])
    record("D8 section_views_create twice: views", (views0, views1))

    # C10: a network with many parts crossing the sample lines, added twice to the views
    if P and S:
        n = 60
        structs = [{"id": f"S{i}", "name": f"S{i}-{tag}", "at": [ox + 5 + 3.2 * i, oy + 5, 11.0], "family": S, "size_index": 0} for i in range(n)]
        pipes_ = [{"name": f"B{i}-{tag}", "start": [ox + 5 + 3.2 * i, oy + 5, 8.0], "end": [ox + 5 + 3.2 * (i + 1), oy + 5, 8.0], "family": P,
                   "size_index": 0, "start_structure": f"S{i}-{tag}", "end_structure": f"S{i + 1}-{tag}"} for i in range(n - 1)]
        big = f"Big {tag}"
        edit("C10 fixture: network of 119 parts along the alignment", [
            {"op": "pipe_network_create", "name": big, "parts_list": key},
            {"op": "pipe_network_add", "network": big, "structures": structs, "pipes": pipes_}], cap=800)
        o1 = edit("C10 parts_to_section_views (first)", [{"op": "parts_to_section_views", "network": big, "alignment": al, "group": grp}], must=False, cap=800)
        o2 = edit("C10 parts_to_section_views (second, same parts)", [{"op": "parts_to_section_views", "network": big, "alignment": al, "group": grp}], must=False, cap=800)
        for label, o in (("first", o1), ("second", o2)):
            w = warnings(o)
            record(f"C10 {label} run", {"applied": o.get("applied"), "result": result(o), "warnings": len(w), "last": w[-1] if w else None})
        heavy = max((o1, o2), key=lambda o: len(warnings(o)))
        w = warnings(heavy)
        if len(w) > 200:
            check("C10 at most 200 warnings plus one '... more warning(s) not listed' line",
                  len(w) == 201 and "more warning(s) not listed" in w[-1], f"{len(w)} warnings, last: {w[-1]}")
        else:
            record("C10 cap not reached (fewer than 201 warnings)", len(w))

    # D8 labels

    for title, op in (("labels_add", {"op": "labels_add", "pipe_network": NET}),
                      ("network_styles with label styles", None)):
        if op is None:
            pls = read("list_styles", quiet=True, kind="pipe_label")
            sls = read("list_styles", quiet=True, kind="structure_label")
            pl_name = next((s.get("name") for s in pls.get("styles", [])), None)
            sl_name = next((s.get("name") for s in sls.get("styles", [])), None)
            if not (pl_name and sl_name):
                record("D8 network_styles skipped (no label styles listed)", (pls.get("_error"), sls.get("_error")))
                continue
            op = {"op": "network_styles", "network": NET, "pipe_label_style": pl_name, "structure_label_style": sl_name}
        c0 = count_types()
        edit(f"D8 {title} (1)", [op], must=False)
        c1 = count_types()
        edit(f"D8 {title} (2)", [op], must=False)
        c2 = count_types()
        record(f"D8 {title}: entity counts that changed (before, after 1st, after 2nd)",
               {k: (c0.get(k), c1.get(k), c2.get(k)) for k in set(c0) | set(c1) | set(c2) if not (c0.get(k) == c1.get(k) == c2.get(k))}
)

    # D8 volume tables twice
    edit("fixture: material list", [{"op": "material_list_create", "alignment": al, "group": grp, "name": f"EW {tag}", "materials": [
        {"name": "Earthwork", "items": [{"surface": eg, "condition": "base"}, {"surface": f"FG {tag}", "condition": "compare"}]}]}], must=False)
    t1 = edit("D8 section_volume_tables (1)", [{"op": "section_volume_tables", "alignment": al, "group": grp, "material_list": f"EW {tag}"}], must=False)
    t2 = edit("D8 section_volume_tables (2)", [{"op": "section_volume_tables", "alignment": al, "group": grp, "material_list": f"EW {tag}"}], must=False)
    record("D8 section_volume_tables twice", (result(t1), result(t2), warnings(t2)[:3]))
    check("D8 volume tables left nothing open for write", not any("still open for write" in x for x in warnings(t1) + warnings(t2)))


# ---------------------------------------------------------------- pressure: D2 D6 D8
def pressure():
    section("D6 D8 D2 pressure network")
    lists_ = (read("list_pressure_part_sizes", quiet=True) or {}).get("partsLists", [])
    lists_ = [l for l in lists_ if isinstance(l.get("pipeSizes"), int) and l["pipeSizes"] > 0]
    if not lists_:
        check("pressure parts list available", False)
        return
    best = max(lists_, key=lambda l: l["pipeSizes"])
    pl = best.get("name") or best["handle"]
    pn = f"PN {tag}"
    edit("fixture: pressure network with a bent run", [
        {"op": "pressure_network_create", "name": pn, "parts_list": pl, "reference_surface": eg},
        {"op": "pressure_pipe_run", "network": pn, "name": f"RUN {tag}", "points": [[ox, oy - 100], [ox + 60, oy - 100], [ox + 100, oy - 60]],
         "size": 150, "match": "ductile", "cover": 1.2}])
    out = read("network_interference", network1=pn, within=1.0)
    record("D6 network_interference on one pressure network",
           {"pairs": len(out.get("pairs", out.get("conflicts", [])) or []), "keys": list(out.keys()) if isinstance(out, dict) else out})


    c0 = count_types()
    edit("D8 pressure_labels_add (1)", [{"op": "pressure_labels_add", "pressure_network": pn}], must=False)
    c1 = count_types()
    edit("D8 pressure_labels_add (2)", [{"op": "pressure_labels_add", "pressure_network": pn}], must=False)
    c2 = count_types()
    record("D8 pressure_labels_add: entity counts that changed",
           {k: (c0.get(k), c1.get(k), c2.get(k)) for k in set(c0) | set(c1) | set(c2) if not (c0.get(k) == c1.get(k) == c2.get(k))})

    handles = [x["handle"] for x in read("list_pressure_pipes", quiet=True, limit=2000).get("pipes", [])]
    handles += [x["handle"] for x in read("list_pressure_fittings", quiet=True, limit=2000).get("fittings", [])]
    before = count_types()
    out = edit("D2 pressure_network_erase", [{"op": "pressure_network_erase", "name": pn}], must=False)
    after = count_types()
    record("D2 erase reply", result(out))
    record("D2 entity counts that changed", {k: (before.get(k), after.get(k)) for k in set(before) | set(after) if before.get(k) != after.get(k)})
    record("D2 get_objects on the old part handles", read("get_objects", quiet=True, handles=handles[:20]))
    record("D2 pressure networks left", [n.get("name") for n in read("list_pressure_networks", quiet=True).get("pressureNetworks", [])])


# ---------------------------------------------------------------- D3 rollback of current styles
def d3():
    section("D3 current style after a rolled-back batch")
    fail = {"op": "layer_set", "name": f"NO SUCH LAYER {tag}", "color": 1}
    for kind, op in (("text_style", {"op": "text_style_create", "name": f"TS {tag}", "set_current": True}),
                     ("dim_style", {"op": "dim_style_create", "name": f"DS {tag}", "set_current": True}),
                     ("mleader_style", {"op": "mleader_style_create", "name": f"ML {tag}", "set_current": True}),
                     ("annotation_scale", {"op": "annotation_scale_add", "scale": "1:777", "name": f"1:777 {tag}", "set_current": True})):
        cur0 = read("list_cad_styles", quiet=True, kind=kind).get("current")
        out = raw_edit([op, fail])
        r = read("list_cad_styles", quiet=True, kind=kind)
        cur1 = r.get("current")
        names = [s.get("name") for s in (r.get("items") or [])]
        record(f"D3 {kind}", {"rolledBack": out.get("applied") is False, "current before": cur0, "current after": cur1,
                               "current still exists": cur1 in names if names else None, "read error": r.get("_error")})
        # a later read of a drawing property that uses it
        draw = raw_edit([{"op": "draw_text", "text": "after rollback", "at": [ox + 500, oy, 0]}], dry_run=True)
        record(f"D3 {kind}: a dry-run draw_text afterwards", {"ok": not draw.get("error"), "error": draw.get("error")})


# ---------------------------------------------------------------- D7
def d7():
    section("D7 surface_solids_create overwrite when creation fails")
    empty = f"EMPTY {tag}"
    edit("fixture: empty surface", [{"op": "surface_create", "name": empty}])
    f = TMP / "old_solids.dwg"
    f.write_bytes(b"old file - must survive a failed creation")
    out = edit("D7 surface_solids_create depth on an empty surface, overwrite", [
        {"op": "surface_solids_create", "surface": empty, "mode": "depth", "depth": 1.0, "file": str(f), "overwrite": True}], must=False)
    record("D7 reply", {"applied": out.get("applied"), "error": out.get("error"), "result": result(out)})
    record("D7 old file after the failed creation", {"exists": f.exists(), "same content": f.exists() and f.read_bytes().startswith(b"old file")})


# ---------------------------------------------------------------- A4 A7
def a4_a7():
    section("A4 dry run then real edit with the same request_id")
    rid = str(uuid.uuid4())
    op = [{"op": "draw_line", "start": [ox + 600, oy, 0], "end": [ox + 610, oy, 0]}]
    d = raw_edit(op, request_id=rid, dry_run=True)
    show("A4 dry run", d)
    r = raw_edit(op, request_id=rid)
    show("A4 real", r)
    h = result(r).get("handle")
    check("A4 dry run succeeds", not d.get("error") and not d.get("exception"), str(d.get("error") or d.get("exception") or ""))
    check("A4 the real edit with the same request_id applies", r.get("applied") is True and h and find(entities(type_="LINE"), h) is not None,
          str(r.get("error") or r.get("exception") or ""))

    section("A7 check_default_names over 500 alignments")
    n = 510
    ops = [{"op": "alignment_create", "name": f"Alignment - ({9000 + i})", "points": [[ox + i, oy + 900], [ox + i, oy + 910]]} for i in range(n)]
    for part in (ops[:255], ops[255:]):     # an edit takes at most 500 operations
        out = raw_edit(part)
        show("A7 create alignments", {k: out.get(k) for k in ("applied", "error", "exception")})
    t0 = time.time()
    try:
        res = qa.check_default_names()
    except Exception as exc:     # noqa: BLE001
        res = {"_error": str(exc)}
    dt = time.time() - t0
    show("A7 check_default_names", {k: (v if k != "defaultNamed" else {kk: len(vv) for kk, vv in v.items()}) for k, v in res.items()})
    real = len(read("list_alignments", quiet=True).get("alignments", []))
    check("A7 totals show the real alignment count", res.get("totals", {}).get("alignments") == real and real >= n,
          f"totals={res.get('totals')} real={real} ({dt:.1f}s)")
    check("A7 all 510 default names flagged", len(res.get("defaultNamed", {}).get("alignments", [])) >= n)
    record("A7 truncated", res.get("truncated"))


BLOCKS = (c1_c2_c3, c4_c5_d1, c6_c7_d5, c8_c9_d4, c10_to_c13_d8, pressure, d3, d7, a4_a7)
only = set(sys.argv[1:])     # e.g. "audit_live.py c4_c5_d1 c10_to_c13_d8" runs just those blocks after the fixtures
for block in BLOCKS:
    if not only or block.__name__ in only:
        guarded(block)

print("\n==================== SUMMARY")
for k, v in RECORDS.items():
    print(f"RECORD {k}: {v}")
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
print(f"files: {TMP}")
