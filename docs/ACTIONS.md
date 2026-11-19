# Actions and edit operations

Generated from `server/catalog.py` by `tools/gen_docs.py` - do not edit by hand.

Everything goes through one MCP tool: `civil3d(action, params)`. A parameter ending in `?` is optional.

## Read actions (40) - never change the drawing

### Drawing (AutoCAD)

| Name | Parameters | What it does |
|---|---|---|
| `ping` | - | Connection check: bridge version, Civil 3D version, active drawing, whether editing is allowed. |
| `get_drawing_info` | - | Active drawing: path, units, current layer, layer/entity counts, counts of Civil 3D objects. |
| `list_layers` | `limit?` default 500 | Layers with on/frozen/locked/plot state, colour and xref flag. |
| `count_entities` | `group_by?` 'type' (default) or 'layer'<br>`layer?` only this layer | Count model-space entities by DXF type or by layer. |
| `get_selection` | `limit?` default 200 | Objects the user has selected in Civil 3D: handle, type, layer, Civil name, extents. |
| `get_objects` | `handles` list of handle strings | Describe objects by handle: type, layer, Civil name, extents. |
| `list_entities` | `type?` DXF type, e.g. LINE, LWPOLYLINE, CIRCLE, ARC, TEXT, MTEXT, POINT, INSERT, HATCH<br>`layer?`<br>`window?` [xmin, ymin, xmax, ymax]<br>`limit?` default 100 | AutoCAD entities in model space with their geometry: lines, polylines (vertices), circles, arcs, text, mtext, points, block references (name, attributes), hatches, dimensions. |
| `list_blocks` | `limit?` default 500 | Block definitions: name, attributes flag, number of references. |
| `list_layouts` | - | Layouts (paper space tabs): name, tab order, plot device, paper size. |

### Terrain

| Name | Parameters | What it does |
|---|---|---|
| `list_surfaces` | - | Surfaces: name, type, style, layer, min/max/mean elevation. |
| `get_surface_elevation` | `surface_name`<br>`x` easting<br>`y` northing | Elevation of a TIN surface at (x, y). |
| `get_surface_volume` | `surface_name` | Cut, fill and net volume of a TIN volume surface. |
| `list_cogo_points` | `limit?` default 200 | COGO points: number, name, easting, northing, elevation, description. |
| `list_feature_lines` | `limit?` default 200 | Feature lines: length, min/max elevation, points. |
| `list_parcels` | `limit?` default 500 | Parcels of every site: name, number, centroid, extents. Read only. |

### Alignments

| Name | Parameters | What it does |
|---|---|---|
| `list_alignments` | - | Alignments (incl. the 'Pipe Run' alignments of pressure networks): type, site, length, stations. |
| `alignment_station_to_point` | `alignment_name`<br>`station`<br>`offset?` default 0 | Easting/northing of a station and offset (offset + = right). |
| `alignment_point_to_station` | `alignment_name`<br>`x`<br>`y` | Station and offset of a point. |
| `list_profiles` | `alignment_name?` only this alignment | Profiles: type, PVI count, station range, min/max elevation. |
| `get_profile_elevation` | `alignment_name`<br>`profile_name`<br>`station` | Elevation of a profile at a station. |
| `list_profile_views` | - | Profile views: alignment, style, station and elevation range, position in the drawing. |
| `list_sample_lines` | `alignment_name?` only this alignment | Sample line groups with the stations of their sample lines. |
| `list_section_views` | `limit?` default 500 | Section views: alignment, sample line group, sample line, station. |

### Corridors

| Name | Parameters | What it does |
|---|---|---|
| `list_corridors` | - | Corridors: baselines with their regions and assemblies, corridor surfaces, out-of-date flag. |
| `list_assemblies` | - | Assemblies with their subassemblies (for corridor_create). |

### Gravity networks

| Name | Parameters | What it does |
|---|---|---|
| `list_pipe_networks` | - | Gravity pipe networks: parts list, reference surface/alignment, pipe and structure counts. |
| `list_pipes` | `network_name?` only this network<br>`limit?` default 200 | Gravity pipes: part, inner diameter, slope (m/m), length, start/end, connected structures. |
| `list_structures` | `network_name?` only this network<br>`limit?` default 200 | Gravity structures: part, location, rim/sump elevation, connected pipes. |
| `list_gravity_parts` | `parts_list?` name or handle | Without parts_list: the gravity parts lists. With it: its pipe and structure families and sizes (use these to fill 'family' + 'inner_diameter' / 'size_index' in pipe_network_add). |

