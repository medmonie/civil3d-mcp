# Roadmap

This file has two parts: what the Civil 3D MCP does today (v0.9.0), and what could be added next, with an effort estimate and a priority for each item.

**Effort** (one developer, including the live test in Civil 3D):

| Size | Meaning |
|---|---|
| S | under 1 day |
| M | 1–3 days |
| L | 1–2 weeks |
| XL | more than 2 weeks, or depends on an API Autodesk does not expose yet |

**Priority** for an infrastructure design office (sewer, water, roads):

| Priority | Meaning |
|---|---|
| P1 | High value, unblocks real project work. Do next. |
| P2 | Useful, saves time on common tasks. |
| P3 | Nice to have, niche, or blocked by the Civil 3D API. |

---

## 1. What it does today (v0.9.0)

40 read actions and 68 edit operations, all through one tool (`civil3d`). Edits run as a single transaction: if anything fails, nothing is changed. Editing needs `MCPALLOW` in Civil 3D. Full parameter list: [docs/ACTIONS.md](docs/ACTIONS.md).

| Area | Read | Create / edit |
|---|---|---|
| **Drawing (AutoCAD)** | Drawing info, layers, entity counts, selection, objects by handle, entities by type/layer, blocks, layouts | Layers. Move, copy, rotate, scale and erase objects. Lines, polylines, points, circles, arcs, text, mtext, hatches, dimensions, leaders, tables. Blocks and attributes. Layouts, xrefs, save / save as, **plot to PDF** |
| **Terrain** | Surfaces (with elevation and volume), COGO points, feature lines, parcels | COGO points. TIN surfaces from points, breaklines and boundaries. Volume surfaces, feature lines, rebuild |
| **Alignments and profiles** | Alignments, station ↔ point, profiles, profile elevation, profile views, sample lines, section views | Alignments with curves. Profiles from PVIs or from a surface. Profile views (style, ranges, bands), parts shown in views. Sample lines, section views |
| **Corridors** | Corridors, assemblies | Assemblies, corridors, corridor surfaces, rebuild |
| **Gravity networks** | Networks, pipes, structures, parts lists | Networks, pipes and structures (named), edit parts, add families to parts lists, labels |
| **Pressure networks** | Networks, pipes, fittings, appurtenances, pipe runs, part sizes | Networks, parts, pipe runs with cover, edit parts, labels |
| **Styles and presentation** | Styles of every kind, and what a style contains | Pipe/structure label styles (text and flow arrow), network styles, alignment label sets, profile view setup, band sets on views, band height |
| **QA** | Layer naming check, default-name check | — |
| **SewerGEMS import** | `sewergems_check` validates the export | `sewergems_import`: network with SewerGEMS names and inverts, one alignment + ground profile + profile view per line, plan styling, LISP-style band table |

**Known Civil 3D API limits.** Autodesk's .NET API offers no way to do these, so the MCP can't either:

- Create gradings or parcels.
- Define what a band style labels.
- Set the text of band titles.

---

## 2. What can be added

Effort and priority for each item. **Why** says what it saves or unblocks.

### P1 — do next

| # | Feature | Effort | Why / notes |
|---|---|---|---|
| 1 | **Bill of quantities export** (.xlsx) | M | Pipe length by diameter and material, manholes by depth band, fittings and valves by type, from any gravity or pressure network. The most-asked deliverable after the drawing itself. |
| 2 | **Trench and excavation quantities** | M | Excavation, bedding, backfill and surplus volumes per pipe from invert, ground surface, trench width and side slope. Reported by depth band (0–2, 2–4, 4–6 m…) for the BOQ. |
| 3 | **Sewer design checks report** | M | Minimum/maximum slope, minimum cover, maximum depth, velocity limits from the SewerGEMS results, pipe size never decreasing downstream, drop manholes. Rules set by parameters, e.g. the Egyptian code values. |
| 4 | **WaterGEMS import** (pressure networks) | M | Same workflow as SewerGEMS, for water lines: pipes, junctions, valves and hydrants as a pressure network, with profile views. |
| 5 | **Civil 3D 2025 / 2026 support** | M–L | One build per version, each against that version's API references and .NET runtime. Most offices run more than one version. |
| 6 | **Band table refresh** | S | Redraw the LISP-style band table for one line after the network or ground changes, without re-importing. |
| 7 | **Crossing and clearance check** | M | Find where gravity and pressure pipes cross, report the vertical clearance at each crossing and flag those below a set minimum. |
| 8 | **Dry run for edits** | S | Validate a batch and report what it would change, then roll back. Lets you review large edits before committing them. |

### P2 — useful

| # | Feature | Effort | Why / notes |
|---|---|---|---|
| 9 | **Plan and profile sheets** (view frames, sheet layouts) | L | Sheet generation through Plan Production. The API covers part of it; the rest needs layouts and viewports set up by code. |
| 10 | **CAD to network** | M | Turn polylines and blocks from a consultant's CAD into a gravity network, using block positions and attribute levels. |
| 11 | **LandXML import / export** | M | Exchange surfaces, alignments and networks with other software and authorities. |
| 12 | **Surface from contours / DEM** | M | Build a surface from contour polylines with their elevations, or from a DEM file. |
| 13 | **Data shortcuts** | M | Create and reference data shortcuts so a network or surface can be shared across drawings. |
| 14 | **Corridor targets** | M | Width and elevation targets (edge of pavement, ditch) from alignments, profiles and polylines. |
| 15 | **Profile view labels for parts** | S | Pipe labels (size, slope, length) and structure labels (name, rim, inverts) inside profile views. |
| 16 | **Earthwork volumes for corridors** | M | Cut and fill between existing ground and the corridor surface, by station range. |
| 17 | **Batch drawings** | M | Run the same read or QA action over every drawing in a folder, e.g. checking layer standards on a submittal set. |
| 18 | **Block attribute round-trip test** | S | Live test of block definition, insertion and attribute editing (already coded, not yet tested live). |
| 19 | **More QA checks** | S each | Objects on layer 0, unreferenced styles, structures with no pipes, pipes with no end structure, out-of-date corridors and surfaces. |

### P3 — later or blocked

| # | Feature | Effort | Why / notes |
|---|---|---|---|
| 20 | **Section view volume tables and mass haul** | L | Material lists and a mass haul diagram for road jobs. Large API surface. |
| 21 | **Automated tests on GitHub** | S | Run the smoke test (catalogue, docs, version) on every push. A live test still needs Civil 3D. |
| 22 | **Signed installer and update check** | M | Removes the Windows and Civil 3D trust prompts; tells you when a new release is out. |
| 23 | **Shared core with the Revit MCP** | L | One server package and one installer for Civil 3D and Revit. |
| 24 | **Grading** | XL | Not in the .NET API. A workaround through feature lines and the COM API is possible but fragile. |
| 25 | **Parcel creation** | XL | Not in the .NET API. Only parcels drawn by a user can be read. |
| 26 | **Native band content** | XL | Bands can be attached and their height set, but not what they label. Use a template band style, or the drawn band table. |

---

## Suggested order

1. **Quantities:** #1 BOQ, #2 trench quantities, #3 design checks. All three reuse the network data the MCP already reads, so they pay back fastest.
2. **Water side:** #4 WaterGEMS import, #7 crossings.
3. **Reach:** #5 Civil 3D 2025/2026.
4. Small items (#6, #8, #15, #18) can be added in between.

Effort figures are estimates. Anything marked XL depends on what Autodesk adds to the API.
