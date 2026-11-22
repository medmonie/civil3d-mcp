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
STYLE_KINDS = ("alignment | alignment_label_set | profile | profile_label_set | profile_view | profile_view_band_set | surface | "
               "corridor | pipe | structure | point | feature_line | sample_line | pressure_pipe_label | pressure_fitting_label | "
               "pressure_appurtenance_label | gravity_parts_list | pipe_label | structure_label | band_profile_data | band_pipe_network | "
               "band_horizontal_geometry | band_vertical_geometry")

# --------------------------------------------------------------------------- read actions (never change the drawing)

READ_GROUPS: dict[str, Group] = {
    "Drawing (AutoCAD)": {
        "ping": ("Connection check: bridge version, Civil 3D version, active drawing, whether editing is allowed.", {}),
        "get_drawing_info": ("Active drawing: path, units, object counts, the drawing target, and a fresh edit request UUID.", {}),
        "list_layers": ("Layers with on/frozen/locked/plot state, colour and xref flag.", {"limit?": "default 500"}),
        "count_entities": ("Count model-space entities by DXF type or by layer.",
                           {"group_by?": "'type' (default) or 'layer'", "layer?": "only this layer"}),
        "get_selection": ("Objects the user has selected in Civil 3D: handle, type, layer, Civil name, extents.", {"limit?": "default 200"}),
        "get_objects": ("Describe objects by handle: type, layer, Civil name, extents.", {"handles": "list of handle strings"}),
        "list_entities": ("AutoCAD entities in model space with their geometry: lines, polylines (vertices), circles, arcs, text, "
                          "mtext, points, block references (name, attributes), hatches, dimensions.",
                          {"type?": "DXF type, e.g. LINE, LWPOLYLINE, CIRCLE, ARC, TEXT, MTEXT, POINT, INSERT, HATCH", "layer?": "",
                           "window?": "[xmin, ymin, xmax, ymax]", "limit?": "default 100"}),
        "list_blocks": ("Block definitions: name, attributes flag, number of references.", {"limit?": "default 500"}),
        "list_layouts": ("Layouts (paper space tabs): name, tab order, plot device, paper size.", {}),
    },
    "Terrain": {
        "list_surfaces": ("Surfaces: name, type, style, layer, min/max/mean elevation.", {}),
        "get_surface_elevation": ("Elevation of a TIN surface at (x, y).", {"surface_name": "", "x": "easting", "y": "northing"}),
        "get_surface_volume": ("Cut, fill and net volume of a TIN volume surface.", {"surface_name": ""}),
        "list_cogo_points": ("COGO points: number, name, easting, northing, elevation, description.", {"limit?": "default 200"}),
        "list_feature_lines": ("Feature lines: length, min/max elevation, points.", {"limit?": "default 200"}),
        "list_parcels": ("Parcels of every site: name, number, centroid, extents. Read only.", {"limit?": "default 500"}),
    },
    "Alignments": {
        "list_alignments": ("Alignments (incl. the 'Pipe Run' alignments of pressure networks): type, site, length, stations.", {}),
        "alignment_station_to_point": ("Easting/northing of a station and offset (offset + = right).",
                                       {"alignment_name": "", "station": "", "offset?": "default 0"}),
        "alignment_point_to_station": ("Station and offset of a point.", {"alignment_name": "", "x": "", "y": ""}),
        "list_profiles": ("Profiles: type, PVI count, station range, min/max elevation.", {"alignment_name?": "only this alignment"}),
        "get_profile_elevation": ("Elevation of a profile at a station.", {"alignment_name": "", "profile_name": "", "station": ""}),
        "list_profile_views": ("Profile views: alignment, style, station and elevation range, position in the drawing.", {}),
        "list_sample_lines": ("Sample line groups with the stations of their sample lines.", {"alignment_name?": "only this alignment"}),
        "list_section_views": ("Section views: alignment, sample line group, sample line, station.", {"limit?": "default 500"}),
    },
    "Corridors": {
        "list_corridors": ("Corridors: baselines with their regions and assemblies, corridor surfaces, out-of-date flag.", {}),
        "list_assemblies": ("Assemblies with their subassemblies (for corridor_create).", {}),
    },
    "Gravity networks": {
        "list_pipe_networks": ("Gravity pipe networks: parts list, reference surface/alignment, pipe and structure counts.", {}),
        "list_pipes": ("Gravity pipes: part, inner diameter, slope (m/m), length, start/end, connected structures.", LIMIT_200),
        "list_structures": ("Gravity structures: part, location, rim/sump elevation, connected pipes.", LIMIT_200),
        "list_gravity_parts": ("Without parts_list: the gravity parts lists. With it: its pipe and structure families and sizes "
                               "(use these to fill 'family' + 'inner_diameter' / 'size_index' in pipe_network_add).",
                               {"parts_list?": "name or handle"}),
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
                           "profile_view_style?": "overrides the presentation setting", "dry_run?": "validate the core network, profiles, and styles transaction",
                           "finish_attempt?": "default 1. Increment only after the preceding finishing transaction is confirmed rolled back."}),
    "watergems_check": ("Read a Bentley WaterGEMS export (and the drawing's CAD layers, when given) without drawing: pipes by "
                        "material and size, the catalogue parts that will stand in, CAD valves/services/hydrants matched to the "
                        "model, the profile lines of the mains, and where model and drawing differ.", WATERGEMS_INPUT),
    "watergems_import": ("Build a WaterGEMS export in the open drawing as a pressure network: pipes at 'cover' below ground, "
                         "PRVs, tees and bends at the nodes, gate valves and service taps from CAD layers, plan labels with the real "
                         "material and size, then an alignment, ground profile and profile view per main. Needs edit permission.",
                         {**WATERGEMS_INPUT, "target": "from get_drawing_info", "request_id": "one UUID for the import",
                          "network?": "default 'Water'", "profile_views_at?": "[x,y], default 50 below the network",
                          "profile_view_gap?": "default 30", "profile_view_style?": "default 'Profile View'",
                          "dry_run?": "validate the network transaction only"}),
}

