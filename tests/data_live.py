"""Live test of the 0.16 data reads (run_data.bat): part data, user-defined properties, sample-line sections, corridor
sections, surface sampling and the ground along pipes.

Run it in a NEW drawing from the Civil 3D metric template (Drawing1, after MCPALLOW); nothing is saved. It builds its own
surface, alignment, profiles, sample lines, a small corridor and a gravity network, then reads them back and checks the
numbers against what was built. Output: tests\\results\\data.txt
"""
import json
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import EDIT_TIMEOUT, call  # noqa: E402

RESULTS: dict[str, str] = {}
TARGET = None


def check(name, ok, detail=""):
    RESULTS[name] = "PASS" if ok else "FAIL"
    print(f"{RESULTS[name]:5} {name} {detail}", flush=True)


def show(title, out):
    text = json.dumps(out, indent=1, default=str)
    print(f"===== {title}\n{text if len(text) < 2500 else text[:2500] + chr(10) + '... (truncated)'}", flush=True)
    return out


def read(method, **params):
    try:
        return show(method, call(method, {**params, "target": TARGET}))
    except Exception as exc:     # noqa: BLE001
        print(f"===== {method}\nFAILED: {exc}", flush=True)
        check(f"read {method}", False, str(exc))
        return {}


def edit(title, ops):
    try:
        out = call("edit", {"operations": ops, "target": TARGET, "request_id": str(uuid.uuid4())}, timeout=EDIT_TIMEOUT)
    except Exception as exc:     # noqa: BLE001
        out = {"applied": False, "exception": str(exc)}
    show("edit: " + title, out)
    bad = [f"{r.get('op')}: {r.get('error')}" for r in out.get("results", []) if r.get("error")]
    check(title, out.get("applied") is True, "; ".join(bad) or str(out.get("error") or out.get("exception") or ""))
    return out


def result(out, i=0):
    try:
        return out["results"][i]["result"]
    except (KeyError, IndexError, TypeError):
        return {}


TARGET = call("get_drawing_info", {}).get("target")
tag = uuid.uuid4().hex[:4]
ox, oy = 5000.0, 5000.0          # away from other test data

# ---------------------------------------------------------------- a sloping plane: z = 10 + 0.02 x' (x' from ox)
grid = [[ox + i * 20, oy + j * 20, 10 + 0.02 * i * 20] for i in range(11) for j in range(-3, 4)]
surf, al = f"EG {tag}", f"AL {tag}"
edit("surface", [{"op": "surface_create", "name": surf, "points": grid}])

# ---------------------------------------------------------------- sample_surface
pts = read("sample_surface", surface=surf, points=[[ox + 50, oy], [ox + 150, oy + 10], [ox - 500, oy]])
el = [p["elevation"] for p in pts.get("points", [])]
check("elevations at points", len(el) == 3 and abs(el[0] - 11.0) < 0.01 and abs(el[1] - 13.0) < 0.01 and el[2] is None, str(el))
ln = read("sample_surface", surface=surf, line=[[ox, oy], [ox + 200, oy]], interval=50)
check("every 50 m along a line", [s["chainage"] for s in ln.get("samples", [])] == [0, 50, 100, 150, 200]
      and abs(ln["samples"][-1]["elevation"] - 14.0) < 0.01, str(ln.get("samples"))[:300])
bp = read("sample_surface", surface=surf, line=[[ox + 10, oy + 5], [ox + 190, oy + 5]])
check("surface breakpoints along a line", bp.get("count", 0) >= 2, str(bp.get("count")))

# ---------------------------------------------------------------- gravity network, part data, ground along pipes
lists = read("list_gravity_parts").get("partsLists", [])
plist = max(lists, key=lambda l: l.get("partFamilies") or 0)
key = plist.get("name") or plist["handle"]
parts = read("list_gravity_parts", parts_list=key)
pf = [f for f in parts.get("pipeFamilies", []) if f.get("sizes")]
sf = [f for f in parts.get("structureFamilies", []) if f.get("sizes") and f.get("partType") == "StructJunction"]
net = f"Net {tag}"
if pf and sf:
    P, S = pf[0]["family"], sf[0]["family"]
    edit("gravity network", [
        {"op": "pipe_network_create", "name": net, "parts_list": key},
        {"op": "pipe_network_add", "network": net,
         "structures": [{"id": "M1", "name": "M1", "at": [ox + 20, oy, 11.0], "family": S, "size_index": 0},
                        {"id": "M2", "name": "M2", "at": [ox + 120, oy, 13.0], "family": S, "size_index": 0}],
         "pipes": [{"name": "P1", "start": [ox + 20, oy, 8.0], "end": [ox + 120, oy, 9.0], "family": P, "size_index": 0,
                    "start_structure": "M1", "end_structure": "M2"}]},
    ])
    pdat = read("get_part_data", network=net)
    by = {x["name"]: x for x in pdat.get("parts", [])}
    check("part data for pipe and structures", {"P1", "M1", "M2"} <= set(by), str(list(by)))
    if "P1" in by:
        f = {x["name"]: x for x in by["P1"]["fields"]}
        check("pipe has catalogue fields", len(f) >= 5, str(list(f)[:15]))
    only = read("get_part_data", network=net, kind="pipes", fields=["PipeInnerDiameter", "InnerDiameterOrWidth"])
    check("field filter", all(len(x["fields"]) <= 2 for x in only.get("parts", [])) and only.get("count") == 1, str(only.get("parts"))[:300])
    gp = read("pipe_ground_profile", network=net, surface=surf, interval=25)
    row = next((x for x in gp.get("pipes", []) if x["pipe"] == "P1"), None)
    if row:
        ch = [s["chainage"] for s in row["samples"]]
        check("samples every 25 m and at the end", ch == [0, 25, 50, 75, 100], str(ch))
        s0, s4 = row["samples"][0], row["samples"][-1]
        # ground: 10 + 0.02*20 = 10.4 at the start, 12.4 at the end
        check("ground under the pipe", abs(s0["ground"] - 10.4) < 0.01 and abs(s4["ground"] - 12.4) < 0.01, f"{s0['ground']} {s4['ground']}")
        check("cover = ground - outside top", abs(s0["cover"] - (s0["ground"] - s0["outsideTop"])) < 0.002 and s0["outsideTop"] > s0["centre"] > s0["invert"], str(s0))
        check("min / max cover", row["minCover"] <= row["maxCover"], f"{row['minCover']} {row['maxCover']}")
