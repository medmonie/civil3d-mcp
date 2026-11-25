"""The catalogue of everything the AI client can ask Civil 3D to do.

This file is the single place for the WORDING the AI client sees (action names, what they do, their parameters).
The behaviour lives in the plugin; `help` and the tests compare the two so they cannot drift apart silently.
docs/ACTIONS.md is generated from this file (tools/gen_docs.py).

Each entry: name -> (summary, {parameter: description}).  A parameter ending in '?' is optional.
The groups mirror the plugin sources: plugin/Read/<Group>Read.cs and plugin/Edit/<Group>Ops.cs.
"""
from __future__ import annotations

Entry = tuple[str, dict[str, str]]
Group = dict[str, Entry]

# Parameter descriptions that repeat.
XY = "[x,y]"
XYZ = "[x,y,z]"
ACI = "AutoCAD colour index 1-255"
LIMIT_200 = {"network_name?": "only this network", "limit?": "default 200"}
STYLE_KINDS = ("alignment | alignment_label_set | point_label | profile | profile_label_set | profile_view | profile_view_band_set | surface | "
               "corridor | pipe | structure | point | feature_line | sample_line | pressure_pipe_label | pressure_fitting_label | "
               "pressure_appurtenance_label | gravity_parts_list | pipe_label | structure_label | band_profile_data | band_pipe_network | "
               "band_horizontal_geometry | band_vertical_geometry")

# --------------------------------------------------------------------------- read actions (never change the drawing)

READ_GROUPS: dict[str, Group] = {
    "Drawing (AutoCAD)": {
        "ping": ("Connection check: bridge version, Civil 3D version, active drawing, whether editing is allowed, the process id and any other running Civil 3D instances with the bridge (otherInstances; only one is the MCP target). "
                 "Answers within a few seconds even when Civil 3D is busy (e.g. the permission dialog is open): then busy: true "
                 "and the drawing details are left out.", {}),
        "get_drawing_info": ("Active drawing: path, units, object counts, the drawing target, and a fresh edit request UUID.", {}),
        "list_layers": ("Layers with on/frozen/locked/plot state, colour and xref flag.", {"limit?": "default 500"}),
        "count_entities": ("Count entities of model space (or a layout) by DXF type or by layer.",
                           {"group_by?": "'type' (default) or 'layer'", "layer?": "only this layer", "layout?": "a paper-space layout instead of model space"}),
        "get_selection": ("Objects the user has selected in Civil 3D: handle, type, layer, Civil name, extents.", {"limit?": "default 200"}),
        "get_objects": ("Describe objects by handle: type, layer, Civil name, extents.", {"handles": "list of handle strings"}),
        "list_entities": ("AutoCAD entities in model space with their geometry: lines, polylines (vertices), circles, arcs, text, "
                          "mtext, points, block references (name, attributes), hatches, dimensions.",
                          {"type?": "DXF type, e.g. LINE, LWPOLYLINE, CIRCLE, ARC, TEXT, MTEXT, POINT, INSERT, HATCH", "layer?": "",
                           "window?": "[xmin, ymin, xmax, ymax]", "limit?": "default 100", "layout?": "a paper-space layout instead of model space"}),
        "list_blocks": ("Block definitions: name, attributes flag, number of references.", {"limit?": "default 500"}),
        "list_layouts": ("Layouts (paper space tabs): name, tab order, plot device, paper size.", {}),
    },
    "Sheets and drawing set-up (AutoCAD)": {
        "list_viewports": ("Viewports on layouts: handle, position and size on paper, scale (1:N), model centre, twist, locked, frozen layers.",
                           {"layout?": "only this layout", "include_paper?": "also the layout's own paper-space viewport"}),
        "list_views": ("Named views: centre, size, twist.", {}),
        "list_xrefs": ("External references: name, path, status (resolved, unloaded, not found), nested, references.", {}),
        "list_cad_styles": ("AutoCAD styles and set-up of one kind, with the current one.",
                            {"kind": "text_style | dim_style | mleader_style | table_style | linetype | layer_state | annotation_scale | page_setup"}),
        "inspect_file": ("Read a drawing or template on disk without opening it: layouts (paper, viewports with scale and model "
                         "area), block references and text on each layout, xrefs, blocks with attribute tags, named views, page setups.",
                         {"path": "full path of a .dwg / .dwt / .dxf", "limit?": "entities listed per layout, default 60"}),
        "get_block_properties": ("A block reference: block name, attributes, and dynamic block properties with their allowed values.",
                                 {"handle": "", "visible_only?": "default true"}),
    },
    "Terrain": {
        "list_surfaces": ("Surfaces: name, type, style, layer, min/max/mean elevation.", {}),
        "get_surface_elevation": ("Elevation of a TIN surface at (x, y).", {"surface_name": "", "x": "easting", "y": "northing"}),
        "get_surface_volume": ("Cut, fill and net volume of a TIN volume surface.", {"surface_name": ""}),
        "list_cogo_points": ("COGO points: number, name, easting, northing, elevation, description.", {"limit?": "default 200"}),
        "get_feature_line": ("One feature line's vertices (PI and elevation points) with plan distance, elevation, grade in/out (%) "
                             "and curve radius, plus min/max elevation and grade.", {"handle?": "", "feature_line?": "name"}),
        "list_feature_lines": ("Feature lines: length, min/max elevation, points.", {"limit?": "default 200"}),
        "list_parcels": ("Parcels of every site: name, number, centroid, extents. Read only.", {"limit?": "default 500"}),
        "list_point_groups": ("Point groups: description, point count, query, style overrides.", {}),
        "sample_surface": ("Surface elevations in one call: at a list of points, or along a polyline / line (the surface's own "
                           "breakpoints, or every 'interval').",
                           {"surface": "", "points?": "[[x,y], ...] up to 20000", "handle?": "polyline, line or arc to sample along",
                            "line?": "[[x1,y1],[x2,y2]] to sample along", "interval?": "m along the line; default: surface breakpoints"}),
        "list_user_properties": ("User-defined property classifications of COGO points or parcels, with each property's type and default.",
                                 {"kind?": "point (default) | parcel"}),
        "get_user_property_values": ("Values of user-defined properties of COGO points or parcels.",
                                     {"kind?": "point (default) | parcel", "classification?": "default: all", "numbers?": "point numbers",
                                      "names?": "parcel names", "limit?": "default 500"}),
        "list_point_file_formats": ("Point file formats available for points_import (name, extension, delimiter).", {}),
        "list_description_keys": ("Description key sets with their keys: code, format, point style, label style, layer.", {}),
        "get_surface_analysis": ("A surface's min/max/mean elevation and its analysis ranges: elevations, slopes and slope "
                                 "arrows (percent) with colours, and watersheds.", {"surface": ""}),
        "list_catchments": ("Catchment groups and catchments: area, perimeter, percent impervious, runoff coefficient, flow path "
                            "length, time of concentration, discharge point, surface, discharge structure.",
                            {"group?": "", "boundary?": "include boundary points", "flow_path?": "include flow path points", "limit?": "default 200"}),
    },
    "Alignments": {
        "list_alignments": ("Alignments (incl. the 'Pipe Run' alignments of pressure networks): type, site, length, stations.", {}),
        "alignment_station_to_point": ("Easting/northing of a station and offset (offset + = right).",
                                       {"alignment_name": "", "station": "", "offset?": "default 0"}),
        "alignment_point_to_station": ("Station and offset of a point.", {"alignment_name": "", "x": "", "y": ""}),
        "list_profiles": ("Profiles: type, PVI count, station range, min/max elevation.", {"alignment_name?": "only this alignment"}),
        "get_profile_elevation": ("Elevation of a profile at a station.", {"alignment_name": "", "profile_name": "", "station": ""}),
        "profile_sheet_window": ("Station range and enclosing plan window for part of a profile view.",
                                 {"alignment_name": "", "profile_view": "", "x_min": "profile drawing X", "x_max": "profile drawing X"}),
        "list_profile_views": ("Profile views: alignment, style, station and elevation range, position in the drawing.", {}),
        "list_sample_lines": ("Sample line groups with the stations of their sample lines.", {"alignment_name?": "only this alignment"}),
        "list_section_views": ("Section views: alignment, sample line group, sample line, station.", {"limit?": "default 500"}),
        "get_superelevation": ("Superelevation of an alignment: curves and critical stations with the slope of each lane and "
                               "shoulder (percent).", {"alignment": ""}),
        "list_intersections": ("Corridor intersections: name, location, corridor, number of roads.", {}),
        "get_alignment_geometry": ("Every line, curve and spiral of an alignment in order: stations, length, start/end, direction; "
                                   "curves with radius, delta, PI, tangent, external, chord; spirals with A, radius in/out, SPI. "
                                   "Also station equations, design speeds, and the parent(s) of an offset or connected alignment.",
                                   {"alignment": ""}),
        "get_profile_geometry": ("PVIs of a profile (station, elevation, grades in/out in %) and its entities: tangents with grade, "
                                 "vertical curves (crest/sag) with length, K, grade change, high/low point; asymmetric lengths, radius.",
                                 {"alignment": "", "profile": ""}),
        "get_material_volumes": ("Areas and volumes of a sample line group's material lists (see material_list_create): at each "
                                 "sample line cut/fill (earthwork) or material area, incremental and cumulative volume, and totals. "
                                 "Civil 3D's own quantity results when it has computed them (source civil3d); otherwise surface "
                                 "materials are computed from the TINs across each sample line, average end area (source computed).",
                                 {"alignment": "", "group": "", "material_list?": "default: all", "step?": "sampling step across the line, default 0.25"}),
        "get_sections": ("Ground sections at the sample lines of an alignment: each TIN surface sampled along each sample line (every "
                         "surface breakpoint) as [offset, elevation], offsets from the alignment (negative = left). Needs no sampled "
                         "sources or section views. Corridor sections: get_corridor_section.",
                         {"alignment": "", "group?": "sample line group, default: all", "from_station?": "", "to_station?": "",
                          "surfaces?": "TIN surfaces, default: the group's surface sources", "limit?": "sample lines, default 50",
                          "max_points?": "per section, default 500 (thinned beyond)"}),
    },
    "Corridors": {
        "list_corridors": ("Corridors: baselines with their regions and assemblies, corridor surfaces, out-of-date flag.", {}),
        "get_corridor_section": ("A corridor's section at a station (nearest frequency station): points with codes, offset and elevation "
                                 "to the baseline; links with codes; shapes with codes and area.",
                                 {"corridor": "", "station": "", "baseline?": "name or alignment, default: the first", "codes?": "codes to keep"}),
        "list_assemblies": ("Assemblies with their subassemblies (for corridor_create).", {}),
        "get_corridor_feature_lines": ("Corridor feature lines by code over a station range: side, start/end station, plan and 3D "
                                       "length, offset and elevation range, points [station, offset, elevation, x, y]; plus the point, "
                                       "link and shape codes the sections in that range carry.",
                                       {"corridor": "", "baseline?": "name or alignment, default: the first", "codes?": "default: all",
                                        "from_station?": "", "to_station?": "", "points?": "default true",
                                        "max_points?": "per feature line, default 500 (thinned beyond)", "offset_baselines?": "default false",
                                        "limit?": "feature lines, default 200"}),
        "get_corridor_targets": ("Subassembly targets of each region (surface, width/offset, elevation/slope) with the objects they "
                                 "point at, and the corridor's slope patterns.",
                                 {"corridor": "", "baseline?": "", "region?": ""}),
    },
    "Gravity networks": {
        "list_pipe_networks": ("Gravity pipe networks: parts list, reference surface/alignment, pipe and structure counts.", {}),
        "list_pipes": ("Gravity pipes: part, inner diameter, slope (m/m), length, start/end, connected structures.", LIMIT_200),
        "list_structures": ("Gravity structures: part, location, rim/sump elevation, connected pipes.", LIMIT_200),
        "list_gravity_parts": ("Without parts_list: the gravity parts lists. With it: its pipe and structure families and sizes "
                               "(use these to fill 'family' + 'inner_diameter' / 'size_index' in pipe_network_add).",
                               {"parts_list?": "name or handle"}),
        "network_hgl": ("Hydraulic and energy grade lines and flow of a gravity network's pipes and structures, next to each pipe's "
                        "crown at both ends (by flow direction) and each structure's rim, with the differences "
                        "(hglAboveCrownUp/Down, hglBelowRim). Facts only; judge them against your design criteria.",
                        {"network": ""}),
"get_part_data": ("Every catalogue field of the parts of a gravity or pressure network (wall thickness, material, inner / "
                          "outer sizes, custom fields), with units.",
                          {"network": "gravity or pressure network", "kind?": "all (default) | pipes | structures | fittings | appurtenances",
                           "parts?": "part names", "fields?": "field names to keep", "limit?": "default 200"}),
        "pipe_ground_profile": ("Ground along every pipe of a network: at both ends and every 'interval', the surface elevation, the "
                                "pipe invert, centre and outside top, and the cover to the outside top; min / max cover per pipe.",
                                {"network": "gravity or pressure network", "surface": "TIN surface", "interval?": "m, default 5",
                                 "pipes?": "pipe names", "limit?": "pipes, default 200"}),
                "network_interference": ("Distances between the pipes of two networks (gravity or pressure), or within one network: every "
                                 "pair closer than 'within' outside to outside, with the gap (negative = overlap), the location, "
                                 "which pipe is above and the vertical separation. Only straight circular pipes are measured; "
                                 "see complete and excludedPipes. Compare the gaps with the clearance your criteria require.",
                                 {"network1": "", "network2?": "default: network1 against itself",
                                  "within?": "search distance in m, default 1.0 (a filter, not a limit)", "limit?": "default 500"}),
    },
    "Pressure networks": {
        "list_pressure_networks": ("Pressure networks: parts list, reference surface/alignment, part and pipe-run counts, elevations.", {}),
        "list_pressure_pipes": ("Pressure pipes: part, diameters and wall (metres), lengths, slope, elevations, cover, start/end, handle.", LIMIT_200),
        "list_pressure_fittings": ("Pressure fittings: part, type, connections, deflection angle, location, handle.", LIMIT_200),
        "list_pressure_appurtenances": ("Valves, hydrants and other appurtenances: part, type, connections, location, handle.", LIMIT_200),
        "list_pipe_runs": ("Pipe runs of pressure networks with their alignment, profile and part count.", {"network_name?": "only this network"}),
        "list_pressure_part_sizes": ("Without parts_list: the pressure parts lists (name, handle, pipe size count). "
                                     "With it: the part descriptions for part_type, to choose 'size' and 'match' values.",
                                     {"parts_list?": "name or handle", "part_type?": "Pipe (default), Fitting, Appurtenance, Elbow, Tee, Reducer, Valve, ...",
                                      "contains?": "text filter, e.g. '150'", "catalog?": "true: list what the list's catalogue offers (add them to the list in Civil 3D's parts list dialog)",
                                      "limit?": "default 100"}),
    },
    "Styles and meta": {
        "list_styles": ("Style names of one kind (for the 'style' / 'label_set' parameters of edit operations). Without a style name an "
                        "edit uses 'Standard' when the drawing has it, otherwise the first style listed.", {"kind": STYLE_KINDS}),
        "get_style": ("What one style contains: a label style's text components and their contents (the property-field syntax to "
                      "copy into label_style_create), a band style's title and height, a profile view band set's bands.",
                      {"kind": "a list_styles kind", "name": ""}),
        "describe_api": ("Developer aid: public members of an Autodesk.* .NET type loaded in Civil 3D.",
                         {"type_name": "full type name", "contains?": "member name filter", "limit?": "default 300"}),
        "diagnose_save": ("Why the drawing cannot be saved ('object cannot be saved ... no file was created'). Read only. Checks "
                          "the header's current layer / styles, writes every object through a probe copy filer (objects that fail "
                          "to write; objects that still point at erased ones), and writes a real copy to %TEMP% that is deleted "
                          "again (the end-to-end save check).",
                          {"scan?": "default true", "save_copy?": "default true",
                           "soft_refs?": "also list soft pointers to erased objects (default false)", "limit?": "rows per list, default 100",
                           "max_objects?": "default 2000000"}),
        "get_edit_permission": ("Whether editing is currently allowed in the active drawing, and how to allow it.", {}),
        "get_edit_status": ("Find an edit by request UUID. Check this after a timeout before retrying. Status remains available while Civil 3D is busy.",
                            {"target": "original drawing target", "request_id": "original edit UUID"}),
        "list_actions": ("What the installed plugin supports (names only). Use 'help' for descriptions.", {}),
    },
}

