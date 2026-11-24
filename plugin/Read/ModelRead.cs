namespace C3dMcpBridge;

/// <summary>
/// Reads added in 0.19: corridor feature lines and codes over a station range, corridor targets and slope patterns,
/// catchments.
/// </summary>
internal static class ModelRead
{
    // ---------------------------------------------------------------- corridor feature lines (#53)

    /// <summary>The baseline by name or by its alignment's name; the first one when no name is given.</summary>
    internal static CivilDb.Baseline Baseline(Transaction tr, CivilDb.Corridor cor, string? name)
    {
        foreach (CivilDb.Baseline b in cor.Baselines)
            if (name == null || SameName(b.Name, name) || SameName(NameOf(tr, () => b.AlignmentId) as string, name)) return b;
        throw new ArgumentException($"Corridor '{cor.Name}' has no baseline '{name}'. Use list_corridors.");
    }

    /// <summary>Every [x] of a list thinned to at most 'max' items, keeping the first and the last.</summary>
    private static List<T> Thin<T>(List<T> items, int max)
    {
        if (items.Count <= max || max < 2) return items;
        double step = (items.Count - 1) / (double)(max - 1);
        return Enumerable.Range(0, max).Select(i => items[(int)Math.Round(i * step)]).ToList();
    }

    /// <summary>
    /// Feature lines of a corridor baseline by code, cut to a station range: point list (station, offset, elevation, x, y),
    /// side, plan and 3D length, elevation range. Also the point / link / shape codes the sections in that range carry.
    /// </summary>
    public static object GetCorridorFeatureLines(JsonElement p) => WithRead((db, tr) =>
    {
        var cor = FindCorridor(Civ, tr, NeedStr(p, "corridor"));
        var bl = Baseline(tr, cor, StrParam(p, "baseline"));
        double from = DblParam(p, "from_station") ?? double.NegativeInfinity;
        double to = DblParam(p, "to_station") ?? double.PositiveInfinity;
        if (to < from) throw new ArgumentException("get_corridor_feature_lines: to_station is before from_station.");
        var only = Has(p, "codes") ? StrList(p, "codes", 200).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        bool withPoints = BoolParam(p, "points") ?? true;
        int maxPoints = IntParam(p, "max_points", 500, 2, 20000);
        int limit = IntParam(p, "limit", 200, 1, 2000);

        var sets = new List<(string baseline, CivilDb.BaselineFeatureLines fl)> { (bl.Name, bl.MainBaselineFeatureLines) };
        if (BoolParam(p, "offset_baselines") == true)
            try { foreach (CivilDb.BaselineFeatureLines o in bl.OffsetBaselineFeatureLinesCol) sets.Add((Safe(() => NameOf(tr, () => o.OffsetAlignmentId))?.ToString() ?? "offset", o)); }
            catch { /* no offset baselines */ }

        var lines = new List<object>();
        int total = 0;
        foreach (var (owner, set) in sets)
        {
            var map = set.FeatureLineCollectionMap;
            foreach (string code in map.CodeNames())
            {
                if (only != null && !only.Contains(code)) continue;
                var col = map[code];
                for (int i = 0; i < col.Count; i++)
                {
                    var pts = col[i].FeatureLinePoints.Cast<CivilDb.FeatureLinePoint>()
                        .Where(fp => fp.Station >= from - 1e-6 && fp.Station <= to + 1e-6).ToList();
                    if (pts.Count == 0) continue;
                    if (total++ >= limit) continue;
                    double len2 = 0, len3 = 0;
                    for (int k = 1; k < pts.Count; k++)
                    {
                        Point3d a = pts[k - 1].XYZ, b = pts[k].XYZ;
                        len2 += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                        len3 += a.DistanceTo(b);
                    }
                    double meanOffset = pts.Average(fp => fp.Offset);
                    lines.Add(new
                    {
                        baseline = owner, code, index = i,
                        side = meanOffset < -1e-6 ? "left" : meanOffset > 1e-6 ? "right" : "on baseline",
                        startStation = R3(pts[0].Station), endStation = R3(pts[^1].Station), pointCount = pts.Count,
                        length2d = R3(len2), length3d = R3(len3),
                        minOffset = R3(pts.Min(fp => fp.Offset)), maxOffset = R3(pts.Max(fp => fp.Offset)),
                        minElevation = R3(pts.Min(fp => fp.XYZ.Z)), maxElevation = R3(pts.Max(fp => fp.XYZ.Z)),
                        style = Safe(() => col[i].StyleName),
                        points = withPoints
                            ? Thin(pts, maxPoints).Select(fp => new[] { R3(fp.Station), R3(fp.Offset), R3(fp.XYZ.Z), R3(fp.XYZ.X), R3(fp.XYZ.Y) }).ToList()
                            : null,
                    });
                }
            }
        }

        // codes carried by the sections (frequency stations) inside the range
        var pointCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var linkCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var shapeCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int stations = 0;
        foreach (double st in bl.SortedStations())
        {
            if (st < from - 1e-6 || st > to + 1e-6) continue;
            stations++;
            try
            {
                var aa = bl.GetAppliedAssemblyAtStation(st);
                foreach (CivilDb.CalculatedPoint cp in aa.Points) foreach (string c in cp.CorridorCodes) pointCodes.Add(c);
                foreach (CivilDb.CalculatedLink cl in aa.Links) foreach (string c in cl.CorridorCodes) linkCodes.Add(c);
                foreach (CivilDb.CalculatedShape cs in aa.Shapes) foreach (string c in cs.CorridorCodes) shapeCodes.Add(c);
            }
            catch { /* station without an applied assembly (gap between regions) */ }
        }
        var codeInfo = new List<object>();
        try
        {
            foreach (CivilDb.FeatureLineCodeInfo ci in cor.FeatureLineCodeInfos)
                codeInfo.Add(new { code = ci.CodeName, connected = Safe(() => ci.IsConnected), payItems = Safe(() => ci.PayItems) });
        }
        catch { /* older corridors */ }

        return new
        {
            corridor = cor.Name, baseline = bl.Name, alignment = NameOf(tr, () => bl.AlignmentId),
            fromStation = double.IsFinite(from) ? R3(from) : (double?)null, toStation = double.IsFinite(to) ? R3(to) : (double?)null,
            featureLineCount = total, returned = lines.Count, truncated = total > lines.Count, featureLines = lines,
            sectionStations = stations,
            codesInRange = new { points = pointCodes.ToList(), links = linkCodes.ToList(), shapes = shapeCodes.ToList() },
            featureLineCodes = codeInfo,
            note = "points are [station, offset, elevation, x, y]; offset to the baseline, negative = left. index: position among the " +
                   "feature lines of that code (use it with corridor_feature_line_export).",
        };
    });