else:
    check("gravity families available", False)

# ---------------------------------------------------------------- alignment, profiles, sample lines, sections
edit("alignment + profiles + sample lines", [
    {"op": "alignment_create", "name": al, "points": [[ox, oy], [ox + 200, oy]]},
    {"op": "profile_from_surface", "alignment": al, "surface": surf, "name": f"{al} EG"},
    {"op": "profile_create", "alignment": al, "name": f"{al} FG", "pvis": [[0, 10.5], [200, 14.5]]},
    {"op": "sample_lines_create", "alignment": al, "group": f"SLG {tag}", "stations": [50, 100, 150]},
])
out = edit("sample the ground", [{"op": "sample_sources_set", "alignment": al, "group": f"SLG {tag}", "sources": [surf]}])
check("ground source sampled", surf in result(out).get("changed", []), str(result(out)))
secs = read("get_sections", alignment=al)
sl = secs.get("sampleLines", [])
check("three sample lines", [s["station"] for s in sl] == [50, 100, 150], str([s.get("station") for s in sl]))
if sl:
    eg = next((x for x in sl[1]["sections"] if x["surface"] == surf), None)
    check("ground section sampled", eg is not None and eg["pointCount"] >= 2, str(sl[1]["sections"])[:300])
    if eg and eg["points"]:
        mid = min(eg["points"], key=lambda p: abs(p[0]))
        check("section spans the sample line width", min(p[0] for p in eg["points"]) <= -19.9 and max(p[0] for p in eg["points"]) >= 19.9,
              f"{eg['points'][0]} .. {eg['points'][-1]}")
        # station 100 -> x' = 100 -> z = 12.0 everywhere across (the plane slopes along the alignment only)
        check("section elevation matches the surface", abs(mid[1] - 12.0) < 0.05, str(mid))
part = read("get_sections", alignment=al, from_station=90, to_station=160)
check("station range filter", [s["station"] for s in part.get("sampleLines", [])] == [100, 150])

# ---------------------------------------------------------------- corridor section
edit("assembly + corridor", [
    {"op": "assembly_create", "name": f"ASM {tag}", "at": [ox, oy - 300], "subassemblies": [
        {"class": "LaneSuperelevationAOR", "side": "Right"}, {"class": "LaneSuperelevationAOR", "side": "Left"}]},
    {"op": "corridor_create", "name": f"COR {tag}", "alignment": al, "profile": f"{al} FG", "assembly": f"ASM {tag}"},
])
cs = read("get_corridor_section", corridor=f"COR {tag}", station=100)
check("corridor section has points and links", len(cs.get("points", [])) >= 4 and len(cs.get("links", [])) >= 2,
      f"{len(cs.get('points', []))} points, {len(cs.get('links', []))} links, station {cs.get('station')}")
crown = [p for p in cs.get("points", []) if "Crown" in p["codes"]]
if crown:
    # design profile at station 100: 10.5 + 4 * 100 / 200 = 12.5
    check("crown on the design profile", abs(crown[0]["elevation"] - 12.5) < 0.05 and abs(crown[0]["offset"]) < 0.01
          and abs(crown[0]["aboveProfile"]) < 0.01, str(crown[0]))
edge = [p for p in cs.get("points", []) if "ETW" in p["codes"]]
if edge:
    check("edge of lane at 3.6 m, 2 % below the crown", any(abs(abs(p["offset"]) - 3.6) < 0.01 and abs(p["elevation"] - (12.5 - 0.072)) < 0.01 for p in edge), str(edge))
top = read("get_corridor_section", corridor=f"COR {tag}", station=100, codes=["Top"])
check("code filter", top.get("links") and all("Top" in l["codes"] for l in top["links"]), str(top.get("links"))[:200])

# ---------------------------------------------------------------- user-defined properties
udp = read("list_user_properties", kind="point")
check("point classifications listed", "classifications" in udp, str(udp.get("count")))
vals = read("get_user_property_values", kind="point", limit=5)
check("point property values read", "items" in vals, str(vals.get("count")))
pud = read("list_user_properties", kind="parcel")
check("parcel classifications listed", "classifications" in pud)

# ---------------------------------------------------------------- facts-only reads (0.16 clean-up)
if pf and sf:
    h = read("network_hgl", network=net)
    check("network_hgl has no pass/fail flags", "flags" not in h and h.get("pipes") and "hglAboveCrownUp" in h["pipes"][0], str(list(h.keys())))
    ni = read("network_interference", network1=net, within=2.0)
    check("network_interference returns pairs with 'within'", "pairs" in ni and ni.get("within") == 2.0, str(list(ni.keys())))

print("\nSUMMARY")
for k, v in RESULTS.items():
    print(f"{v:5} {k}")
print("ALL PASS" if RESULTS and all(v == "PASS" for v in RESULTS.values()) else "FAILED", flush=True)
