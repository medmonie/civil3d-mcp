# Changelog

## 0.17.0

Road geometry, earthwork quantities and plan-production additions from the Civil 3D API review. 67 read actions, 110 edit operations. Live test: `tests\run_geometry.bat` (ALL PASS on Civil 3D 2027).

**Quantities**
- `material_list_create`: a material list on a sample line group, materials from surfaces (base / compare / above / below) and corridor shapes, with factors and the volume method. The surfaces are switched to sampled.
- `get_material_volumes`: cut / fill (earthwork) or material area at each sample line, incremental and cumulative volumes, totals. Civil 3D's own quantity results when it has computed them; otherwise surface materials are computed from the TINs across each sample line by average end area (`source: computed`). Corridor-shape materials need Civil 3D's Compute Materials.

**Profiles**
- `profile_create` PVIs accept vertical curves: symmetric parabola by `length` or `k`, circular by `radius`, asymmetric by `lengths`.
- `profile_pvis_set`: add, change (elevation or curve) or remove PVIs of a layout profile.
- `get_profile_geometry`: PVIs with grades, tangents with grade, curves with length, K, grade change and high / low point.

**Alignments**
- `alignment_create`: spiral-curve-spiral at each PI (`spiral_in`, `spiral_out`, `spiral_type`) and per-PI `curves`.
- `alignment_set`: station equations, design speeds, widenings of offset alignments.
- `alignment_connected_create`: connected alignment (curb return) between two alignments; the building block of an intersection.
- `get_alignment_geometry`: every line, curve and spiral with stations, radius, delta, PI, A-value; station equations, design speeds, offset and connected parents.

**Feature lines**
- `feature_line_edit`: elevations from a surface, at vertices or raised; PIs and elevation points inserted or deleted; curve radii.
- `get_feature_line`: vertices with plan distance, elevation, grades and radius.

**Plan production and data**
- `labels_place`: station-offset, alignment geometry (every line / curve / spiral), surface spot elevation, slope and contour labels, parcel area, profile PVI / crest / sag / tangent, station-elevation in a profile view, note labels.
- `profile_views_create`: several profile views of one alignment, `length` stations each, in rows or columns.
- `udp_create`: point or parcel user-defined property definitions (string, double with data type, integer, boolean, enum; defaults and bounds).
- `points_export`: COGO points (all or one point group) to a file in a point file format, after commit.

## 0.16.0

The MCP is kept to facts and actions; design judgement (codes, project criteria, quantities) belongs to the AI client. This release removes the built-in limits and adds the raw data design checks need. 63 read actions, 101 edit operations.

**Clean-up (facts only)**
- `network_hgl` no longer flags flooding or surcharge and has no `freeboard`. It returns each pipe's crown at both ends (by flow) and each structure's rim next to the HGL, with the differences `hglAboveCrownUp/Down` and `hglBelowRim`.
- `network_interference`: `clearance` (a design limit, default 0.3 m) becomes `within`, a search distance (default 1.0 m; `clearance` still accepted). The reply lists `pairs` with their gap and `overlap`, and counts `overlaps`; no conflicts or clashes are judged.
- WaterGEMS import: the build assumptions that were constants are now parameters and are reported under `assumptions`: `part_end_clearance` (2 m), `stand_in_od_factor` (1.15), `elbow_min_deflection` (5°), `elbow_tolerance` (6°). The cover check is described as a check of the requested `cover`, not of any code.

