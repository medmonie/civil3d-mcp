namespace C3dMcpBridge;

/// <summary>
/// Civil 3D additions of 0.17: alignment station equations / design speeds / widenings, connected alignments,
/// material lists (earthwork quantities), feature line editing, more label types, several profile views at once,
/// user-defined property definitions and point export. Facts and actions only: no design limits are applied.
/// </summary>
internal static class GeometryOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["alignment_set"] = AlignmentSet;
        ops["alignment_connected_create"] = AlignmentConnectedCreate;
        ops["material_list_create"] = MaterialListCreate;
        ops["feature_line_edit"] = FeatureLineEdit;
        ops["labels_place"] = LabelsPlace;
        ops["profile_views_create"] = ProfileViewsCreate;
        ops["udp_create"] = UdpCreate;
        ops["points_export"] = PointsExport;
    }

    // ---------------------------------------------------------------- alignments

    /// <summary>Station equations, design speeds and (offset alignments) widenings.</summary>
    private static object AlignmentSet(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"), OpenMode.ForWrite);
        var result = new Dictionary<string, object?> { ["alignment"] = al.Name };

        if (BoolParam(p, "clear_station_equations") == true)
            while (al.StationEquations.Count > 0) al.StationEquations.Remove(0);
        var eqs = ArrParam(p, "station_equations", 500);
        foreach (var e in eqs)
        {
            double back = NeedDbl(e, "back"), ahead = NeedDbl(e, "ahead");
            var type = Choice(e, "type", "increasing", "increasing", "decreasing") == "decreasing"
                ? CivilDb.StationEquationType.Decreasing : CivilDb.StationEquationType.Increasing;
            al.StationEquations.Add(back, ahead, type);
        }
        result["stationEquations"] = al.StationEquations.Count;

        if (BoolParam(p, "clear_design_speeds") == true)
            while (al.DesignSpeeds.Count > 0) al.DesignSpeeds.Remove(0);
        foreach (var e in ArrParam(p, "design_speeds", 500))
        {
            var ds = al.DesignSpeeds.Add(NeedDbl(e, "station"), NeedPositive(e, "speed"));
            if (StrParam(e, "comment") is string cm) ds.Comment = cm;
        }
        result["designSpeeds"] = al.DesignSpeeds.Count;

        var widenings = ArrParam(p, "widenings", 500);
        if (widenings.Count > 0)
        {
            if (al.AlignmentType != CivilDb.AlignmentType.Offset)
                throw new ArgumentException($"alignment_set: widenings need an offset alignment; '{al.Name}' is {al.AlignmentType}.");
            foreach (var e in widenings)
                al.OffsetAlignmentInfo.AddWidening(NeedDbl(e, "start"), NeedDbl(e, "end"), NeedDbl(e, "offset"));
            result["widenings"] = widenings.Count;
        }
        return result;
    }

    /// <summary>
    /// A connected alignment (curb return) between two alignments, at the stations given, with an arc or a spiral-curve
    /// group. This is the building block of an intersection; the Create Intersection wizard itself is not in the API.
    /// </summary>
    private static object AlignmentConnectedCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Alignment>(c.Tr, AllAlignmentIds(c.Civ, c.Tr), name, "An alignment");
        var inc = FindAlignment(c.Civ, c.Tr, NeedStr(p, "incoming"));
        var outg = FindAlignment(c.Civ, c.Tr, NeedStr(p, "outgoing"));
        var group = EnumParam(p, "curve_group", Autodesk.Civil.CurbReturnCurveGroupType.Arc);
        var cp = new CivilDb.ConnectedAlignmentParams
        {
            IncomingParentAlignmentId = inc.ObjectId,
            OutgoingParentAlignmentId = outg.ObjectId,
            IncomingParentAlignmentStation = NeedDbl(p, "incoming_station"),
            OutgoingParentAlignmentStation = NeedDbl(p, "outgoing_station"),
            CurveRadius = NeedPositive(p, "radius"),
            CurveGroupType = group,
            OffsetIn = DblParam(p, "offset_in") ?? 0,
            OffsetOut = DblParam(p, "offset_out") ?? 0,
            GreaterThan180 = BoolParam(p, "greater_than_180") ?? false,
            SpiralDefinition = AlignmentOps.SpiralTypeParam(p),
        };
        if (DblParam(p, "spiral_in") is double si) cp.SpiralInLength = si;
        if (DblParam(p, "spiral_out") is double so) cp.SpiralOutLength = so;
        // Civil 3D needs a positive overlap with each parent; 1 unit unless given.
        cp.ConnectionOverlapLengthIn = PositiveParam(p, "overlap_in") ?? 1.0;
        cp.ConnectionOverlapLengthOut = PositiveParam(p, "overlap_out") ?? 1.0;

        ObjectId siteId = StrParam(p, "site") is { Length: > 0 } site
            ? TryFindSite(c.Civ, c.Tr, site)?.ObjectId ?? throw new ArgumentException($"Site '{site}' not found.")
            : ObjectId.Null;
        var id = CivilDb.Alignment.CreateConnectedAlignment(name, siteId, c.Layer(p),
            Styles.Id(c.Civ.Styles.AlignmentStyles, StrParam(p, "style"), "alignment style"),
            Styles.Id(c.Civ.Styles.LabelSetStyles.AlignmentLabelSetStyles, StrParam(p, "label_set"), "alignment label set"),
            cp);
        var al = (CivilDb.Alignment)c.Tr.GetObject(id, OpenMode.ForRead);
        return new
        {
            alignment = name, handle = H(id), incoming = inc.Name, outgoing = outg.Name, curveGroup = group.ToString(),
            length = Safe(() => R3(al.Length)), startStation = Safe(() => R3(al.StartingStation)), endStation = Safe(() => R3(al.EndingStation)),
        };
    }

    // ---------------------------------------------------------------- material lists

    /// <summary>
    /// A material list on a sample line group: each material from surfaces (with a condition) and/or corridor shapes.
    /// Civil 3D then computes the material sections; read the quantities with get_material_volumes.
    /// </summary>
    private static object MaterialListCreate(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string groupName = NeedStr(p, "group");
        var g = TryFindSampleLineGroup(c.Tr, al, groupName, OpenMode.ForWrite)
                ?? throw new ArgumentException($"Sample line group '{groupName}' not found on alignment '{al.Name}'. Use list_sample_lines.");
        string name = NeedStr(p, "name");
        string ifExists = Choice(p, "if_exists", "error", "error", "replace");
        var lists = g.MaterialLists;
        bool exists = false;
        foreach (CivilDb.QTOMaterialList l in lists) if (SameName(l.Name, name)) exists = true;
        if (exists && ifExists == "error") throw new ArgumentException($"material_list_create: '{name}' exists on '{g.Name}'; pass if_exists: replace.");
        if (exists) lists.Remove(name);

        if (StrParam(p, "method") is { Length: > 0 })
            lists.VolumeCalculationMethodType = Choice(p, "method", "average_end_area", "average_end_area", "prismoidal", "composite") switch
            {
                "prismoidal" => CivilDb.MaterialVolumeCalculationMethodType.Prismoidal,
                "composite" => CivilDb.MaterialVolumeCalculationMethodType.CompositeVolume,
                _ => CivilDb.MaterialVolumeCalculationMethodType.AverageEndArea,
            };

        // A material can only use surfaces the group samples: turn sampling on for every surface named.
        var sampledNow = new List<string>();
        var wantSurfaces = NeedArr(p, "materials", 50).SelectMany(m => ArrParam(m, "items", 50))
            .Select(it => StrParam(it, "surface")).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var sname in wantSurfaces)
        {
            var sid = FindSurface(c.Civ, c.Tr, sname!).ObjectId;
            bool found = false;
            foreach (CivilDb.SectionSource src in g.GetSectionSources())
                if (src.SourceId == sid)
                {
                    found = true;
                    if (!src.IsSampled) { src.IsSampled = true; sampledNow.Add(sname!); }
                }
            if (!found) throw new ArgumentException($"material_list_create: surface '{sname}' is not a section source of '{g.Name}'.");
        }

        var list = lists.Add(name);
        var made = new List<object>();
        foreach (var m in NeedArr(p, "materials", 50))
        {
            string mName = NeedStr(m, "name");
            var mat = list.Add(mName);
            mat.QuantityType = Choice(m, "type", "earthwork", "cut", "fill", "cut_fill", "earthwork", "structure") switch
            {
                "cut" => CivilDb.MaterialQuantityType.Cut,
                "fill" => CivilDb.MaterialQuantityType.Fill,
                "cut_fill" => CivilDb.MaterialQuantityType.CutFill,
                "structure" => CivilDb.MaterialQuantityType.Structure,
                _ => CivilDb.MaterialQuantityType.Earthwork,
            };
            foreach (var (key, type) in new[] { ("cut_factor", CivilDb.MaterialFactorType.Cut), ("fill_factor", CivilDb.MaterialFactorType.Fill), ("refill_factor", CivilDb.MaterialFactorType.Refill) })
                if (DblParam(m, key) is double f)
                {
                    if (!mat.IsFactorApplicable(type)) throw new ArgumentException($"material_list_create: {key} does not apply to a {mat.QuantityType} material.");
                    mat.SetFactor(type, f);
                }
            var items = new List<object>();
            foreach (var it in NeedArr(m, "items", 50))
            {
                if (StrParam(it, "surface") is { Length: > 0 } sname)
                {
                    var surf = FindSurface(c.Civ, c.Tr, sname);
                    var item = mat.Add(surf.ObjectId);
                    var cond = Choice(it, "condition", "above", "above", "below", "base", "compare", "include", "exclude") switch
                    {
                        "below" => CivilDb.MaterialConditionType.Below,
                        "base" => CivilDb.MaterialConditionType.Base,
                        "compare" => CivilDb.MaterialConditionType.Compare,
                        "include" => CivilDb.MaterialConditionType.Include,
                        "exclude" => CivilDb.MaterialConditionType.Exclude,
                        _ => CivilDb.MaterialConditionType.Above,
                    };
                    if (!item.IsConditionApplicable(cond)) throw new ArgumentException($"material_list_create: condition {cond} does not apply to a {mat.QuantityType} material.");
                    item.Condition = cond;
                    items.Add(new { surface = surf.Name, condition = cond.ToString() });
                }
                else if (StrParam(it, "corridor") is { Length: > 0 } cname)
                {
                    var cor = FindCorridor(c.Civ, c.Tr, cname);
                    string shape = NeedStr(it, "shape");
                    mat.Add(cor.ObjectId, shape);
                    items.Add(new { corridor = cor.Name, shape });
                }
                else throw new ArgumentException("material_list_create: each item needs 'surface' (with 'condition') or 'corridor' + 'shape'.");
            }
            made.Add(new { name = mName, type = mat.QuantityType.ToString(), items });
        }
        return new
        {
            alignment = al.Name, group = g.Name, materialList = name, guid = list.Guid, materials = made, sampledNow,
            method = lists.VolumeCalculationMethodType.ToString(),
            note = "Read the areas and volumes with get_material_volumes.",
        };
    }

    // ---------------------------------------------------------------- feature lines

    internal static CivilDb.FeatureLine FindFeatureLine(Database db, Transaction tr, JsonElement p, OpenMode mode)
    {
        if (StrParam(p, "handle") is { Length: > 0 } h)
            return tr.GetObject(IdFromHandle(db, h), mode) as CivilDb.FeatureLine
                   ?? throw new ArgumentException($"Handle {h} is not a feature line.");
        string name = StrParam(p, "feature_line") ?? StrParam(p, "name") ?? throw new ArgumentException("Give 'handle' or 'feature_line' (name).");
        foreach (var id in ModelSpaceIdsByDxf(db, tr, "AECC_FEATURE_LINE"))
            if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.FeatureLine fl && SameName(fl.Name, name))
                return (CivilDb.FeatureLine)tr.GetObject(id, mode);
        throw new ArgumentException($"Feature line '{name}' not found. Use list_feature_lines.");
    }

    /// <summary>Elevations from a surface, elevations at given vertices, PIs and elevation points added or removed, curve radii.</summary>
    private static object FeatureLineEdit(EditContext c, JsonElement p)
    {
        var fl = FindFeatureLine(c.Db, c.Tr, p, OpenMode.ForWrite);
        var done = new List<string>();

        foreach (var pt in Has(p, "delete_pis") ? PtList(p, "delete_pis", "feature_line_edit", 1, 5000) : new())
            fl.DeletePIPoint(Nearest(fl, pt, Autodesk.Civil.FeatureLinePointType.PIPoint));
        foreach (var pt in Has(p, "delete_elevation_points") ? PtList(p, "delete_elevation_points", "feature_line_edit", 1, 5000) : new())
            fl.DeleteElevationPoint(Nearest(fl, pt, Autodesk.Civil.FeatureLinePointType.ElevationPoint));
        if (Has(p, "delete_pis") || Has(p, "delete_elevation_points")) done.Add("deleted points");

        foreach (var pt in Has(p, "insert_pis") ? PtList(p, "insert_pis", "feature_line_edit", 1, 5000) : new())
            fl.InsertPIPoint(pt);
        foreach (var pt in Has(p, "insert_elevation_points") ? PtList(p, "insert_elevation_points", "feature_line_edit", 1, 5000) : new())
            fl.InsertElevationPoint(pt);
        if (Has(p, "insert_pis") || Has(p, "insert_elevation_points")) done.Add("inserted points");

        if (StrParam(p, "from_surface") is { Length: > 0 } sname)
        {
            var surf = FindSurface(c.Civ, c.Tr, sname);
            fl.AssignElevationsFromSurface(surf.ObjectId, BoolParam(p, "include_intermediate") ?? true);
            done.Add($"elevations from {surf.Name}");
        }
        foreach (var e in ArrParam(p, "elevations", 5000))
        {
            int i = IntParam(e, "index", -1, 0, 100000);
            fl.SetPointElevation(i, NeedDbl(e, "elevation"));
        }
        if (Has(p, "elevations")) done.Add("elevations set");
        if (DblParam(p, "raise") is double dz)
        {
            var all = fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints).Cast<Point3d>().ToList();
            for (int i = 0; i < all.Count; i++) fl.SetPointElevation(i, all[i].Z + dz);
            done.Add($"raised {dz}");
        }
        foreach (var e in ArrParam(p, "radii", 5000))
            fl.SetCurveRadius(IntParam(e, "index", -1, 0, 100000), NeedPositive(e, "radius"));
        if (Has(p, "radii")) done.Add("radii set");

        return new
        {
            featureLine = Safe(() => fl.Name), handle = H(fl.ObjectId), done,
            points = Safe(() => fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints).Count),
            minElevation = Safe(() => R3(fl.MinElevation)), maxElevation = Safe(() => R3(fl.MaxElevation)),
            minGrade = Safe(() => R3(fl.MinGrade * 100)), maxGrade = Safe(() => R3(fl.MaxGrade * 100)),
        };
    }

    /// <summary>The feature line point of the given kind nearest (in plan) to a requested point.</summary>
    private static Point3d Nearest(CivilDb.FeatureLine fl, Point3d near, Autodesk.Civil.FeatureLinePointType kind)
    {
        var pts = fl.GetPoints(kind).Cast<Point3d>().ToList();
        if (pts.Count == 0) throw new ArgumentException($"feature_line_edit: the feature line has no {kind} points.");
        var best = pts.OrderBy(q => Math.Pow(q.X - near.X, 2) + Math.Pow(q.Y - near.Y, 2)).First();
        if (Math.Sqrt(Math.Pow(best.X - near.X, 2) + Math.Pow(best.Y - near.Y, 2)) > 0.01)
            throw new ArgumentException($"feature_line_edit: no {kind} within 0.01 of ({near.X}, {near.Y}).");
        return best;
    }

    // ---------------------------------------------------------------- labels

    private static ObjectId LabelStyle(EditContext c, JsonElement p, CivilStyles.LabelStyleCollection styles, string what) =>
        Styles.Id(styles, StrParam(p, "style"), what);

    private static ObjectId Marker(EditContext c, JsonElement p) =>
        Styles.Id(c.Civ.Styles.MarkerStyles, StrParam(p, "marker_style"), "marker style");

    /// <summary>One label request: kind decides what it labels and which parameters it reads.</summary>
    private static object LabelsPlace(EditContext c, JsonElement p)
    {
        var labels = c.Civ.Styles.LabelStyles;
        var made = new List<object>();
        foreach (var e in NeedArr(p, "labels", 2000))
        {
            string kind = Choice(e, "kind", "", "station_offset", "alignment_geometry", "surface_spot", "surface_slope",
                "surface_contours", "parcel_area", "profile_pvi", "profile_crest", "profile_sag", "profile_tangent",
                "station_elevation", "note");
            var ids = new List<ObjectId>();
            switch (kind)
            {
                case "station_offset":
                {
                    var al = FindAlignment(c.Civ, c.Tr, NeedStr(e, "alignment"));
                    var at = NeedPt(e, "at", "station_offset label");
                    ids.Add(CivilDb.StationOffsetLabel.Create(al.ObjectId,
                        LabelStyle(c, e, labels.AlignmentLabelStyles.StationOffsetLabelStyles, "station offset label style"),
                        Marker(c, e), new Point2d(at.X, at.Y)));
                    break;
                }
                case "alignment_geometry":
                {
                    // a label on every line, curve and spiral of the alignment
                    var al = FindAlignment(c.Civ, c.Tr, NeedStr(e, "alignment"));
                    var lineStyle = Styles.Id(labels.AlignmentLabelStyles.LineLabelStyles, StrParam(e, "line_style"), "alignment line label style");
                    var curveStyle = Styles.Id(labels.AlignmentLabelStyles.CurveLabelStyles, StrParam(e, "curve_style"), "alignment curve label style");
                    var spiralStyle = Styles.Id(labels.AlignmentLabelStyles.SpiralLabelStyles, StrParam(e, "spiral_style"), "alignment spiral label style");
                    for (int i = 0; i < al.Entities.Count; i++)
                    {
                        var ent = al.Entities.GetEntityByOrder(i);
                        for (int k = 0; k < ent.SubEntityCount; k++)
                        {
                            try
                            {
                                switch (ent[k])
                                {
                                    case CivilDb.AlignmentSubEntityLine ln: ids.Add(CivilDb.AlignmentTangentLabel.Create(ln, lineStyle)); break;
                                    case CivilDb.AlignmentSubEntityArc arc: ids.Add(CivilDb.AlignmentCurveLabel.Create(arc, curveStyle)); break;
                                    case CivilDb.AlignmentSubEntitySpiral sp: ids.Add(CivilDb.AlignmentSpiralLabel.Create(sp, spiralStyle)); break;
                                }
                            }
                            catch (Exception ex) { c.Warn($"alignment_geometry label on entity {i + 1}: {ex.Message}"); }
                        }
                    }
                    break;
                }
                case "surface_spot":
                {
                    var s = FindSurface(c.Civ, c.Tr, NeedStr(e, "surface"));
                    var style = LabelStyle(c, e, labels.SurfaceLabelStyles.SpotElevationLabelStyles, "spot elevation label style");
                    var marker = Marker(c, e);
                    foreach (var at in PtList(e, "points", "surface_spot label", 1, 5000))
                        ids.Add(CivilDb.SurfaceElevationLabel.Create(s.ObjectId, new Point2d(at.X, at.Y), style, marker));
                    break;
                }
                case "surface_slope":
                {
                    var s = FindSurface(c.Civ, c.Tr, NeedStr(e, "surface"));
                    var style = LabelStyle(c, e, labels.SurfaceLabelStyles.SlopeLabelStyles, "slope label style");
                    if (Has(e, "from"))
                    {
                        Point3d a = NeedPt(e, "from", "surface_slope label"), b = NeedPt(e, "to", "surface_slope label");
                        ids.Add(CivilDb.SurfaceSlopeLabel.Create(s.ObjectId, new Point2d(a.X, a.Y), new Point2d(b.X, b.Y), style));
                    }
                    else
                        foreach (var at in PtList(e, "points", "surface_slope label", 1, 5000))
                            ids.Add(CivilDb.SurfaceSlopeLabel.Create(s.ObjectId, new Point2d(at.X, at.Y), style));
                    break;
                }
                case "surface_contours":
                {
                    var s = FindSurface(c.Civ, c.Tr, NeedStr(e, "surface"));
                    var line = PtList(e, "line", "surface_contours label", 2, 1000);
                    if (DblParam(e, "interval") is double iv && iv > 0)
                    {
                        CivilDb.SurfaceContourLabelGroup.CreateMultipleAtInterval(s.ObjectId, new Point2d(line[0].X, line[0].Y),
                            new Point2d(line[^1].X, line[^1].Y), iv);
                        made.Add(new { kind, surface = s.Name, interval = iv });
                        continue;
                    }
                    var pc = new Point2dCollection(line.Select(q => new Point2d(q.X, q.Y)).ToArray());
                    ids.Add(CivilDb.SurfaceContourLabelGroup.Create(s.ObjectId, pc));
                    break;
                }
                case "parcel_area":
                {
                    var style = LabelStyle(c, e, labels.ParcelLabelStyles.AreaLabelStyles, "parcel area label style");
                    var want = Has(e, "parcels") ? StrList(e, "parcels", 5000).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
                    foreach (ObjectId sid in c.Civ.GetSiteIds())
                    {
                        var site = (CivilDb.Site)c.Tr.GetObject(sid, OpenMode.ForRead);
                        foreach (ObjectId pid in site.GetParcelIds())
                        {
                            var par = (CivilDb.Parcel)c.Tr.GetObject(pid, OpenMode.ForRead);
                            if (want == null || want.Contains(par.Name)) ids.Add(CivilDb.ParcelAreaLabel.Create(pid, style));
                        }
                    }
                    break;
                }
                case "profile_pvi" or "profile_crest" or "profile_sag" or "profile_tangent":
                {
                    var pv = FindProfileView(c.Civ, c.Tr, NeedStr(e, "profile_view"));
                    var al = (CivilDb.Alignment)c.Tr.GetObject(pv.AlignmentId, OpenMode.ForRead);
                    var pr = FindProfile(c.Tr, al, NeedStr(e, "profile"));
                    var pl = labels.ProfileLabelStyles;
                    ids.Add(kind switch
                    {
                        "profile_pvi" => CivilDb.ProfilePVILabelGroup.Create(pv.ObjectId, pr.ObjectId, LabelStyle(c, e, pl.GradeBreakLabelStyles, "profile grade break label style")),
                        "profile_crest" => CivilDb.ProfileCrestCurveLabelGroup.Create(pv.ObjectId, pr.ObjectId, LabelStyle(c, e, pl.CurveLabelStyles, "profile curve label style")),
                        "profile_sag" => CivilDb.ProfileSagCurveLabelGroup.Create(pv.ObjectId, pr.ObjectId, LabelStyle(c, e, pl.CurveLabelStyles, "profile curve label style")),
                        _ => CivilDb.ProfileLineLabelGroup.Create(pv.ObjectId, pr.ObjectId, LabelStyle(c, e, pl.LineLabelStyles, "profile line label style")),
                    });
                    break;
                }
                case "station_elevation":
                {
                    var pv = FindProfileView(c.Civ, c.Tr, NeedStr(e, "profile_view"));
                    ids.Add(CivilDb.StationElevationLabel.Create(pv.ObjectId,
                        LabelStyle(c, e, labels.ProfileViewLabelStyles.StationElevationLabelStyles, "station elevation label style"),
                        Marker(c, e), NeedDbl(e, "station"), NeedDbl(e, "elevation")));
                    break;
                }
                case "note":
                {
                    var at = NeedPt(e, "at", "note label");
                    var id = CivilDb.NoteLabel.Create(at, LabelStyle(c, e, labels.GeneralNoteLabelStyles, "note label style"), Marker(c, e));
                    ids.Add(id);
                    break;
                }
            }
            made.Add(new { kind, count = ids.Count, handles = ids.Take(50).Select(H).ToList() });
        }
        return new { labels = made };
    }

    // ---------------------------------------------------------------- profile views

    /// <summary>Several profile views of one alignment, each covering 'length' of stations, laid out in rows or columns.</summary>
    private static object ProfileViewsCreate(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var opts = new CivilDb.MultipleProfileViewsCreationOptions
        {
            LengthOfEachView = NeedPositive(p, "length"),
            MaxViewInRowOrColumn = IntParam(p, "per_row", 5, 1, 1000),
            GapBetweenViewsInRow = DblParam(p, "gap_x") ?? 50,
            GapBetweenViewsInColumn = DblParam(p, "gap_y") ?? 50,
            DrawOrder = Choice(p, "order", "rows", "rows", "columns") == "columns" ? Autodesk.Civil.ProfileViewPlotType.ByColumns : Autodesk.Civil.ProfileViewPlotType.ByRows,
            StartCorner = EnumParam(p, "start_corner", Autodesk.Civil.ProfileViewStartCornerType.UpperLeft),
        };
        var bandSet = AlignmentOps.BandSetFor(c, StrParam(p, "band_set"));
        var style = Styles.Id(c.Civ.Styles.ProfileViewStyles, StrParam(p, "style"), "profile view style");
        string name = StrParam(p, "name") ?? al.Name + " - PV";
        var ids = CivilDb.ProfileView.CreateMultiple(al.ObjectId, NeedPt(p, "at", "profile_views_create"), name, bandSet, style, opts);
        var views = new List<object>();
        foreach (ObjectId id in ids)
        {
            var pv = (CivilDb.ProfileView)c.Tr.GetObject(id, OpenMode.ForWrite);
            Safe(() => AlignmentOps.FillBandData(c, pv, al));
            views.Add(new { name = pv.Name, handle = H(id), startStation = Safe(() => R3(pv.StationStart)), endStation = Safe(() => R3(pv.StationEnd)) });
        }
        return new { alignment = al.Name, count = views.Count, views };
    }

    // ---------------------------------------------------------------- user-defined properties

    /// <summary>A point or parcel user-defined property (classification created if missing).</summary>
    private static object UdpCreate(EditContext c, JsonElement p)
    {
        string target = Choice(p, "for", "point", "point", "parcel");
        var classes = target == "parcel" ? c.Civ.ParcelUDPClassifications : c.Civ.PointUDPClassifications;
        string clsName = NeedStr(p, "classification");
        CivilDb.UDPClassification? cls = null;
        foreach (CivilDb.UDPClassification k in classes) if (SameName(k.Name, clsName)) cls = k;
        bool newClass = cls == null;
        cls ??= classes.Add(clsName);

        var made = new List<object>();
        foreach (var e in NeedArr(p, "properties", 200))
        {
            string name = NeedStr(e, "name");
            string type = Choice(e, "type", "string", "string", "double", "integer", "boolean", "enum");
            CivilDb.AttributeTypeInfo info = type switch
            {
                "double" => new CivilDb.AttributeTypeInfoDouble(name, EnumParam(e, "data_type", CivilDb.AttributeTypeInfoDoubleDataType.Double)),
                "integer" => new CivilDb.AttributeTypeInfoInt(name),
                "boolean" => new CivilDb.AttributeTypeInfoBool(name),
                "enum" => new CivilDb.AttributeTypeInfoEnum(name, StrList(e, "values", 500)),
                _ => new CivilDb.AttributeTypeInfoString(name),
            };
            if (StrParam(e, "description") is string d) info.Description = d;
            if (Has(e, "default"))
            {
                info.UseDefaultValue = true;
                var dv = e.GetProperty("default");
                switch (info)
                {
                    case CivilDb.AttributeTypeInfoDouble di: di.DefaultValue = dv.GetDouble(); break;
                    case CivilDb.AttributeTypeInfoInt ii: ii.DefaultValue = dv.GetInt32(); break;
                    case CivilDb.AttributeTypeInfoBool bi: bi.DefaultValue = dv.GetBoolean(); break;
                    case CivilDb.AttributeTypeInfoEnum ei: ei.DefaultValue = Text(dv); break;
                    case CivilDb.AttributeTypeInfoString si: si.DefaultValue = Text(dv); break;
                }
            }
            if (DblParam(e, "min") is double lo)
            {
                if (info is CivilDb.AttributeTypeInfoDouble dd) { dd.LowerBoundValue = lo; dd.LowerBoundInclusive = true; }
                else if (info is CivilDb.AttributeTypeInfoInt id2) { id2.LowerBoundValue = (int)lo; id2.LowerBoundInclusive = true; }
            }
            if (DblParam(e, "max") is double hi)
            {
                if (info is CivilDb.AttributeTypeInfoDouble dd) { dd.UpperBoundValue = hi; dd.UpperBoundInclusive = true; }
                else if (info is CivilDb.AttributeTypeInfoInt id2) { id2.UpperBoundValue = (int)hi; id2.UpperBoundInclusive = true; }
            }
            switch (info)
            {
                case CivilDb.AttributeTypeInfoDouble x: cls.CreateUDP(x); break;
                case CivilDb.AttributeTypeInfoInt x: cls.CreateUDP(x); break;
                case CivilDb.AttributeTypeInfoBool x: cls.CreateUDP(x); break;
                case CivilDb.AttributeTypeInfoEnum x: cls.CreateUDP(x); break;
                case CivilDb.AttributeTypeInfoString x: cls.CreateUDP(x); break;
            }
            made.Add(new { name, type });
        }
        return new { @for = target, classification = cls.Name, newClassification = newClass, properties = made };
    }

    // ---------------------------------------------------------------- point export

    /// <summary>Write COGO points (all, or one point group) to a file in a point file format. Runs after the edit commits.</summary>
    private static object PointsExport(EditContext c, JsonElement p)
    {
        string path = Path.GetFullPath(NeedStr(p, "path"));
        if (File.Exists(path) && BoolParam(p, "overwrite") != true)
            throw new ArgumentException($"points_export: {path} exists; pass overwrite: true.");
        string? dir = Path.GetDirectoryName(path);
        if (dir == null || !Directory.Exists(dir)) throw new ArgumentException($"points_export: folder not found: {dir}");
        var formats = CivilDb.PointFileFormatCollection.GetPointFileFormats(c.Db);
        string formatName = NeedStr(p, "format");
        CivilDb.PointFileFormat format;
        try { format = formats[formatName]; }
        catch { throw new ArgumentException($"points_export: point file format '{formatName}' not found. Use list_point_file_formats."); }

        ObjectId groupId = ObjectId.Null;
        if (StrParam(p, "point_group") is { Length: > 0 } g)
        {
            foreach (ObjectId id in c.Civ.PointGroups)
                if (SameName(((CivilDb.PointGroup)c.Tr.GetObject(id, OpenMode.ForRead)).Name, g)) groupId = id;
            if (groupId.IsNull) throw new ArgumentException($"Point group '{g}' not found. Use list_point_groups.");
        }
        c.AfterCommit.Add(() =>
        {
            if (groupId.IsNull) CivilDb.CogoPointCollection.ExportPoints(path, format);
            else CivilDb.CogoPointCollection.ExportPoints(path, format, groupId);
        });
        return new { path, format = formatName, pointGroup = StrParam(p, "point_group"), note = "Written after the edit commits." };
    }
}
