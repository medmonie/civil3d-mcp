# Roadmap

This file has two parts: what the Civil 3D MCP does today (v0.16.0), and what could be added next, with an effort estimate and a priority for each item.

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

## 1. What it does today (v0.18.0)

63 read actions and 101 edit operations, all through one tool (`civil3d`). Edits run as a single transaction: if anything fails, nothing is changed. A dry run executes and rolls back. Each edit names the drawing it is for and carries a request ID, so it cannot land in the wrong drawing or run twice. Editing needs permission per drawing (`MCPALLOW` or the dialog). Works with Claude, ChatGPT desktop and Codex; ChatGPT in a browser connects read-only. Full parameter list: [docs/ACTIONS.md](docs/ACTIONS.md).

| Area | Read | Create / edit |
|---|---|---|
| **Drawing (AutoCAD)** | Drawing info, layers, entity counts, selection, objects by handle, entities by type/layer, blocks, layouts | Layers. Move, copy, rotate, scale and erase objects. Lines, polylines, points, circles, arcs, text, mtext, hatches, dimensions, leaders, tables (in model space or on a layout). Blocks and attributes, blocks from other drawings, dynamic blocks. Save / save as |
| **Sheets and set-up (AutoCAD)** | Viewports, named views, xrefs, styles, layer states, annotation scales, page setups, block properties | Layouts from templates, page setups, viewports, named views, title blocks, **PDF: one layout, multi-sheet, or one per layout**. Text / dimension / multileader styles, linetypes, annotation scales, layer states, purge. Xrefs (attach, reload, detach, bind), PDF and image underlays |
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
| 1 | ~~Bill of quantities export~~ | → skill | Design work, not MCP: done by the client (a separate skill) from `list_pipes`, `get_part_data`, `pipe_ground_profile`. |
| 2 | ~~Trench and excavation quantities~~ | → skill | Same: computed by the client from `pipe_ground_profile` / `get_sections` and the project's trench detail. |
| 3 | ~~Sewer design checks report~~ | → skill | Same: the client compares MCP facts with the code or project criteria. The data it needs was added in 0.16. |
| 4 | ~~WaterGEMS import~~ | done | Done in 0.11.0. Next: exact PVC parts once the parts list has them, hydrant branches, reducers and crosses. |
| 5 | ~~**Civil 3D 2024 / 2025 support**~~ ✅ 0.12 | M–L | Done: one DLL per release (2024, 2025, 2027). 2026 still to build and test (.NET 8, same as 2025). |
| 6 | **Band table refresh** | S | Redraw the LISP-style band table for one line after the network or ground changes, without re-importing. |
| 7 | ~~Crossing and clearance check~~ ✅ 0.15 | M | `network_interference` gives the distance between pipes of two networks (gravity or pressure); the minimum is the client's criterion. |
| 8 | **SewerGEMS model vs drawing check** | M | After an import, compare the drawing's network (names, inverts, sizes, lengths) with the export and list differences, so later edits in Civil 3D can be checked against the model. |

### P1 — sheets and AutoCAD (done in 0.13, pending live test of each)

| # | Feature | Effort | Why / notes |
|---|---|---|---|
| 27 | ✅ 0.13 **Viewports** | M | Create and change viewports on a layout: size, position, scale (1:500…), centre or model window, rotation, lock, layers frozen per viewport. |
| 28 | ✅ 0.13 **Named views** | S | Save a model-space area as a view; build a viewport from it. |
| 29 | ✅ 0.13 **Title block from a file** | S | Bring a block (or a whole DWG) in from another file, place it on a layout and fill its attributes (title, sheet number, revision). Draw operations take a `layout` so text and blocks can go on paper space. |
| 30 | ✅ 0.13 **Layout from a template** | M | Copy a layout with its page setup, title block and viewports from a .dwt / .dwg; set device, paper, plot style and scale of a layout. |
| 31 | ✅ 0.13 **Multi-sheet PDF** | M | Several layouts into one PDF, or one PDF per layout, each with its own page setup. |
| 32 | ✅ 0.13 **AutoCAD styles** | M | Text styles, dimension styles, multileader styles, linetypes from .lin files, annotation scales. |
| 33 | ✅ 0.13 **Layer states, purge** | S | Save / restore layer states; purge unused layers, blocks, styles and linetypes. |
| 34 | ✅ 0.13 **Xrefs and underlays** | M | List xrefs with status; reload, detach, bind. Attach PDF and image underlays. |
| 35 | ✅ 0.13 **Dynamic blocks** | S | Read and set dynamic block properties (visibility, distances, flip). |

### P2 — sheets and AutoCAD, next

| # | Feature | Effort | Why / notes |
|---|---|---|---|
| 36 | ~~Plan and profile sheets~~ ✅ 0.14 | L | Frames along an alignment, one layout per frame with a rotated plan viewport and the matching profile view, match lines. Replaces #9 with an MCP-native version (Civil 3D's Plan Production is only partly in the API). |
| 37 | **Sheet index table** | S | Sheet number, title and revision across layouts, drawn as a table. |
| 38 | **Fields and data tables** | M | Fields in text (sheet number, file name, date) and tables built from object data (pipe schedules, coordinate tables). |
| 39 | **Geolocation / coordinate system** | S | Read and set the drawing's coordinate system. |
| 40 | **Audit** | S | Not in the .NET API; would need a command call. |
| 46 | **Multi-sheet PDF with mixed paper sizes** | M | AutoCAD's plot engine puts only same-size sheets in one PDF; Autodesk points to the Publish API (DSD) for mixed A1 / A0 sets. |
| 47 | **"Fit to paper" for a layout PDF** | S | `page_setup_set scale: "fit"` on a layout did not change the PDF in the live test (an A0 layout on A1 paper stayed 1:1). Same-size plotting is correct. |