**New data reads**
- `get_part_data`: every catalogue field of the parts of a gravity or pressure network (wall thickness, material, sizes, custom fields) with units; filter by kind, part and field.
- `list_user_properties`, `get_user_property_values`: user-defined property classifications and values of COGO points and parcels.
- `get_sections`: ground sections at the sample lines of an alignment, by station range: each TIN surface sampled along each sample line as [offset, elevation] (read from the surface itself; Civil 3D's section objects returned no points through the API in testing). `sample_sources_set` (new edit operation) chooses which sources a sample line group samples, so sections exist. `sample_lines_create` now draws each line across the alignment (`left_width` / `right_width`, default 20 m); a line made from a station alone had no width and sampled nothing.
- `get_corridor_section`: a corridor's points, links and shapes (codes, offset, absolute elevation, height above the profile, areas) at the nearest frequency station.
- `sample_surface`: elevations at up to 20,000 points, or along a polyline / line (surface breakpoints or a fixed interval).
- `pipe_ground_profile`: ground, invert, centre, outside top and cover along every pipe of a network at a set interval, with min / max cover per pipe.
- `tests\run_data.bat`: live test of these reads in a new drawing.

**Robustness**
- **One target with several Civil 3D windows.** Each instance announces itself in `bridge-<pid>.json`; a Civil 3D started while another is running no longer takes over the MCP connection silently. `MCPSTATUS` switches on purpose, and when the target closes another running instance takes over. `ping` reports `pid`, `isTarget` and `otherInstances`.
- `points_import` refuses a file whose point numbers the drawing already has (re-importing them could crash Civil 3D).
- `get_sections` no longer calls `SampleLineGroup.GetSectionSources` (needs write access and crashed Civil 3D inside a read); default surfaces come from the sample lines' existing sections.
- Live test fixture for `network_interference` uses a circular pipe family.

## 0.15.1

Fixes from a ChatGPT review of 0.15.0:

- `network_interference`: pipes with an open end (no structure) were wrongly skipped as "sharing a structure" when a network was checked against itself, so crossings could be missed. Naming the same network twice (any letter case) now counts as one network. Only straight circular pipes are measured; curved or segmented pressure pipes and box / egg-shaped pipes are listed in `excludedPipes`, and `complete` says whether every pipe was checked.
- `network_table` and `network_hgl` use the pipe's real inner height for non-circular pipes (box, egg) instead of its width, and follow each pipe's flow direction: inverts in / out at a structure and HGL up / down are by flow, not by drawing direction; bidirectional pipes appear on both sides.
- `tests\run_civil.bat` adds an open-ended crossing and a box culvert case.

## 0.15.0

Civil 3D gaps from the roadmap (#41–45): 8 read actions and 10 edit operations (56 / 100 in all), live-tested in Civil 3D 2027 (`tests\run_civil.bat` in a new drawing, ALL PASS) and built for 2024, 2025 and 2027.

- **Network tables.** `network_table` draws a pipe, structure or pressure-pipe schedule as an AutoCAD table: from / to, size, material, length, slope and inverts for pipes; rim, sump, depth and the inverts in and out for structures. Civil 3D's own network tables cannot be created through .NET, so the table is a snapshot.
- **Survey points.** `points_import` reads a point file with any of the drawing's point file formats, optionally into a point group (description keys apply). `point_group_create` makes a point group from include / exclude filters (numbers, raw or full descriptions, names, elevations, wildcards), with optional style overrides. `description_key_create` adds a key (code, format, point style, label style, layer) to a key set. Reads: `list_point_groups`, `list_point_file_formats`, `list_description_keys`; style kind `point_label`.
- **Surface analysis.** `surface_analysis_set` sets elevation, slope or slope-arrow ranges (given, or N equal ranges) with colours and can turn that display on in a surface style. `surface_waterdrop` draws flow paths downhill. `get_surface_analysis` reads the ranges and watersheds.
- **Alignments.** `alignment_from_polyline` (with curves between tangents), `alignment_offset_create`, `get_superelevation` (curves, critical stations, slope of every lane and shoulder), `superelevation_slope_set`, `list_intersections`.
- **Hydraulics and clearances.** `hgl_set` writes HGL / EGL and flow to pipes and structures; `network_hgl` reads them and flags HGL above a rim (or within a freeboard) and above a pipe crown. `network_interference` finds pipe pairs of two networks (gravity or pressure, or one network against itself) closer than a clearance, with the gap, location and which pipe is above.

## 0.14.2

- Profile views made by the WaterGEMS import get bands again. `profile_view_create` takes `band_set: "auto"` (the WaterGEMS default): 'Standard' when it has bands, otherwise the band set with "Elevation" in its name and the fewest bands (in Civil 3D's metric template: EG-FG Elevations and Stations). That template has no 'Standard' band set, so views fell back to its first set, 'Stations Only'. The reply lists the band set and the bands, and warns when a view still has none.
- Profile data bands without a profile get the alignment's first profile (the ground), and its second profile when there is one, so they show values.
- `profile_view_bands` takes `band_set` to put every band of a set on an existing profile view; use it to add bands to views made earlier.
- SewerGEMS import takes `band_set` too (by default it keeps Standard and its drawn band table).
- `tests\run_bands.bat` checks the band sets of the open drawing and, in a dry run, that new and existing profile views get bands.

## 0.14.1

- The WaterGEMS live test verifies the active DWG's full path after Save As and stops before importing if the save fails, produces a warning, or leaves the source active. The bridge reports post-commit failures separately from the drawing transaction.
- Plan/profile sheets split at the smaller of the configured length and the available profile viewport width. Fitted plans use each split sheet's station interval, mapped through the Civil 3D profile view and alignment. Sheet previews and creation results include the interval.
- Sheet creation reports success only when every requested layout has its required viewports and all content transactions committed. Results identify completed, interrupted and pending layouts.
- Layouts are copied and filled one at a time. Long line lists are written in bounded transactions. Repeating the original request with the same UUID and drawing in the same bridge session resumes after a missing viewport, a rolled-back transaction or a lost reply. Changed plans require a new UUID.
- Offline regression coverage includes narrow viewports, split-sheet plans, missing copied viewports, a 100-sheet run, 1,050 line labels on one layout, lost replies, and refusal to import into an unverified DWG.

## 0.14.0

Plan and profile sheets from an office template, live-tested in Civil 3D 2027 on the Ridge ICD template
(`tests\run_pp_sheets.bat`, ALL PASS; the four-sheet PDF checked page by page).

- **`pp_sheets_plan` / `pp_sheets_create`.** Numbered layouts from a template sheet: profile viewport on the sheet's
  profile views (views mode, split above `max_length`, default 500 m) or on stacked plain-CAD profile rows (rows mode,
  `row_spacing` default 500 m); plan viewport zoomed to the sheet's alignments at a standard scale; list of lines,
  sheet label `( nn / NN )` and scale bar updated; key-plan box moved only when the plan area is inside the key plan.
- **`inspect_file`** reads a drawing without opening it: layouts, viewports, paper-space entities and attributes,
  xrefs, blocks, views, page setups.
- `list_entities` / `count_entities` take `layout`; `list_alignments` returns each alignment's `extents`;
  `viewport_set` takes `on`.
- `layout_from_template`: xrefs with relative paths are re-pathed to the template's folder; the template's viewports are
  re-created (viewports copied from another drawing stay inactive and plot blank) and switched on with the layout
  current. Replacing the current layout leaves it first.
- `viewport_set` changes a viewport with its layout current, so the change is not undone by the layout's viewport cache
  when the layout is next shown or plotted.

## 0.13.0

Sheets and AutoCAD set-up: 5 read actions and 21 edit operations (46 / 90 in all), live-tested in Civil 3D 2027 (`tests\run_sheets.bat`, 42 checks, ALL PASS) and type-checked against the 2024 and 2025 APIs.

- **Sheets.** `viewport_create` / `viewport_set` (window fitted or 1:N, named view, twist, lock, layers frozen per viewport), `view_create`, `layout_from_template` (a template layout with its page setup, title block and viewports), `layout_delete`, `page_setup_set` (named page setup, device, paper, plot style, rotation, scale), `title_block_insert`, `block_import`. Every draw operation takes `layout` to draw on paper space. Reads: `list_viewports`, `list_views`.
- **PDF.** `plot_pdf` plots several layouts into one multi-sheet PDF (`layouts`, or `"all"`) or one PDF per layout (`folder`). Each layout keeps its own paper and scale (switching to the PDF plotter used to fall back to Letter). Layouts are made current before they are plotted, as AutoCAD requires. Mixed paper sizes in one PDF are refused up front with the reason (plot-engine limit).
- **AutoCAD set-up.** `text_style_create`, `dim_style_create`, `mleader_style_create` (and `style` on text, dimensions and leaders), `linetype_load`, `annotation_scale_add`, `layer_state_save` / `layer_state_restore`, `purge`. Read: `list_cad_styles` (text, dimension, multileader and table styles, linetypes, layer states, annotation scales, page setups).
- **Xrefs and underlays.** `xref_reload`, `xref_detach`, `xref_bind` (after the commit), `underlay_attach` (PDF page or image). Read: `list_xrefs`.
- **Blocks.** `dynamic_block_set`; read `get_block_properties` (attributes and dynamic properties).
- New layouts are created without Civil 3D's automatic viewport and initialised at once, so viewports added in the same batch are real viewports.

## 0.12.1

Fixes from an external review (ChatGPT/Codex test run), each with an offline regression test in `tests/watergems_test.py`:

- WaterGEMS parts: a stand-in pipe is never smaller than the WaterGEMS bore (PVC Ø200 was drawn as DN150; now DN250). A PRV uses a pressure-reducing-valve part when the parts list has one; a gate-valve stand-in is listed in `standIns`. Laying depth allows for the stand-in's size, and the reply has `coverCheck`: the cover of every built piece from its real outside diameter and elevations against the WaterGEMS ground.
- WaterGEMS failures are explicit: `profile_views_at` is checked before anything is drawn, and once the network is committed every reply says `networkCommitted: true` with the state of each stage (`stages`) and the error, including stages the bridge rolled back.
- WaterGEMS retries go to the bridge, which compares the request (operations and `dry_run`) with the recorded one; the importer no longer returns a recorded result itself.
- WaterGEMS input: rows of one element that disagree on position, elevation, nodes, size or material are a problem (repeated time-step rows are still accepted). A CAD layer with more entities than can be read is refused instead of read in part (`list_entities` now returns up to 20,000).
- Mains for profiles are taken from a longest-first spanning tree, so looped networks plan in linear time (1,740-pipe grid: 0.1 s).
- `pressure_add` counts only connections that succeeded and reports each failed one.
- Split mode (`C3D_MCP_SPLIT=1`) adds `civil3d_import` for the SewerGEMS / WaterGEMS imports. The bundle loads in Civil 3D only (`Platform="Civil3D"`).

## 0.12.0

- Civil 3D 2024 and 2025 support next to 2027, from one source tree. Each release gets its own DLL, compiled against its API and runtime: 2024 → .NET Framework 4.8 (`Contents\2024`, with System.Text.Json; `Compat.cs` and `AssemblyResolver.cs` bridge the missing .NET members), 2025 → .NET 8 (`Contents\2025`), 2027 → .NET 10 (`Contents\2027`). `PackageContents.xml` has one component per release, so one bundle and one installer (`dist\C3dMcp.zip`) serve all three.
- `build.bat` / `build_package.bat` build every release whose DLLs are found: installed, set in `plugin\local.props`, or copied into `refs\YYYY\` (git-ignored). `build_package.bat` falls back to `plugin\prebuilt\YYYY\`, logs one `RELEASE` line per release, and also writes `dist\C3dMcp-source.zip`. New `setup_server.bat` creates the Python environment and runs the smoke test.
- Gravity pipe connections read `StartStructureId` / `EndStructureId` (present in all three releases). In Civil 3D 2024, `pressure_part_set` with a new size is refused with a clear message (no swap-size method in that API).
- Status: 2027 built and live-tested (WaterGEMS import, ALL PASS). The 2025 port passed the full live edit and read tests on its PC; the 2024 port built with 0 errors there. The merged 0.12 DLLs for 2024 and 2025 still need their live test.

## 0.11.0

- Import from Bentley WaterGEMS: `watergems_check` (export + drawing, nothing drawn) and `watergems_import` (pressure network with WaterGEMS names at a set cover, tees, bends and PRVs at the nodes, gate valves and service taps from CAD layers, plan labels with the real material and size, and an alignment, ground profile and profile view per main). A difference report compares the model with the drawn pipes.
- Pressure parts: `pressure_add` takes `name`, `description` and `part` (the exact part description) for pipes, fittings and appurtenances, and names the failing part in its errors. `pressure_labels_add` takes `handles` to label only some parts. `list_pressure_part_sizes` with `catalog: true` lists what the parts list's catalogue offers, and shows each listed part's style.

## 0.10.0

- Works with ChatGPT desktop and Codex as well as Claude: the installer writes a Codex TOML snippet next to the Claude JSON one, and the README covers each client. `--read-only` serves only reads, for ChatGPT in a browser through Secure MCP Tunnel.
- Bound edits and approval to a specific open drawing. Write calls now require the drawing `target` and a `request_id` UUID from `get_drawing_info`.
- Added `get_edit_status` and same-request deduplication for interrupted calls. A request that failed without changing the drawing (for example Civil 3D was busy) can be resent with the same ID. Repeated handles in one edit operation are resolved once per object.
- Validated SewerGEMS labels, flow paths, metric units, and catalogue sizes before writing. The import verifies pipe connections, bore, and inverts.
- Created the SewerGEMS network, profile objects, and styles in a shared transaction. Profile placement and band tables use a separately tracked edit with grouped line and text annotations.

## 0.9.0
- Import from Bentley SewerGEMS: `sewergems_check` (validate the export, nothing drawn) and `sewergems_import` (gravity network with SewerGEMS names and inverts, one alignment + ground profile + profile view per line). Reads the full-output workbook or FlexTable CSVs, honours `Is Reversed?`, maps the true bore to the catalogue size, optional rename table.
- Presentation for the import: plan styles and labels (flow arrow, `ø200 L=22m`, manhole names), alignments without labels on a layer that is off, *Profile View* style, and a LISP-style band table under every profile view (ground, invert with drops, depth, distance, slope ‰, diameter, material).
- Styles: `get_style` (what a label, band or band-set style contains), `label_style_create`, `network_styles`, `alignment_label_set`, `profile_view_set`, `profile_view_bands`, `band_style_set`; `list_styles` covers pipe/structure label styles and band styles.
- `parts_list_add_family`: add a catalogue part family with chosen sizes to a gravity parts list.
- `pipe_network_add`: structures and pipes take `name` and `description`.
- Known Civil 3D behaviour, now handled: a coned manhole cannot be shallower than frame + cone (about 1.7 m) and Civil 3D raises its rim silently; the importer uses a flat-topped manhole and reports any level it could not keep.

## 0.8.0
Code review and reorganisation; behaviour is unchanged except where noted.
- Plugin sources are grouped by subject, the same way in `Read/` and `Edit/`: drawing, terrain, alignments, corridors, gravity, pressure. The `...More` / `...Advanced` files are gone.
- Duplicated code removed: one generic find-by-name (`Core/Find.cs`), one paging helper for every list with a `limit`, one place for styles, file-path checks, text / angle / polyline helpers and parameter validation.
- Faster on large drawings and large batches: pressure networks and corridors come from the Civil 3D collections instead of a scan of model space; parts are resolved once per batch instead of once per pipe; pipe ends are matched to structures and fittings through a grid index instead of comparing every pair; surface statistics are computed once per surface; requests are read in blocks instead of character by character.
- Style and parts-list names are now readable: `list_styles` returns names (it returned only handles), pressure parts lists have a name before any network uses them, and without a style name an edit prefers the style called "Standard" over whichever style happens to be first (for pressure pipe labels the first one was "Null").
- A network without a reference surface / alignment now reports `null` for it instead of an error text.
- `docs/ACTIONS.md` is generated (`tools/gen_docs.py`) and the offline test fails when it is out of date.
- Small changes: `list_blocks`, `list_parcels` and `list_section_views` take `limit` and reply with total / returned / truncated like the other lists; `object_set` also handles assemblies; `draw_leader` without `height` follows the multileader style.

## 0.7.0
- Civil 3D: pressure_labels_add, section_views_create, assembly_create (stock subassemblies), corridor_create, corridor_surface_add, breaklines from existing feature lines / 3D polylines (`handles`); reads list_assemblies, list_parcels, list_section_views, pressure label styles in list_styles.
- AutoCAD: plot_pdf (layout or model space, DWG To PDF.pc3).
- Not possible through the Civil 3D .NET API: creating parcels and grading objects (read-only / not exposed). Grade pads with feature lines used as surface breaklines instead.

## 0.6.0
- Civil 3D: profile_view_create, profile_view_add_parts, sample_lines_create, labels_add, surface_add_breaklines, surface_add_boundary, surface_volume_create, feature_line_create, curved pressure pipes (`through`); reads list_profile_views, list_sample_lines, list_feature_lines, get_surface_volume, corridor regions and surfaces.
- AutoCAD: draw_leader, draw_table, horizontal / vertical dimensions, block attributes (draw_block, attribute_set), block_define, linetype in entity_set, layout_create, xref_attach, drawing_save (in place, copy, DXF); read list_layouts.
- Fix: structure rim and sump levels now stick (automatic surface / depth control is switched off when a level is given).

## 0.5.0
- AutoCAD features: list_entities (geometry), list_blocks; draw_circle, draw_arc, draw_mtext, draw_hatch, draw_block, draw_dimension; entity_copy, entity_rotate, entity_scale, text_set; colour on all draw operations.
- v0.4 verified live in Civil 3D 2027 (all edit stages pass).

## 0.4.0
Review fixes
- Replies containing NaN/Infinity (e.g. cover without a surface) no longer fail as "invalid JSON request".
- Removal of networks was matched by name in the audit log only (wrong drawing / reused name could pass); replaced by the general edit path.
- Plans held live part objects across calls; edits now resolve everything inside their own transaction.
- Read transactions are committed instead of aborted; results are fully materialised before the transaction ends.
- Request parsing separated from execution; clearer errors; idle connections time out; one queue timeout instead of per-method special cases.
- Version in one place (BridgeInfo.cs), checked against PackageContents.xml by the smoke test.
Organisation
- Plugin split into Bridge / Core / Read / Edit; Windows Forms isolated in Ui.cs; shared lookups and parameter parsing.
- Server split into server / catalog / bridge_client / qa; tests moved to tests/; check_api.py replaced by tools/typecheck (real compiler) and tools/apitool.
Features
- One MCP tool `civil3d(action, params)`; optional split mode.
- One-time edit permission per Civil 3D session (dialog, MCPALLOW, MCPREVOKE).
- `edit` with 30 operations, single transaction per batch, `dry_run`.
- New reads: get_objects, list_cogo_points, list_styles, list_structures, list_gravity_parts, list_pressure_appurtenances, list_pipe_runs, get_edit_permission, list_actions.

## 0.3.0
Pressure network creation with plan/apply and a dialog per batch.

## 0.2.0
Read-only bridge: networks, surfaces, alignments, profiles, corridors, geometry queries.