SEWERGEMS_INPUT = {
    "path?": "full path of the SewerGEMS workbook (.xlsx with sheets Manhole, Outfall, Conduit)",
    "manholes_csv?": "or: manhole FlexTable CSV (must include X and Y)", "outfalls_csv?": "", "conduits_csv?": "conduit FlexTable CSV",
    "rename_csv?": "two columns: SewerGEMS label, drawing label (e.g. MH-19,MH-1-19)",
    "line_prefix?": "default 'LINE' (lines are LINE1, LINE1-3, ...)",
}

WATERGEMS_INPUT = {
    "path": "full path of the WaterGEMS full-output workbook (.xlsx with sheets Pipe, Junction, PRV, ...)",
    "target?": "from get_drawing_info (default: the active drawing)",
    "parts_list?": "pressure parts list, default 'Water'; its pipes, tees, bends and gate valves are used",
    "cover?": "m to the top of pipe below the WaterGEMS node elevation (taken as ground), default 1.0",
    "sizes?": "override the drawn size per WaterGEMS bore, e.g. {\"99.4\": 110}; default: PVC/PE bore -> next outside diameter, others -> DN",
    "valve_layer?": "CAD layer of valve chambers (closed polylines or blocks) -> gate valves on the model pipe beneath",
    "service_layers?": "CAD layers of service connections (polylines; size read from the layer name, e.g. WATER-63) -> teed-off pipes",
    "hydrant_layer?": "CAD layer of hydrant blocks (reported; kept as blocks)", "cad_pipe_layers?": "CAD layers of the drawn pipes -> difference report",
    "main_min_mm?": "pipes of at least this drawn size get profile views, default 160", "line_prefix?": "default 'WL'",
    "min_line_length?": "m, default 50", "valve_tolerance?": "m, default 3", "tap_tolerance?": "m, default 1.5",
    "part_end_clearance?": "m kept between a valve or tap and a pipe end or another valve/tap, default 2",
    "stand_in_od_factor?": "outside diameter / DN assumed for a stand-in part when setting cover, default 1.15",
    "elbow_min_deflection?": "degrees below which two pipes meet without a bend, default 5",
    "elbow_tolerance?": "degrees a deflection may differ from a catalogue bend, default 6",
}

