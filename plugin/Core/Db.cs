namespace C3dMcpBridge;

/// <summary>Drawing access and small formatting helpers shared by the read actions and the edit operations. Main thread only.</summary>
internal static class Db
{
    public static Document ActiveDoc() =>
        AcApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("No active drawing in Civil 3D.");

    /// <summary>The Civil 3D side of the active drawing.</summary>
    public static CivilDoc Civ => CivilApp.ActiveDocument;

    /// <summary>Run a read-only query in its own transaction and commit it (commit is cheaper than abort).</summary>
    public static T WithRead<T>(Func<Database, Transaction, T> body)
    {
        var db = ActiveDoc().Database;
        using var tr = db.TransactionManager.StartTransaction();
        var result = body(db, tr);
        tr.Commit();
        return result;
    }

    // ---------------------------------------------------------------- output formatting

    /// <summary>Evaluate a probe; on failure report the error text instead of failing the whole call.</summary>
    public static object? Safe<T>(Func<T> probe)
    {
        try { return probe(); }
        catch (Exception ex) { return "error: " + ex.Message; }
    }

    /// <summary>The message for the caller: argument and state errors are already written for them, others name their type.</summary>
    public static string ErrorText(Exception ex) =>
        ex is ArgumentException or InvalidOperationException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";

    public static double R3(double v) => double.IsFinite(v) ? Math.Round(v, 3) : v;

    public static double[] P3(Point3d p) => new[] { R3(p.X), R3(p.Y), R3(p.Z) };

    public static double Rad(double degrees) => degrees * Math.PI / 180.0;

    public static double Deg(double radians) => R3(radians * 180.0 / Math.PI);

    public static string H(ObjectId id) => id.Handle.ToString();

    public static string DxfOrName(ObjectId id) => id.ObjectClass.DxfName ?? id.ObjectClass.Name;

    public static bool SameName(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>{min, max} corners of an entity, or the error text when it has no extents.</summary>
    public static object? Extents(Entity e) => Safe(() =>
    {
        var x = e.GeometricExtents;
        return new { min = P3(x.MinPoint), max = P3(x.MaxPoint) };
    });

    /// <summary>
    /// The name of the Civil 3D object a property refers to (reference surface, baseline alignment, ...); null when
    /// nothing is referenced. The property is passed as a lambda because Civil 3D throws "Retrieve attribute failed"
    /// for some references that are not set, instead of returning a null id.
    /// </summary>
    public static object? NameOf(Transaction tr, Func<ObjectId> reference)
    {
        ObjectId id;
        try { id = reference(); }
        catch { return null; }
        return id.IsNull || !id.IsValid || id.IsErased ? null : Safe(() => ((CivilDb.Entity)tr.GetObject(id, OpenMode.ForRead)).Name);
    }

    /// <summary>
    /// The reply of a list action with a 'limit': {total, returned, truncated, [key]: rows}. Only the first 'limit' items
    /// are turned into rows (which is what opens the objects); the rest are just counted.
    /// </summary>
    public static Dictionary<string, object?> Paged<T>(string key, IEnumerable<T> items, int limit, Func<T, object> row)
    {
        var rows = new List<object>();
        int total = 0;
        foreach (var item in items)
            if (total++ < limit) rows.Add(row(item));
        return new() { ["total"] = total, ["returned"] = rows.Count, ["truncated"] = total > rows.Count, [key] = rows };
    }

    // ---------------------------------------------------------------- drawing lookups

    public static BlockTableRecord ModelSpace(Database db, Transaction tr, OpenMode mode = OpenMode.ForRead)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], mode);
    }

    /// <summary>Model-space objects of one DXF class. Compares class names only; no object is opened.</summary>
    public static List<ObjectId> ModelSpaceIdsByDxf(Database db, Transaction tr, string dxf) =>
        ModelSpace(db, tr).Cast<ObjectId>().Where(id => SameName(DxfOrName(id), dxf)).ToList();

    public static ObjectId IdFromHandle(Database db, string handle)
    {
        long value;
        try { value = Convert.ToInt64(handle.Trim(), 16); }
        catch { throw new ArgumentException($"'{handle}' is not a valid object handle (hexadecimal text such as '71B7')."); }
        if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsNull || id.IsErased)
            throw new ArgumentException($"No object with handle '{handle}' in this drawing.");
        return id;
    }

    /// <summary>Layer by name; null means the current layer.</summary>
    public static ObjectId LayerId(Database db, Transaction tr, string? name)
    {
        if (string.IsNullOrEmpty(name)) return db.Clayer;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        return lt.Has(name) ? lt[name] : throw new ArgumentException($"Layer '{name}' does not exist (create it first with layer_create).");
    }

    /// <summary>Throws unless 'name' is usable as a layer / block name.</summary>
    public static void CheckSymbolName(string name, string what)
    {
        try { SymbolUtilityServices.ValidateSymbolName(name, false); }
        catch { throw new ArgumentException($"'{name}' is not a valid {what} name."); }
    }

    /// <summary>A lightweight 2D polyline through the points, at the elevation of the first one.</summary>
    public static Polyline FlatPolyline(IReadOnlyList<Point3d> pts, bool closed = false)
    {
        var pl = new Polyline();
        for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
        pl.Elevation = pts[0].Z;
        pl.Closed = closed;
        return pl;
    }
}
