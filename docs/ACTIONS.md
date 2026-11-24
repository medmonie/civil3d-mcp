# Actions and edit operations

Generated from `server/catalog.py` by `tools/gen_docs.py` - do not edit by hand.

Everything goes through one MCP tool: `civil3d(action, params)`. A parameter ending in `?` is optional.

## Read actions (67) - never change the drawing

### Drawing (AutoCAD)

| Name | Parameters | What it does |
|---|---|---|
| `ping` | - | Connection check: bridge version, Civil 3D version, active drawing, whether editing is allowed, the process id and any other running Civil 3D instances with the bridge (otherInstances; only one is the MCP target). |
| `get_drawing_info` | - | Active drawing: path, units, object counts, the drawing target, and a fresh edit request UUID. |
| `list_layers` | `limit?` default 500 | Layers with on/frozen/locked/plot state, colour and xref flag. |
| `count_entities` | `group_by?` 'type' (default) or 'layer'<br>`layer?` only this layer<br>`layout?` a paper-space layout instead of model space | Count entities of model space (or a layout) by DXF type or by layer. |
| `get_selection` | `limit?` default 200 | Objects the user has selected in Civil 3D: handle, type, layer, Civil name, extents. |
| `get_objects` | `handles` list of handle strings | Describe objects by handle: type, layer, Civil name, extents. |
| `list_entities` | `type?` DXF type, e.g. LINE, LWPOLYLINE, CIRCLE, ARC, TEXT, MTEXT, POINT, INSERT, HATCH<br>`layer?`<br>`window?` [xmin, ymin, xmax, ymax]<br>`limit?` default 100<br>`layout?` a paper-space layout instead of model space | AutoCAD entities in model space with their geometry: lines, polylines (vertices), circles, arcs, text, mtext, points, block references (name, attributes), hatches, dimensions. |
| `list_blocks` | `limit?` default 500 | Block definitions: name, attributes flag, number of references. |
| `list_layouts` | - | Layouts (paper space tabs): name, tab order, plot device, paper size. |

### Sheets and drawing set-up (AutoCAD)

| Name | Parameters | What it does |
|---|---|---|
| `list_viewports` | `layout?` only this layout<br>`include_paper?` also the layout's own paper-space viewport | Viewports on layouts: handle, position and size on paper, scale (1:N), model centre, twist, locked, frozen layers. |
| `list_views` | - | Named views: centre, size, twist. |
| `list_xrefs` | - | External references: name, path, status (resolved, unloaded, not found), nested, references. |
| `list_cad_styles` | `kind` text_style \| dim_style \| mleader_style \| table_style \| linetype \| layer_state \| annotation_scale \| page_setup | AutoCAD styles and set-up of one kind, with the current one. |
| `inspect_file` | `path` full path of a .dwg / .dwt / .dxf<br>`limit?` entities listed per layout, default 60 | Read a drawing or template on disk without opening it: layouts (paper, viewports with scale and model area), block references and text on each layout, xrefs, blocks with attribute tags, named views, page setups. |
| `get_block_properties` | `handle`<br>`visible_only?` default true | A block reference: block name, attributes, and dynamic block properties with their allowed values. |

### Terrain