### Pressure networks

| Name | Parameters | What it does |
|---|---|---|
| `list_pressure_networks` | - | Pressure networks: parts list, reference surface/alignment, part and pipe-run counts, elevations. |
| `list_pressure_pipes` | `network_name?` only this network<br>`limit?` default 200 | Pressure pipes: part, diameters and wall (metres), lengths, slope, elevations, cover, start/end, handle. |
| `list_pressure_fittings` | `network_name?` only this network<br>`limit?` default 200 | Pressure fittings: part, type, connections, deflection angle, location, handle. |
| `list_pressure_appurtenances` | `network_name?` only this network<br>`limit?` default 200 | Valves, hydrants and other appurtenances: part, type, connections, location, handle. |
| `list_pipe_runs` | `network_name?` only this network | Pipe runs of pressure networks with their alignment, profile and part count. |
| `list_pressure_part_sizes` | `parts_list?` name or handle<br>`part_type?` Pipe (default), Fitting, Appurtenance, Elbow, Tee, Reducer, Valve, ...<br>`contains?` text filter, e.g. '150'<br>`limit?` default 100 | Without parts_list: the pressure parts lists (name, handle, pipe size count). With it: the part descriptions for part_type, to choose 'size' and 'match' values. |

### Styles and meta

| Name | Parameters | What it does |
|---|---|---|
| `list_styles` | `kind` alignment \| alignment_label_set \| profile \| profile_label_set \| profile_view \| profile_view_band_set \| surface \| corridor \| pipe \| structure \| point \| feature_line \| sample_line \| pressure_pipe_label \| pressure_fitting_label \| pressure_appurtenance_label \| gravity_parts_list \| pipe_label \| structure_label \| band_profile_data \| band_pipe_network \| band_horizontal_geometry \| band_vertical_geometry | Style names of one kind (for the 'style' / 'label_set' parameters of edit operations). Without a style name an edit uses 'Standard' when the drawing has it, otherwise the first style listed. |
| `get_style` | `kind` a list_styles kind<br>`name` | What one style contains: a label style's text components and their contents (the property-field syntax to copy into label_style_create), a band style's title and height, a profile view band set's bands. |
| `describe_api` | `type_name` full type name<br>`contains?` member name filter<br>`limit?` default 300 | Developer aid: public members of an Autodesk.* .NET type loaded in Civil 3D. |
| `get_edit_permission` | - | Whether editing is currently allowed in this Civil 3D session, and how to allow it. |
| `list_actions` | - | What the installed plugin supports (names only). Use 'help' for descriptions. |

### QA and help (computed in the MCP server)

| Name | Parameters | What it does |
|---|---|---|
| `help` | `topic?` an action or edit operation name | This catalogue with every parameter. Pass topic to narrow it. |
| `check_layer_names` | `pattern` regular expression<br>`skip_xref_layers?` default true | QA: layers whose names do not fully match a regular expression (e.g. the BEP layer convention). |
| `check_default_names` | - | QA: surfaces, alignments and networks still carrying Civil 3D's auto-generated names. |
| `sewergems_check` | `path?` full path of the SewerGEMS workbook (.xlsx with sheets Manhole, Outfall, Conduit)<br>`manholes_csv?` or: manhole FlexTable CSV (must include X and Y)<br>`outfalls_csv?`<br>`conduits_csv?` conduit FlexTable CSV<br>`rename_csv?` two columns: SewerGEMS label, drawing label (e.g. MH-19,MH-1-19)<br>`line_prefix?` default 'LINE' (lines are LINE1, LINE1-3, ...) | Read a Bentley SewerGEMS export and check it without drawing: manholes, conduits, drops, problems (missing nodes, uphill pipes, crown above ground, ...) and the profile lines it would make. |
| `sewergems_import` | `path?` full path of the SewerGEMS workbook (.xlsx with sheets Manhole, Outfall, Conduit)<br>`manholes_csv?` or: manhole FlexTable CSV (must include X and Y)<br>`outfalls_csv?`<br>`conduits_csv?` conduit FlexTable CSV<br>`rename_csv?` two columns: SewerGEMS label, drawing label (e.g. MH-19,MH-1-19)<br>`line_prefix?` default 'LINE' (lines are LINE1, LINE1-3, ...)<br>`network?` default 'Sewer'<br>`parts_list?` default 'Sanitary Sewer'<br>`pipe_family?` words of the pipe family, default 'PVC'<br>`structure_family?` default 'Cylindrical Junction Structure NF SI' (flat top, fits 1.2 m deep manholes); added to the parts list if missing<br>`structure_diameter?` mm, default 1200<br>`profile_views_at?` [x,y] top-left of the first view, default 50 below the network<br>`profile_view_gap?` vertical gap between views, default 30<br>`presentation?` false to skip styling; or overrides of: pipe_style, structure_style, structure_label_style, pipe_label_style, pipe_label_text, label_height_mm, alignment_label_set, profile_view_style, bands<br>`profile_view_style?`<br>`dry_run?` check the network batch only | Build a SewerGEMS export in the open drawing: a gravity network (manholes named as in SewerGEMS or the rename table, pipes at the SewerGEMS inverts), then per line an alignment from the downstream end, a ground profile through the manhole rims and a profile view with that line's parts. Needs edit permission; stops without drawing if sewergems_check reports problems. |