PP_SHEETS_INPUT = {
    "template": "full path of an office sheet drawing to copy (its layout has plan, profile and key-plan viewports, title block, list of lines, sheet label)",
    "template_layout?": "layout in the template (default: its first)",
    "mode?": "auto (default) | views (Civil 3D profile views in the drawing) | rows (plain-CAD profiles stacked in rows)",
    "sheets?": "rows mode: [{lines: [names], plan_window?: [xmin, ymin, xmax, ymax]}, ...] one entry per sheet",
    "row_spacing?": "rows mode: m between the profile rows of two sheets, default 500",
    "first_row_center?": "rows mode: [x, y] profile centre of the first sheet (default: the template's)",
    "max_length?": "views mode: maximum span per sheet, default 500 drawing units, capped at the profile viewport width",
    "profile_views?": "views mode: only these profile views",
    "plan?": "fit (default: zoom to the sheet's alignments at a standard scale) | template (keep the template's plan area)",
    "plan_scales?": "allowed plan scales N of 1:N, default 100 ... 10000", "plan_margin?": "default 0.1 (10 %)",
    "first_sheet?": "number of the first sheet, default 1", "total_sheets?": "for the label ( n / total ), default: the last sheet",
    "layout_prefix?": "default '' (layouts 01, 02, ...)", "line_layer?": "layer of the list of lines, default 'EN-LINE NAME'",
    "label_contains?": "text of the sheet label, default 'PLAN/PROFILE'",
    "viewports?": "{plan, profile, key}: template viewport handles (from inspect_file) when the automatic choice is wrong",
}

# computed in the MCP server from read actions; they never reach the plugin under these names
LOCAL_ACTIONS: Group = {
    "help": ("This catalogue with every parameter. Pass topic to narrow it.", {"topic?": "an action or edit operation name"}),
    "check_layer_names": ("QA: layers whose names do not fully match a regular expression (e.g. the BEP layer convention).",
                          {"pattern": "regular expression", "skip_xref_layers?": "default true"}),
    "check_default_names": ("QA: surfaces, alignments and networks still carrying Civil 3D's auto-generated names.", {}),
    "sewergems_check": ("Read a Bentley SewerGEMS export and check it without drawing: manholes, conduits, drops, problems "
                        "(missing nodes, uphill pipes, crown above ground, ...) and the profile lines it would make.", SEWERGEMS_INPUT),
    "sewergems_import": ("Build a SewerGEMS export in the open drawing: a gravity network (manholes named as in SewerGEMS or the "
                         "rename table, pipes at the SewerGEMS inverts), then per line an alignment from the downstream end, a ground "
                         "profile through the manhole rims and a profile view with that line's parts. Needs edit permission; stops "
                         "without drawing if sewergems_check reports problems.",
                          {**SEWERGEMS_INPUT, "target": "from get_drawing_info", "request_id": "one UUID for the import", "network?": "default 'Sewer'", "parts_list?": "default 'Sanitary Sewer'",
                          "pipe_family?": "words of the pipe family, default 'PVC'",
                          "structure_family?": "default 'Cylindrical Junction Structure NF SI' (flat top, fits 1.2 m deep manholes); added to the parts list if missing",
                          "structure_diameter?": "mm, default 1200", "profile_views_at?": "[x,y] top-left of the first view, default 50 below the network",
                          "profile_view_gap?": "vertical gap between views, default 30",
                          "presentation?": "false to skip styling; or overrides of: pipe_style, structure_style, structure_label_style, "
                           "pipe_label_style, pipe_label_text, label_height_mm, alignment_label_set, profile_view_style, band_table",
                           "profile_view_style?": "overrides the presentation setting", "band_set?": "band set for the profile views: a name, or auto (Standard when it has bands, else an Elevations band set)", "dry_run?": "validate the core network, profiles, and styles transaction",
                           "finish_attempt?": "default 1. Increment only after the preceding finishing transaction is confirmed rolled back."}),
    "pp_sheets_plan": ("Plan and profile sheets from an office sheet template, without drawing: which profiles, lines, plan scale and "
                       "label each sheet gets, and what was found in the template (viewports, list of lines, label, key-plan box, scale bar).",
                       PP_SHEETS_INPUT),
    "pp_sheets_create": ("Make plan and profile sheets as layouts of the open drawing from an office sheet template: per sheet the "
                         "profile viewport on its profiles, the plan viewport zoomed to its lines, the key-plan box and scale bar "
                         "adjusted, the list of lines and the sheet label rewritten. Run pp_sheets_plan first.",
                         {**PP_SHEETS_INPUT, "target": "from get_drawing_info", "request_id": "new edit request UUID",
                          "if_exists?": "error (default) | replace existing layouts of the same names"}),
    "watergems_check": ("Read a Bentley WaterGEMS export (and the drawing's CAD layers, when given) without drawing: pipes by "
                        "material and size, the catalogue parts that will stand in, CAD valves/services/hydrants matched to the "
                        "model, the profile lines of the mains, and where model and drawing differ.", WATERGEMS_INPUT),
    "watergems_import": ("Build a WaterGEMS export in the open drawing as a pressure network: pipes at 'cover' below ground, "
                         "PRVs, tees and bends at the nodes, gate valves and service taps from CAD layers, plan labels with the real "
                         "material and size, then an alignment, ground profile and profile view per main. Needs edit permission.",
                         {**WATERGEMS_INPUT, "target": "from get_drawing_info", "request_id": "one UUID for the import",
                          "network?": "default 'Water'", "profile_views_at?": "[x,y], default 50 below the network",
                          "profile_view_gap?": "default 30", "profile_view_style?": "default 'Profile View'", "band_set?": "default auto: Standard when it has bands, else an Elevations band set (e.g. EG-FG Elevations and Stations), or a name",
                          "dry_run?": "validate the network transaction only"}),
}

IMPORT_ACTIONS = {"sewergems_import", "watergems_import", "pp_sheets_create"}

# --------------------------------------------------------------------------- edit operations (inside action 'edit')

EDIT_ACTION = (
    "Run a batch of operations on a specific open drawing. First call get_drawing_info and verify the drawing name. "
    "Pass its target and new_edit_request_id as target and request_id. Use the target from the reads used to prepare this edit. "
    "A changed session or active drawing stops the edit. The drawing operations share one transaction. "
    "dry_run aborts that transaction after validation and is not recorded under its request_id, so the same ID can be used "
    "for the real edit. File outputs run after commit. Editing requires permission for the "
    "target drawing. If the response is interrupted, check get_edit_status with the original target and request_id; "
    "resend only with the same parameters and request ID for a retry. A request whose state is 'error' changed nothing "
    "(for example Civil 3D was busy) and may be resent with the same request ID."
)

LAYER_PROPS = {"color?": ACI, "on?": "", "frozen?": "", "locked?": "", "plot?": ""}
ON_LAYER = {"layer?": "default: current layer", "color?": ACI}
DRAW_ON = {**ON_LAYER, "layout?": "paper-space layout to draw on (default: model space)"}
IF_EXISTS = {"if_exists?": "error (default) | skip | replace"}
NETWORK_PROPS = {"reference_surface?": "", "reference_alignment?": "", "description?": ""}
PROFILE_STYLE = {"layer?": "", "style?": "", "label_set?": ""}
PVIS = ("[[station, elevation], ...] or [{station, elevation, curve?: parabola|circular|asymmetric|none, length?, k?, "
        "radius?, lengths?: [before, after]}]; curve inferred from length/k/radius/lengths")