IMPORT_ACTIONS = {"sewergems_import", "watergems_import"}

# --------------------------------------------------------------------------- edit operations (inside action 'edit')

EDIT_ACTION = (
    "Run a batch of operations on a specific open drawing. First call get_drawing_info and verify the drawing name. "
    "Pass its target and new_edit_request_id as target and request_id. Use the target from the reads used to prepare this edit. "
    "A changed session or active drawing stops the edit. The drawing operations share one transaction. "
    "dry_run aborts that transaction after validation. File outputs run after commit. Editing requires permission for the "
    "target drawing. If the response is interrupted, check get_edit_status with the original target and request_id; "
    "resend only with the same parameters and request ID for a retry. A request whose state is 'error' changed nothing "
    "(for example Civil 3D was busy) and may be resent with the same request ID."
)

LAYER_PROPS = {"color?": ACI, "on?": "", "frozen?": "", "locked?": "", "plot?": ""}
ON_LAYER = {"layer?": "default: current layer", "color?": ACI}
NETWORK_PROPS = {"reference_surface?": "", "reference_alignment?": "", "description?": ""}
PROFILE_STYLE = {"layer?": "", "style?": "", "label_set?": ""}

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
        "draw_line": ("Draw a line.", {"start": XYZ, "end": XYZ, **ON_LAYER}),
        "draw_polyline": ("Draw a polyline (2D if all z are equal, otherwise 3D).", {"points": "[[x,y] or [x,y,z], ...]", "closed?": "", **ON_LAYER}),
        "draw_point": ("Draw an AutoCAD point.", {"at": XYZ, **ON_LAYER}),
        "draw_circle": ("Draw a circle.", {"center": XYZ, "radius": "", **ON_LAYER}),
        "draw_arc": ("Draw an arc, counter-clockwise from start_deg to end_deg (0 = east).",
                     {"center": XYZ, "radius": "", "start_deg": "", "end_deg": "", **ON_LAYER}),
        "draw_text": ("Draw single-line text.", {"text": "", "at": XYZ, "height?": "default 2.5", "rotation_deg?": "", **ON_LAYER}),
        "draw_mtext": ("Draw multiline text (\\P starts a new line).",
                       {"text": "", "at": XYZ, "height?": "default 2.5", "width?": "wrap width", "rotation_deg?": "", **ON_LAYER}),
        "draw_annotations": ("Draw up to 5,000 line and text items inside one edit operation.",
                             {"items": "[{op: draw_line | draw_text | draw_mtext, ...}, ...]"}),
        "draw_hatch": ("Hatch a closed boundary given by its corner points.",
                       {"points": "[[x,y], ...]", "pattern?": "SOLID (default), ANSI31, AR-CONC, ...", "scale?": "",
                        "keep_boundary?": "default true (associative hatch)", **ON_LAYER}),
        "draw_dimension": ("Draw a dimension between two points.",
                           {"from": XY, "to": XY, "line_at": "[x,y] a point on the dimension line",
                            "type?": "aligned (default) | horizontal | vertical", "text?": "override text", **ON_LAYER}),
        "draw_leader": ("Draw a multileader: an arrow with a text note.",
                        {"arrow_at": XY, "text_at": XY, "text": "", "height?": "default: the multileader style", **ON_LAYER}),
        "draw_table": ("Draw a table from rows of cell values.",
                       {"at": "[x,y] top-left corner", "rows": "[[cell, ...], ...]", "title?": "merged title row", "text_height?": "default 2.5",
                        "row_height?": "", "column_width?": "", **ON_LAYER}),
    },
    "Blocks": {
        "draw_block": ("Insert a block that is already defined in the drawing (see list_blocks).",
                       {"name": "", "at": XYZ, "scale?": "default 1", "rotation_deg?": "", "attributes?": "{TAG: value, ...}", **ON_LAYER}),
        "block_define": ("Define a block from existing plain AutoCAD entities.",
                         {"name": "", "handles": "list", "base": "[x,y] insertion base point",
                          "replace_originals?": "erase them and insert the block in their place"}),
        "attribute_set": ("Change attribute values of a block reference.", {"handle": "", "attributes": "{TAG: value, ...}"}),
    },
    "Drawing file": {
        "layout_create": ("Create a layout (paper space tab).", {"name": ""}),
        "xref_attach": ("Attach a drawing as an external reference.",
                        {"path": "full path to a .dwg", "name?": "", "at?": "[x,y,z] default origin", "layer?": ""}),
        "drawing_save": ("Save the drawing; runs after the batch is committed. No path: save in place. path: Save As (the open drawing "
                         "continues under the new name). path + copy:true: write a copy and keep working in the original. format dxf: export.",
                         {"path?": "full path", "copy?": "default false", "format?": "dwg (default) | dxf", "overwrite?": "default false"}),
        "plot_pdf": ("Plot one layout (or model space) to a PDF with 'DWG To PDF.pc3'; runs after the batch is committed. Model space is "
                     "plotted to its extents, fitted to the paper; a paper-space layout is plotted 1:1 as laid out.",
                     {"path": "full path ending in .pdf", "layout?": "default: the current tab",
                      "paper?": "words of a paper size, e.g. 'A1' or 'ISO full bleed A3'", "plot_style?": "e.g. monochrome.ctb",
                      "overwrite?": "default false"}),
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
        "surface_volume_create": ("Create a TIN volume surface between two surfaces and return cut / fill / net volumes.",
                                  {"name": "", "base": "base surface", "comparison": "comparison surface"}),
        "feature_line_create": ("Create a feature line from 3D points.",
                                {"name": "", "points": "[[x,y,z], ...]", "closed?": "", "site?": "created if missing", "layer?": ""}),
    },
    "Alignments": {
        "alignment_create": ("Create an alignment from PI points (straight tangents; optional curve radius at every PI).",
                             {"name": "", "points": "[[x,y], ...]", "radius?": "curve radius at each PI", "site?": "default siteless",
                              "layer?": "", "style?": "", "label_set?": "", "description?": ""}),
        "profile_create": ("Create a layout profile from PVIs on an alignment.",
                           {"alignment": "", "name": "", "pvis": "[[station, elevation], ...]", **PROFILE_STYLE}),
        "profile_from_surface": ("Create a surface (existing ground) profile.", {"alignment": "", "surface": "", "name": "", **PROFILE_STYLE}),
        "profile_view_create": ("Create a profile view of an alignment (shows all its profiles).",
                                {"alignment": "", "at": "[x,y] lower-left insertion point", "name?": "", "style?": "", "band_set?": ""}),
        "profile_view_add_parts": ("Show network parts in a profile view.",
                                   {"profile_view": "", "pipe_network?": "gravity network name", "pressure_network?": "", "handles?": "individual parts",
                                    "pipe_names?": "filter gravity pipes by name", "structure_names?": "filter gravity structures by name"}),
        "sample_lines_create": ("Create sample lines on an alignment (the group is created if missing).",
                                {"alignment": "", "group": "", "stations?": "[station, ...]", "interval?": "every N along the alignment",
                                 "prefix?": "default 'SL-'"}),
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
    },
    "Styles and presentation": {
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
        "profile_view_bands": ("Replace the bands of a profile view. Each band uses an existing band style and its data source.",
                               {"profile_view": "", "bottom?": "[{type: profile_data|pipe_network|horizontal_geometry|vertical_geometry, style, "
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
        "pipe_set": ("Change one gravity pipe.",
                     {"handle": "", "start?": XYZ, "end?": XYZ,
                      "inner_diameter?": "nearest catalogue size, in the units of list_gravity_parts (mm in metric lists)", "description?": ""}),
        "structure_set": ("Change one gravity structure.",
                          {"handle": "", "location?": XYZ, "rim?": "", "sump?": "", "rotation_deg?": "", "description?": ""}),
        "parts_list_add_family": ("Add a part family from the Civil 3D catalogue to a gravity parts list, with the sizes given "
                                  "(e.g. a shallow flat-top manhole for a sanitary list). Nothing changes if it is already there.",
                                  {"parts_list": "", "domain": "pipe | structure", "family": "exact family description, e.g. 'Cylindrical Junction Structure NF SI'",
                                   "sizes?": "[inner diameter or width, ...] in the catalogue units (mm)"}),
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
        out.append("Import a Bentley SewerGEMS / WaterGEMS model into the drawing open in Civil 3D (it draws, so it needs "
                   "edit permission). Call with action=<name> and params={...}; run the matching *_check action of the read "
                   "tool first, and pass target and request_id from get_drawing_info.")
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
