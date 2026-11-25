using Autodesk.AutoCAD.EditorInput;

namespace C3dMcpBridge;

/// <summary>
/// show_objects: select and / or zoom to objects so the person sees what the client found. Changes only the screen
/// (view and pickfirst selection), never the drawing, so it needs no edit permission and is not an undo step.
/// </summary>
internal static class ViewRead
{
    public static object ShowObjects(JsonElement p)
    {
        const string op = "show_objects";
        var handles = Has(p, "handles") ? StrList(p, "handles", 5000) : new List<string>();
        var window = Has(p, "window") ? PtList(p, "window", "show_objects window", 2, 2) : null;
        bool select = BoolParam(p, "select") ?? handles.Count > 0;
        bool zoom = BoolParam(p, "zoom") ?? true;
        double margin = DblParam(p, "margin") ?? 0.1;
        if (margin < 0 || margin > 5) throw new ArgumentException($"{op}: 'margin' is a fraction of the size, 0 to 5 (default 0.1).");
        if (window != null && window.Count != 2) throw new ArgumentException($"{op}: 'window' is two corners [[x1, y1], [x2, y2]].");
        if (handles.Count == 0 && window == null && !Has(p, "select"))
            throw new ArgumentException($"{op}: give 'handles' (objects to show) or 'window' (an area to zoom to); select: false clears the selection.");

        var doc = ActiveDoc();
        var db = doc.Database;
        var ed = doc.Editor;
        var ids = new List<ObjectId>();
        var missing = new List<object>();
        var ext = new Extents3d();
        bool hasExt = false;
        string? space = null;
        var spaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (doc.LockDocument())
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var h in handles)
                {
                    try
                    {
                        var id = IdFromHandle(db, h);
                        if (id.IsErased) throw new ArgumentException("erased");
                        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) throw new ArgumentException("not a drawing object");
                        var owner = (BlockTableRecord)tr.GetObject(ent.OwnerId, OpenMode.ForRead);
                        if (!owner.IsLayout) throw new ArgumentException("inside a block definition, not in model space or on a layout");
                        spaces.Add(((Layout)tr.GetObject(owner.LayoutId, OpenMode.ForRead)).LayoutName);
                        ids.Add(id);
                        try { var e = ent.GeometricExtents; if (hasExt) ext.AddExtents(e); else { ext = e; hasExt = true; } }
                        catch { /* no extents (empty or not drawn): selected but not used for the zoom */ }
                    }
                    catch (Exception ex) { missing.Add(new { handle = h, error = ex.Message }); }
                }
                tr.Commit();
            }
            if (handles.Count > 0 && ids.Count == 0)
                throw new ArgumentException($"{op}: none of the handles is a drawing object in '{Path.GetFileName(doc.Name)}': " +
                                            string.Join("; ", missing.Take(5).Select(m => m.ToString())));
            if (spaces.Count > 1)
                throw new ArgumentException($"{op}: the objects are in different spaces ({string.Join(", ", spaces)}); show one space at a time.");
            if (window != null)
            {
                var w = new Extents3d();
                w.AddPoint(new Point3d(Math.Min(window[0].X, window[1].X), Math.Min(window[0].Y, window[1].Y), 0));
                w.AddPoint(new Point3d(Math.Max(window[0].X, window[1].X), Math.Max(window[0].Y, window[1].Y), 0));
                if (hasExt) ext.AddExtents(w); else { ext = w; hasExt = true; }
            }

            // Make the objects' space current (Model, or their layout) before zooming / selecting.
            space = spaces.FirstOrDefault();
            if (space != null && !string.Equals(LayoutManager.Current.CurrentLayout, space, StringComparison.OrdinalIgnoreCase))
                LayoutManager.Current.CurrentLayout = space;

            bool zoomed = false;
            if (zoom && hasExt)
            {
                using var view = ed.GetCurrentView();
                var toDcs = (Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target)
                             * Matrix3d.Displacement(view.Target - Point3d.Origin)
                             * Matrix3d.PlaneToWorld(view.ViewDirection)).Inverse();
                var d = ext;
                d.TransformBy(toDcs);
                double w = Math.Max(d.MaxPoint.X - d.MinPoint.X, 0), h = Math.Max(d.MaxPoint.Y - d.MinPoint.Y, 0);
                double floor = Math.Max(Math.Max(w, h) * 0.05, 1.0);   // a point or a short line still gets a usable view
                w = Math.Max(w, floor) * (1 + 2 * margin);
                h = Math.Max(h, floor) * (1 + 2 * margin);
                double aspect = view.Width / view.Height;
                if (w / h > aspect) h = w / aspect; else w = h * aspect;
                view.CenterPoint = new Point2d((d.MinPoint.X + d.MaxPoint.X) / 2, (d.MinPoint.Y + d.MaxPoint.Y) / 2);
                view.Width = w;
                view.Height = h;
                ed.SetCurrentView(view);
                zoomed = true;
            }

            if (select) ed.SetImpliedSelection(ids.ToArray());
            else if (Has(p, "select")) ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            return new
            {
                drawing = Path.GetFileName(doc.Name),
                space = space ?? LayoutManager.Current.CurrentLayout,
                selected = select ? ids.Count : 0,
                zoomed,
                extents = hasExt ? new { min = P3(ext.MinPoint), max = P3(ext.MaxPoint) } : null,
                notFound = missing,
                note = "Only the screen changed (view and selection); the drawing is not modified.",
            };
        }
    }
}
