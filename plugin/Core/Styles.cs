namespace C3dMcpBridge;

/// <summary>Civil 3D styles: the collections an edit may take a style name from, and resolving a name to a style.</summary>
internal static class Styles
{
    private static CivilStyles.LabelStylesRoot Labels(CivilDoc civ) => civ.Styles.LabelStyles;

    /// <summary>list_styles 'kind' -> the collection. The single list of supported kinds.</summary>
    private static readonly Dictionary<string, Func<CivilDoc, CivilStyles.StyleCollectionBase>> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alignment"] = civ => civ.Styles.AlignmentStyles,
        ["alignment_label_set"] = civ => civ.Styles.LabelSetStyles.AlignmentLabelSetStyles,
        ["profile"] = civ => civ.Styles.ProfileStyles,
        ["profile_label_set"] = civ => civ.Styles.LabelSetStyles.ProfileLabelSetStyles,
        ["profile_view"] = civ => civ.Styles.ProfileViewStyles,
        ["profile_view_band_set"] = civ => civ.Styles.ProfileViewBandSetStyles,
        ["surface"] = civ => civ.Styles.SurfaceStyles,
        ["corridor"] = civ => civ.Styles.CorridorStyles,
        ["pipe"] = civ => civ.Styles.PipeStyles,
        ["structure"] = civ => civ.Styles.StructureStyles,
        ["point"] = civ => civ.Styles.PointStyles,
        ["point_label"] = civ => Labels(civ).PointLabelStyles.LabelStyles,
        ["feature_line"] = civ => civ.Styles.FeatureLineStyles,
        ["sample_line"] = civ => civ.Styles.SampleLineStyles,
        ["pressure_pipe_label"] = PressurePipeLabels,
        ["pressure_fitting_label"] = PressureFittingLabels,
        ["pressure_appurtenance_label"] = PressureAppurtenanceLabels,
        ["gravity_parts_list"] = civ => civ.Styles.PartsListSet,
        ["pipe_label"] = civ => Labels(civ).PipeLabelStyles.PlanProfileLabelStyles,
        ["structure_label"] = civ => Labels(civ).StructureLabelStyles.LabelStyles,
        ["band_profile_data"] = civ => civ.Styles.BandStyles.ProfileViewProfileDataBandStyles,
        ["band_pipe_network"] = civ => civ.Styles.BandStyles.ProfileViewPipeNetworkBandStyles,
        ["band_horizontal_geometry"] = civ => civ.Styles.BandStyles.ProfileViewHorizontalGeometryBandStyles,
        ["band_vertical_geometry"] = civ => civ.Styles.BandStyles.ProfileViewVerticalGeometryBandStyles,
    };

    public static CivilStyles.StyleCollectionBase PressurePipeLabels(CivilDoc civ) =>
        CivilStyles.LabelStylesRootPressurePipesExtension.GetPressurePipeLabelStyles(Labels(civ)).PlanProfileLabelStyles;

    public static CivilStyles.StyleCollectionBase PressureFittingLabels(CivilDoc civ) =>
        CivilStyles.LabelStylesRootPressurePipesExtension.GetPressureFittingLabelStyles(Labels(civ)).LabelStyles;

    public static CivilStyles.StyleCollectionBase PressureAppurtenanceLabels(CivilDoc civ) =>
        CivilStyles.LabelStylesRootPressurePipesExtension.GetPressureAppurtenanceLabelStyles(Labels(civ)).LabelStyles;

    public static IEnumerable<string> KindNames => Kinds.Keys;

    /// <summary>The band type that goes with a band style kind (band_profile_data -> ProfileData).</summary>
    public static Autodesk.Civil.BandType? BandTypeOf(string kind) => kind.ToLowerInvariant() switch
    {
        "band_profile_data" => Autodesk.Civil.BandType.ProfileData,
        "band_pipe_network" => Autodesk.Civil.BandType.PipeNetwork,
        "band_horizontal_geometry" => Autodesk.Civil.BandType.HorizontalGeometry,
        "band_vertical_geometry" => Autodesk.Civil.BandType.VerticalGeometry,
        _ => null,
    };

    public static CivilStyles.StyleCollectionBase Collection(CivilDoc civ, string kind) =>
        Kinds.TryGetValue(kind, out var get)
            ? get(civ)
            : throw new ArgumentException($"kind must be one of: {string.Join(", ", Kinds.Keys)}.");

    /// <summary>
    /// The name of a style or parts list. StyleBase re-declares Name as set-only; the getter is on its base class,
    /// Autodesk.Civil.DatabaseServices.DBObject. Returns null when the object has no readable name.
    /// </summary>
    public static string? NameOf(DBObject style)
    {
        try { return style is CivilDb.DBObject named && named.Name is { Length: > 0 } name ? name : null; }
        catch { return null; }
    }

    /// <summary>The style Civil 3D templates use as their general-purpose default.</summary>
    internal const string StandardStyle = "Standard";

    /// <summary>A style by name. Without a name: the style called "Standard" when there is one, otherwise the first.</summary>
    public static ObjectId Id(CivilStyles.StyleCollectionBase styles, string? name, string what)
    {
        if (!string.IsNullOrEmpty(name))
        {
            try { return styles[name]; }
            catch { throw new ArgumentException($"{what} '{name}' not found in this drawing. Use list_styles."); }
        }
        if (styles.Count == 0) throw new InvalidOperationException($"This drawing has no {what}; the template is missing it.");
        try { return styles[StandardStyle]; }
        catch { return styles[0]; }
    }

    /// <summary>The name of a label style component (text, arrow, ...); null when it cannot be read.</summary>
    public static string? ComponentName(DBObject component)
    {
        try
        {
            var general = component.GetType().GetProperty("General")?.GetValue(component);
            return general?.GetType().GetProperty("Name")?.GetValue(general) as string;
        }
        catch { return null; }
    }

    /// <summary>The name of the style behind an id (for replies).</summary>
    public static string? NameOf(Transaction tr, ObjectId styleId) => NameOf(tr.GetObject(styleId, OpenMode.ForRead));
}
