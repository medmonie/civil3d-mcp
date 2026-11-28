# Changelog

## 0.24.0

The API gap packages of roadmap #69 - #80 (`docs/API_GAP_0.23.md`). Tested live on Civil 3D 2027: `tests\run_v024a.bat`
58 checks and `tests\run_v024b.bat` 41 checks, ALL PASS; the 0.22 and 0.23 suites and the SewerGEMS import pass again
on the same build. Built for 2024 / 2025 / 2027.

**New: gravity network editing (#69)**
- `pipe_break` (split a pipe with an existing or new structure, grade kept), `parts_move` (to another network),
  `pipe_connect` (connect / disconnect an end), `pipe_regrade` (slope holding the start or end invert),
  `pipe_end_adjust` (to the structure centre or walls), `structure_resize` (by pipe depths), `pipe_flow_direction`.
  Parts are named by handle or by name (with `network`).
- Read `network_trace`: the chain of pipes and structures between two parts, with length, slopes and levels.

**New: data on objects (#70, #71)**
- `udp_set`: write user-defined property values on COGO points and parcels (type-checked; enumerations by value).
- `parcel_set` (number, address, tax ID, style), `point_group_action` (lock, unlock, update, delete its points with
  confirm). `list_styles kind: parcel`.
- `property_set`: an allowlist of single Civil 3D settings (starting chainage, profile update mode, profile view
  ranges and split, pipe flow / hydraulics, structure sump control and depth, corridor triangle length / region lock /
  rebuild), each read back before / after.

**New: import provenance (#72)**
- `source_tag_set` and read `get_source_tags`: source model, element ID and import run stored as XData (C3DMCP) on the
  object. `pipe_network_add` and `pressure_add` take `source_id` per item; the SewerGEMS and WaterGEMS imports now tag
  every structure, pipe, fitting and appurtenance with its model ID (prerequisite for #6 and #8).

**New: pressure runs (#73)**
- `pressure_run_edit` also removes bends and vertical bends, follows a profile at an offset, sets the vertical offset
  type, resets the reference profile and breaks a run in two.

**New: labels (#74)**
- `alignment_labels_add` (major / minor stations, geometry points), `profile_grade_labels_add`,
  `spanning_pipe_label_add` (plan or profile view), `profile_view_depth_label_add`, `crossing_pressure_labels_add`.

**New: TIN surfaces (#75)**
- `surface_tin_edit` (add / move / delete vertices, set elevations, add / swap / delete edges, raise all or inside a
  polygon), `surface_simplify`, `surface_smooth` (natural neighbour), `surface_minimize_flat_areas`,
  `surface_mask_add`; reads `tin_triangles`, `tin_vertices`.

**New: command runner, first step (#76)**
- `command_run`: an allowlist of commands with no API and no dialog (audit, audit_fix, regenall, purge_regapps,
  purge_zero_length, purge_empty_text, zoom_extents), queued after the commit and audited.

**New: alignments, plan production, AutoCAD (#77 - #79)**
- `alignment_reverse`, `alignment_copy_to_site`, `alignment_to_polyline`; reads `alignment_stations`,
  `alignment_distance`; `get_profile_elevation` adds the grade and the station label.
- Reads `list_view_frames` (view frame groups, frames, match lines) and `list_civil_tables`.
- `draw_spline`, `draw_ellipse`, `draw_wipeout`, `draw_field_text` (file name, dates, author, title, layout or any
  field), `group_create`, `group_edit`; read `list_groups`.

**Smaller tool description (#80)**
- `C3D_MCP_COMPACT=1` lists only the action and operation names (about 12 % of the full description, ~1,700 tokens);
  the client reads details with `help`, which now takes several topics separated by commas.

**Fixed during the live test**
- `network_trace`: Civil 3D's shortest path leaves out the end part; it is added.
- `surface_mask_add`: Civil 3D needs a render material even for a display mask; the drawing's Global material is used.

## 0.23.0

Code audit 0.23 (`docs/AUDIT_0.23.md`) and the coordinate system (roadmap #39). Tested live on Civil 3D 2027
(`tests\run_v023.bat`, 56 checks); built for 2024 / 2025 / 2027.

**New**
- `get_coordinate_system`, `list_coordinate_systems`, `transform_coordinates` (drawing <-> longitude / latitude) and
  the edit `coordinate_system_set` (applied after the commit; a dry run only validates; `.` clears it).
- File boundary on both sides (`plugin/Core/FilePolicy.cs`, `server/filepolicy.py`): full paths only, no device paths,
  nothing in the bridge folder, no writes under Windows / Program Files / Startup / plug-in folders, never an
  executable or script type. Optional `%LOCALAPPDATA%\C3dMcpBridge\policy.json` with `allowedRoots` (fails closed).
  Workbook / CSV inputs: 64 MB and 200,000 rows; layer QA pattern 300 characters.

**Fixed**
- H-1: failed edits reached the client as successes on MCP SDK 1.9 - 1.18. `requirements.txt` now needs 1.19+ and the
  server refuses to start on an older SDK.
- H-2: `points_export` could write any extension into any folder; data extensions only, through the file policy.
- H-3: one failed socket accept stopped the bridge for the session while it still reported itself as listening; the
  accept loop now backs off and continues.
- M-1: "permission not granted" was stored as the request's final result; now retryable after MCPALLOW.
- M-2: a commit that throws left no receipt and skipped clean-up; now a rolled-back receipt (`failedAt: commit`).
- M-3: material-volume sampling is limited to 2,000,000 samples (a tiny step froze Civil 3D).
- M-4: whole-number parameters (decimals, per_row, columns, count, colour indexes) refuse text, fractions and
  out-of-range values instead of silently changing them.
- M-5: bridge replies have one deadline per call; cancelled reads are abandoned within 0.5 s (edits never are).
- M-8: a request without the token gets one answer and the connection is closed.

**Installer** (developed as 0.22.1, not released separately)
- The civil3d entry was no longer picked up: Claude Desktop 6.x reads
  `%LOCALAPPDATA%\Claude-Data\claude_*\claude_desktop_config.json`, which the installer did not write. It now writes
  that file too (plus the classic and older Store locations).
- Client set-up moved from `configure_clients.ps1` to the server exe (`c3d-mcp-server.exe --configure-clients`), so
  company policies that block PowerShell scripts no longer stop it. Each file is backed up, merged, written in one step
  and read back; an existing entry keeps its args and env.
- `install.bat` stops while Claude Desktop is running (it would write its old settings back over the new entry);
  `/claude_open` overrides. It unblocks the downloaded files and writes `install_result.txt`.
- `uninstall.bat` removes the civil3d entry from Claude Desktop and Codex too.
- Codex (and the ChatGPT desktop app, which shares its config) gets `C3D_MCP_SPLIT = "1"`: separate read / edit /
  import tools, so reads can be allowed without edits (as the CSI MCP servers do). An existing value is kept.
- `c3d-mcp-server.exe --run-chatgpt-tunnel` starts OpenAI's `tunnel-client` with the server read-only
  (`--mcp.stdio-send-initialized-notification`, health on 127.0.0.1:0), replacing the hand-written PowerShell.
- An entry left from a source install (`python.exe` + `server.py`) became exe + `server.py` after the update, and the
  exe refused the argument ("could not start"). The installer now drops every argument except `--read-only`, and the
  server ignores unknown arguments with a warning instead of exiting.
- Test: `tests/client_config_test.py` (offline).

## 0.22.0

**New**
- `list_entities` reads inside a block definition (`block`) and the model space or a layout of another drawing on disk
  (`path`), without opening it: e.g. a standard manhole or basin detail. Nested blocks show as INSERT and can be read
  the same way.
- `style_copy`: copy any Civil 3D style (label, object, band, band set, ...) under a new name, so the copy can be
  edited and the original kept.
- `profile_view_bands` with `band_set` now uses Civil 3D's own band-set import (copying the rows only when Civil 3D
  refuses), and takes `rows` to set the profiles / network / gap of each imported row.

**Fixed**
- `profile_pvis_set`: changing only a PVI's elevation kept no vertical curve (it was removed and re-added plain). The
  PVI is now moved in place and keeps its parabola, asymmetric parabola or circular curve (`curvesKept`). A curve that
  was asked for and cannot be made now fails the edit (nothing applied) instead of a warning with the curve missing.
  (ChatGPT audit H5)
- A failed or dry-run batch could leave a profile's PVIs changed: Civil 3D does not undo PVI edits when the transaction
  is aborted. `profile_pvis_set` now snapshots the profile and the engine puts it back before any rollback
  (`EditContext.BeforeAbort`). Found by the new live test.
- `get_alignment_geometry`: the note said directions were counter-clockwise from east. Directions are now computed from
  the coordinates and given as `bearing` (azimuth, clockwise from north), `angleFromEast` and `quadrantBearing`
  (e.g. N 45°30'00.0" E); curves give `startBearing` / `endBearing`; a spiral's left/right is `turn`.
- `inspect_file`: `limit` capped only the entities of each layout, so a large template could return 100+ KB. It is now
  one budget for the whole reply (layout entities, blocks, views, page setups) with a `truncated` list; `layout` reads
  one layout only.

## 0.21.0 (not released on its own: shipped in 0.22.0)

**New**
- `show_objects` (read): select objects and zoom to them, or zoom to a window, so the client can show the person what it
  found. Changes only the view and selection, never the drawing. Objects on a layout make that layout current. (#52)
- `profile_view_labels_add`: label the network parts shown in a profile view (gravity pipes and structures, pressure
  pipes, fittings, appurtenances), with optional styles. Parts already labelled in that view are skipped. (#15)
- `get_material_volumes` takes `from_station` / `to_station`: totals cover only the sample lines in that stretch. With a
  material list of existing ground vs a corridor surface this gives corridor earthwork by station range. (#16)

**Fixed**
- Corridor surfaces stayed empty ("point outside surface") after a corridor was created or rebuilt by the MCP: their
  TINs are now rebuilt after the commit (corridor_surface_add, corridor_rebuild and every operation that rebuilds a
  corridor).
- `get_material_volumes` found no surface for corridor surface items (named "<corridor> <surface>" in the material
  list); it now resolves them through the corridor, and warns when a surface gives no elevation at all.

**Protocol hardening** (from the ChatGPT protocol audit, `docs/MCP_PROTOCOL_AUDIT.log`; reviewed and merged)
- Edit ledger: finished request IDs are no longer evicted, so a replayed ID can never run twice; at 8,192 IDs per
  Civil 3D session new edits are refused (restart Civil 3D). A failed edit may be resent with the same ID only when it
  timed out in the queue before it started (`retryable: true` in get_edit_status); any other error needs a look at the
  drawing first.
- Failed edits and failed imports/sheets now come back as MCP tool errors (`isError`) with the full receipt attached,
  so clients no longer treat a rolled-back batch as success.
- A cancelled MCP call sends no further bridge requests (later import / sheet stages stop); an edit already sent keeps
  its request ID.
- Bridge transport: request 4 MiB, reply 32 MiB and discovery file 16 KiB limits; a reply cut off mid-line, without a
  status flag or without a result is reported as an error; an idle connection is dropped after 5 s; a partial request at
  end of stream is never run.
- `check_default_names` reads every list from one drawing (takes `target`); sheet stages stop after 8 attempts;
  stricter input types (`dry_run`, `skip_xref_layers`, help `topic`); `requirements.txt` pins anyio and pydantic.
- Tests: `tests/live_protocol_test.py` (`run_protocol.bat`), `tools/ledger_audit` (offline ledger check).

**Installer**
- `install.bat` now adds `civil3d` to Claude Desktop (classic and Microsoft Store / MSIX installs) and Codex itself
  (`package\configure_clients.ps1`): other servers and settings are kept, files are backed up first, invalid JSON is
  left unchanged and reported. Before, it only wrote snippets: a snippet pasted whole into a config that already had
  `mcpServers` made the file invalid, and the Store version of Claude reads its config from another folder.

Live test: `tests\run_next.bat` (Civil 3D 2027, ALL PASS); `tests\run_configure_clients.bat` (sample configs, ALL PASS).

## 0.20.0

**Found in the live test**
- `section_volume_tables` made the drawing unsavable ("object cannot be saved ... no file was created"): Civil 3D leaves
  every table made by `CreateVolumeTable` open for write once the edit ends. The bridge now releases those tables after
  every edit (committed, rolled back or dry run) and reports any it cannot release.
- `diagnose_save` crashed Civil 3D with its file filer; it now always scans with a copy filer (the %TEMP% copy is the
  real save check).
- `ping` answers within 2 s while Civil 3D is busy (e.g. the permission dialog is open): `busy: true`.
- A second edit while one is running (or waiting for the permission dialog) is refused at once, not after 30 s.
- SewerGEMS / WaterGEMS: a retry with the same request_id during a later phase says `pending: true`.
- `block_import` with `replace` never changes the drawing's own layers or styles.
- `assembly_create` attaches each subassembly to the outer edge of the previous one on its side.
- `network_interference` within one network skips pipes connected to each other (pressure fittings included).
- `surface_solids_create` with `overwrite` keeps the old file when making the solids fails.
- `labels_add` / `pressure_labels_add` skip parts already labelled; `network_styles` restyles existing labels.

**MCP server**
- Tools are async: the blocking bridge call runs in a worker thread, so pings, cancellation and other requests are served
  during long edits; progress is reported every 10 s while Civil 3D works (permission dialog, imports).
- Clear error text from failed calls; edits with non-object operations are refused up front.

**Bridge**
- `bridge.json` is written atomically; the client retries a half-written file.
- A dry run is not recorded under its request_id (the real edit can reuse it); the request ledger drops its oldest
  finished entries instead of refusing all edits after 8192; a second edit is refused while one is running (permission
  dialog, plotting).

**Edit operations**
- `entity_copy` / `block_define` deep-clone (attributes and polyline vertices kept; copies stay in their own space).
- Indexes (`index`, `size_index`, feature-line `elevations` / `radii`) must be whole numbers in range; never clamped.
- `part_swap` needs `names` or `handles`; handles must belong to the network.
- File paths must be full paths in every file input / output; `inspect_file` accepts repeated attribute tags.
- At most 200 warnings per batch; `pipe_ground_profile` at most 2000 samples per pipe; `sample_surface` refuses an
  interval that would exceed 20000 samples; `sample_lines_create` warns at its 5000-line cap; a silently renamed
  pressure part is reported.
- WaterGEMS / SewerGEMS imports report a still-running import as pending and survive an expired cached reply.

**New**
- `diagnose_save` (read): why a drawing cannot be saved (header styles, every object written through a probe DWG
  filer, a copy written to %TEMP%). `tests\run_save.bat` replays the 0.15-0.19 live suites and runs it after every edit.

## 0.19.1

**Installer**
- `install.bat` and `uninstall.bat` install for the account signed in to the desktop. When the window runs as another account (Run as administrator with someone else's credentials, Run as different user, common on servers and domain controllers), they stop before copying or deleting anything and say which account the window runs as. Previously the plug-in and server went into that other account's profile. They also print which account they install for. No admin rights are needed.

## 0.19.0

Third batch from the Civil 3D API review (roadmap #53, #68). 70 read actions, 130 edit operations. Live test: `tests\run_model.bat`; ALL PASS on Civil 3D 2027 together with `run_civil`, `run_data`, `run_geometry` and `run_extras` in one drawing.

**Corridors**
- `get_corridor_feature_lines` (#53): feature lines by code over a station range: side, start / end station, plan and 3D length, offset and elevation range, points `[station, offset, elevation, x, y]` (thinned to `max_points`); plus the point / link / shape codes the sections in that range carry.
- `corridor_feature_line_export`: a corridor feature line (code + index or side) as an alignment, a profile on an alignment, 3D polyline(s), or a static grading feature line.
- `corridor_points_export`: COGO points at the feature-line points (all codes or chosen ones) in a new point group.
- `get_corridor_targets` / `corridor_targets_set`: subassembly targets (surface, width / offset, elevation) per region, set to surfaces, alignments, profiles or drawing objects.
- `corridor_slope_pattern_add`: slope pattern between two corridor feature lines (a style is created when the drawing has none).
- `corridor_solids_export`: 3D solids (or bodies) from shapes and / or links, for the corridor, a baseline or a region range; returns the total solid volume.

**Surfaces**
- `surface_definition_add`: contours, DEM files, point groups, drawing objects (points, lines, blocks, text, 3D faces, polyface meshes), wall breaklines, breakline (.flt) files; rebuilds and reports points and elevations.
- `surface_create_from`: from LandXML, a DEM (grid surface), a .tin file, a cropped area of another surface, or a static copy of a corridor surface.
- `surface_snapshot`: create / rebuild / remove.
- `surface_solids_create`: 3D solids to a depth, a fixed elevation or another surface (or to a DWG file after commit); returns the total volume.

**Hydrology**
- `catchment_create` / `list_catchments`: catchments from a boundary on a surface, flow path traced downhill from a point (or given), percent impervious, runoff coefficient, discharge structure; area, perimeter, flow path length, time of concentration.

**Limits found in the live test**
- Corridor exports need a site: an exported alignment goes into a site named `Corridor Exports` unless `site` is given. Objects sharing a site interact; a feature line laid over an alignment of the same site was flattened to elevation 0, so grading feature lines are exported siteless by default, as static copies built through the feature line's own points.
- `TinSurface.CreateByCropping` writes only into another drawing; `surface_create_from` crop pastes the source surface into a new TIN and clips it with a non-destructive outer boundary.
- Contours need `max_distance` > `weeding_distance`; the Civil 3D dialog defaults (100 / 15 / 4°) are used.
- `Catchment.ImperviousArea` is a 0-1 fraction; the MCP takes and reports percent.
- Wall breakline `elevation` is the absolute level of the offset side.
- A 5 x 5-cell ESRI ASCII grid was refused ("UnableToReadFile"); a 50 x 50 grid was read.
- `RunoffMethod`, `FlowPathLength`, `AverageSlope` of catchments exist only in the 2027 API; read by reflection (null on 2024 / 2025).
- Not tested live: `tin_file` source, DEM `null_elevation`, solids written to a DWG file, corridor solids per baseline / region.

**Developer tools**
- `tools/apitool` also lists public fields (value types such as `WallBreaklineCreationData`).

## 0.18.0

Second batch from the Civil 3D API review. 67 read actions, 120 edit operations. Live test: `tests\run_extras.bat` (Civil 3D 2027).

**Profiles**
- `profile_derive`: a profile from a corridor feature line (by code), an offset alignment's profile at a cross slope, or a static layout copy of a corridor or offset profile (Civil 3D refuses static copies of surface and layout profiles).

**Corridors**
- `corridor_baseline_add`: another baseline with its first region.
- `corridor_region_edit`: add, merge, split at a station, move start / end, extra stations, assembly frequency.
- `corridor_surface_set`: corridor surface boundaries (extents, feature-line code or points; outside / inside), masks, breakline codes.

**Surfaces**
- `surface_extract`: contours at an interval or at given levels, major / minor contours, border, watersheds, grid lines, optionally onto a layer.

**Networks**
- `pipe_curved_add`: curved gravity pipe by radius or through a point, connected to named structures.
- `part_swap`: swap gravity pipes / structures to another family and size.
- `parts_to_section_views`: show network parts in every section view of a sample line group.
- `pressure_run_edit`: bends at plan PIs, vertical bends at station / elevation, merge another run into a run.

**Sections**
- `section_volume_tables`: total-volume or material tables on every section view of a group (Civil 3D fills them once it has computed the material sections).

**Tests**
- `geometry_live.py` and `extras_live.py` name sample lines and pipe runs with the run's tag, so the live tests can share one drawing.

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