EDIT_GROUPS: dict[str, Group] = {
    "Layers and existing objects": {
        "layer_create": ("Create a layer.", {"name": "", **LAYER_PROPS, "if_exists?": "'skip' to accept an existing layer"}),
        "layer_set": ("Change a layer.", {"name": "", "new_name?": "", **LAYER_PROPS}),
        "entity_set": ("Move objects to a layer and/or set their colour or linetype.",
                       {"handles": "list", "layer?": "", "color?": ACI, "linetype?": "e.g. DASHED, CENTER (loaded from acadiso.lin if needed)"}),
        "entity_move": ("Displace objects.", {"handles": "list", "dx?": "", "dy?": "", "dz?": ""}),
        "entity_copy": ("Copy plain AutoCAD entities with a displacement; returns the new handles.", {"handles": "list", "dx?": "", "dy?": "", "dz?": ""}),
        "entity_rotate": ("Rotate plain AutoCAD entities about a base point (counter-clockwise).", {"handles": "list", "base": XY, "angle_deg": ""}),
        "entity_scale": ("Scale plain AutoCAD entities about a base point.", {"handles": "list", "base": XY, "factor": ""}),
        "entity_erase": ("Erase objects by handle (any object type, including Civil 3D objects and network parts).", {"handles": "list"}),
        "text_set": ("Change the content and/or height of a TEXT or MTEXT.", {"handle": "", "text?": "", "height?": ""}),
        "object_set": ("Rename / describe / restyle / re-layer a Civil 3D object.",
                       {"kind": "alignment | profile | surface | corridor | assembly | pipe_network | pressure_network", "name": "",
                        "alignment?": "required when kind is profile", "new_name?": "", "description?": "", "style?": "", "layer?": ""}),
    },
    "Drawing (AutoCAD)": {
        "draw_line": ("Draw a line.", {"start": XYZ, "end": XYZ, **DRAW_ON}),
        "draw_polyline": ("Draw a polyline (2D if all z are equal, otherwise 3D).", {"points": "[[x,y] or [x,y,z], ...]", "closed?": "", **DRAW_ON}),
        "draw_point": ("Draw an AutoCAD point.", {"at": XYZ, **DRAW_ON}),
        "draw_circle": ("Draw a circle.", {"center": XYZ, "radius": "", **DRAW_ON}),
        "draw_arc": ("Draw an arc, counter-clockwise from start_deg to end_deg (0 = east).",
                     {"center": XYZ, "radius": "", "start_deg": "", "end_deg": "", **DRAW_ON}),
        "draw_text": ("Draw single-line text.", {"text": "", "at": XYZ, "height?": "default 2.5", "rotation_deg?": "", "style?": "text style", **DRAW_ON}),
        "draw_mtext": ("Draw multiline text (\\P starts a new line).",
                       {"text": "", "at": XYZ, "height?": "default 2.5", "width?": "wrap width", "rotation_deg?": "", "style?": "text style", **DRAW_ON}),
        "draw_annotations": ("Draw up to 5,000 line and text items inside one edit operation.",
                             {"items": "[{op: draw_line | draw_text | draw_mtext, ...}, ...]"}),
        "draw_hatch": ("Hatch a closed boundary given by its corner points.",
                       {"points": "[[x,y], ...]", "pattern?": "SOLID (default), ANSI31, AR-CONC, ...", "scale?": "",
                        "keep_boundary?": "default true (associative hatch)", **DRAW_ON}),
        "draw_dimension": ("Draw a dimension between two points.",
                           {"from": XY, "to": XY, "line_at": "[x,y] a point on the dimension line",
                            "type?": "aligned (default) | horizontal | vertical", "text?": "override text", "style?": "dimension style", **DRAW_ON}),
        "draw_leader": ("Draw a multileader: an arrow with a text note.",
                        {"arrow_at": XY, "text_at": XY, "text": "", "height?": "default: the multileader style", "style?": "multileader style", **DRAW_ON}),
        "draw_table": ("Draw a table from rows of cell values.",
                       {"at": "[x,y] top-left corner", "rows": "[[cell, ...], ...]", "title?": "merged title row", "text_height?": "default 2.5",
                        "row_height?": "", "column_width?": "", **DRAW_ON}),
    },
    "Blocks": {
        "draw_block": ("Insert a block that is already defined in the drawing (see list_blocks).",
                       {"name": "", "at": XYZ, "scale?": "default 1", "rotation_deg?": "", "attributes?": "{TAG: value, ...}", **DRAW_ON}),
        "block_define": ("Define a block from existing plain AutoCAD entities.",
                         {"name": "", "handles": "list", "base": "[x,y] insertion base point",
                          "replace_originals?": "erase them and insert the block in their place"}),
        "attribute_set": ("Change attribute values of a block reference.", {"handle": "", "attributes": "{TAG: value, ...}"}),
        "block_import": ("Bring block definitions in from another drawing (.dwg/.dwt/.dxf), or the whole file as one block.",
                         {"path": "full path", "blocks?": "block names to copy; omit to bring the whole file in as one block",
                          "as_block?": "name for the whole-file block (default: the file name)", "replace?": "redefine blocks the drawing already has (default false); the drawing's own layers and styles are never changed"}),
        "title_block_insert": ("Put a title block on a layout and fill its attributes; with path it is first brought in from that file.",
                               {"layout": "", "path?": "full path of the title block drawing", "block?": "block name (in that file, or already defined)",
                                "at?": "[x,y] on paper, default [0,0]", "scale?": "", "rotation_deg?": "", "attributes?": "{TAG: value, ...}",
                                "replace?": "redefine if already in the drawing", "layer?": ""}),
        "dynamic_block_set": ("Set dynamic block properties (visibility state, distances, flip...). get_block_properties lists them.",
                              {"handle": "", "properties": "{name: value, ...}"}),
    },
    "Drawing file and sheets": {
        "layout_create": ("Create a layout (paper space tab).", {"name": ""}),
        "layout_delete": ("Delete a layout.", {"name": ""}),
        "layout_from_template": ("New layout copied from a layout of a template (.dwt/.dwg): its page setup, title block, viewports "
                                 "and notes, with the blocks, layers and styles they use (ones the drawing has are kept).",
                                 {"path": "full path of the template", "layout?": "layout in the template (default: its first)",
                                  "name?": "new layout name (default: the template layout's)", "if_exists?": "error (default) | replace"}),
        "page_setup_set": ("Page setup of a layout: a named page setup, plot device, paper, plot style, rotation, scale.",
                           {"layout": "", "page_setup?": "named page setup to apply first", "device?": "e.g. 'DWG To PDF.pc3'",
                            "paper?": "words of a paper size, e.g. 'ISO full bleed A1'", "plot_style?": "e.g. monochrome.ctb",
                            "rotation?": "0 | 90 | 180 | 270", "scale?": "'1:1' | 'fit' | '1:N'", "center?": ""}),
        "viewport_create": ("Create a viewport on a layout showing a model area at a scale. Give the area as window (fitted unless "
                            "scale is given), as view_center + scale, or as a named view.",
                            {"layout": "", "at": "[x,y] centre on paper (mm)", "width": "on paper", "height": "on paper",
                             "window?": "[xmin, ymin, xmax, ymax] in model space", "view_center?": "[x,y] in model space", "view?": "named view",
                             "scale?": "\"1:500\" (paper mm to drawing units)", "custom_scale?": "paper units per drawing unit, instead of scale",
                             "twist_deg?": "view twist (counter-clockwise), e.g. to run an alignment along the sheet",
                             "lock?": "", "freeze_layers?": "layers frozen in this viewport only", "layer?": ""}),
        "viewport_set": ("Change a viewport: position, size, model area, scale, twist, lock, layers frozen in it.",
                         {"handle": "", "at?": "", "width?": "", "height?": "", "window?": "", "view_center?": "", "view?": "",
                          "scale?": "", "custom_scale?": "", "twist_deg?": "", "lock?": "", "on?": "switch the viewport on/off",
                          "freeze_layers?": "", "thaw_layers?": ""}),
        "view_create": ("Save a model-space area as a named view (for viewports and plotting).",
                        {"name": "", "window": "[xmin, ymin, xmax, ymax]", "twist_deg?": "", "if_exists?": "error (default) | replace"}),
        "drawing_save": ("Save the drawing; runs after the batch is committed. No path: save in place. path: Save As (the open drawing "
                         "continues under the new name). path + copy:true: write a copy and keep working in the original. format dxf: export.",
                         {"path?": "full path", "copy?": "default false", "format?": "dwg (default) | dxf", "overwrite?": "default false"}),
        "plot_pdf": ("Plot to PDF with 'DWG To PDF.pc3'; runs after the batch is committed. One layout (path + layout), several "
                     "layouts as one multi-sheet PDF (path + layouts), or one PDF per layout (folder + layouts). Model space is plotted "
                     "to its extents, fitted to the paper; a layout keeps its own page setup (paper, scale). One multi-sheet PDF needs the same paper and "
                     "orientation on every layout (an AutoCAD plot-engine limit); use folder for mixed sizes.",
                     {"path?": "full path ending in .pdf", "layout?": "default: the current tab",
                      "layouts?": "list of layout names, or \"all\"", "folder?": "folder for one PDF per layout ('<drawing> - <layout>.pdf')",
                      "paper?": "words of a paper size, e.g. 'A1' or 'ISO full bleed A3'", "plot_style?": "e.g. monochrome.ctb",
                      "overwrite?": "default false"}),
    },
    "External references and underlays": {
        "xref_attach": ("Attach a drawing as an external reference.",
                        {"path": "full path to a .dwg", "name?": "", "at?": "[x,y,z] default origin", "layer?": "", "layout?": ""}),
        "xref_reload": ("Reload xrefs (all when names is omitted); runs after the commit.", {"names?": "xref names"}),
        "xref_detach": ("Detach xrefs; runs after the commit.", {"names": "xref names"}),
        "xref_bind": ("Bind xrefs into the drawing; runs after the commit.", {"names": "xref names", "insert?": "true: plain names (Insert); default Bind (xref$0$ prefixes)"}),
        "underlay_attach": ("Attach a PDF page or an image (png, jpg, tif, bmp, gif) as an underlay.",
                            {"path": "full path", "at?": "[x,y] lower-left, default origin", "page?": "PDF page, default 1",
                             "scale?": "PDF scale factor, default 1", "width?": "image width in drawing units", "rotation_deg?": "", **DRAW_ON}),
    },
    "AutoCAD styles and set-up": {
        "text_style_create": ("Create (or update) a text style.",
                              {"name": "", "font?": "arial.ttf (default), romans.shx, ...", "height?": "0 = set per text (default)",
                               "width_factor?": "", "oblique_deg?": "", "set_current?": "", **IF_EXISTS}),
        "dim_style_create": ("Create (or update) a dimension style, starting from based_on or the current style.",
                             {"name": "", "based_on?": "", "text_style?": "", "text_height?": "", "arrow_size?": "",
                              "extension_offset?": "", "extension_beyond?": "", "text_gap?": "", "decimals?": "", "overall_scale?": "",
                              "linear_scale?": "", "text_above?": "true: text above the dimension line, aligned with it", "set_current?": "", **IF_EXISTS}),
        "mleader_style_create": ("Create (or update) a multileader style.",
                                 {"name": "", "based_on?": "", "text_style?": "", "text_height?": "", "arrow_size?": "", "landing_gap?": "",
                                  "dogleg_length?": "0 = no dogleg", "set_current?": "", **IF_EXISTS}),
        "linetype_load": ("Load linetypes from a .lin file.", {"names": "e.g. [\"DASHED\", \"CENTER\"]", "file?": "default acadiso.lin (metric) / acad.lin"}),
        "annotation_scale_add": ("Add an annotation scale (paper mm to drawing units) and optionally make it current.",
                                 {"scale": "\"1:500\"", "name?": "default '1:500'", "set_current?": ""}),
        "layer_state_save": ("Save the current layer settings (on, frozen, locked, plot, colour, linetype...) as a layer state.",
                             {"name": "", "description?": "", **IF_EXISTS}),
        "layer_state_restore": ("Restore a saved layer state.", {"name": ""}),
        "purge": ("Remove unused blocks, layers, linetypes, styles and regapps (repeated until nothing more is unused).",
                  {"kinds?": "blocks | layers | linetypes | text_styles | dim_styles | mleader_styles | table_styles | regapps (default all)",
                   "list_only?": "report what is unused without removing it"}),
    },
    "Terrain": {
        "cogo_points_add": ("Add COGO points.", {"points": "[{x, y, z?, description?, name?}, ...]"}),
        "cogo_point_set": ("Change one COGO point.", {"number": "", "x?": "", "y?": "", "z?": "", "description?": "", "name?": ""}),
        "cogo_points_remove": ("Remove COGO points.", {"numbers": "list of point numbers"}),
        "surface_create": ("Create a TIN surface, optionally from points.",
                           {"name": "", "points?": "[[x,y,z], ...]", "style?": "", "layer?": "", "description?": ""}),
        "surface_add_points": ("Add points to a TIN surface.", {"name": "", "points": "[[x,y,z], ...]"}),
        "surface_add_breaklines": ("Add breaklines to a TIN surface, from points or from existing feature lines / 3D polylines "
                                   "(handles) - the way to grade a pad or channel.",
                                   {"name": "", "lines?": "[[[x,y,z], ...], ...] one list of points per breakline",
                                    "handles?": "existing feature lines, 3D polylines or lines",
                                    "type?": "standard (default) | non_destructive | proximity"}),
        "surface_add_boundary": ("Add a boundary to a surface.",
                                 {"name": "", "points": "[[x,y], ...]", "type?": "outer (default) | hide | show | data_clip",
                                  "non_destructive?": "default true"}),
        "surface_rebuild": ("Rebuild a surface.", {"name": ""}),
        "surface_extract": ("Extract drawing objects from a TIN surface: contours at an interval (optionally between low and high) or at "
                            "given elevations, major/minor contours, border, watersheds, grid lines.",
                            {"surface": "", "what": "contours | contours_at | major_contours | minor_contours | border | watershed | gridded",
                             "interval?": "for contours", "low?": "", "high?": "", "elevations?": "for contours_at",
                             "mode?": "plan (default, flat) | model (3D)", "layer?": ""}),
        "surface_volume_create": ("Create a TIN volume surface between two surfaces and return cut / fill / net volumes.",
                                  {"name": "", "base": "base surface", "comparison": "comparison surface"}),
        "feature_line_create": ("Create a feature line from 3D points.",
                                {"name": "", "points": "[[x,y,z], ...]", "closed?": "", "site?": "created if missing", "layer?": ""}),
        "feature_line_edit": ("Edit a feature line: elevations from a surface, at vertices (index from get_feature_line) or raised by an "
                              "amount; PIs and elevation points inserted or deleted; curve radii at vertices.",
                              {"handle?": "", "feature_line?": "name", "from_surface?": "", "include_intermediate?": "default true",
                               "elevations?": "[{index, elevation}]", "raise?": "add to every elevation", "insert_pis?": "[[x,y,z]]",
                               "delete_pis?": "[[x,y]] (nearest PI within 0.01)", "insert_elevation_points?": "[[x,y,z]]",
                               "delete_elevation_points?": "[[x,y]]", "radii?": "[{index, radius}]"}),
        "points_export": ("Write COGO points (all, or one point group) to a file in one of the drawing's point file formats; "
                          "runs after the batch is committed.",
                          {"path": "", "format": "see list_point_file_formats", "point_group?": "", "overwrite?": "default false"}),
        "udp_create": ("Define user-defined properties for points or parcels in a classification (created if missing).",
                       {"for?": "point (default) | parcel", "classification": "",
                        "properties": "[{name, type?: string|double|integer|boolean|enum, values?: enum values, default?, "
                                      "description?, min?, max?, data_type?: Double|Distance|Elevation|Area|Percent|...}]"}),
        "points_import": ("Import a point file (survey) with one of the drawing's point file formats, optionally into a point "
                          "group (created if missing). Description keys apply as in Civil 3D.",
                          {"path": "full path of the .txt/.csv/.pnt file", "format": "see list_point_file_formats, e.g. 'PENZD (comma delimited)'",
                           "point_group?": ""}),
        "point_group_create": ("Create (or with if_exists: replace, re-query) a point group. Filters take Civil 3D's lists and "
                               "wildcards, e.g. include_raw_descriptions: 'MH*,IL*', include_numbers: '1-100,205'.",
                               {"name": "", "description?": "", "include_all?": "", "include_numbers?": "", "include_raw_descriptions?": "",
                                "include_full_descriptions?": "", "include_names?": "", "include_elevations?": "e.g. '>10'",
                                "exclude_numbers?": "", "exclude_raw_descriptions?": "", "exclude_names?": "", "exclude_elevations?": "",
                                "point_style?": "override", "label_style?": "override (list_styles kind point_label)",
                                "if_exists?": "error (default) | replace"}),
        "description_key_create": ("Add a description key (code, e.g. 'MH*') to a key set (created if missing) with the point "
                                   "style, label style and layer it applies.",
                                   {"code": "", "key_set?": "default 'Default'", "format?": "full description, default $*",
                                    "point_style?": "", "label_style?": "", "layer?": "", "apply_to_points?": "re-apply keys to all points",
                                    "if_exists?": "error (default) | replace"}),
        "surface_analysis_set": ("Elevation, slope or slope-arrow banding of a surface: ranges with colours (or 'count' equal "
                                 "ranges), and optionally turn that display on in a surface style and give it to the surface.",
                                 {"surface": "", "type?": "elevations (default) | slopes | slope_arrows",
                                  "ranges?": "[{min, max, color?: ACI}, ...] elevations in drawing units, slopes in percent",
                                  "count?": "default 8 (without ranges)", "max_slope?": "percent, default 30 (slopes without ranges)",
                                  "show_in_style?": "surface style to show the analysis in (changes that style for every surface using it)"}),
        "surface_waterdrop": ("Waterdrop (flow path) polylines downhill from points on a surface.",
                              {"surface": "", "points": "[[x,y], ...]", "kind?": "polyline2d (default) | polyline3d", "layer?": ""}),
        "surface_definition_add": ("Add data to a TIN surface's definition and rebuild it: contours (polylines at their elevation, "
                                   "or point lists), a DEM file, a point group, drawing objects, wall breaklines, a breakline (.flt) file.",
                                   {"surface": "", "kind": "contours | dem | point_group | drawing_objects | wall_breaklines | breakline_file",
                                    "handles?": "contours / drawing objects", "lines?": "contours: [[[x,y,z], ...], ...]",
                                    "path?": "dem / breakline_file", "null_elevation?": "dem", "point_group?": "",
                                    "object_type?": "drawing_objects: points | lines | blocks | texts | faces | polyfaces",
                                    "maintain_edges?": "default true", "description?": "",
                                    "walls?": "[{handle, elevation | elevations: [per vertex] (absolute level of the wall's offset side), right_side?: default true}]",
                                    "mid_ordinate?": "default 1", "max_distance?": "supplementing; contours 100, others 0",
                                    "weeding_distance?": "contours 15, others 0", "weeding_angle?": "contours 4, others 0", "minimize_flat_areas?": "contours, default false", "rebuild?": "default true"}),
        "surface_create_from": ("Create a surface from a LandXML file (TIN), a DEM (grid surface), a .tin file, a cropped area of "
                                "another surface, or a static copy of a corridor surface.",
                                {"name": "", "source": "landxml | dem | tin_file | crop | corridor", "path?": "landxml / dem / tin_file",
                                 "landxml_surface?": "default: the first in the file", "surface?": "crop: source surface",
                                 "points?": "crop: [[x,y], ...] polygon", "corridor?": "", "corridor_surface?": "",
                                 "style?": "", "layer?": "", "description?": ""}),
        "surface_snapshot": ("Create, rebuild or remove a surface's snapshot.", {"surface": "", "action?": "create (default) | rebuild | remove"}),
        "surface_solids_create": ("3D solids between a TIN surface and a depth below it, a fixed elevation or another surface; with "
                                  "file, written to a new DWG after the edit commits instead. Returns the solids' total volume.",
                                  {"surface": "", "mode": "depth | elevation | surface", "depth?": "", "elevation?": "", "bottom_surface?": "",
                                   "layer?": "existing layer, default 0", "color?": "ACI, default 256 (ByLayer)", "file?": ".dwg path",
                                   "overwrite?": "default false"}),
        "catchment_create": ("Create a catchment from a boundary on a surface, in a group (created if missing), with its flow path "
                             "traced downhill from a point or given; returns area, flow path length and time of concentration.",
                             {"name": "", "surface": "", "boundary?": "[[x,y,z], ...]", "handle?": "polyline boundary instead",
                              "group?": "default 'Catchments'", "style?": "", "layer?": "", "flow_from?": "[x,y] most distant point",
                              "flow_path?": "[[x,y,z], ...]", "runoff_coefficient?": "", "impervious_percent?": "0-100",
                              "network?": "for discharge_structure", "discharge_structure?": "structure name"}),
    },
    "Alignments": {
        "alignment_create": ("Create an alignment from PI points: fixed tangents, and at each interior PI a free curve, optionally "
                             "with entry/exit spirals (spiral-curve-spiral).",
                             {"name": "", "points": "[[x,y], ...]", "radius?": "curve radius at every PI",
                              "spiral_in?": "spiral length before every curve", "spiral_out?": "default = spiral_in",
                              "curves?": "per interior PI: radius, or {radius, spiral_in?, spiral_out?}, or null for no curve (overrides radius)",
                              "spiral_type?": "Clothoid (default) | CubicParabola | Bloss | ...", "site?": "default siteless",
                              "layer?": "", "style?": "", "label_set?": "", "description?": ""}),
        "alignment_set": ("Station equations, design speeds and (offset alignments) widenings of an alignment.",
                          {"alignment": "", "station_equations?": "[{back: raw station back, ahead, type?: increasing|decreasing}]",
                           "clear_station_equations?": "", "design_speeds?": "[{station, speed, comment?}]", "clear_design_speeds?": "",
                           "widenings?": "offset alignments only: [{start, end, offset}]"}),
        "alignment_connected_create": ("A connected alignment (curb return) joining two alignments at given stations with an arc "
                                       "or a spiral-curve group. Building block of an intersection (the intersection wizard is not in the API).",
                                       {"name": "", "incoming": "alignment", "incoming_station": "", "outgoing": "alignment", "outgoing_station": "",
                                        "radius": "", "curve_group?": "Arc (default) | SCS | SSCS | SCSS | SSCSS | CCC",
                                        "spiral_in?": "", "spiral_out?": "", "offset_in?": "", "offset_out?": "", "overlap_in?": "default 1", "overlap_out?": "default 1",
                                        "greater_than_180?": "", "site?": "", "layer?": "", "style?": "", "label_set?": ""}),
        "alignment_from_polyline": ("Create an alignment from an existing polyline (handle), optionally with curves between "
                                    "its tangents.",
                                    {"name": "", "handle": "polyline", "add_curves?": "default false", "erase_polyline?": "default false",
                                     "site?": "", "layer?": "", "style?": "", "label_set?": "", "description?": ""}),
        "alignment_offset_create": ("Create an offset alignment (negative offset = left), optionally for a station range.",
                                    {"name": "", "parent": "", "offset": "", "start_station?": "", "end_station?": "", "style?": ""}),
        "superelevation_slope_set": ("Change the cross slope of one lane or shoulder at an existing superelevation critical "
                                     "station (see get_superelevation).",
                                     {"alignment": "", "station": "", "slope_percent": "e.g. -2.5",
                                      "segment?": "RightInLaneCrossSlope (default) | RightOutLaneCrossSlope | LeftInLaneCrossSlope | "
                                                  "LeftOutLaneCrossSlope | ...ShoulderCrossSlope", "tolerance?": "default 0.01"}),
        "profile_create": ("Create a layout profile from PVIs on an alignment, with vertical curves where asked.",
                           {"alignment": "", "name": "", "pvis": PVIS, **PROFILE_STYLE}),
        "profile_pvis_set": ("Add, change (elevation and/or curve) or remove PVIs of a layout profile. A PVI within 0.001 of a given "
                             "station is changed; otherwise one is added. curve: 'none' removes a curve.",
                             {"alignment": "", "profile": "", "pvis?": PVIS + "; elevation optional when changing", "remove?": "[station, ...]"}),
        "profile_from_surface": ("Create a surface (existing ground) profile.", {"alignment": "", "surface": "", "name": "", **PROFILE_STYLE}),
        "profile_derive": ("A profile from another object: a corridor feature line (kind corridor_feature_line), an offset alignment's "
                           "profile at a cross slope (offset_slope), or a static layout copy of a corridor or offset profile (static_copy).",
                           {"kind": "corridor_feature_line | offset_slope | static_copy", "name": "",
                            "alignment?": "for corridor_feature_line and static_copy", "corridor?": "", "baseline?": "default: first",
                            "code?": "feature line code, e.g. ETW", "index?": "which feature line of that code, default 0",
                            "offset_alignment?": "for offset_slope", "profile?": "source profile (offset_slope: the parent's profile)",
                            "slope_percent?": "for offset_slope, e.g. -2", **PROFILE_STYLE}),
        "profile_view_create": ("Create a profile view of an alignment (shows all its profiles).",
                                {"alignment": "", "at": "[x,y] lower-left insertion point", "name?": "", "style?": "",
                                 "band_set?": "name, or auto (Standard when it has bands, else the band set with Elevation in its name; default: Standard or the first set)"}),
        "profile_views_create": ("Several profile views of one alignment, each covering 'length' of stations, laid out in rows or columns.",
                                 {"alignment": "", "at": "[x,y] insertion point", "length": "stations per view", "per_row?": "default 5",
                                  "gap_x?": "default 50", "gap_y?": "default 50", "order?": "rows (default) | columns",
                                  "start_corner?": "UpperLeft (default) | LowerLeft | UpperRight | LowerRight", "name?": "", "style?": "", "band_set?": ""}),
        "profile_view_add_parts": ("Show network parts in a profile view.",
                                   {"profile_view": "", "pipe_network?": "gravity network name", "pressure_network?": "", "handles?": "individual parts",
                                    "pipe_names?": "filter gravity pipes by name", "structure_names?": "filter gravity structures by name"}),
        "sample_lines_create": ("Create sample lines on an alignment (the group is created if missing).",
                                {"alignment": "", "group": "", "stations?": "[station, ...]", "interval?": "every N along the alignment",
                                 "prefix?": "default 'SL-'", "left_width?": "m, default 20", "right_width?": "m, default 20"}),
        "sample_sources_set": ("Choose which sources (surfaces, corridors, corridor surfaces, networks) a sample line group samples, so "
                               "sections exist for get_sections and section views.",
                               {"alignment": "", "group": "", "sources?": "source names, default: all", "sampled?": "default true"}),
        "material_list_create": ("A material list on a sample line group (earthwork and structure quantities): materials from surfaces "
                                 "(with a condition) and/or corridor shapes; the surfaces are switched to sampled. Read the result with get_material_volumes.",
                                 {"alignment": "", "group": "", "name": "",
                                  "materials": "[{name, type?: earthwork (default)|cut|fill|cut_fill|structure, items: [{surface, condition?: "
                                               "above|below|base|compare|include|exclude} | {corridor, shape: shape code}], cut_factor?, fill_factor?, refill_factor?}]",
                                  "method?": "average_end_area (default) | prismoidal | composite", "if_exists?": "error (default) | replace"}),
        "section_volume_tables": ("A total-volume or material table on every section view of a sample line group, for a material list.",
                                  {"alignment": "", "group": "", "material_list": "", "type?": "total (default) | material",
                                   "offset_x?": "", "offset_y?": ""}),
        "section_views_create": ("Create a section view for every sample line of a group, laid out in a grid. All section sources of the "
                                 "group (surfaces, corridors, networks) are sampled first.",
                                 {"alignment": "", "group": "sample line group", "at": "[x,y] lower-left of the first view",
                                  "columns?": "default 5", "spacing_x?": "default 80", "spacing_y?": "default 50"}),
    },
    "Corridors": {
        "assembly_create": ("Create an assembly from stock subassemblies, attached in the order given (each one to the end of the "
                            "previous one on its side).",
                            {"name": "", "at": "[x,y] where the assembly is drawn",
                             "type?": "UndividedCrownedRoad (default) | UndividedPlanarRoad | DividedCrownedRoad | DividedPlanarRoad | Railway | Other",
                             "subassemblies": "[{class: e.g. 'LaneSuperelevationAOR' or 'BasicLane', side?: Right | Left, name?}, ...]"}),
        "corridor_create": ("Create a corridor: one baseline (alignment + design profile) and one region with an assembly; it is rebuilt at once.",
                            {"name": "", "alignment": "", "profile": "design profile", "assembly": "", "start_station?": "", "end_station?": "",
                             "baseline?": "name, default BL-1", "region?": "name, default RG-1"}),
        "corridor_surface_add": ("Add a corridor surface built from link codes.", {"corridor": "", "name": "", "link_codes?": "default ['Top']"}),
        "corridor_rebuild": ("Rebuild a corridor.", {"name": ""}),
        "corridor_baseline_add": ("Add a baseline (alignment + profile) to a corridor, with a first region when an assembly is given.",
                                  {"corridor": "", "alignment": "", "profile": "", "name?": "default BL-n", "assembly?": "",
                                   "region?": "default RG-1", "start_station?": "", "end_station?": "", "rebuild?": "default true"}),
        "corridor_region_edit": ("Regions of a baseline: add, merge, split at a station, move start/end, add or delete extra stations, "
                                 "assembly frequency.",
                                 {"corridor": "", "baseline?": "default: first", "add?": "{name, assembly, start_station, end_station}",
                                  "merge?": "[first region, last region]", "region?": "region to change", "start_station?": "", "end_station?": "",
                                  "split_at?": "station", "add_stations?": "[station | {station, description}]", "delete_stations?": "[station]",
                                  "frequency?": "{tangents?, curves?, spirals?, profile_curves?}", "rebuild?": "default true"}),
        "corridor_surface_set": ("Boundaries, masks and breakline codes of a corridor surface.",
                                 {"corridor": "", "surface": "corridor surface name", "feature_line_codes?": "[code] added as breaklines",
                                  "boundaries?": "[{name, extents: true | feature_line_code | points, type?: outside (default) | inside}]",
                                  "masks?": "[{name, feature_line_code | points}]", "rebuild?": "default true"}),
        "corridor_feature_line_export": ("Export a corridor feature line (by code, and index or side) as an alignment, a profile on "
                                         "an alignment, 3D polyline(s) or a static grading feature line.",
                                         {"corridor": "", "code": "", "as": "alignment | profile | polyline3d | grading_feature_line",
                                          "baseline?": "default: first", "index?": "default 0", "side?": "left | right",
                                          "name?": "needed for alignment / profile", "alignment?": "profile: the alignment to put it on",
                                          "alignment_type?": "Centerline (default) | Offset | CurbReturn | Utility | Rail",
                                          "site?": "created if missing; default: alignment in site 'Corridor Exports', feature line siteless "
                                                   "(objects sharing a site interact: a feature line over an alignment is flattened)",
                                          "split?": "polyline3d: one polyline per continuous piece", **PROFILE_STYLE}),
        "corridor_points_export": ("COGO points at the corridor's feature-line points (all point codes or the codes given), in a new point group.",
                                   {"corridor": "", "point_group": "new group name", "codes?": "point codes, default: all"}),
        "corridor_targets_set": ("Point subassembly targets (surface, width/offset, elevation/slope) of a baseline's regions at "
                                 "surfaces, alignments, profiles or drawing objects. Names from get_corridor_targets.",
                                 {"corridor": "", "baseline?": "default: first", "regions?": "default: all",
                                  "targets": "[{target: name or logical name, subassembly?: part of its name, surfaces? | alignments? | "
                                             "profiles?: [{alignment, profile}] | handles?, option?: Nearest | Farthest | Flattest | Steepest}]",
                                  "rebuild?": "default true"}),
        "corridor_slope_pattern_add": ("A slope pattern between two corridor feature lines.",
                                       {"corridor": "", "from": "{code, side? | index?, baseline?}", "to": "{code, side? | index?, baseline?}",
                                        "baseline?": "default for from/to", "style?": "slope pattern style (one is created if the drawing has none)",
                                        "start_station?": "", "end_station?": "", "clear?": "remove existing slope patterns first"}),
        "corridor_solids_export": ("3D solids (or bodies) from a corridor's shapes and/or links into this drawing, for the corridor, "
                                   "a baseline or a region (optionally a station range); returns the solids' total volume.",
                                   {"corridor": "", "baseline?": "", "region?": "", "start_station?": "region range", "end_station?": "",
                                    "shapes?": "default true", "links?": "default false", "bodies?": "default false (3D solids)",
                                    "sweep?": "default false", "include_codes?": "", "exclude_codes?": "", "layer?": ""}),
    },
    "Styles and presentation": {
        "labels_place": ("Place Civil 3D labels. Each item has a kind: station_offset {alignment, at}; alignment_geometry {alignment, "
                         "line_style?, curve_style?, spiral_style?} (every line/curve/spiral); surface_spot {surface, points}; "
                         "surface_slope {surface, points | from+to}; surface_contours {surface, line, interval?}; parcel_area {parcels?}; "
                         "profile_pvi / profile_crest / profile_sag / profile_tangent {profile_view, profile}; "
                         "station_elevation {profile_view, station, elevation}; note {at}. style? and marker_style? are optional.",
                         {"labels": "[{kind, ...}]"}),
        "label_style_create": ("Create (or replace) a label style made of text components. Contents use Civil 3D property fields; "
                               "get_style on an existing style shows the exact syntax.",
                               {"kind": "pipe_label | structure_label | pressure_pipe_label | ...", "name": "",
                                "components": "[{text, name?, height?: mm on paper, attachment?: MiddleCenter|BottomLeft|..., x_offset?: mm, y_offset?: mm} "
                                              "or {type: 'arrow'} (direction arrow; along a pipe it shows the flow), ...]",
                                "if_exists?": "fail (default) | skip | replace"}),
        "network_styles": ("Set the plan style and layer of every pipe and structure of a gravity network, and add plan labels.",
                           {"network": "", "pipe_style?": "", "structure_style?": "", "pipe_label_style?": "labels every pipe",
                            "structure_label_style?": "labels every structure", "pipe_layer?": "", "structure_layer?": "", "label_layer?": "",
                            "structure_label_dx?": "label offset from the structure, default 3", "structure_label_dy?": "default -3"}),
        "alignment_label_set": ("Replace the labels of alignments with a label set (e.g. one without station labels), optionally restyle them.",
                                {"alignments": "list of names", "label_set": "see list_styles kind alignment_label_set", "style?": "", "layer?": ""}),
        "profile_view_set": ("Change a profile view: style, station range, elevation range.",
                             {"profile_view": "", "style?": "", "station_start?": "", "station_end?": "", "elevation_min?": "", "elevation_max?": ""}),
        "band_style_set": ("Change a band style's title and size (what the band writes at each station is set in Civil 3D's dialog).",
                           {"kind": "band_profile_data | band_pipe_network | ...", "name": "", "title?": "", "height?": "mm on paper",
                            "title_width?": "mm", "title_text_height?": "mm"}),
        "profile_view_bands": ("Replace the bands of a profile view: every band of a band set, or bands listed one by one "
                               "(each with an existing band style and its data source).",
                               {"profile_view": "", "band_set?": "a band set name, or auto (as profile_view_create); "
                                "profile data bands get the alignment's first profile", "bottom?": "[{type: profile_data|pipe_network|horizontal_geometry|vertical_geometry, style, "
                                "profile1?, profile2?, network?: gravity network (pipe_network bands), gap?: mm, label_start?, label_end?}, ...]",
                                "top?": "same"}),
    },
    "Gravity networks": {
        "pipe_network_create": ("Create an empty gravity pipe network.", {"name": "", "parts_list?": "name or handle", **NETWORK_PROPS}),
        "pipe_network_add": ("Add structures and straight pipes to a gravity network. A part is chosen by 'family' (words contained in "
                             "the family description) plus 'inner_diameter' (units as shown by list_gravity_parts) or 'size_index'. "
                             "Name endpoint structures for strict connections. Geometric matching skips ambiguous endpoints and reports them.",
                             {"network": "", "structures?": "[{id?, name?, description?, at:[x,y,z], family, inner_diameter? | size_index?, rotation_deg?, rim?, sump?}, ...]",
                              "pipes?": "[{id?, name?, description?, start:[x,y,z], end:[x,y,z] (centreline points), family, inner_diameter? | size_index?, start_structure?, end_structure?, verify_bore_mm? (metric drawings)}, ...]",
                              "parts_list?": "default: the network's", "apply_rules?": "default false", "connect_tolerance?": "default 0.01"}),
        "pipe_curved_add": ("Add a curved gravity pipe between two points, by radius (and clockwise) or through a point.",
                            {"network": "", "start": "[x,y,z] centreline", "end": "[x,y,z]", "radius?": "", "clockwise?": "default false",
                             "through?": "[x,y] a point on the arc", "family?": "pipe family (list_gravity_parts)", "size_index?": "", "inner_diameter?": "alternative to size_index", "name?": "", "start_structure?": "name", "end_structure?": "name",
                             "parts_list?": "", "apply_rules?": "default false"}),
        "part_swap": ("Swap the gravity pipes or structures named by 'names' or 'handles' (one of them is required) to another family / size.",
                      {"network": "", "names?": "[part name]", "handles?": "[handle] (parts of this network)", "family": "pipe or structure family (list_gravity_parts)", "size_index?": "", "inner_diameter?": "alternative to size_index", "parts_list?": ""}),
        "parts_to_section_views": ("Show gravity network parts in every section view of a sample line group.",
                                   {"network": "", "alignment": "", "group": "", "names?": "[part name], default: all", "handles?": ""}),
        "pipe_set": ("Change one gravity pipe.",
                     {"handle": "", "start?": XYZ, "end?": XYZ,
                      "inner_diameter?": "nearest catalogue size, in the units of list_gravity_parts (mm in metric lists)", "description?": ""}),
        "structure_set": ("Change one gravity structure.",
                          {"handle": "", "location?": XYZ, "rim?": "", "sump?": "", "rotation_deg?": "", "description?": ""}),
        "parts_list_add_family": ("Add a part family from the Civil 3D catalogue to a gravity parts list, with the sizes given "
                                  "(e.g. a shallow flat-top manhole for a sanitary list). Nothing changes if it is already there.",
                                  {"parts_list": "", "domain": "pipe | structure", "family": "exact family description, e.g. 'Cylindrical Junction Structure NF SI'",
                                   "sizes?": "[inner diameter or width, ...] in the catalogue units (mm)"}),
        "network_table": ("Draw a pipe or structure schedule of a network as an AutoCAD table (snapshot of the current data: draw "
                          "it again after changes). Pipes: from/to, size, material, length, slope, inverts. Structures: rim, "
                          "sump, depth, inverts in/out.",
                          {"network": "", "kind?": "pipes (default) | structures | pressure_pipes", "at": XY, "title?": "",
                           "text_height?": "", "row_height?": "", "column_width?": "", **DRAW_ON}),
        "hgl_set": ("Set hydraulic / energy grade lines and flow on a gravity network's pipes and structures (e.g. from a "
                    "SewerGEMS or StormCAD run) so profile views and labels can show them.",
                    {"network": "", "pipes?": "[{name, hgl_up?, hgl_down?, egl_up?, egl_down?, flow?}, ...]",
                     "structures?": "[{name, hgl?, egl?}, ...]"}),
        "labels_add": ("Add default labels to the pipes and structures of a gravity network.",
                       {"pipe_network": "", "pipes?": "default true", "structures?": "default true"}),
    },
    "Pressure networks": {
        "pressure_network_create": ("Create an empty pressure network.", {"name": "", "parts_list": "name or handle", **NETWORK_PROPS}),
        "pressure_network_set": ("Change a pressure network.", {"name": "", "new_name?": "", "parts_list?": "", **NETWORK_PROPS}),
        "pressure_network_erase": ("Erase a pressure network with all its parts.", {"name": ""}),
        "pressure_add": ("Add straight pipes, fittings and appurtenances to a pressure network (new or existing). Each part is "
                         "chosen from the parts list by 'size' (a whole number in its description, e.g. 150) and 'match' (words "
                         "in it, e.g. 'ductile'); exactly one part must match. Fittings/appurtenances connect to pipe ends at the "
                         "same point. z is the pipe centreline elevation.",
                         {"network": "", "pipes?": "[{id?, name?, description?, start:[x,y,z], end:[x,y,z], size, match? (or part: exact description), "
                                                    "through?: [x,y] makes a curved pipe through that point}, ...]",
                          "fittings?": "[{id?, name?, description?, at:[x,y,z], type?: Elbow|Tee|Reducer|Cross|Cap|..., size, match? (or part)}, ...]",
                          "appurtenances?": "[{id?, name?, description?, at:[x,y,z], type?: Valve|Hydrant|..., size, match? (or part)}, ...]",
                          "parts_list?": "default: the network's", "connect_tolerance?": "default 0.01"}),
        "pressure_pipe_run": ("Create a pipe run along a 2D path at a depth of cover below the network's reference surface, with "
                              "bends added automatically (the way the Civil 3D pipe-run command works).",
                              {"network": "", "name": "", "points": "[[x,y], ...]", "size": "", "match?": "", "cover?": "default 1.0",
                               "auto_bends?": "default true", "keep_path?": "keep the path polyline", "layer?": "", "parts_list?": ""}),
        "pressure_run_edit": ("Edit a pressure pipe run: bends at plan points (PIs), vertical bends at [station, elevation], or merge "
                              "another run into it.",
                              {"network": "", "run": "", "bends?": "[[x,y]]", "vertical_bends?": "[[station, elevation]]", "merge?": "other run name"}),
        "pressure_part_set": ("Move or resize ONE pressure part.",
                              {"handle": "", "start?": "[x,y,z] (pipe)", "end?": "[x,y,z] (pipe)", "location?": "[x,y,z] (fitting/appurtenance)",
                               "size?": "new size", "match?": "", "description?": ""}),
        "pressure_labels_add": ("Add plan labels to the pipes, fittings and appurtenances of a pressure network.",
                                {"pressure_network": "", "pipes?": "default true", "fittings?": "default true", "appurtenances?": "default true",
                                 "handles?": "label only these parts (default: every part of the chosen kinds)",
                                 "pipe_style?": "see list_styles kind pressure_pipe_label", "fitting_style?": "", "appurtenance_style?": ""}),
    },
}


