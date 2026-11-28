namespace C3dMcpBridge;

/// <summary>
/// TIN surface editing (roadmap #75): add / delete / swap triangle edges, add / move / delete vertices, set vertex
/// elevations, raise the surface; simplify, smooth (natural neighbour), minimise flat areas; masks. Points are matched
/// to the nearest TIN vertex within 'tolerance'. Every change is a surface edit operation in Civil 3D's definition.
/// Reads: tin_triangles (triangles in a window), tin_vertices (inside a polygon).
/// </summary>
internal static class TinOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["surface_tin_edit"] = TinEdit;
        ops["surface_simplify"] = Simplify;
        ops["surface_smooth"] = Smooth;
        ops["surface_minimize_flat_areas"] = MinimizeFlat;
        ops["surface_mask_add"] = MaskAdd;
    }

    private static CivilDb.TinSurfaceVertex Vertex(CivilDb.TinSurface s, double x, double y, double tol, string what)
    {
        CivilDb.TinSurfaceTriangle? tri = null;
        try { tri = s.FindTriangleAtXY(x, y); } catch { }
        var candidates = new List<CivilDb.TinSurfaceVertex>();
        if (tri != null) candidates.AddRange(new[] { tri.Vertex1, tri.Vertex2, tri.Vertex3 });
        try { if (s.FindVertexAtXY(x, y) is { } v) candidates.Add(v); } catch { }
        // A point on the border lies in no triangle: look a little around it.
        if (candidates.Count == 0)
            foreach (var (dx, dy) in new[] { (tol, 0.0), (-tol, 0.0), (0.0, tol), (0.0, -tol) })
                try { if (s.FindTriangleAtXY(x + dx, y + dy) is { } t) candidates.AddRange(new[] { t.Vertex1, t.Vertex2, t.Vertex3 }); } catch { }
        var best = candidates.OrderBy(v => Math.Sqrt(Math.Pow(v.Location.X - x, 2) + Math.Pow(v.Location.Y - y, 2))).FirstOrDefault();
        if (best == null || Math.Sqrt(Math.Pow(best.Location.X - x, 2) + Math.Pow(best.Location.Y - y, 2)) > tol)
            throw new ArgumentException($"{what}: no TIN vertex within {tol} m of ({R3(x)}, {R3(y)}).");
        return best;
    }

    private static CivilDb.TinSurfaceEdge Edge(CivilDb.TinSurface s, double x, double y, string what)
    {
        CivilDb.TinSurfaceEdge? e = null;
        try { e = s.FindEdgeAtXY(x, y); } catch { }
        return e ?? throw new ArgumentException($"{what}: no triangle edge at ({R3(x)}, {R3(y)}); give a point on the edge.");
    }

    private static List<double[]> Rows(JsonElement p, string key, int n, string what)
    {
        var rows = new List<double[]>();
        foreach (var e in ArrParam(p, key, 5000))
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != n || e.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                throw new ArgumentException($"{what}: each {key} item is {n} numbers.");
            rows.Add(e.EnumerateArray().Select(v => v.GetDouble()).ToArray());
        }
        return rows;
    }

    private static object Stats(CivilDb.TinSurface s) => new
    {
        triangles = Safe(() => s.GetTriangles(false).Count),
        minElevation = Safe(() => R3(s.GetGeneralProperties().MinimumElevation)),
        maxElevation = Safe(() => R3(s.GetGeneralProperties().MaximumElevation)),
    };

    private static object TinEdit(EditContext c, JsonElement p)
    {
        var s = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        double tol = DblParam(p, "tolerance") ?? 0.05;
        var before = Stats(s);
        var done = new Dictionary<string, int>();
        void Count(string k) => done[k] = done.TryGetValue(k, out int n) ? n + 1 : 1;

        foreach (var r in Rows(p, "add_vertices", 3, "surface_tin_edit")) { s.AddVertex(new Point3d(r[0], r[1], r[2])); Count("verticesAdded"); }
        foreach (var r in Rows(p, "set_elevations", 3, "surface_tin_edit"))
        { s.SetVertexElevation(Vertex(s, r[0], r[1], tol, "set_elevations"), r[2]); Count("elevationsSet"); }
        foreach (var r in Rows(p, "move_vertices", 4, "surface_tin_edit"))
        { s.MoveVertex(Vertex(s, r[0], r[1], tol, "move_vertices"), new Point2d(r[2], r[3])); Count("verticesMoved"); }
        foreach (var r in Rows(p, "add_lines", 4, "surface_tin_edit"))
        { s.AddLine(Vertex(s, r[0], r[1], tol, "add_lines start"), Vertex(s, r[2], r[3], tol, "add_lines end")); Count("linesAdded"); }
        foreach (var r in Rows(p, "swap_edges", 2, "surface_tin_edit")) { s.SwapEdge(Edge(s, r[0], r[1], "swap_edges")); Count("edgesSwapped"); }
        foreach (var r in Rows(p, "delete_lines", 2, "surface_tin_edit")) { s.DeleteLine(Edge(s, r[0], r[1], "delete_lines")); Count("linesDeleted"); }
        foreach (var r in Rows(p, "delete_vertices", 2, "surface_tin_edit"))
        { s.DeleteVertex(Vertex(s, r[0], r[1], tol, "delete_vertices")); Count("verticesDeleted"); }
        if (DblParam(p, "raise") is double dz) { s.RaiseSurface(dz); Count("raised"); }
        if (Has(p, "raise_inside"))
        {
            var poly = PtList(p, "raise_inside", "surface_tin_edit raise_inside", 3, 5000);
            var border = new Point3dCollection(poly.Select(q => new Point3d(q.X, q.Y, 0)).Append(new Point3d(poly[0].X, poly[0].Y, 0)).ToArray());
            var verts = s.GetVerticesInsideBorder(border);
            if (verts.Length == 0) throw new ArgumentException("surface_tin_edit: no TIN vertex inside raise_inside.");
            s.RaiseVertices(verts, NeedDbl(p, "raise_by"));
            done["verticesRaised"] = verts.Length;
        }
        if (done.Count == 0)
            throw new ArgumentException("surface_tin_edit: give add_vertices, set_elevations, move_vertices, add_lines, swap_edges, delete_lines, delete_vertices, raise or raise_inside + raise_by.");
        s.Rebuild();
        return new { surface = s.Name, done, before, after = Stats(s) };
    }

    private static Point3dCollection? Region(EditContext c, JsonElement p, string key)
    {
        if (!Has(p, key)) return null;
        var poly = PtList(p, key, key, 3, 5000);
        return new Point3dCollection(poly.Select(q => new Point3d(q.X, q.Y, 0)).Append(new Point3d(poly[0].X, poly[0].Y, 0)).ToArray());
    }

    private static object Simplify(EditContext c, JsonElement p)
    {
        var s = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        var method = Choice(p, "method", "point_removal", "point_removal", "edge_contraction") == "edge_contraction"
            ? Autodesk.Civil.SurfaceSimplifyType.EdgeContraction : Autodesk.Civil.SurfaceSimplifyType.PointRemoval;
        var o = new CivilDb.SurfaceSimplifyOptions(method);
        double? pct = DblParam(p, "percent"), dzMax = DblParam(p, "max_elevation_change");
        if (pct == null && dzMax == null) throw new ArgumentException("surface_simplify: give percent (of points to remove) and/or max_elevation_change (m).");
        if (pct is double pc)
        {
            if (pc <= 0 || pc >= 100) throw new ArgumentException("surface_simplify: percent is between 0 and 100.");
            o.UsePercentageToRemove = true; o.PercentageToRemove = pc / 100.0;
        }
        if (dzMax is double dm) { o.UseMaximumChangeInElevation = true; o.MaximumChangeInElevation = dm; }
        if (Region(c, p, "region") is { } region) o.UserSpecifiedPolygonRegion = region; else o.SetSurfaceBorderAsRegion();
        var before = Stats(s);
        s.SimplifySurface(o);
        s.Rebuild();
        return new { surface = s.Name, method = method.ToString(), before, after = Stats(s) };
    }

    private static object Smooth(EditContext c, JsonElement p)
    {
        var s = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        var o = new CivilDb.SurfacePointOutputOptions();
        string at = Choice(p, "points", "grid", "grid", "centroids", "random", "edge_midpoints");
        o.OutputLocations = at switch
        {
            "centroids" => Autodesk.Civil.SurfacePointOutputLocationsType.Centroids,
            "random" => Autodesk.Civil.SurfacePointOutputLocationsType.RandomPoints,
            "edge_midpoints" => Autodesk.Civil.SurfacePointOutputLocationsType.EdgeMidPoints,
            _ => Autodesk.Civil.SurfacePointOutputLocationsType.GridBased,
        };
        if (at == "grid")
        {
            double sp = NeedDbl(p, "spacing");
            if (sp <= 0) throw new ArgumentException("surface_smooth: spacing must be positive.");
            o.GridSpacingX = sp; o.GridSpacingY = DblParam(p, "spacing_y") ?? sp; o.GridOrientation = Rad(DblParam(p, "orientation_deg") ?? 0);
        }
        if (at == "random") o.RandomPointsNumber = WholeParam(p, "count", 100, 1, 1_000_000);
        o.OutputRegions = new[] { Region(c, p, "region") ?? throw new ArgumentException("surface_smooth: give region [[x,y], ...] (the area to smooth).") };
        var before = Stats(s);
        s.SmoothSurfaceByNNI(o);
        s.Rebuild();
        return new { surface = s.Name, method = "natural neighbour", points = at, before, after = Stats(s) };
    }

    private static object MinimizeFlat(EditContext c, JsonElement p)
    {
        var s = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        var o = new CivilDb.SurfaceMinimizeFlatAreaOptions(BoolParam(p, "fill_gaps") ?? true, BoolParam(p, "swap_edges") ?? true,
                                                           BoolParam(p, "add_points_to_triangles") ?? true, BoolParam(p, "add_points_to_edges") ?? true);
        var before = Stats(s);
        s.MinimizeFlatAreas(o);
        s.Rebuild();
        return new { surface = s.Name, before, after = Stats(s) };
    }

    private static object MaskAdd(EditContext c, JsonElement p)
    {
        var s = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"), OpenMode.ForWrite);
        string name = NeedStr(p, "name");
        var poly = c.Tr.GetObject(IdFromHandle(c.Db, NeedStr(p, "polyline")), OpenMode.ForRead) as Curve
                   ?? throw new ArgumentException("surface_mask_add: polyline must be a closed polyline handle.");
        if (!poly.Closed) throw new ArgumentException("surface_mask_add: the polyline must be closed.");
        var type = Choice(p, "type", "inside", "inside", "outside") == "outside" ? CivilDb.SurfaceMaskType.OutSide : CivilDb.SurfaceMaskType.InSide;
        // The creation data needs a render material even when the mask is not render-only: the drawing's "Global".
        var materials = (DBDictionary)c.Tr.GetObject(c.Db.MaterialDictionaryId, OpenMode.ForRead);
        var material = materials.Contains("Global") ? materials.GetAt("Global")
                       : materials.Cast<DBDictionaryEntry>().Select(e => e.Value).FirstOrDefault();
        if (material.IsNull) throw new InvalidOperationException("surface_mask_add: the drawing has no render material (Global).");
        var data = new CivilDb.SurfaceMaskCreationData(name, StrParam(p, "description") ?? "", s.ObjectId,
                                                       new ObjectIdCollection { poly.ObjectId }, DblParam(p, "mid_ordinate") ?? 0.1,
                                                       material, type, false);
        var mask = s.Masks.Add(data);
        return new { surface = s.Name, mask = Safe(() => mask.Name), type = type.ToString(), masks = Safe(() => s.Masks.Count) };
    }

    // ---------------------------------------------------------------- reads

    public static object TinTriangles(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var s = Civ.GetSurfaceIds().Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<CivilDb.TinSurface>()
                   .FirstOrDefault(x => SameName(x.Name, NeedStr(p, "surface")))
                ?? throw new ArgumentException($"tin_triangles: no TIN surface '{NeedStr(p, "surface")}'.");
        int limit = IntParam(p, "limit", 500, 1, 20000);
        var w = PtList(p, "window", "tin_triangles window", 2, 2);
        double x0 = Math.Min(w[0].X, w[1].X), x1 = Math.Max(w[0].X, w[1].X), y0 = Math.Min(w[0].Y, w[1].Y), y1 = Math.Max(w[0].Y, w[1].Y);
        if ((x1 - x0) * (y1 - y0) <= 0) throw new ArgumentException("tin_triangles: the window has no area.");
        var rows = new List<object>();
        int total = 0;
        foreach (CivilDb.TinSurfaceTriangle t in s.GetTriangles(false))
        {
            var v = new[] { t.Vertex1.Location, t.Vertex2.Location, t.Vertex3.Location };
            double cx = v.Average(q => q.X), cy = v.Average(q => q.Y);
            if (cx < x0 || cx > x1 || cy < y0 || cy > y1) continue;
            total++;
            if (rows.Count < limit) rows.Add(v.Select(P3).ToArray());
        }
        return new { surface = s.Name, total, returned = rows.Count, truncated = total > rows.Count, triangles = rows };
    });

    public static object TinVertices(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var s = Civ.GetSurfaceIds().Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<CivilDb.TinSurface>()
                   .FirstOrDefault(x => SameName(x.Name, NeedStr(p, "surface")))
                ?? throw new ArgumentException($"tin_vertices: no TIN surface '{NeedStr(p, "surface")}'.");
        int limit = IntParam(p, "limit", 1000, 1, 50000);
        var poly = PtList(p, "polygon", "tin_vertices polygon", 3, 5000);
        var border = new Point3dCollection(poly.Select(q => new Point3d(q.X, q.Y, 0)).Append(new Point3d(poly[0].X, poly[0].Y, 0)).ToArray());
        var verts = s.GetVerticesInsideBorder(border);
        return new { surface = s.Name, total = verts.Length, returned = Math.Min(limit, verts.Length), truncated = verts.Length > limit,
                     vertices = verts.Take(limit).Select(v => P3(v.Location)).ToList() };
    });
}
