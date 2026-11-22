# Changelog

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
