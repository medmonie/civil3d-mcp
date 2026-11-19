namespace C3dMcpBridge;

/// <summary>Ground data: COGO points, TIN surfaces (points, breaklines, boundaries, volumes) and feature lines.</summary>
internal static class TerrainOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["cogo_points_add"] = CogoPointsAdd;
        ops["cogo_point_set"] = CogoPointSet;
        ops["cogo_points_remove"] = CogoPointsRemove;
        ops["surface_create"] = SurfaceCreate;
        ops["surface_add_points"] = SurfaceAddPoints;
        ops["surface_add_breaklines"] = SurfaceAddBreaklines;
        ops["surface_add_boundary"] = SurfaceAddBoundary;
        ops["surface_rebuild"] = SurfaceRebuild;
        ops["surface_volume_create"] = SurfaceVolumeCreate;
        ops["feature_line_create"] = FeatureLineCreate;
    }

    // ---------------------------------------------------------------- COGO points

    private static uint PointNumber(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out var n) && n > 0
            ? n
            : throw new ArgumentException("A point number must be a positive whole number.");

    private static object CogoPointsAdd(EditContext c, JsonElement p)
    {
        var specs = NeedArr(p, "points", 20000);
        var numbers = new List<uint>();
        for (int i = 0; i < specs.Count; i++)
        {
            var s = specs[i];
            var at = CheckedPoint(NeedDbl(s, "x"), NeedDbl(s, "y"), DblParam(s, "z") ?? 0.0, $"point {i + 1}");
            var id = c.Civ.CogoPoints.Add(at, StrParam(s, "description") ?? "", true);
            var cp = (CivilDb.CogoPoint)c.Tr.GetObject(id, OpenMode.ForWrite);
            if (StrParam(s, "name") is { Length: > 0 } name) cp.PointName = name;
            numbers.Add(cp.PointNumber);
        }
        return new { added = numbers.Count, firstNumber = numbers.First(), lastNumber = numbers.Last() };
    }

    private static object CogoPointSet(EditContext c, JsonElement p)
    {
        uint number = Has(p, "number") ? PointNumber(p.GetProperty("number")) : throw new ArgumentException("Missing parameter 'number'.");
        ObjectId id;
        try { id = c.Civ.CogoPoints.GetPointByPointNumber(number); }
        catch { throw new ArgumentException($"COGO point {number} does not exist."); }
        var cp = (CivilDb.CogoPoint)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (DblParam(p, "x") is double x) cp.Easting = x;
        if (DblParam(p, "y") is double y) cp.Northing = y;
        if (DblParam(p, "z") is double z) cp.Elevation = z;
        if (StrParam(p, "description") is string d) cp.RawDescription = d;
        if (StrParam(p, "name") is string n) cp.PointName = n;
        return new { number, easting = R3(cp.Easting), northing = R3(cp.Northing), elevation = R3(cp.Elevation) };
    }

    private static object CogoPointsRemove(EditContext c, JsonElement p)
    {
        var numbers = NeedArr(p, "numbers", 20000).Select(PointNumber).ToList();
        foreach (var n in numbers)
        {
            try { c.Civ.CogoPoints.Remove(n); }
            catch { throw new ArgumentException($"COGO point {n} does not exist."); }
        }
        return new { removed = numbers.Count };
    }

    // ---------------------------------------------------------------- TIN surfaces

    private static void RequireNewSurfaceName(EditContext c, string name) =>
        RequireNewName<CivilDb.Surface>(c.Tr, c.Civ.GetSurfaceIds(), name, "A surface");

    private static object SurfaceCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewSurfaceName(c, name);
        var id = Has(p, "style")
            ? CivilDb.TinSurface.Create(name, Styles.Id(c.Civ.Styles.SurfaceStyles, StrParam(p, "style"), "surface style"))
            : CivilDb.TinSurface.Create(c.Db, name);
        var surf = (CivilDb.TinSurface)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(p, "description") is string d) surf.Description = d;
        if (StrParam(p, "layer") is string) surf.LayerId = c.Layer(p);
        int added = 0;
        if (Has(p, "points"))
        {
            var pts = PtList(p, "points", "surface_create", 3, 200000);
            surf.AddVertices(new Point3dCollection(pts.ToArray()));
            added = pts.Count;
        }
        return new { surface = name, handle = H(id), pointsAdded = added };
    }

    private static object SurfaceAddPoints(EditContext c, JsonElement p)
    {
        var surf = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "name"));
        var pts = PtList(p, "points", "surface_add_points", 1, 200000);
        surf.AddVertices(new Point3dCollection(pts.ToArray()));
        return new { surface = surf.Name, pointsAdded = pts.Count };
    }

    private static object SurfaceRebuild(EditContext c, JsonElement p)
    {
        var s = FindSurface(c.Civ, c.Tr, NeedStr(p, "name"), OpenMode.ForWrite);
        s.Rebuild();
        return new { surface = s.Name, rebuilt = true };
    }

    /// <summary>Breaklines come from point lists ('lines') and / or from existing feature lines, 3D polylines or lines
    /// ('handles') - the usual way to model a graded pad or channel.</summary>
    private static object SurfaceAddBreaklines(EditContext c, JsonElement p)
    {
        const double midOrdinate = 1.0;   // tessellation of arcs in the source geometry
        var surf = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "name"));
        string type = Choice(p, "type", "standard", "standard", "non_destructive", "proximity");
        var breaklines = surf.BreaklinesDefinition;
        int added = 0;

        if (Has(p, "handles"))
        {
            var ids = new ObjectIdCollection(StrList(p, "handles", 5000).Select(h => IdFromHandle(c.Db, h)).ToArray());
            switch (type)
            {
                case "standard": breaklines.AddStandardBreaklines(ids, midOrdinate, 0.0, 0.0, 0.0); break;
                case "non_destructive": breaklines.AddNonDestructiveBreaklines(ids, midOrdinate); break;
                default: breaklines.AddProximityBreaklines(ids, midOrdinate); break;
            }
            added += ids.Count;
        }
        var lines = ArrParam(p, "lines", 2000);
        for (int i = 0; i < lines.Count; i++)
        {
            var pts = new Point3dCollection(Points(lines[i], $"surface_add_breaklines: line {i + 1}", 2).ToArray());
            switch (type)
            {
                case "standard": breaklines.AddStandardBreaklines(pts, midOrdinate, 0.0, 0.0, 0.0); break;
                case "non_destructive": breaklines.AddNonDestructiveBreaklines(pts, midOrdinate); break;
                default: breaklines.AddProximityBreaklines(pts, midOrdinate); break;
            }
            added++;
        }
        if (added == 0) throw new ArgumentException("surface_add_breaklines: give 'lines' and/or 'handles'.");
        return new
        {
            surface = surf.Name, breaklinesAdded = added, type,
            note = "Civil 3D reports rejected breaklines (e.g. crossing a surface point at another level) only in its Event Viewer.",
        };
    }

    private static object SurfaceAddBoundary(EditContext c, JsonElement p)
    {
        var surf = FindSurface(c.Civ, c.Tr, NeedStr(p, "name"), OpenMode.ForWrite);
        var pts = PtList(p, "points", "surface_add_boundary", 3, 20000);
        string type = Choice(p, "type", "outer", "outer", "hide", "show", "data_clip");
        var boundaryType = type switch
        {
            "hide" => Autodesk.Civil.SurfaceBoundaryType.Hide,
            "show" => Autodesk.Civil.SurfaceBoundaryType.Show,
            "data_clip" => Autodesk.Civil.SurfaceBoundaryType.DataClip,
            _ => Autodesk.Civil.SurfaceBoundaryType.Outer,
        };
        if (pts[0].DistanceTo(pts[^1]) > 1e-9) pts.Add(pts[0]);   // close the ring
        surf.BoundariesDefinition.AddBoundaries(new Point3dCollection(pts.ToArray()), 1.0, boundaryType, BoolParam(p, "non_destructive") ?? true);
        return new { surface = surf.Name, boundary = type, vertices = pts.Count - 1 };
    }

    private static object SurfaceVolumeCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewSurfaceName(c, name);
        var baseSurface = FindSurface(c.Civ, c.Tr, NeedStr(p, "base"));
        var comparison = FindSurface(c.Civ, c.Tr, NeedStr(p, "comparison"));
        var id = CivilDb.TinVolumeSurface.Create(name, baseSurface.ObjectId, comparison.ObjectId);
        var v = ((CivilDb.TinVolumeSurface)c.Tr.GetObject(id, OpenMode.ForRead)).GetVolumeProperties();
        return new
        {
            surface = name, handle = H(id), baseSurface = baseSurface.Name, comparisonSurface = comparison.Name,
            cut = R3(v.UnadjustedCutVolume), fill = R3(v.UnadjustedFillVolume), net = R3(v.UnadjustedNetVolume),
            note = "Cubic drawing units. net = fill - cut as Civil 3D reports it.",
        };
    }

    // ---------------------------------------------------------------- feature lines

    private static object FeatureLineCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var pts = PtList(p, "points", "feature_line_create", 2, 20000);

        // The API converts an existing entity into a feature line: draw a 3D polyline, convert it, remove it.
        var path = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts.ToArray()), BoolParam(p, "closed") ?? false);
        var pathId = c.Append(path, StrParam(p, "layer"));
        ObjectId id;
        if (StrParam(p, "site") is string site)
        {
            var siteId = TryFindSite(c.Civ, c.Tr, site)?.ObjectId ?? CivilDb.Site.Create(c.Civ, site);
            id = CivilDb.FeatureLine.Create(name, pathId, siteId);
        }
        else id = CivilDb.FeatureLine.Create(name, pathId);
        try { path.Erase(); } catch (Exception ex) { c.Warn("the source 3D polyline could not be removed: " + ex.Message); }

        var fl = (CivilDb.FeatureLine)c.Tr.GetObject(id, OpenMode.ForRead);
        return new
        {
            featureLine = name, handle = H(id), length2d = Safe(() => R3(fl.Length2D)),
            minElevation = Safe(() => R3(fl.MinElevation)), maxElevation = Safe(() => R3(fl.MaxElevation)),
        };
    }
}
