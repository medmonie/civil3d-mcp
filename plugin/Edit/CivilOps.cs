namespace C3dMcpBridge;

/// <summary>
/// Civil 3D additions of 0.15: network tables, point import / point groups / description keys, surface analysis,
/// waterdrop paths, alignments from polylines and offsets, superelevation slopes, and hydraulic grade lines.
/// </summary>
internal static class CivilOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["network_table"] = NetworkTable;
        ops["points_import"] = PointsImport;
        ops["point_group_create"] = PointGroupCreate;
        ops["description_key_create"] = DescriptionKeyCreate;
        ops["surface_analysis_set"] = SurfaceAnalysisSet;
        ops["surface_waterdrop"] = SurfaceWaterdrop;
        ops["alignment_from_polyline"] = AlignmentFromPolyline;
        ops["alignment_offset_create"] = AlignmentOffsetCreate;
        ops["superelevation_slope_set"] = SuperelevationSlopeSet;
        ops["hgl_set"] = HglSet;
    }

    private static string F(double v, int digits = 2) => double.IsFinite(v) ? v.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture) : "";

    // ---------------------------------------------------------------- network tables

    /// <summary>
    /// A pipe or structure schedule drawn as an AutoCAD table from the network's current data. Civil 3D's own
    /// network tables cannot be created through the .NET API; this table does not update itself: draw it again.
    /// </summary>
    private static object NetworkTable(EditContext c, JsonElement p)
    {
        string kind = Choice(p, "kind", "pipes", "pipes", "structures", "pressure_pipes");
        string network = NeedStr(p, "network");
        var rows = new List<List<string>>();
        string title;
        if (kind == "pressure_pipes")
        {
            var net = FindPressureNetwork(c.Civ, c.Tr, network);
            rows.Add(new() { "Pipe", "Description", "DN (mm)", "Length (m)", "Start elev.", "End elev." });
            foreach (ObjectId id in net.GetPipeIds())
            {
                var pp = (CivilDb.PressurePipe)c.Tr.GetObject(id, OpenMode.ForRead);
                rows.Add(new() { pp.Name, pp.PartDescription ?? "", F(pp.NominalDiameter * 1000, 0), F(pp.Length3DCenterToCenter),
                                 F(pp.StartElevation, 3), F(pp.EndElevation, 3) });
            }
            title = StrParam(p, "title") ?? $"{net.Name} - Pipe Schedule";
        }
        else
        {
            var net = FindPipeNetwork(c.Civ, c.Tr, network);
            string Name(ObjectId id) => id.IsNull ? "" : ((CivilDb.Part)c.Tr.GetObject(id, OpenMode.ForRead)).Name;
            if (kind == "pipes")
            {
                rows.Add(new() { "Pipe", "From", "To", "Size", "Material", "Length (m)", "Slope (%)", "Start invert", "End invert" });
                foreach (ObjectId id in net.GetPipeIds())
                {
                    var pipe = (CivilDb.Pipe)c.Tr.GetObject(id, OpenMode.ForRead);
                    double r = GravityRead.InnerHalfHeight(pipe);
                    rows.Add(new() { pipe.Name, Name(pipe.StartStructureId), Name(pipe.EndStructureId), pipe.PartSizeName ?? "",
                                     pipe.PartFamilyName ?? "", F(pipe.Length2D), F(pipe.Slope * 100, 2),
                                     F(pipe.StartPoint.Z - r, 3), F(pipe.EndPoint.Z - r, 3) });
                }
                title = StrParam(p, "title") ?? $"{net.Name} - Pipe Schedule";
            }
            else
            {
                rows.Add(new() { "Structure", "Rim", "Sump", "Depth (m)", "Pipes in (invert)", "Pipes out (invert)" });
                foreach (ObjectId id in net.GetStructureIds())
                {
                    var s = (CivilDb.Structure)c.Tr.GetObject(id, OpenMode.ForRead);
                    var ins = new List<string>();
                    var outs = new List<string>();
                    for (int i = 0; i < s.ConnectedPipesCount; i++)
                    {
                        var pipe = (CivilDb.Pipe)c.Tr.GetObject(s.get_ConnectedPipe(i), OpenMode.ForRead);
                        bool atStart = pipe.StartStructureId == id;
                        double inv = (atStart ? pipe.StartPoint.Z : pipe.EndPoint.Z) - GravityRead.InnerHalfHeight(pipe);
                        string cell = $"{pipe.Name} ({F(inv, 3)})";
                        var flow = pipe.FlowDirection;
                        if (flow == CivilDb.FlowDirectionType.Bidirectional)
                        {
                            cell += " [bidirectional]";
                            ins.Add(cell);
                            outs.Add(cell);
                        }
                        else if (atStart == (flow == CivilDb.FlowDirectionType.StartToEnd)) outs.Add(cell);
                        else ins.Add(cell);
                    }
                    rows.Add(new() { s.Name, F(s.RimElevation, 3), F(s.SumpElevation, 3), F(s.RimElevation - s.SumpElevation),
                                     string.Join(", ", ins), string.Join(", ", outs) });
                }
                title = StrParam(p, "title") ?? $"{net.Name} - Structure Schedule";
            }
        }
        if (rows.Count == 1) throw new ArgumentException($"network_table: network '{network}' has no {kind.Replace('_', ' ')}.");
        var result = DrawOps.MakeTable(c, p, rows, title);
        return new { kind, network, rows = rows.Count - 1, table = result };
    }

    // ---------------------------------------------------------------- points

    private static object PointsImport(EditContext c, JsonElement p)
    {
        string path = NeedStr(p, "path");
        if (!File.Exists(path)) throw new ArgumentException($"points_import: file not found: {path}");
        var formats = CivilDb.PointFileFormatCollection.GetPointFileFormats(c.Db);
        string formatName = NeedStr(p, "format");
        CivilDb.PointFileFormat format;
        try { format = formats[formatName]; }
        catch { throw new ArgumentException($"points_import: point file format '{formatName}' not found. Use list_point_file_formats."); }
        uint before = c.Civ.CogoPoints.Count;
        uint imported;
        if (StrParam(p, "point_group") is { Length: > 0 } group)
        {
            var gid = PointGroupId(c, group, create: true);
            imported = CivilDb.CogoPointCollection.ImportPoints(path, format, gid);
        }
        else imported = CivilDb.CogoPointCollection.ImportPoints(path, format);
        return new { file = Path.GetFileName(path), format = formatName, imported, pointsBefore = before, pointsAfter = c.Civ.CogoPoints.Count };
    }

    private static ObjectId PointGroupId(EditContext c, string name, bool create)
    {
        foreach (ObjectId id in c.Civ.PointGroups)
            if (SameName(((CivilDb.PointGroup)c.Tr.GetObject(id, OpenMode.ForRead)).Name, name)) return id;
        if (!create) throw new ArgumentException($"Point group '{name}' not found. Use list_point_groups.");
        return c.Civ.PointGroups.Add(name);
    }

    private static object PointGroupCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        string ifExists = Choice(p, "if_exists", "error", "error", "replace");
        bool existed = false;
        ObjectId id = ObjectId.Null;
        foreach (ObjectId g in c.Civ.PointGroups)
            if (SameName(((CivilDb.PointGroup)c.Tr.GetObject(g, OpenMode.ForRead)).Name, name)) { id = g; existed = true; }
        if (existed && ifExists == "error") throw new ArgumentException($"point_group_create: '{name}' exists; pass if_exists: replace to change its query.");
        if (!existed) id = c.Civ.PointGroups.Add(name);
        var pg = (CivilDb.PointGroup)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(p, "description") is string d) pg.Description = d;

        var q = new CivilDb.StandardPointGroupQuery();
        bool any = false;
        void Set(string param, Action<string> setter) { if (StrParam(p, param) is { Length: > 0 } v) { setter(v); any = true; } }
        if (BoolParam(p, "include_all") == true) { q.IncludeAllPoints = true; any = true; }
        Set("include_numbers", v => q.IncludeNumbers = v);
        Set("include_raw_descriptions", v => q.IncludeRawDescriptions = v);
        Set("include_full_descriptions", v => q.IncludeFullDescriptions = v);
        Set("include_names", v => q.IncludeNames = v);
        Set("include_elevations", v => q.IncludeElevations = v);
        Set("exclude_numbers", v => q.ExcludeNumbers = v);
        Set("exclude_raw_descriptions", v => q.ExcludeRawDescriptions = v);
        Set("exclude_names", v => q.ExcludeNames = v);
        Set("exclude_elevations", v => q.ExcludeElevations = v);
        if (!any) throw new ArgumentException("point_group_create: give include_all or at least one include_/exclude_ filter (e.g. include_raw_descriptions: \"MH*,IL*\").");
        pg.SetQuery(q);
        if (StrParam(p, "point_style") is string ps) { pg.PointStyleId = Styles.Id(c.Civ.Styles.PointStyles, ps, "point style"); pg.IsPointStyleOverridden = true; }
        if (StrParam(p, "label_style") is string ls)
        {
            pg.PointLabelStyleId = Styles.Id(c.Civ.Styles.LabelStyles.PointLabelStyles.LabelStyles, ls, "point label style");
            pg.IsPointLabelStyleOverridden = true;
        }
        pg.Update();
        return new { pointGroup = pg.Name, handle = H(id), replaced = existed, points = pg.PointsCount, query = Safe(() => pg.GetQuery().QueryString) };
    }

    private static object DescriptionKeyCreate(EditContext c, JsonElement p)
    {
        var sets = CivilDb.PointDescriptionKeySetCollection.GetPointDescriptionKeySets(c.Db);
        string setName = StrParam(p, "key_set") ?? "Default";
        var setId = sets.Contains(setName) ? sets[setName] : sets.Add(setName);
        var set = (CivilDb.PointDescriptionKeySet)c.Tr.GetObject(setId, OpenMode.ForWrite);
        string code = NeedStr(p, "code");
        bool existed = set.Contains(code);
        if (existed && Choice(p, "if_exists", "error", "error", "replace") == "error")
            throw new ArgumentException($"description_key_create: code '{code}' exists in '{setName}'; pass if_exists: replace.");
        var key = (CivilDb.PointDescriptionKey)c.Tr.GetObject(existed ? set[code] : set.Add(code), OpenMode.ForWrite);
        if (StrParam(p, "format") is string f) key.Format = f;
        if (StrParam(p, "point_style") is string ps) { key.StyleId = Styles.Id(c.Civ.Styles.PointStyles, ps, "point style"); key.ApplyStyleId = true; }
        if (StrParam(p, "label_style") is string ls)
        {
            key.LabelStyleId = Styles.Id(c.Civ.Styles.LabelStyles.PointLabelStyles.LabelStyles, ls, "point label style");
            key.ApplyLabelStyleId = true;
        }
        if (StrParam(p, "layer") is string layer) { key.LayerId = LayerId(c.Db, c.Tr, layer); key.ApplyLayerId = true; }
        int applied = 0;
        if (BoolParam(p, "apply_to_points") == true)
            foreach (ObjectId g in c.Civ.PointGroups)
            {
                var pg = (CivilDb.PointGroup)c.Tr.GetObject(g, OpenMode.ForWrite);
                if (pg.Name == CivilDb.PointGroup.AllPointsGroupName) { pg.ApplyDescriptionKeys(); applied++; }
            }
        return new { keySet = setName, code, replaced = existed, format = key.Format, appliedToAllPoints = applied > 0 };
    }

    // ---------------------------------------------------------------- surface analysis

    private static readonly short[] Palette = { 5, 4, 3, 2, 30, 1, 6, 150, 90, 40, 210, 250 };

    /// <summary>
    /// Elevation, slope or slope-arrow ranges of a surface (each with an AutoCAD colour), and optionally turns that
    /// display on in the surface's style. Without ranges, 'count' equal ranges between the surface's min and max.
    /// </summary>
    private static object SurfaceAnalysisSet(EditContext c, JsonElement p)
    {
        var surf = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"), OpenMode.ForWrite);
        string type = Choice(p, "type", "elevations", "elevations", "slopes", "slope_arrows");
        var props = surf.GetGeneralProperties();
        var ranges = new List<(double lo, double hi, short color)>();
        if (Has(p, "ranges"))
        {
            var specs = NeedArr(p, "ranges", 64);
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                ranges.Add((NeedDbl(s, "min"), NeedDbl(s, "max"), (short)IntParam(s, "color", Palette[i % Palette.Length], 1, 255)));
            }
        }
        else
        {
            int count = IntParam(p, "count", 8, 1, 64);
            double lo, hi;
            if (type == "elevations") { lo = props.MinimumElevation; hi = props.MaximumElevation; }
            else { lo = 0; hi = DblParam(p, "max_slope") ?? 30.0; }   // percent; the API gives no surface maximum slope
            double step = (hi - lo) / count;
            for (int i = 0; i < count; i++) ranges.Add((lo + i * step, lo + (i + 1) * step, Palette[i % Palette.Length]));
        }
        Autodesk.AutoCAD.Colors.Color Col(short aci) => Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
        // slopes are given in percent; the API takes ratios
        double S(double pct) => pct / 100.0;
        switch (type)
        {
            case "elevations":
                surf.Analysis.SetElevationData(ranges.Select(r => new CivilDb.SurfaceAnalysisElevationData(r.lo, r.hi, Col(r.color))).ToArray());
                break;
            case "slopes":
                surf.Analysis.SetSlopeData(ranges.Select(r => new CivilDb.SurfaceAnalysisSlopeData(S(r.lo), S(r.hi), Col(r.color))).ToArray());
                break;
            default:
                surf.Analysis.SetSlopeArrowData(ranges.Select(r => new CivilDb.SurfaceAnalysisSlopeArrowData(S(r.lo), S(r.hi), Col(r.color))).ToArray());
                break;
        }
        string? styleShown = null;
        if (StrParam(p, "show_in_style") is { Length: > 0 } styleName)
        {
            var style = (CivilStyles.SurfaceStyle)c.Tr.GetObject(Styles.Id(c.Civ.Styles.SurfaceStyles, styleName, "surface style"), OpenMode.ForWrite);
            var display = type switch
            {
                "elevations" => CivilStyles.SurfaceDisplayStyleType.Elevations,
                "slopes" => CivilStyles.SurfaceDisplayStyleType.Slopes,
                _ => CivilStyles.SurfaceDisplayStyleType.SlopeArrows,
            };
            style.GetDisplayStylePlan(display).Visible = true;
            style.GetDisplayStyleModel(display).Visible = true;
            surf.StyleId = style.ObjectId;
            styleShown = styleName;
        }
        return new
        {
            surface = surf.Name, type, units = type == "elevations" ? "drawing units" : "percent",
            ranges = ranges.Select(r => new { min = R3(r.lo), max = R3(r.hi), color = r.color }).ToList(),
            styleShown,
            note = styleShown == null ? "The ranges are stored; the surface shows them only when its style displays this analysis (pass show_in_style)." : null,
        };
    }

    private static object SurfaceWaterdrop(EditContext c, JsonElement p)
    {
        var surf = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"), OpenMode.ForWrite);
        var kind = Choice(p, "kind", "polyline2d", "polyline2d", "polyline3d") == "polyline3d"
            ? Autodesk.Civil.WaterdropObjectType.Polyline3D : Autodesk.Civil.WaterdropObjectType.Polyline2D;
        var handles = new List<string>();
        foreach (var at in PtList(p, "points", "surface_waterdrop", 1, 500))
            foreach (ObjectId id in surf.Analysis.CreateWaterdrop(new Point2d(at.X, at.Y), kind))
            {
                if (StrParam(p, "layer") is string layer)
                    ((Entity)c.Tr.GetObject(id, OpenMode.ForWrite)).LayerId = LayerId(c.Db, c.Tr, layer);
                handles.Add(H(id));
            }
        return new { surface = surf.Name, paths = handles.Count, handles };
    }

    // ---------------------------------------------------------------- alignments

    private static object AlignmentFromPolyline(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Alignment>(c.Tr, AllAlignmentIds(c.Civ, c.Tr), name, "An alignment");
        var plineId = IdFromHandle(c.Db, NeedStr(p, "handle"));
        string dxf = DxfOrName(plineId);
        if (dxf is not ("LWPOLYLINE" or "POLYLINE" or "LINE" or "ARC"))
            throw new ArgumentException($"alignment_from_polyline: handle is a {dxf}; give a polyline.");
        var options = new CivilDb.PolylineOptions
        {
            PlineId = plineId,
            AddCurvesBetweenTangents = BoolParam(p, "add_curves") ?? false,
            EraseExistingEntities = BoolParam(p, "erase_polyline") ?? false,
        };
        ObjectId siteId = StrParam(p, "site") is { Length: > 0 } site
            ? TryFindSite(c.Civ, c.Tr, site)?.ObjectId ?? throw new ArgumentException($"Site '{site}' not found.")
            : ObjectId.Null;
        var id = CivilDb.Alignment.Create(c.Civ, options, name, siteId, c.Layer(p),
            Styles.Id(c.Civ.Styles.AlignmentStyles, StrParam(p, "style"), "alignment style"),
            Styles.Id(c.Civ.Styles.LabelSetStyles.AlignmentLabelSetStyles, StrParam(p, "label_set"), "alignment label set"));
        var al = (CivilDb.Alignment)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(p, "description") is string d) al.Description = d;
        return new { alignment = name, handle = H(id), entities = Safe(() => al.Entities.Count), length = Safe(() => R3(al.Length)) };
    }

    private static object AlignmentOffsetCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Alignment>(c.Tr, AllAlignmentIds(c.Civ, c.Tr), name, "An alignment");
        var parent = FindAlignment(c.Civ, c.Tr, NeedStr(p, "parent"));
        double offset = NeedDbl(p, "offset");
        if (Math.Abs(offset) < 1e-9) throw new ArgumentException("alignment_offset_create: offset must not be 0 (negative = left).");
        var style = Styles.Id(c.Civ.Styles.AlignmentStyles, StrParam(p, "style"), "alignment style");
        ObjectId id = DblParam(p, "start_station") is double s0 && DblParam(p, "end_station") is double s1
            ? CivilDb.Alignment.CreateOffsetAlignment(name, parent.ObjectId, offset, style, s0, s1)
            : CivilDb.Alignment.CreateOffsetAlignment(name, parent.ObjectId, offset, style);
        var al = (CivilDb.Alignment)c.Tr.GetObject(id, OpenMode.ForRead);
        return new { alignment = name, handle = H(id), parent = parent.Name, offset, length = Safe(() => R3(al.Length)) };
    }

    private static object SuperelevationSlopeSet(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"), OpenMode.ForWrite);
        double station = NeedDbl(p, "station");
        var segment = EnumParam(p, "segment", Autodesk.Civil.SuperelevationCrossSegmentType.RightInLaneCrossSlope);
        double slope = NeedDbl(p, "slope_percent") / 100.0;
        var hit = al.SuperelevationCriticalStations.Cast<CivilDb.SuperelevationCriticalStation>()
                    .OrderBy(s => Math.Abs(s.Station - station)).FirstOrDefault();
        if (hit == null || Math.Abs(hit.Station - station) > (DblParam(p, "tolerance") ?? 0.01))
            throw new ArgumentException($"superelevation_slope_set: no critical station at {station} on '{al.Name}'. Use get_superelevation.");
        hit.SetSlope(segment, slope);
        return new { alignment = al.Name, station = R3(hit.Station), segment = segment.ToString(), slopePercent = R3(hit.GetSlope(segment) * 100) };
    }

    // ---------------------------------------------------------------- hydraulic grade lines

    /// <summary>HGL / EGL values on gravity pipes and structures, e.g. from a SewerGEMS or StormCAD run.</summary>
    private static object HglSet(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"));
        var pipes = net.GetPipeIds().Cast<ObjectId>().Select(id => (CivilDb.Pipe)c.Tr.GetObject(id, OpenMode.ForRead))
                       .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        var structures = net.GetStructureIds().Cast<ObjectId>().Select(id => (CivilDb.Structure)c.Tr.GetObject(id, OpenMode.ForRead))
                            .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        int pipeCount = 0, structureCount = 0;
        foreach (var s in ArrParam(p, "pipes", 20000))
        {
            string n = NeedStr(s, "name");
            if (!pipes.TryGetValue(n, out var pipe)) throw new ArgumentException($"hgl_set: pipe '{n}' not in '{net.Name}'.");
            pipe.UpgradeOpen();
            if (DblParam(s, "hgl_up") is double hu) pipe.HydraulicGradeLineUp = hu;
            if (DblParam(s, "hgl_down") is double hd) pipe.HydraulicGradeLineDown = hd;
            if (DblParam(s, "egl_up") is double eu) pipe.EnergyGradeLineUp = eu;
            if (DblParam(s, "egl_down") is double ed) pipe.EnergyGradeLineDown = ed;
            if (DblParam(s, "flow") is double q) pipe.FlowRate = q;
            pipeCount++;
        }
        foreach (var s in ArrParam(p, "structures", 20000))
        {
            string n = NeedStr(s, "name");
            if (!structures.TryGetValue(n, out var st)) throw new ArgumentException($"hgl_set: structure '{n}' not in '{net.Name}'.");
            st.UpgradeOpen();
            if (DblParam(s, "hgl") is double h) st.HydraulicGradeLine = h;
            if (DblParam(s, "egl") is double e) st.EnergyGradeLine = e;
            structureCount++;
        }
        if (pipeCount + structureCount == 0) throw new ArgumentException("hgl_set: give pipes and/or structures.");
        return new { network = net.Name, pipes = pipeCount, structures = structureCount };
    }
}