def _flatten(groups: dict[str, Group]) -> Group:
    flat: Group = {}
    for group in groups.values():
        for name, entry in group.items():
            assert name not in flat, f"{name} is listed twice"
            flat[name] = entry
    return flat


READ_ACTIONS: Group = _flatten(READ_GROUPS)
EDIT_OPS: Group = _flatten(EDIT_GROUPS)

# --------------------------------------------------------------------------- rendering

EDIT_PARAMS = {"operations": "list of edit operations", "target": "drawing target from get_drawing_info",
               "request_id": "unique edit UUID", "dry_run?": "bool"}


def _signatures(groups: dict[str, Group]) -> list[str]:
    """One heading per group, one line per entry: '- name(param, param?): summary'."""
    out: list[str] = []
    for title, group in groups.items():
        out.append(f"[{title}]")
        out += [f"- {name}({', '.join(params)}): {summary}" if params else f"- {name}: {summary}"
                for name, (summary, params) in group.items()]
    return out


def tool_description(mode: str = "all") -> str:
    """Description advertised to MCP clients. Mode is 'all', 'read', 'edit' or 'import' (split mode's import tool)."""
    out: list[str] = []
    if mode == "import":
        out.append("Import a Bentley SewerGEMS / WaterGEMS model, or make plan and profile sheets, in the drawing open in "
                   "Civil 3D (it draws, so it needs edit permission). Call with action=<name> and params={...}; run the matching "
                   "*_check / pp_sheets_plan action of the read tool first, and pass target and request_id from get_drawing_info.")
        out += _signatures({"Imports": {k: v for k, v in LOCAL_ACTIONS.items() if k in IMPORT_ACTIONS}})
        return "\n".join(out)
    if mode in ("all", "read"):
        out.append("Work with the drawing open in Autodesk Civil 3D on this computer. Call with action=<name> and params={...}. "
                   "Reading never changes anything. Parameters ending in ? are optional. action='help' returns every "
                   "parameter description.")
        out.append("\nREAD ACTIONS")
        out += _signatures(READ_GROUPS)
        local = LOCAL_ACTIONS if mode == "all" else {k: v for k, v in LOCAL_ACTIONS.items() if k not in IMPORT_ACTIONS}
        out += _signatures({"QA / help": local})
    if mode in ("all", "edit"):
        out.append("\nEDIT ACTION\n- edit(operations, target, request_id, dry_run?): " + EDIT_ACTION)
        out.append("\nEDIT OPERATIONS (each is {op: name, ...})")
        out += _signatures(EDIT_GROUPS)
        out.append("\nCoordinates are drawing units (easting, northing, elevation). Objects are addressed by name, parts and "
                   "plain entities by the 'handle' the read actions return. Before a large edit, read the relevant lists, "
                   "then send ONE batch.")
    return "\n".join(out)


def help_data(topic: str | None = None, *, include_edits: bool = True) -> dict:
    def full(entries: Group) -> dict:
        return {name: {"summary": summary, "params": params} for name, (summary, params) in entries.items()}

    local = LOCAL_ACTIONS if include_edits else {k: v for k, v in LOCAL_ACTIONS.items() if k not in IMPORT_ACTIONS}
    entries = ((READ_ACTIONS, "action"), (local, "action"))
    edit = {"summary": EDIT_ACTION, "params": EDIT_PARAMS}
    if topic is None:
        data = {"readActions": full(READ_ACTIONS), "localActions": full(local)}
        if include_edits:
            data.update(edit=edit, editOperations=full(EDIT_OPS))
        return data
    if include_edits and topic == "edit":
        return {"edit": {**edit, "operations": full(EDIT_OPS)}}
    if include_edits:
        entries += ((EDIT_OPS, "edit operation (use inside action 'edit')"),)
    for group, kind in entries:
        if topic in group:
            summary, params = group[topic]
            return {topic: {"summary": summary, "params": params, "kind": kind}}
    raise ValueError(f"Unknown help topic '{topic}'.")