## The `edit` action

Change the drawing. params = {operations: [ {op: <name>, ...}, ... ], dry_run?: bool}. The whole list is ONE transaction and one undo step: if any operation fails nothing is changed. dry_run runs everything and rolls it back, so use it to validate a large batch first. Editing needs a ONE-TIME permission per Civil 3D session: on the first edit a Yes/No dialog appears in Civil 3D (or the user runs MCPALLOW); after that no edit asks again.

## Edit operations (68) - items of `operations`

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
| `draw_line` | `start` [x,y,z]<br>`end` [x,y,z]<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw a line. |
| `draw_polyline` | `points` [[x,y] or [x,y,z], ...]<br>`closed?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw a polyline (2D if all z are equal, otherwise 3D). |
| `draw_point` | `at` [x,y,z]<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw an AutoCAD point. |
| `draw_circle` | `center` [x,y,z]<br>`radius`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw a circle. |
| `draw_arc` | `center` [x,y,z]<br>`radius`<br>`start_deg`<br>`end_deg`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw an arc, counter-clockwise from start_deg to end_deg (0 = east). |
| `draw_text` | `text`<br>`at` [x,y,z]<br>`height?` default 2.5<br>`rotation_deg?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw single-line text. |
| `draw_mtext` | `text`<br>`at` [x,y,z]<br>`height?` default 2.5<br>`width?` wrap width<br>`rotation_deg?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw multiline text (\P starts a new line). |
| `draw_hatch` | `points` [[x,y], ...]<br>`pattern?` SOLID (default), ANSI31, AR-CONC, ...<br>`scale?`<br>`keep_boundary?` default true (associative hatch)<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Hatch a closed boundary given by its corner points. |
| `draw_dimension` | `from` [x,y]<br>`to` [x,y]<br>`line_at` [x,y] a point on the dimension line<br>`type?` aligned (default) \| horizontal \| vertical<br>`text?` override text<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw a dimension between two points. |
| `draw_leader` | `arrow_at` [x,y]<br>`text_at` [x,y]<br>`text`<br>`height?` default: the multileader style<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw a multileader: an arrow with a text note. |
| `draw_table` | `at` [x,y] top-left corner<br>`rows` [[cell, ...], ...]<br>`title?` merged title row<br>`text_height?` default 2.5<br>`row_height?`<br>`column_width?`<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Draw a table from rows of cell values. |

### Blocks

| Name | Parameters | What it does |
|---|---|---|
| `draw_block` | `name`<br>`at` [x,y,z]<br>`scale?` default 1<br>`rotation_deg?`<br>`attributes?` {TAG: value, ...}<br>`layer?` default: current layer<br>`color?` AutoCAD colour index 1-255 | Insert a block that is already defined in the drawing (see list_blocks). |
| `block_define` | `name`<br>`handles` list<br>`base` [x,y] insertion base point<br>`replace_originals?` erase them and insert the block in their place | Define a block from existing plain AutoCAD entities. |
| `attribute_set` | `handle`<br>`attributes` {TAG: value, ...} | Change attribute values of a block reference. |

### Drawing file

| Name | Parameters | What it does |
|---|---|---|
| `layout_create` | `name` | Create a layout (paper space tab). |
| `xref_attach` | `path` full path to a .dwg<br>`name?`<br>`at?` [x,y,z] default origin<br>`layer?` | Attach a drawing as an external reference. |
| `drawing_save` | `path?` full path<br>`copy?` default false<br>`format?` dwg (default) \| dxf<br>`overwrite?` default false | Save the drawing; runs after the batch is committed. No path: save in place. path: Save As (the open drawing continues under the new name). path + copy:true: write a copy and keep working in the original. format dxf: export. |
| `plot_pdf` | `path` full path ending in .pdf<br>`layout?` default: the current tab<br>`paper?` words of a paper size, e.g. 'A1' or 'ISO full bleed A3'<br>`plot_style?` e.g. monochrome.ctb<br>`overwrite?` default false | Plot one layout (or model space) to a PDF with 'DWG To PDF.pc3'; runs after the batch is committed. Model space is plotted to its extents, fitted to the paper; a paper-space layout is plotted 1:1 as laid out. |

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

### Alignments

| Name | Parameters | What it does |
|---|---|---|
| `alignment_create` | `name`<br>`points` [[x,y], ...]<br>`radius?` curve radius at each PI<br>`site?` default siteless<br>`layer?`<br>`style?`<br>`label_set?`<br>`description?` | Create an alignment from PI points (straight tangents; optional curve radius at every PI). |
| `profile_create` | `alignment`<br>`name`<br>`pvis` [[station, elevation], ...]<br>`layer?`<br>`style?`<br>`label_set?` | Create a layout profile from PVIs on an alignment. |
| `profile_from_surface` | `alignment`<br>`surface`<br>`name`<br>`layer?`<br>`style?`<br>`label_set?` | Create a surface (existing ground) profile. |
| `profile_view_create` | `alignment`<br>`at` [x,y] lower-left insertion point<br>`name?`<br>`style?`<br>`band_set?` | Create a profile view of an alignment (shows all its profiles). |
| `profile_view_add_parts` | `profile_view`<br>`pipe_network?` gravity network name<br>`pressure_network?`<br>`handles?` individual parts | Show network parts in a profile view. |
| `sample_lines_create` | `alignment`<br>`group`<br>`stations?` [station, ...]<br>`interval?` every N along the alignment<br>`prefix?` default 'SL-' | Create sample lines on an alignment (the group is created if missing). |
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
| `label_style_create` | `kind` pipe_label \| structure_label \| pressure_pipe_label \| ...<br>`name`<br>`components` [{text, name?, height?: mm on paper, attachment?: MiddleCenter\|BottomLeft\|..., x_offset?: mm, y_offset?: mm} or {type: 'arrow'} (direction arrow; along a pipe it shows the flow), ...]<br>`if_exists?` fail (default) \| skip \| replace | Create (or replace) a label style made of text components. Contents use Civil 3D property fields; get_style on an existing style shows the exact syntax. |
| `network_styles` | `network`<br>`pipe_style?`<br>`structure_style?`<br>`pipe_label_style?` labels every pipe<br>`structure_label_style?` labels every structure<br>`pipe_layer?`<br>`structure_layer?`<br>`label_layer?`<br>`structure_label_dx?` label offset from the structure, default 3<br>`structure_label_dy?` default -3 | Set the plan style and layer of every pipe and structure of a gravity network, and add plan labels. |
| `alignment_label_set` | `alignments` list of names<br>`label_set` see list_styles kind alignment_label_set<br>`style?`<br>`layer?` | Replace the labels of alignments with a label set (e.g. one without station labels), optionally restyle them. |
| `profile_view_set` | `profile_view`<br>`style?`<br>`station_start?`<br>`station_end?`<br>`elevation_min?`<br>`elevation_max?` | Change a profile view: style, station range, elevation range. |
| `band_style_set` | `kind` band_profile_data \| band_pipe_network \| ...<br>`name`<br>`title?`<br>`height?` mm on paper<br>`title_width?` mm<br>`title_text_height?` mm | Change a band style's title and size (what the band writes at each station is set in Civil 3D's dialog). |
| `profile_view_bands` | `profile_view`<br>`bottom?` [{type: profile_data\|pipe_network\|horizontal_geometry\|vertical_geometry, style, profile1?, profile2?, network?: gravity network (pipe_network bands), gap?: mm, label_start?, label_end?}, ...]<br>`top?` same | Replace the bands of a profile view. Each band uses an existing band style and its data source. |

### Gravity networks

| Name | Parameters | What it does |
|---|---|---|
| `pipe_network_create` | `name`<br>`parts_list?` name or handle<br>`reference_surface?`<br>`reference_alignment?`<br>`description?` | Create an empty gravity pipe network. |
| `pipe_network_add` | `network`<br>`structures?` [{id?, name?, description?, at:[x,y,z], family, inner_diameter? \| size_index?, rotation_deg?, rim?, sump?}, ...]<br>`pipes?` [{id?, name?, description?, start:[x,y,z], end:[x,y,z] (centreline points), family, inner_diameter? \| size_index?}, ...]<br>`parts_list?` default: the network's<br>`apply_rules?` default false<br>`connect_tolerance?` default 0.01 | Add structures and straight pipes to a gravity network. A part is chosen by 'family' (words contained in the family description) plus 'inner_diameter' (units as shown by list_gravity_parts) or 'size_index'. Pipes connect to structures at their end points. |
| `pipe_set` | `handle`<br>`start?` [x,y,z]<br>`end?` [x,y,z]<br>`inner_diameter?` nearest catalogue size, in the units of list_gravity_parts (mm in metric lists)<br>`description?` | Change one gravity pipe. |
| `structure_set` | `handle`<br>`location?` [x,y,z]<br>`rim?`<br>`sump?`<br>`rotation_deg?`<br>`description?` | Change one gravity structure. |
| `parts_list_add_family` | `parts_list`<br>`domain` pipe \| structure<br>`family` exact family description, e.g. 'Cylindrical Junction Structure NF SI'<br>`sizes?` [inner diameter or width, ...] in the catalogue units (mm) | Add a part family from the Civil 3D catalogue to a gravity parts list, with the sizes given (e.g. a shallow flat-top manhole for a sanitary list). Nothing changes if it is already there. |
| `labels_add` | `pipe_network`<br>`pipes?` default true<br>`structures?` default true | Add default labels to the pipes and structures of a gravity network. |

### Pressure networks

| Name | Parameters | What it does |
|---|---|---|
| `pressure_network_create` | `name`<br>`parts_list` name or handle<br>`reference_surface?`<br>`reference_alignment?`<br>`description?` | Create an empty pressure network. |
| `pressure_network_set` | `name`<br>`new_name?`<br>`parts_list?`<br>`reference_surface?`<br>`reference_alignment?`<br>`description?` | Change a pressure network. |
| `pressure_network_erase` | `name` | Erase a pressure network with all its parts. |
| `pressure_add` | `network`<br>`pipes?` [{id?, start:[x,y,z], end:[x,y,z], size, match?, through?: [x,y] makes a curved pipe through that point}, ...]<br>`fittings?` [{id?, at:[x,y,z], type?: Elbow\|Tee\|Reducer\|Cross\|Cap\|..., size, match?}, ...]<br>`appurtenances?` [{id?, at:[x,y,z], type?: Valve\|Hydrant\|..., size, match?}, ...]<br>`parts_list?` default: the network's<br>`connect_tolerance?` default 0.01 | Add straight pipes, fittings and appurtenances to a pressure network (new or existing). Each part is chosen from the parts list by 'size' (a whole number in its description, e.g. 150) and 'match' (words in it, e.g. 'ductile'); exactly one part must match. Fittings/appurtenances connect to pipe ends at the same point. z is the pipe centreline elevation. |
| `pressure_pipe_run` | `network`<br>`name`<br>`points` [[x,y], ...]<br>`size`<br>`match?`<br>`cover?` default 1.0<br>`auto_bends?` default true<br>`keep_path?` keep the path polyline<br>`layer?`<br>`parts_list?` | Create a pipe run along a 2D path at a depth of cover below the network's reference surface, with bends added automatically (the way the Civil 3D pipe-run command works). |
| `pressure_part_set` | `handle`<br>`start?` [x,y,z] (pipe)<br>`end?` [x,y,z] (pipe)<br>`location?` [x,y,z] (fitting/appurtenance)<br>`size?` new size<br>`match?`<br>`description?` | Move or resize ONE pressure part. |
| `pressure_labels_add` | `pressure_network`<br>`pipes?` default true<br>`fittings?` default true<br>`appurtenances?` default true<br>`pipe_style?` see list_styles kind pressure_pipe_label<br>`fitting_style?`<br>`appurtenance_style?` | Add plan labels to the pipes, fittings and appurtenances of a pressure network. |
