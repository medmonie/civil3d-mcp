namespace C3dMcpBridge;

internal sealed record ActionDef(string Name, string Kind, Func<JsonElement, object> Run);

/// <summary>
/// The one table of everything the bridge can do. "read" actions never change the drawing; the single "edit" action
/// runs a batch of edit operations behind the one-time permission (see WriteGate and EditEngine).
/// </summary>
internal static class Registry
{
    private static readonly Dictionary<string, ActionDef> Actions = new(StringComparer.Ordinal);

    private static void Read(string name, Func<JsonElement, object> run) => Actions[name] = new ActionDef(name, "read", run);

    static Registry()
    {
        // drawing (AutoCAD)
        Read("ping", DrawingRead.Ping);
        Read("get_drawing_info", DrawingRead.GetDrawingInfo);
        Read("list_layers", DrawingRead.ListLayers);
        Read("count_entities", DrawingRead.CountEntities);
        Read("get_selection", DrawingRead.GetSelection);
        Read("get_objects", DrawingRead.GetObjects);
        Read("list_entities", DrawingRead.ListEntities);
        Read("list_blocks", DrawingRead.ListBlocks);
        Read("list_layouts", DrawingRead.ListLayouts);
        Read("list_viewports", SheetRead.ListViewports);
        Read("list_views", SheetRead.ListViews);
        Read("list_xrefs", SheetRead.ListXrefs);
        Read("list_cad_styles", SheetRead.ListCadStyles);
        Read("get_block_properties", SheetRead.GetBlockProperties);
        Read("inspect_file", SheetRead.InspectFile);
        // terrain
        Read("list_surfaces", TerrainRead.ListSurfaces);
        Read("get_surface_elevation", TerrainRead.GetSurfaceElevation);
        Read("get_surface_volume", TerrainRead.GetSurfaceVolume);
        Read("list_cogo_points", TerrainRead.ListCogoPoints);
        Read("list_feature_lines", TerrainRead.ListFeatureLines);
        Read("list_parcels", TerrainRead.ListParcels);
        // alignments
        Read("list_alignments", AlignmentRead.ListAlignments);
        Read("alignment_station_to_point", AlignmentRead.StationToPoint);
        Read("alignment_point_to_station", AlignmentRead.PointToStation);
        Read("list_profiles", AlignmentRead.ListProfiles);
        Read("get_profile_elevation", AlignmentRead.GetProfileElevation);
        Read("list_profile_views", AlignmentRead.ListProfileViews);
        Read("list_sample_lines", AlignmentRead.ListSampleLines);
        Read("list_section_views", AlignmentRead.ListSectionViews);
        // corridors
        Read("list_corridors", CorridorRead.ListCorridors);
        Read("list_assemblies", CorridorRead.ListAssemblies);
        // gravity networks
        Read("list_pipe_networks", GravityRead.ListPipeNetworks);
        Read("list_pipes", GravityRead.ListPipes);
        Read("list_structures", GravityRead.ListStructures);
        Read("list_gravity_parts", GravityRead.ListGravityParts);
        // pressure networks
        Read("list_pressure_networks", PressureRead.ListPressureNetworks);
        Read("list_pressure_pipes", PressureRead.ListPressurePipes);
        Read("list_pressure_fittings", PressureRead.ListPressureFittings);
        Read("list_pressure_appurtenances", PressureRead.ListPressureAppurtenances);
        Read("list_pipe_runs", PressureRead.ListPipeRuns);
        Read("list_pressure_part_sizes", PressureRead.ListPressurePartSizes);
        // styles and meta
        Read("list_styles", StyleRead.ListStyles);
        Read("get_style", StyleRead.GetStyle);
        Read("describe_api", ApiRead.DescribeApi);
        Read("get_edit_permission", _ => WriteGate.Status());
        Read("get_edit_status", EditRequests.Status);
        Read("list_actions", ListActions);
        // the only action that can change a drawing
        Actions["edit"] = new ActionDef("edit", "write", EditEngine.Run);
    }

    public static bool TryGet(string name, out ActionDef action) => Actions.TryGetValue(name, out action!);

    private static object ListActions(JsonElement p) => new
    {
        bridgeVersion = BridgeInfo.Version,
        actions = Actions.Values.Select(a => new { name = a.Name, kind = a.Kind }).ToList(),
        editOperations = EditEngine.OperationNames,
        editPermission = WriteGate.Status(),
    };
}