### Civil 3D, added (done in 0.15)

| # | Feature | Effort | Priority | Done |
|---|---|---|---|---|
| 41 | ✅ 0.15 **Pipe and structure tables** | S | P2 | `network_table`: pipe, structure and pressure-pipe schedules drawn as AutoCAD tables. Civil 3D's own network tables are not in the .NET API, so the table is a snapshot: draw it again after changes. |
| 42 | ✅ 0.15 **Point files, point groups, description keys** | M | P2 | `points_import` (any point file format of the drawing, into a point group), `point_group_create` (queries with wildcards and ranges), `description_key_create`; reads `list_point_groups`, `list_point_file_formats`, `list_description_keys`. |
| 43 | ✅ 0.15 **Surface analysis** | M | P2 | `surface_analysis_set` (elevation, slope and slope-arrow ranges with colours, shown through a surface style), `surface_waterdrop`, `get_surface_analysis` (ranges, watersheds). Cut / fill: `surface_volume_create`. |
| 44 | ✅ 0.15 **Alignment from polyline, intersections, superelevation** | M–L | P2 | `alignment_from_polyline`, `alignment_offset_create`, `get_superelevation`, `superelevation_slope_set`, `list_intersections`. Creating intersections and calculating superelevation are commands only (not in .NET). |
| 45 | ✅ 0.15 **Interference check and hydraulic grade line** | M | P2 | `network_interference` (distances between pipes of two networks, gravity or pressure, or within one), `hgl_set`, `network_hgl` (HGL next to crown and rim; facts only since 0.16). Structures are not part of the clearance check yet. |

### MCP data access (0.16)

| # | Feature | Done |
|---|---|---|
| 48 | ✅ 0.16 **Facts-only reads** | `network_hgl`, `network_interference` and the WaterGEMS import no longer carry design limits. |
| 49 | ✅ 0.16 **Part data and user properties** | `get_part_data`, `list_user_properties`, `get_user_property_values`. |
| 50 | ✅ 0.16 **Section data** | `get_sections`, `get_corridor_section`. |
| 51 | ✅ 0.16 **Surface sampling** | `sample_surface`, `pipe_ground_profile`. |
| 52 | **Selection and view control** | Highlight, select or zoom to objects so the client can show what it found. |
| 53 | **Corridor feature lines and codes along a station range** | Beyond a single station. |
| 54 | ✅ 0.17 **Earthwork / material quantities** | `material_list_create`, `get_material_volumes`. |
| 55 | ✅ 0.17 **Vertical curves** | Curves in `profile_create`, `profile_pvis_set`, `get_profile_geometry`. |
| 56 | ✅ 0.17 **Full alignment geometry** | Spirals, station equations, design speeds, widenings, `get_alignment_geometry`. |
| 57 | ✅ 0.17 **Connected alignments** | `alignment_connected_create` (curb returns). |
| 58 | ✅ 0.17 **Feature line editing** | `feature_line_edit`, `get_feature_line`. |
| 59 | ✅ 0.17 **More labels** | `labels_place` (station-offset, geometry, surface, parcel, profile, note). |
| 60 | ✅ 0.17 **Several profile views** | `profile_views_create`. |
| 61 | ✅ 0.17 **User-defined property definitions** | `udp_create`. |
| 62 | ✅ 0.17 **Point export** | `points_export`. |
| 63 | ✅ 0.18 **Derived profiles** | `profile_derive` (corridor feature line, offset at a slope, static copy). |
| 64 | ✅ 0.18 **Corridor detail** | `corridor_baseline_add`, `corridor_region_edit`, `corridor_surface_set`. Slope patterns not yet. |
| 65 | ✅ 0.18 **Surface extraction** | `surface_extract`. |
| 66 | ✅ 0.18 **Network extras** | `pipe_curved_add`, `part_swap`, `parts_to_section_views`, `pressure_run_edit`. |
| 67 | ✅ 0.18 **Section volume tables** | `section_volume_tables`. |
| 68 | **Next from the API review** | Corridor feature-line export (alignment / polyline / COGO points), corridor targets and slope patterns, surface definition from contours / DEM / point groups, catchments, solids for BIM. |

### P2 — useful

| # | Feature | Effort | Why / notes |
|---|---|---|---|
| 9 | ~~Plan and profile sheets~~ | → #36 | Moved to the sheet section. |
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

The MCP gives facts and actions; design checks, quantities and code compliance are done by the AI client (as skills
kept outside this repository) with the project's criteria.

1. **Data access still missing:** #52 selection and view control, #53 corridor data along a station range, #11 LandXML, #13 data shortcuts.
2. **Round-trips:** #8 model vs drawing differences (facts), #6 band table refresh, #15 profile view labels for parts.
3. **Reach:** #5 Civil 3D 2026 (2024 / 2025 done in 0.12).

Effort figures are estimates. Anything marked XL depends on what Autodesk adds to the API.