    // ---------------------------------------------------------------- corridor targets and slope patterns

    private static object TargetName(Transaction tr, ObjectId id)
    {
        if (id.IsNull || id.IsErased) return "(none)";
        try
        {
            var o = tr.GetObject(id, OpenMode.ForRead);
            string? name = o is CivilDb.Entity ce ? Safe(() => ce.Name) as string : null;
            return new { type = DxfOrName(id), name, handle = H(id) };
        }
        catch { return new { type = DxfOrName(id), name = (string?)null, handle = H(id) }; }
    }

    internal static List<object> Targets(Transaction tr, CivilDb.BaselineRegion rg) =>
        rg.GetTargets().Cast<CivilDb.SubassemblyTargetInfo>().Select(t => (object)new
        {
            subassembly = Safe(() => t.SubassemblyName), assemblyGroup = Safe(() => t.AssemblyGroupName),
            target = Safe(() => t.DisplayName), logicalName = Safe(() => t.LogicalName), type = Safe(() => t.TargetType.ToString()),
            option = Safe(() => t.TargetToOption.ToString()),
            objects = t.TargetIds.Cast<ObjectId>().Select(id => TargetName(tr, id)).ToList(),
        }).ToList();

    /// <summary>Per region: the subassembly targets (surface, width / offset, elevation) and what they point at; slope patterns.</summary>
    public static object GetCorridorTargets(JsonElement p) => WithRead((db, tr) =>
    {
        var cor = FindCorridor(Civ, tr, NeedStr(p, "corridor"));
        string? only = StrParam(p, "baseline");
        string? region = StrParam(p, "region");
        var baselines = new List<object>();
        foreach (CivilDb.Baseline b in cor.Baselines)
        {
            if (only != null && !SameName(b.Name, only) && !SameName(NameOf(tr, () => b.AlignmentId) as string, only)) continue;
            var regions = new List<object>();
            foreach (CivilDb.BaselineRegion rg in b.BaselineRegions)
            {
                if (region != null && !SameName(rg.Name, region)) continue;
                regions.Add(new { name = rg.Name, startStation = R3(rg.StartStation), endStation = R3(rg.EndStation), targets = Safe(() => Targets(tr, rg)) });
            }
            baselines.Add(new { name = b.Name, alignment = NameOf(tr, () => b.AlignmentId), regions });
        }
        var patterns = new List<object>();
        try
        {
            foreach (CivilDb.CorridorSlopePattern sp in cor.SlopePatterns)
                patterns.Add(new
                {
                    from = Safe(() => sp.FeatureLine1.CodeName), to = Safe(() => sp.FeatureLine2.CodeName),
                    style = Safe(() => sp.StyleName), startStation = Safe(() => R3(sp.StartStation)), endStation = Safe(() => R3(sp.EndStation)),
                });
        }
        catch { /* none */ }
        return new { corridor = cor.Name, baselines, slopePatterns = patterns,
                     note = "Set targets with corridor_targets_set (target = the 'target' or 'logicalName' shown here)." };
    });