| Name | Parameters | What it does |
|---|---|---|
| `list_surfaces` | - | Surfaces: name, type, style, layer, min/max/mean elevation. |
| `get_surface_elevation` | `surface_name`<br>`x` easting<br>`y` northing | Elevation of a TIN surface at (x, y). |
| `get_surface_volume` | `surface_name` | Cut, fill and net volume of a TIN volume surface. |
| `list_cogo_points` | `limit?` default 200 | COGO points: number, name, easting, northing, elevation, description. |
| `get_feature_line` | `handle?`<br>`feature_line?` name | One feature line's vertices (PI and elevation points) with plan distance, elevation, grade in/out (%) and curve radius, plus min/max elevation and grade. |
| `list_feature_lines` | `limit?` default 200 | Feature lines: length, min/max elevation, points. |
| `list_parcels` | `limit?` default 500 | Parcels of every site: name, number, centroid, extents. Read only. |
| `list_point_groups` | - | Point groups: description, point count, query, style overrides. |
| `sample_surface` | `surface`<br>`points?` [[x,y], ...] up to 20000<br>`handle?` polyline, line or arc to sample along<br>`line?` [[x1,y1],[x2,y2]] to sample along<br>`interval?` m along the line; default: surface breakpoints | Surface elevations in one call: at a list of points, or along a polyline / line (the surface's own breakpoints, or every 'interval'). |
| `list_user_properties` | `kind?` point (default) \| parcel | User-defined property classifications of COGO points or parcels, with each property's type and default. |
| `get_user_property_values` | `kind?` point (default) \| parcel<br>`classification?` default: all<br>`numbers?` point numbers<br>`names?` parcel names<br>`limit?` default 500 | Values of user-defined properties of COGO points or parcels. |
| `list_point_file_formats` | - | Point file formats available for points_import (name, extension, delimiter). |
| `list_description_keys` | - | Description key sets with their keys: code, format, point style, label style, layer. |
| `get_surface_analysis` | `surface` | A surface's min/max/mean elevation and its analysis ranges: elevations, slopes and slope arrows (percent) with colours, and watersheds. |

### Alignments

| Name | Parameters | What it does |
|---|---|---|
| `list_alignments` | - | Alignments (incl. the 'Pipe Run' alignments of pressure networks): type, site, length, stations. |
| `alignment_station_to_point` | `alignment_name`<br>`station`<br>`offset?` default 0 | Easting/northing of a station and offset (offset + = right). |
| `alignment_point_to_station` | `alignment_name`<br>`x`<br>`y` | Station and offset of a point. |
| `list_profiles` | `alignment_name?` only this alignment | Profiles: type, PVI count, station range, min/max elevation. |
| `get_profile_elevation` | `alignment_name`<br>`profile_name`<br>`station` | Elevation of a profile at a station. |
| `profile_sheet_window` | `alignment_name`<br>`profile_view`<br>`x_min` profile drawing X<br>`x_max` profile drawing X | Station range and enclosing plan window for part of a profile view. |
| `list_profile_views` | - | Profile views: alignment, style, station and elevation range, position in the drawing. |
| `list_sample_lines` | `alignment_name?` only this alignment | Sample line groups with the stations of their sample lines. |
| `list_section_views` | `limit?` default 500 | Section views: alignment, sample line group, sample line, station. |
| `get_superelevation` | `alignment` | Superelevation of an alignment: curves and critical stations with the slope of each lane and shoulder (percent). |
| `list_intersections` | - | Corridor intersections: name, location, corridor, number of roads. |
| `get_alignment_geometry` | `alignment` | Every line, curve and spiral of an alignment in order: stations, length, start/end, direction; curves with radius, delta, PI, tangent, external, chord; spirals with A, radius in/out, SPI. Also station equations, design speeds, and the parent(s) of an offset or connected alignment. |
| `get_profile_geometry` | `alignment`<br>`profile` | PVIs of a profile (station, elevation, grades in/out in %) and its entities: tangents with grade, vertical curves (crest/sag) with length, K, grade change, high/low point; asymmetric lengths, radius. |
| `get_material_volumes` | `alignment`<br>`group`<br>`material_list?` default: all<br>`step?` sampling step across the line, default 0.25 | Areas and volumes of a sample line group's material lists (see material_list_create): at each sample line cut/fill (earthwork) or material area, incremental and cumulative volume, and totals. Civil 3D's own quantity results when it has computed them (source civil3d); otherwise surface materials are computed from the TINs across each sample line, average end area (source computed). |
| `get_sections` | `alignment`<br>`group?` sample line group, default: all<br>`from_station?`<br>`to_station?`<br>`surfaces?` TIN surfaces, default: the group's surface sources<br>`limit?` sample lines, default 50<br>`max_points?` per section, default 500 (thinned beyond) | Ground sections at the sample lines of an alignment: each TIN surface sampled along each sample line (every surface breakpoint) as [offset, elevation], offsets from the alignment (negative = left). Needs no sampled sources or section views. Corridor sections: get_corridor_section. |

### Corridors

| Name | Parameters | What it does |
|---|---|---|
| `list_corridors` | - | Corridors: baselines with their regions and assemblies, corridor surfaces, out-of-date flag. |
| `get_corridor_section` | `corridor`<br>`station`<br>`baseline?` name or alignment, default: the first<br>`codes?` codes to keep | A corridor's section at a station (nearest frequency station): points with codes, offset and elevation to the baseline; links with codes; shapes with codes and area. |
| `list_assemblies` | - | Assemblies with their subassemblies (for corridor_create). |

### Gravity networks

| Name | Parameters | What it does |
|---|---|---|
| `list_pipe_networks` | - | Gravity pipe networks: parts list, reference surface/alignment, pipe and structure counts. |
| `list_pipes` | `network_name?` only this network<br>`limit?` default 200 | Gravity pipes: part, inner diameter, slope (m/m), length, start/end, connected structures. |
| `list_structures` | `network_name?` only this network<br>`limit?` default 200 | Gravity structures: part, location, rim/sump elevation, connected pipes. |
| `list_gravity_parts` | `parts_list?` name or handle | Without parts_list: the gravity parts lists. With it: its pipe and structure families and sizes (use these to fill 'family' + 'inner_diameter' / 'size_index' in pipe_network_add). |
| `network_hgl` | `network` | Hydraulic and energy grade lines and flow of a gravity network's pipes and structures, next to each pipe's crown at both ends (by flow direction) and each structure's rim, with the differences (hglAboveCrownUp/Down, hglBelowRim). Facts only; judge them against your design criteria. |
| `get_part_data` | `network` gravity or pressure network<br>`kind?` all (default) \| pipes \| structures \| fittings \| appurtenances<br>`parts?` part names<br>`fields?` field names to keep<br>`limit?` default 200 | Every catalogue field of the parts of a gravity or pressure network (wall thickness, material, inner / outer sizes, custom fields), with units. |
| `pipe_ground_profile` | `network` gravity or pressure network<br>`surface` TIN surface<br>`interval?` m, default 5<br>`pipes?` pipe names<br>`limit?` pipes, default 200 | Ground along every pipe of a network: at both ends and every 'interval', the surface elevation, the pipe invert, centre and outside top, and the cover to the outside top; min / max cover per pipe. |
| `network_interference` | `network1`<br>`network2?` default: network1 against itself<br>`within?` search distance in m, default 1.0 (a filter, not a limit)<br>`limit?` default 500 | Distances between the pipes of two networks (gravity or pressure), or within one network: every pair closer than 'within' outside to outside, with the gap (negative = overlap), the location, which pipe is above and the vertical separation. Only straight circular pipes are measured; see complete and excludedPipes. Compare the gaps with the clearance your criteria require. |

### Pressure networks

| Name | Parameters | What it does |
|---|---|---|
| `list_pressure_networks` | - | Pressure networks: parts list, reference surface/alignment, part and pipe-run counts, elevations. |
| `list_pressure_pipes` | `network_name?` only this network<br>`limit?` default 200 | Pressure pipes: part, diameters and wall (metres), lengths, slope, elevations, cover, start/end, handle. |
| `list_pressure_fittings` | `network_name?` only this network<br>`limit?` default 200 | Pressure fittings: part, type, connections, deflection angle, location, handle. |
| `list_pressure_appurtenances` | `network_name?` only this network<br>`limit?` default 200 | Valves, hydrants and other appurtenances: part, type, connections, location, handle. |
| `list_pipe_runs` | `network_name?` only this network | Pipe runs of pressure networks with their alignment, profile and part count. |
| `list_pressure_part_sizes` | `parts_list?` name or handle<br>`part_type?` Pipe (default), Fitting, Appurtenance, Elbow, Tee, Reducer, Valve, ...<br>`contains?` text filter, e.g. '150'<br>`catalog?` true: list what the list's catalogue offers (add them to the list in Civil 3D's parts list dialog)<br>`limit?` default 100 | Without parts_list: the pressure parts lists (name, handle, pipe size count). With it: the part descriptions for part_type, to choose 'size' and 'match' values. |

### Styles and meta

| Name | Parameters | What it does |
|---|---|---|
| `list_styles` | `kind` alignment \| alignment_label_set \| point_label \| profile \| profile_label_set \| profile_view \| profile_view_band_set \| surface \| corridor \| pipe \| structure \| point \| feature_line \| sample_line \| pressure_pipe_label \| pressure_fitting_label \| pressure_appurtenance_label \| gravity_parts_list \| pipe_label \| structure_label \| band_profile_data \| band_pipe_network \| band_horizontal_geometry \| band_vertical_geometry | Style names of one kind (for the 'style' / 'label_set' parameters of edit operations). Without a style name an edit uses 'Standard' when the drawing has it, otherwise the first style listed. |
| `get_style` | `kind` a list_styles kind<br>`name` | What one style contains: a label style's text components and their contents (the property-field syntax to copy into label_style_create), a band style's title and height, a profile view band set's bands. |
| `describe_api` | `type_name` full type name<br>`contains?` member name filter<br>`limit?` default 300 | Developer aid: public members of an Autodesk.* .NET type loaded in Civil 3D. |
| `get_edit_permission` | - | Whether editing is currently allowed in the active drawing, and how to allow it. |
| `get_edit_status` | `target` original drawing target<br>`request_id` original edit UUID | Find an edit by request UUID. Check this after a timeout before retrying. Status remains available while Civil 3D is busy. |
| `list_actions` | - | What the installed plugin supports (names only). Use 'help' for descriptions. |

### QA and help (computed in the MCP server)