    // ---------------------------------------------------------------- catchments

    /// <summary>Catchment groups and catchments: area, perimeter, impervious area, runoff coefficient, flow path, time of concentration.</summary>
    public static object ListCatchments(JsonElement p) => WithRead((db, tr) =>
    {
        string? only = StrParam(p, "group");
        bool boundary = BoolParam(p, "boundary") ?? false;
        bool flowPath = BoolParam(p, "flow_path") ?? false;
        int limit = IntParam(p, "limit", 200, 1, 5000);
        var groups = new List<object>();
        int total = 0, returned = 0;
        var col = Civ.GetCatchmentGroups();
        foreach (ObjectId gid in col)
        {
            var g = (CivilDb.CatchmentGroup)tr.GetObject(gid, OpenMode.ForRead);
            if (only != null && !SameName(g.Name, only)) continue;
            var rows = new List<object>();
            foreach (ObjectId cid in g.GetAllCatchmentIds())
            {
                if (total++ >= limit) continue;
                returned++;
                var c = (CivilDb.Catchment)tr.GetObject(cid, OpenMode.ForRead);
                rows.Add(Catchment(tr, c, boundary, flowPath));
            }
            groups.Add(new { name = g.Name, handle = H(gid), catchments = rows });
        }
        return new { total, returned, truncated = total > returned, groups,
                     note = "Areas in square drawing units, lengths in drawing units, slopes as Civil 3D reports them, times in Civil 3D's units (minutes)." };
    });

    /// <summary>A property that only newer releases have (2027: RunoffMethod, FlowPathLength, AverageSlope); null when missing.</summary>
    private static object? Later(object o, string name)
    {
        var prop = o.GetType().GetProperty(name);
        if (prop == null) return null;
        try { return prop.GetValue(o) switch { double d => R3(d), null => null, var v => v.ToString() }; }
        catch { return null; }   // not set for this catchment
    }

    internal static object Catchment(Transaction tr, CivilDb.Catchment c, bool boundary, bool flowPath) => new
    {
        name = c.Name, handle = H(c.ObjectId), style = Safe(() => c.StyleName),
        area2d = Safe(() => R3(c.Area2d)), perimeter2d = Safe(() => R3(c.Perimeter2d)),
        imperviousPercent = Safe(() => R3(c.ImperviousArea * 100.0)), runoffCoefficient = Safe(() => R3(c.RunoffCoefficient)),
        runoffMethod = Later(c, "RunoffMethod"),
        flowPathLength = Later(c, "FlowPathLength"), averageSlope = Later(c, "AverageSlope"),
        timeOfConcentration = Safe(() => R3(c.TimeOfConcentration)),
        dischargePoint = Safe(() => P3(c.DischargePoint)),
        mostDistantPoint = Safe(() => P3(c.HydrologicallyMostDistantPoint)), mostDistantLength = Safe(() => R3(c.HydrologicallyMostDistantLength)),
        surface = Safe(() => c.ReferenceSurfaceName), dischargeStructure = NameOf(tr, () => c.ReferencePipeNetworkStructureId),
        boundary = boundary ? Safe(() => c.BoundaryPolyline3d.Cast<Point3d>().Select(P3).ToList()) : null,
        flowPath = flowPath ? Safe(() => c.GetFlowPath().GetPath().Cast<Point3d>().Select(P3).ToList()) : null,
    };
}