| Name | Parameters | What it does |
|---|---|---|
| `help` | `topic?` an action or edit operation name | This catalogue with every parameter. Pass topic to narrow it. |
| `check_layer_names` | `pattern` regular expression<br>`skip_xref_layers?` default true | QA: layers whose names do not fully match a regular expression (e.g. the BEP layer convention). |
| `check_default_names` | - | QA: surfaces, alignments and networks still carrying Civil 3D's auto-generated names. |
| `sewergems_check` | `path?` full path of the SewerGEMS workbook (.xlsx with sheets Manhole, Outfall, Conduit)<br>`manholes_csv?` or: manhole FlexTable CSV (must include X and Y)<br>`outfalls_csv?`<br>`conduits_csv?` conduit FlexTable CSV<br>`rename_csv?` two columns: SewerGEMS label, drawing label (e.g. MH-19,MH-1-19)<br>`line_prefix?` default 'LINE' (lines are LINE1, LINE1-3, ...) | Read a Bentley SewerGEMS export and check it without drawing: manholes, conduits, drops, problems (missing nodes, uphill pipes, crown above ground, ...) and the profile lines it would make. |
| `sewergems_import` | `path?` full path of the SewerGEMS workbook (.xlsx with sheets Manhole, Outfall, Conduit)<br>`manholes_csv?` or: manhole FlexTable CSV (must include X and Y)<br>`outfalls_csv?`<br>`conduits_csv?` conduit FlexTable CSV<br>`rename_csv?` two columns: SewerGEMS label, drawing label (e.g. MH-19,MH-1-19)<br>`line_prefix?` default 'LINE' (lines are LINE1, LINE1-3, ...)<br>`target` from get_drawing_info<br>`request_id` one UUID for the import<br>`network?` default 'Sewer'<br>`parts_list?` default 'Sanitary Sewer'<br>`pipe_family?` words of the pipe family, default 'PVC'<br>`structure_family?` default 'Cylindrical Junction Structure NF SI' (flat top, fits 1.2 m deep manholes); added to the parts list if missing<br>`structure_diameter?` mm, default 1200<br>`profile_views_at?` [x,y] top-left of the first view, default 50 below the network<br>`profile_view_gap?` vertical gap between views, default 30<br>`presentation?` false to skip styling; or overrides of: pipe_style, structure_style, structure_label_style, pipe_label_style, pipe_label_text, label_height_mm, alignment_label_set, profile_view_style, band_table<br>`profile_view_style?` overrides the presentation setting<br>`band_set?` band set for the profile views: a name, or auto (Standard when it has bands, else an Elevations band set)<br>`dry_run?` validate the core network, profiles, and styles transaction<br>`finish_attempt?` default 1. Increment only after the preceding finishing transaction is confirmed rolled back. | Build a SewerGEMS export in the open drawing: a gravity network (manholes named as in SewerGEMS or the rename table, pipes at the SewerGEMS inverts), then per line an alignment from the downstream end, a ground profile through the manhole rims and a profile view with that line's parts. Needs edit permission; stops without drawing if sewergems_check reports problems. |
| `pp_sheets_plan` | `template` full path of an office sheet drawing to copy (its layout has plan, profile and key-plan viewports, title block, list of lines, sheet label)<br>`template_layout?` layout in the template (default: its first)<br>`mode?` auto (default) \| views (Civil 3D profile views in the drawing) \| rows (plain-CAD profiles stacked in rows)<br>`sheets?` rows mode: [{lines: [names], plan_window?: [xmin, ymin, xmax, ymax]}, ...] one entry per sheet<br>`row_spacing?` rows mode: m between the profile rows of two sheets, default 500<br>`first_row_center?` rows mode: [x, y] profile centre of the first sheet (default: the template's)<br>`max_length?` views mode: maximum span per sheet, default 500 drawing units, capped at the profile viewport width<br>`profile_views?` views mode: only these profile views<br>`plan?` fit (default: zoom to the sheet's alignments at a standard scale) \| template (keep the template's plan area)<br>`plan_scales?` allowed plan scales N of 1:N, default 100 ... 10000<br>`plan_margin?` default 0.1 (10 %)<br>`first_sheet?` number of the first sheet, default 1<br>`total_sheets?` for the label ( n / total ), default: the last sheet<br>`layout_prefix?` default '' (layouts 01, 02, ...)<br>`line_layer?` layer of the list of lines, default 'EN-LINE NAME'<br>`label_contains?` text of the sheet label, default 'PLAN/PROFILE'<br>`viewports?` {plan, profile, key}: template viewport handles (from inspect_file) when the automatic choice is wrong | Plan and profile sheets from an office sheet template, without drawing: which profiles, lines, plan scale and label each sheet gets, and what was found in the template (viewports, list of lines, label, key-plan box, scale bar). |
| `pp_sheets_create` | `template` full path of an office sheet drawing to copy (its layout has plan, profile and key-plan viewports, title block, list of lines, sheet label)<br>`template_layout?` layout in the template (default: its first)<br>`mode?` auto (default) \| views (Civil 3D profile views in the drawing) \| rows (plain-CAD profiles stacked in rows)<br>`sheets?` rows mode: [{lines: [names], plan_window?: [xmin, ymin, xmax, ymax]}, ...] one entry per sheet<br>`row_spacing?` rows mode: m between the profile rows of two sheets, default 500<br>`first_row_center?` rows mode: [x, y] profile centre of the first sheet (default: the template's)<br>`max_length?` views mode: maximum span per sheet, default 500 drawing units, capped at the profile viewport width<br>`profile_views?` views mode: only these profile views<br>`plan?` fit (default: zoom to the sheet's alignments at a standard scale) \| template (keep the template's plan area)<br>`plan_scales?` allowed plan scales N of 1:N, default 100 ... 10000<br>`plan_margin?` default 0.1 (10 %)<br>`first_sheet?` number of the first sheet, default 1<br>`total_sheets?` for the label ( n / total ), default: the last sheet<br>`layout_prefix?` default '' (layouts 01, 02, ...)<br>`line_layer?` layer of the list of lines, default 'EN-LINE NAME'<br>`label_contains?` text of the sheet label, default 'PLAN/PROFILE'<br>`viewports?` {plan, profile, key}: template viewport handles (from inspect_file) when the automatic choice is wrong<br>`target` from get_drawing_info<br>`request_id` new edit request UUID<br>`if_exists?` error (default) \| replace existing layouts of the same names | Make plan and profile sheets as layouts of the open drawing from an office sheet template: per sheet the profile viewport on its profiles, the plan viewport zoomed to its lines, the key-plan box and scale bar adjusted, the list of lines and the sheet label rewritten. Run pp_sheets_plan first. |
| `watergems_check` | `path` full path of the WaterGEMS full-output workbook (.xlsx with sheets Pipe, Junction, PRV, ...)<br>`target?` from get_drawing_info (default: the active drawing)<br>`parts_list?` pressure parts list, default 'Water'; its pipes, tees, bends and gate valves are used<br>`cover?` m to the top of pipe below the WaterGEMS node elevation (taken as ground), default 1.0<br>`sizes?` override the drawn size per WaterGEMS bore, e.g. {"99.4": 110}; default: PVC/PE bore -> next outside diameter, others -> DN<br>`valve_layer?` CAD layer of valve chambers (closed polylines or blocks) -> gate valves on the model pipe beneath<br>`service_layers?` CAD layers of service connections (polylines; size read from the layer name, e.g. WATER-63) -> teed-off pipes<br>`hydrant_layer?` CAD layer of hydrant blocks (reported; kept as blocks)<br>`cad_pipe_layers?` CAD layers of the drawn pipes -> difference report<br>`main_min_mm?` pipes of at least this drawn size get profile views, default 160<br>`line_prefix?` default 'WL'<br>`min_line_length?` m, default 50<br>`valve_tolerance?` m, default 3<br>`tap_tolerance?` m, default 1.5<br>`part_end_clearance?` m kept between a valve or tap and a pipe end or another valve/tap, default 2<br>`stand_in_od_factor?` outside diameter / DN assumed for a stand-in part when setting cover, default 1.15<br>`elbow_min_deflection?` degrees below which two pipes meet without a bend, default 5<br>`elbow_tolerance?` degrees a deflection may differ from a catalogue bend, default 6 | Read a Bentley WaterGEMS export (and the drawing's CAD layers, when given) without drawing: pipes by material and size, the catalogue parts that will stand in, CAD valves/services/hydrants matched to the model, the profile lines of the mains, and where model and drawing differ. |
| `watergems_import` | `path` full path of the WaterGEMS full-output workbook (.xlsx with sheets Pipe, Junction, PRV, ...)<br>`target?` from get_drawing_info (default: the active drawing)<br>`parts_list?` pressure parts list, default 'Water'; its pipes, tees, bends and gate valves are used<br>`cover?` m to the top of pipe below the WaterGEMS node elevation (taken as ground), default 1.0<br>`sizes?` override the drawn size per WaterGEMS bore, e.g. {"99.4": 110}; default: PVC/PE bore -> next outside diameter, others -> DN<br>`valve_layer?` CAD layer of valve chambers (closed polylines or blocks) -> gate valves on the model pipe beneath<br>`service_layers?` CAD layers of service connections (polylines; size read from the layer name, e.g. WATER-63) -> teed-off pipes<br>`hydrant_layer?` CAD layer of hydrant blocks (reported; kept as blocks)<br>`cad_pipe_layers?` CAD layers of the drawn pipes -> difference report<br>`main_min_mm?` pipes of at least this drawn size get profile views, default 160<br>`line_prefix?` default 'WL'<br>`min_line_length?` m, default 50<br>`valve_tolerance?` m, default 3<br>`tap_tolerance?` m, default 1.5<br>`part_end_clearance?` m kept between a valve or tap and a pipe end or another valve/tap, default 2<br>`stand_in_od_factor?` outside diameter / DN assumed for a stand-in part when setting cover, default 1.15<br>`elbow_min_deflection?` degrees below which two pipes meet without a bend, default 5<br>`elbow_tolerance?` degrees a deflection may differ from a catalogue bend, default 6<br>`target` from get_drawing_info<br>`request_id` one UUID for the import<br>`network?` default 'Water'<br>`profile_views_at?` [x,y], default 50 below the network<br>`profile_view_gap?` default 30<br>`profile_view_style?` default 'Profile View'<br>`band_set?` default auto: Standard when it has bands, else an Elevations band set (e.g. EG-FG Elevations and Stations), or a name<br>`dry_run?` validate the network transaction only | Build a WaterGEMS export in the open drawing as a pressure network: pipes at 'cover' below ground, PRVs, tees and bends at the nodes, gate valves and service taps from CAD layers, plan labels with the real material and size, then an alignment, ground profile and profile view per main. Needs edit permission. |

## The `edit` action

Run a batch of operations on a specific open drawing. First call get_drawing_info and verify the drawing name. Pass its target and new_edit_request_id as target and request_id. Use the target from the reads used to prepare this edit. A changed session or active drawing stops the edit. The drawing operations share one transaction. dry_run aborts that transaction after validation. File outputs run after commit. Editing requires permission for the target drawing. If the response is interrupted, check get_edit_status with the original target and request_id; resend only with the same parameters and request ID for a retry. A request whose state is 'error' changed nothing (for example Civil 3D was busy) and may be resent with the same request ID.

## Edit operations (110) - items of `operations`

### Layers and existing objects

| Name | Parameters | What it does |
|---|---|---|
| `layer_create` | `name`<br>`color?` AutoCAD colour index 1-255<br>`on?`<br>`frozen?`<br>`locked?`<br>`plot?`<br>`if_exists?` 'skip' to accept an existing layer | Create a layer. |
| `layer_set` | `name`<br>`new_name?`<br>`color?` AutoCAD colour index 1-255<br>`on?`<br>`frozen?`<br>`locked?`<br>`plot?` | Change a layer. |
| `entity_set` | `handles` list<br>`layer?`<br>`color?` AutoCAD colour index 1-255<br>`linetype?` e.g. DASHED, CENTER (loaded from acadiso.lin if needed) | Move objects to a layer and/or set their colour or linetype. |
| `entity_move` | `handles` list<br>`dx?`<br>`dy?`<br>`dz?` | Displace objects. |
| `entity_copy` | `handles` list<br>`dx?`<br>`dy?`<br>`dz?` | Copy plain AutoCAD entities with a displacement; returns the new handles. |
| `entity_rotate` | `handles` list<br>`base` [x,y]<br>`angle_deg` | Rotate plain AutoCAD entities about a base point (counter-clockwise). |
| `entity_scale` | `handles` list<br>`base` [x,y]<br>`factor` | Scale plain AutoCAD entities about a base point. |
| `entity_erase` | `handles` list | Erase objects by handle (any object type, including Civil 3D objects and network parts). |
| `text_set` | `handle`<br>`text?`<br>`height?` | Change the content and/or height of a TEXT or MTEXT. |
| `object_set` | `kind` alignment \| profile \| surface \| corridor \| assembly \| pipe_network \| pressure_network<br>`name`<br>`alignment?` required when kind is profile<br>`new_name?`<br>`description?`<br>`style?`<br>`layer?` | Rename / describe / restyle / re-layer a Civil 3D object. |

### Drawing (AutoCAD)

| Name | Parameters | What it does |
|---|---|---|
| `draw_line` | `start` [x,y,z]<br>`end` [x,y,z]<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a line. |
| `draw_polyline` | `points` [[x,y] or [x,y,z], ...]<br>`closed?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a polyline (2D if all z are equal, otherwise 3D). |
| `draw_point` | `at` [x,y,z]<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw an AutoCAD point. |
| `draw_circle` | `center` [x,y,z]<br>`radius`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a circle. |
| `draw_arc` | `center` [x,y,z]<br>`radius`<br>`start_deg`<br>`end_deg`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw an arc, counter-clockwise from start_deg to end_deg (0 = east). |
| `draw_text` | `text`<br>`at` [x,y,z]<br>`height?` default 2.5<br>`rotation_deg?`<br>`style?` text style<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw single-line text. |
| `draw_mtext` | `text`<br>`at` [x,y,z]<br>`height?` default 2.5<br>`width?` wrap width<br>`rotation_deg?`<br>`style?` text style<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw multiline text (\P starts a new line). |
| `draw_annotations` | `items` [{op: draw_line \| draw_text \| draw_mtext, ...}, ...] | Draw up to 5,000 line and text items inside one edit operation. |
| `draw_hatch` | `points` [[x,y], ...]<br>`pattern?` SOLID (default), ANSI31, AR-CONC, ...<br>`scale?`<br>`keep_boundary?` default true (associative hatch)<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Hatch a closed boundary given by its corner points. |
| `draw_dimension` | `from` [x,y]<br>`to` [x,y]<br>`line_at` [x,y] a point on the dimension line<br>`type?` aligned (default) \| horizontal \| vertical<br>`text?` override text<br>`style?` dimension style<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a dimension between two points. |
| `draw_leader` | `arrow_at` [x,y]<br>`text_at` [x,y]<br>`text`<br>`height?` default: the multileader style<br>`style?` multileader style<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a multileader: an arrow with a text note. |
| `draw_table` | `at` [x,y] top-left corner<br>`rows` [[cell, ...], ...]<br>`title?` merged title row<br>`text_height?` default 2.5<br>`row_height?`<br>`column_width?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a table from rows of cell values. |

### Blocks

| Name | Parameters | What it does |
|---|---|---|
| `draw_block` | `name`<br>`at` [x,y,z]<br>`scale?` default 1<br>`rotation_deg?`<br>`attributes?` {TAG: value, ...}<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Insert a block that is already defined in the drawing (see list_blocks). |
| `block_define` | `name`<br>`handles` list<br>`base` [x,y] insertion base point<br>`replace_originals?` erase them and insert the block in their place | Define a block from existing plain AutoCAD entities. |
| `attribute_set` | `handle`<br>`attributes` {TAG: value, ...} | Change attribute values of a block reference. |
| `block_import` | `path` full path<br>`blocks?` block names to copy; omit to bring the whole file in as one block<br>`as_block?` name for the whole-file block (default: the file name)<br>`replace?` redefine blocks the drawing already has (default false) | Bring block definitions in from another drawing (.dwg/.dwt/.dxf), or the whole file as one block. |
| `title_block_insert` | `layout`<br>`path?` full path of the title block drawing<br>`block?` block name (in that file, or already defined)<br>`at?` [x,y] on paper, default [0,0]<br>`scale?`<br>`rotation_deg?`<br>`attributes?` {TAG: value, ...}<br>`replace?` redefine if already in the drawing<br>`layer?` | Put a title block on a layout and fill its attributes; with path it is first brought in from that file. |
| `dynamic_block_set` | `handle`<br>`properties` {name: value, ...} | Set dynamic block properties (visibility state, distances, flip...). get_block_properties lists them. |

### Drawing file and sheets

| Name | Parameters | What it does |
|---|---|---|
| `layout_create` | `name` | Create a layout (paper space tab). |
| `layout_delete` | `name` | Delete a layout. |
| `layout_from_template` | `path` full path of the template<br>`layout?` layout in the template (default: its first)<br>`name?` new layout name (default: the template layout's)<br>`if_exists?` error (default) \| replace | New layout copied from a layout of a template (.dwt/.dwg): its page setup, title block, viewports and notes, with the blocks, layers and styles they use (ones the drawing has are kept). |
| `page_setup_set` | `layout`<br>`page_setup?` named page setup to apply first<br>`device?` e.g. 'DWG To PDF.pc3'<br>`paper?` words of a paper size, e.g. 'ISO full bleed A1'<br>`plot_style?` e.g. monochrome.ctb<br>`rotation?` 0 \| 90 \| 180 \| 270<br>`scale?` '1:1' \| 'fit' \| '1:N'<br>`center?` | Page setup of a layout: a named page setup, plot device, paper, plot style, rotation, scale. |
| `viewport_create` | `layout`<br>`at` [x,y] centre on paper (mm)<br>`width` on paper<br>`height` on paper<br>`window?` [xmin, ymin, xmax, ymax] in model space<br>`view_center?` [x,y] in model space<br>`view?` named view<br>`scale?` "1:500" (paper mm to drawing units)<br>`custom_scale?` paper units per drawing unit, instead of scale<br>`twist_deg?` view twist (counter-clockwise), e.g. to run an alignment along the sheet<br>`lock?`<br>`freeze_layers?` layers frozen in this viewport only<br>`layer?` | Create a viewport on a layout showing a model area at a scale. Give the area as window (fitted unless scale is given), as view_center + scale, or as a named view. |
| `viewport_set` | `handle`<br>`at?`<br>`width?`<br>`height?`<br>`window?`<br>`view_center?`<br>`view?`<br>`scale?`<br>`custom_scale?`<br>`twist_deg?`<br>`lock?`<br>`on?` switch the viewport on/off<br>`freeze_layers?`<br>`thaw_layers?` | Change a viewport: position, size, model area, scale, twist, lock, layers frozen in it. |
| `view_create` | `name`<br>`window` [xmin, ymin, xmax, ymax]<br>`twist_deg?`<br>`if_exists?` error (default) \| replace | Save a model-space area as a named view (for viewports and plotting). |
| `drawing_save` | `path?` full path<br>`copy?` default false<br>`format?` dwg (default) \| dxf<br>`overwrite?` default false | Save the drawing; runs after the batch is committed. No path: save in place. path: Save As (the open drawing continues under the new name). path + copy:true: write a copy and keep working in the original. format dxf: export. |
| `plot_pdf` | `path?` full path ending in .pdf<br>`layout?` default: the current tab<br>`layouts?` list of layout names, or "all"<br>`folder?` folder for one PDF per layout ('<drawing> - <layout>.pdf')<br>`paper?` words of a paper size, e.g. 'A1' or 'ISO full bleed A3'<br>`plot_style?` e.g. monochrome.ctb<br>`overwrite?` default false | Plot to PDF with 'DWG To PDF.pc3'; runs after the batch is committed. One layout (path + layout), several layouts as one multi-sheet PDF (path + layouts), or one PDF per layout (folder + layouts). Model space is plotted to its extents, fitted to the paper; a layout keeps its own page setup (paper, scale). One multi-sheet PDF needs the same paper and orientation on every layout (an AutoCAD plot-engine limit); use folder for mixed sizes. |

### External references and underlays

| Name | Parameters | What it does |
|---|---|---|
| `xref_attach` | `path` full path to a .dwg<br>`name?`<br>`at?` [x,y,z] default origin<br>`layer?`<br>`layout?` | Attach a drawing as an external reference. |
| `xref_reload` | `names?` xref names | Reload xrefs (all when names is omitted); runs after the commit. |
| `xref_detach` | `names` xref names | Detach xrefs; runs after the commit. |
| `xref_bind` | `names` xref names<br>`insert?` true: plain names (Insert); default Bind (xref$0$ prefixes) | Bind xrefs into the drawing; runs after the commit. |
| `underlay_attach` | `path` full path<br>`at?` [x,y] lower-left, default origin<br>`page?` PDF page, default 1<br>`scale?` PDF scale factor, default 1<br>`width?` image width in drawing units<br>`rotation_deg?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Attach a PDF page or an image (png, jpg, tif, bmp, gif) as an underlay. |

### AutoCAD styles and set-up

| Name | Parameters | What it does |
|---|---|---|
| `text_style_create` | `name`<br>`font?` arial.ttf (default), romans.shx, ...<br>`height?` 0 = set per text (default)<br>`width_factor?`<br>`oblique_deg?`<br>`set_current?`<br>`if_exists?` error (default) \| skip \| replace | Create (or update) a text style. |
| `dim_style_create` | `name`<br>`based_on?`<br>`text_style?`<br>`text_height?`<br>`arrow_size?`<br>`extension_offset?`<br>`extension_beyond?`<br>`text_gap?`<br>`decimals?`<br>`overall_scale?`<br>`linear_scale?`<br>`text_above?` true: text above the dimension line, aligned with it<br>`set_current?`<br>`if_exists?` error (default) \| skip \| replace | Create (or update) a dimension style, starting from based_on or the current style. |
| `mleader_style_create` | `name`<br>`based_on?`<br>`text_style?`<br>`text_height?`<br>`arrow_size?`<br>`landing_gap?`<br>`dogleg_length?` 0 = no dogleg<br>`set_current?`<br>`if_exists?` error (default) \| skip \| replace | Create (or update) a multileader style. |
| `linetype_load` | `names` e.g. ["DASHED", "CENTER"]<br>`file?` default acadiso.lin (metric) / acad.lin | Load linetypes from a .lin file. |
| `annotation_scale_add` | `scale` "1:500"<br>`name?` default '1:500'<br>`set_current?` | Add an annotation scale (paper mm to drawing units) and optionally make it current. |
| `layer_state_save` | `name`<br>`description?`<br>`if_exists?` error (default) \| skip \| replace | Save the current layer settings (on, frozen, locked, plot, colour, linetype...) as a layer state. |
| `layer_state_restore` | `name` | Restore a saved layer state. |
| `purge` | `kinds?` blocks \| layers \| linetypes \| text_styles \| dim_styles \| mleader_styles \| table_styles \| regapps (default all)<br>`list_only?` report what is unused without removing it | Remove unused blocks, layers, linetypes, styles and regapps (repeated until nothing more is unused). |

### Terrain

| Name | Parameters | What it does |
|---|---|---|
| `cogo_points_add` | `points` [{x, y, z?, description?, name?}, ...] | Add COGO points. |
| `cogo_point_set` | `number`<br>`x?`<br>`y?`<br>`z?`<br>`description?`<br>`name?` | Change one COGO point. |
| `cogo_points_remove` | `numbers` list of point numbers | Remove COGO points. |
| `surface_create` | `name`<br>`points?` [[x,y,z], ...]<br>`style?`<br>`layer?`<br>`description?` | Create a TIN surface, optionally from points. |
| `surface_add_points` | `name`<br>`points` [[x,y,z], ...] | Add points to a TIN surface. |
| `surface_add_breaklines` | `name`<br>`lines?` [[[x,y,z], ...], ...] one list of points per breakline<br>`handles?` existing feature lines, 3D polylines or lines<br>`type?` standard (default) \| non_destructive \| proximity | Add breaklines to a TIN surface, from points or from existing feature lines / 3D polylines (handles) - the way to grade a pad or channel. |
| `surface_add_boundary` | `name`<br>`points` [[x,y], ...]<br>`type?` outer (default) \| hide \| show \| data_clip<br>`non_destructive?` default true | Add a boundary to a surface. |
| `surface_rebuild` | `name` | Rebuild a surface. |
| `surface_volume_create` | `name`<br>`base` base surface<br>`comparison` comparison surface | Create a TIN volume surface between two surfaces and return cut / fill / net volumes. |
| `feature_line_create` | `name`<br>`points` [[x,y,z], ...]<br>`closed?`<br>`site?` created if missing<br>`layer?` | Create a feature line from 3D points. |
| `feature_line_edit` | `handle?`<br>`feature_line?` name<br>`from_surface?`<br>`include_intermediate?` default true<br>`elevations?` [{index, elevation}]<br>`raise?` add to every elevation<br>`insert_pis?` [[x,y,z]]<br>`delete_pis?` [[x,y]] (nearest PI within 0.01)<br>`insert_elevation_points?` [[x,y,z]]<br>`delete_elevation_points?` [[x,y]]<br>`radii?` [{index, radius}] | Edit a feature line: elevations from a surface, at vertices (index from get_feature_line) or raised by an amount; PIs and elevation points inserted or deleted; curve radii at vertices. |
| `points_export` | `path`<br>`format` see list_point_file_formats<br>`point_group?`<br>`overwrite?` default false | Write COGO points (all, or one point group) to a file in one of the drawing's point file formats; runs after the batch is committed. |
| `udp_create` | `for?` point (default) \| parcel<br>`classification`<br>`properties` [{name, type?: string\|double\|integer\|boolean\|enum, values?: enum values, default?, description?, min?, max?, data_type?: Double\|Distance\|Elevation\|Area\|Percent\|...}] | Define user-defined properties for points or parcels in a classification (created if missing). |
| `points_import` | `path` full path of the .txt/.csv/.pnt file<br>`format` see list_point_file_formats, e.g. 'PENZD (comma delimited)'<br>`point_group?` | Import a point file (survey) with one of the drawing's point file formats, optionally into a point group (created if missing). Description keys apply as in Civil 3D. |
| `point_group_create` | `name`<br>`description?`<br>`include_all?`<br>`include_numbers?`<br>`include_raw_descriptions?`<br>`include_full_descriptions?`<br>`include_names?`<br>`include_elevations?` e.g. '>10'<br>`exclude_numbers?`<br>`exclude_raw_descriptions?`<br>`exclude_names?`<br>`exclude_elevations?`<br>`point_style?` override<br>`label_style?` override (list_styles kind point_label)<br>`if_exists?` error (default) \| replace | Create (or with if_exists: replace, re-query) a point group. Filters take Civil 3D's lists and wildcards, e.g. include_raw_descriptions: 'MH*,IL*', include_numbers: '1-100,205'. |
| `description_key_create` | `code`<br>`key_set?` default 'Default'<br>`format?` full description, default $*<br>`point_style?`<br>`label_style?`<br>`layer?`<br>`apply_to_points?` re-apply keys to all points<br>`if_exists?` error (default) \| replace | Add a description key (code, e.g. 'MH*') to a key set (created if missing) with the point style, label style and layer it applies. |
| `surface_analysis_set` | `surface`<br>`type?` elevations (default) \| slopes \| slope_arrows<br>`ranges?` [{min, max, color?: ACI}, ...] elevations in drawing units, slopes in percent<br>`count?` default 8 (without ranges)<br>`max_slope?` percent, default 30 (slopes without ranges)<br>`show_in_style?` surface style to show the analysis in (changes that style for every surface using it) | Elevation, slope or slope-arrow banding of a surface: ranges with colours (or 'count' equal ranges), and optionally turn that display on in a surface style and give it to the surface. |
| `surface_waterdrop` | `surface`<br>`points` [[x,y], ...]<br>`kind?` polyline2d (default) \| polyline3d<br>`layer?` | Waterdrop (flow path) polylines downhill from points on a surface. |

### Alignments

| Name | Parameters | What it does |
|---|---|---|
| `alignment_create` | `name`<br>`points` [[x,y], ...]<br>`radius?` curve radius at every PI<br>`spiral_in?` spiral length before every curve<br>`spiral_out?` default = spiral_in<br>`curves?` per interior PI: radius, or {radius, spiral_in?, spiral_out?}, or null for no curve (overrides radius)<br>`spiral_type?` Clothoid (default) \| CubicParabola \| Bloss \| ...<br>`site?` default siteless<br>`layer?`<br>`style?`<br>`label_set?`<br>`description?` | Create an alignment from PI points: fixed tangents, and at each interior PI a free curve, optionally with entry/exit spirals (spiral-curve-spiral). |
| `alignment_set` | `alignment`<br>`station_equations?` [{back: raw station back, ahead, type?: increasing\|decreasing}]<br>`clear_station_equations?`<br>`design_speeds?` [{station, speed, comment?}]<br>`clear_design_speeds?`<br>`widenings?` offset alignments only: [{start, end, offset}] | Station equations, design speeds and (offset alignments) widenings of an alignment. |
| `alignment_connected_create` | `name`<br>`incoming` alignment<br>`incoming_station`<br>`outgoing` alignment<br>`outgoing_station`<br>`radius`<br>`curve_group?` Arc (default) \| SCS \| SSCS \| SCSS \| SSCSS \| CCC<br>`spiral_in?`<br>`spiral_out?`<br>`offset_in?`<br>`offset_out?`<br>`overlap_in?` default 1<br>`overlap_out?` default 1<br>`greater_than_180?`<br>`site?`<br>`layer?`<br>`style?`<br>`label_set?` | A connected alignment (curb return) joining two alignments at given stations with an arc or a spiral-curve group. Building block of an intersection (the intersection wizard is not in the API). |
| `alignment_from_polyline` | `name`<br>`handle` polyline<br>`add_curves?` default false<br>`erase_polyline?` default false<br>`site?`<br>`layer?`<br>`style?`<br>`label_set?`<br>`description?` | Create an alignment from an existing polyline (handle), optionally with curves between its tangents. |
| `alignment_offset_create` | `name`<br>`parent`<br>`offset`<br>`start_station?`<br>`end_station?`<br>`style?` | Create an offset alignment (negative offset = left), optionally for a station range. |
| `superelevation_slope_set` | `alignment`<br>`station`<br>`slope_percent` e.g. -2.5<br>`segment?` RightInLaneCrossSlope (default) \| RightOutLaneCrossSlope \| LeftInLaneCrossSlope \| LeftOutLaneCrossSlope \| ...ShoulderCrossSlope<br>`tolerance?` default 0.01 | Change the cross slope of one lane or shoulder at an existing superelevation critical station (see get_superelevation). |
| `profile_create` | `alignment`<br>`name`<br>`pvis` [[station, elevation], ...] or [{station, elevation, curve?: parabola\|circular\|asymmetric\|none, length?, k?, radius?, lengths?: [before, after]}]; curve inferred from length/k/radius/lengths<br>`layer?`<br>`style?`<br>`label_set?` | Create a layout profile from PVIs on an alignment, with vertical curves where asked. |
| `profile_pvis_set` | `alignment`<br>`profile`<br>`pvis?` [[station, elevation], ...] or [{station, elevation, curve?: parabola\|circular\|asymmetric\|none, length?, k?, radius?, lengths?: [before, after]}]; curve inferred from length/k/radius/lengths; elevation optional when changing<br>`remove?` [station, ...] | Add, change (elevation and/or curve) or remove PVIs of a layout profile. A PVI within 0.001 of a given station is changed; otherwise one is added. curve: 'none' removes a curve. |
| `profile_from_surface` | `alignment`<br>`surface`<br>`name`<br>`layer?`<br>`style?`<br>`label_set?` | Create a surface (existing ground) profile. |
| `profile_view_create` | `alignment`<br>`at` [x,y] lower-left insertion point<br>`name?`<br>`style?`<br>`band_set?` name, or auto (Standard when it has bands, else the band set with Elevation in its name; default: Standard or the first set) | Create a profile view of an alignment (shows all its profiles). |
| `profile_views_create` | `alignment`<br>`at` [x,y] insertion point<br>`length` stations per view<br>`per_row?` default 5<br>`gap_x?` default 50<br>`gap_y?` default 50<br>`order?` rows (default) \| columns<br>`start_corner?` UpperLeft (default) \| LowerLeft \| UpperRight \| LowerRight<br>`name?`<br>`style?`<br>`band_set?` | Several profile views of one alignment, each covering 'length' of stations, laid out in rows or columns. |
| `profile_view_add_parts` | `profile_view`<br>`pipe_network?` gravity network name<br>`pressure_network?`<br>`handles?` individual parts<br>`pipe_names?` filter gravity pipes by name<br>`structure_names?` filter gravity structures by name | Show network parts in a profile view. |
| `sample_lines_create` | `alignment`<br>`group`<br>`stations?` [station, ...]<br>`interval?` every N along the alignment<br>`prefix?` default 'SL-'<br>`left_width?` m, default 20<br>`right_width?` m, default 20 | Create sample lines on an alignment (the group is created if missing). |
| `sample_sources_set` | `alignment`<br>`group`<br>`sources?` source names, default: all<br>`sampled?` default true | Choose which sources (surfaces, corridors, corridor surfaces, networks) a sample line group samples, so sections exist for get_sections and section views. |
| `material_list_create` | `alignment`<br>`group`<br>`name`<br>`materials` [{name, type?: earthwork (default)\|cut\|fill\|cut_fill\|structure, items: [{surface, condition?: above\|below\|base\|compare\|include\|exclude} \| {corridor, shape: shape code}], cut_factor?, fill_factor?, refill_factor?}]<br>`method?` average_end_area (default) \| prismoidal \| composite<br>`if_exists?` error (default) \| replace | A material list on a sample line group (earthwork and structure quantities): materials from surfaces (with a condition) and/or corridor shapes; the surfaces are switched to sampled. Read the result with get_material_volumes. |
| `section_views_create` | `alignment`<br>`group` sample line group<br>`at` [x,y] lower-left of the first view<br>`columns?` default 5<br>`spacing_x?` default 80<br>`spacing_y?` default 50 | Create a section view for every sample line of a group, laid out in a grid. All section sources of the group (surfaces, corridors, networks) are sampled first. |

### Corridors

| Name | Parameters | What it does |
|---|---|---|
| `assembly_create` | `name`<br>`at` [x,y] where the assembly is drawn<br>`type?` UndividedCrownedRoad (default) \| UndividedPlanarRoad \| DividedCrownedRoad \| DividedPlanarRoad \| Railway \| Other<br>`subassemblies` [{class: e.g. 'LaneSuperelevationAOR' or 'BasicLane', side?: Right \| Left, name?}, ...] | Create an assembly from stock subassemblies, attached in the order given (each one to the end of the previous one on its side). |
| `corridor_create` | `name`<br>`alignment`<br>`profile` design profile<br>`assembly`<br>`start_station?`<br>`end_station?`<br>`baseline?` name, default BL-1<br>`region?` name, default RG-1 | Create a corridor: one baseline (alignment + design profile) and one region with an assembly; it is rebuilt at once. |
| `corridor_surface_add` | `corridor`<br>`name`<br>`link_codes?` default ['Top'] | Add a corridor surface built from link codes. |
| `corridor_rebuild` | `name` | Rebuild a corridor. |

### Styles and presentation

| Name | Parameters | What it does |
|---|---|---|
| `labels_place` | `labels` [{kind, ...}] | Place Civil 3D labels. Each item has a kind: station_offset {alignment, at}; alignment_geometry {alignment, line_style?, curve_style?, spiral_style?} (every line/curve/spiral); surface_spot {surface, points}; surface_slope {surface, points \| from+to}; surface_contours {surface, line, interval?}; parcel_area {parcels?}; profile_pvi / profile_crest / profile_sag / profile_tangent {profile_view, profile}; station_elevation {profile_view, station, elevation}; note {at}. style? and marker_style? are optional. |
| `label_style_create` | `kind` pipe_label \| structure_label \| pressure_pipe_label \| ...<br>`name`<br>`components` [{text, name?, height?: mm on paper, attachment?: MiddleCenter\|BottomLeft\|..., x_offset?: mm, y_offset?: mm} or {type: 'arrow'} (direction arrow; along a pipe it shows the flow), ...]<br>`if_exists?` fail (default) \| skip \| replace | Create (or replace) a label style made of text components. Contents use Civil 3D property fields; get_style on an existing style shows the exact syntax. |
| `network_styles` | `network`<br>`pipe_style?`<br>`structure_style?`<br>`pipe_label_style?` labels every pipe<br>`structure_label_style?` labels every structure<br>`pipe_layer?`<br>`structure_layer?`<br>`label_layer?`<br>`structure_label_dx?` label offset from the structure, default 3<br>`structure_label_dy?` default -3 | Set the plan style and layer of every pipe and structure of a gravity network, and add plan labels. |
| `alignment_label_set` | `alignments` list of names<br>`label_set` see list_styles kind alignment_label_set<br>`style?`<br>`layer?` | Replace the labels of alignments with a label set (e.g. one without station labels), optionally restyle them. |
| `profile_view_set` | `profile_view`<br>`style?`<br>`station_start?`<br>`station_end?`<br>`elevation_min?`<br>`elevation_max?` | Change a profile view: style, station range, elevation range. |
| `band_style_set` | `kind` band_profile_data \| band_pipe_network \| ...<br>`name`<br>`title?`<br>`height?` mm on paper<br>`title_width?` mm<br>`title_text_height?` mm | Change a band style's title and size (what the band writes at each station is set in Civil 3D's dialog). |
| `profile_view_bands` | `profile_view`<br>`band_set?` a band set name, or auto (as profile_view_create); profile data bands get the alignment's first profile<br>`bottom?` [{type: profile_data\|pipe_network\|horizontal_geometry\|vertical_geometry, style, profile1?, profile2?, network?: gravity network (pipe_network bands), gap?: mm, label_start?, label_end?}, ...]<br>`top?` same | Replace the bands of a profile view: every band of a band set, or bands listed one by one (each with an existing band style and its data source). |

### Gravity networks

| Name | Parameters | What it does |
|---|---|---|
| `pipe_network_create` | `name`<br>`parts_list?` name or handle<br>`reference_surface?`<br>`reference_alignment?`<br>`description?` | Create an empty gravity pipe network. |
| `pipe_network_add` | `network`<br>`structures?` [{id?, name?, description?, at:[x,y,z], family, inner_diameter? \| size_index?, rotation_deg?, rim?, sump?}, ...]<br>`pipes?` [{id?, name?, description?, start:[x,y,z], end:[x,y,z] (centreline points), family, inner_diameter? \| size_index?, start_structure?, end_structure?, verify_bore_mm? (metric drawings)}, ...]<br>`parts_list?` default: the network's<br>`apply_rules?` default false<br>`connect_tolerance?` default 0.01 | Add structures and straight pipes to a gravity network. A part is chosen by 'family' (words contained in the family description) plus 'inner_diameter' (units as shown by list_gravity_parts) or 'size_index'. Name endpoint structures for strict connections. Geometric matching skips ambiguous endpoints and reports them. |
| `pipe_set` | `handle`<br>`start?` [x,y,z]<br>`end?` [x,y,z]<br>`inner_diameter?` nearest catalogue size, in the units of list_gravity_parts (mm in metric lists)<br>`description?` | Change one gravity pipe. |
| `structure_set` | `handle`<br>`location?` [x,y,z]<br>`rim?`<br>`sump?`<br>`rotation_deg?`<br>`description?` | Change one gravity structure. |
| `parts_list_add_family` | `parts_list`<br>`domain` pipe \| structure<br>`family` exact family description, e.g. 'Cylindrical Junction Structure NF SI'<br>`sizes?` [inner diameter or width, ...] in the catalogue units (mm) | Add a part family from the Civil 3D catalogue to a gravity parts list, with the sizes given (e.g. a shallow flat-top manhole for a sanitary list). Nothing changes if it is already there. |
| `network_table` | `network`<br>`kind?` pipes (default) \| structures \| pressure_pipes<br>`at` [x,y]<br>`title?`<br>`text_height?`<br>`row_height?`<br>`column_width?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255<br>`layout?` paper-space layout to draw on (default: model space) | Draw a pipe or structure schedule of a network as an AutoCAD table (snapshot of the current data: draw it again after changes). Pipes: from/to, size, material, length, slope, inverts. Structures: rim, sump, depth, inverts in/out. |
| `hgl_set` | `network`<br>`pipes?` [{name, hgl_up?, hgl_down?, egl_up?, egl_down?, flow?}, ...]<br>`structures?` [{name, hgl?, egl?}, ...] | Set hydraulic / energy grade lines and flow on a gravity network's pipes and structures (e.g. from a SewerGEMS or StormCAD run) so profile views and labels can show them. |
| `labels_add` | `pipe_network`<br>`pipes?` default true<br>`structures?` default true | Add default labels to the pipes and structures of a gravity network. |

### Pressure networks

| Name | Parameters | What it does |
|---|---|---|
| `pressure_network_create` | `name`<br>`parts_list` name or handle<br>`reference_surface?`<br>`reference_alignment?`<br>`description?` | Create an empty pressure network. |
| `pressure_network_set` | `name`<br>`new_name?`<br>`parts_list?`<br>`reference_surface?`<br>`reference_alignment?`<br>`description?` | Change a pressure network. |
| `pressure_network_erase` | `name` | Erase a pressure network with all its parts. |
| `pressure_add` | `network`<br>`pipes?` [{id?, name?, description?, start:[x,y,z], end:[x,y,z], size, match? (or part: exact description), through?: [x,y] makes a curved pipe through that point}, ...]<br>`fittings?` [{id?, name?, description?, at:[x,y,z], type?: Elbow\|Tee\|Reducer\|Cross\|Cap\|..., size, match? (or part)}, ...]<br>`appurtenances?` [{id?, name?, description?, at:[x,y,z], type?: Valve\|Hydrant\|..., size, match? (or part)}, ...]<br>`parts_list?` default: the network's<br>`connect_tolerance?` default 0.01 | Add straight pipes, fittings and appurtenances to a pressure network (new or existing). Each part is chosen from the parts list by 'size' (a whole number in its description, e.g. 150) and 'match' (words in it, e.g. 'ductile'); exactly one part must match. Fittings/appurtenances connect to pipe ends at the same point. z is the pipe centreline elevation. |
| `pressure_pipe_run` | `network`<br>`name`<br>`points` [[x,y], ...]<br>`size`<br>`match?`<br>`cover?` default 1.0<br>`auto_bends?` default true<br>`keep_path?` keep the path polyline<br>`layer?`<br>`parts_list?` | Create a pipe run along a 2D path at a depth of cover below the network's reference surface, with bends added automatically (the way the Civil 3D pipe-run command works). |
| `pressure_part_set` | `handle`<br>`start?` [x,y,z] (pipe)<br>`end?` [x,y,z] (pipe)<br>`location?` [x,y,z] (fitting/appurtenance)<br>`size?` new size<br>`match?`<br>`description?` | Move or resize ONE pressure part. |
| `pressure_labels_add` | `pressure_network`<br>`pipes?` default true<br>`fittings?` default true<br>`appurtenances?` default true<br>`handles?` label only these parts (default: every part of the chosen kinds)<br>`pipe_style?` see list_styles kind pressure_pipe_label<br>`fitting_style?`<br>`appurtenance_style?` | Add plan labels to the pipes, fittings and appurtenances of a pressure network. |
