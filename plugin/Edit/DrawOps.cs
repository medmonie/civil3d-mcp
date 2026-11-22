namespace C3dMcpBridge;

/// <summary>Drawing new plain AutoCAD entities: lines, polylines, points, circles, arcs, text, hatches, dimensions, leaders, tables.</summary>
internal static class DrawOps
{
    private const double DefaultTextHeight = 2.5;

    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["draw_line"] = DrawLine;
        ops["draw_polyline"] = DrawPolyline;
        ops["draw_point"] = DrawPoint;
        ops["draw_circle"] = DrawCircle;
        ops["draw_arc"] = DrawArc;
        ops["draw_text"] = DrawText;
        ops["draw_mtext"] = DrawMText;
        ops["draw_annotations"] = DrawAnnotations;
        ops["draw_hatch"] = DrawHatch;
        ops["draw_dimension"] = DrawDimension;
        ops["draw_leader"] = DrawLeader;
        ops["draw_table"] = DrawTable;
    }

    private static object DrawAnnotations(EditContext c, JsonElement p)
    {
        var items = NeedArr(p, "items", 5000);
        foreach (var item in items)
        {
            _ = NeedStr(item, "op") switch
            {
                "draw_line" => DrawLine(c, item),
                "draw_text" => DrawText(c, item),
                "draw_mtext" => DrawMText(c, item),
                _ => throw new ArgumentException("draw_annotations accepts draw_line, draw_text, and draw_mtext items."),
            };
        }
        return new { drawn = items.Count };
    }

    // ---------------------------------------------------------------- geometry

    private static object DrawLine(EditContext c, JsonElement p)
    {
        var a = NeedPt(p, "start", "draw_line");
        var b = NeedPt(p, "end", "draw_line");
        if (a.DistanceTo(b) < 1e-9) throw new ArgumentException("draw_line: start and end are the same point.");
        return new { handle = H(c.Append(new Line(a, b), p)) };
    }

    private static object DrawPolyline(EditContext c, JsonElement p)
    {
        var pts = PtList(p, "points", "draw_polyline", 2, 20000);
        bool closed = BoolParam(p, "closed") ?? false;
        // One elevation throughout: a light 2D polyline. Varying elevations: a 3D polyline.
        bool flat = pts.All(q => Math.Abs(q.Z - pts[0].Z) < 1e-9);
        Entity e = flat ? FlatPolyline(pts, closed) : new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts.ToArray()), closed);
        var id = c.Append(e, p);
        return new { handle = H(id), type = DxfOrName(id), vertices = pts.Count };
    }

    private static object DrawPoint(EditContext c, JsonElement p) =>
        new { handle = H(c.Append(new DBPoint(NeedPt(p, "at", "draw_point")), p)) };

    private static object DrawCircle(EditContext c, JsonElement p)
    {
        var e = new Circle(NeedPt(p, "center", "draw_circle"), Vector3d.ZAxis, NeedPositive(p, "radius"));
        return new { handle = H(c.Append(e, p)) };
    }

    private static object DrawArc(EditContext c, JsonElement p)
    {
        var e = new Arc(NeedPt(p, "center", "draw_arc"), NeedPositive(p, "radius"),
            Rad(NeedDbl(p, "start_deg")), Rad(NeedDbl(p, "end_deg")));   // counter-clockwise from start to end
        return new { handle = H(c.Append(e, p)), length = R3(e.Length) };
    }

    private static object DrawHatch(EditContext c, JsonElement p)
    {
        var pts = PtList(p, "points", "draw_hatch", 3, 5000);
        string pattern = StrParam(p, "pattern") ?? "SOLID";
        bool keepBoundary = BoolParam(p, "keep_boundary") ?? true;

        // Boundary first: a hatch built on a real closed polyline is associative and reports its area reliably.
        var boundary = FlatPolyline(pts, closed: true);
        var boundaryId = c.Append(boundary, p);
        double area = boundary.Area;

        var h = new Hatch();
        var id = c.Append(h, p);
        h.Normal = Vector3d.ZAxis;
        h.Elevation = pts[0].Z;
        try { h.SetHatchPattern(HatchPatternType.PreDefined, pattern); }
        catch { throw new ArgumentException($"draw_hatch: unknown hatch pattern '{pattern}' (e.g. SOLID, ANSI31, ANSI37, AR-CONC)."); }
        if (PositiveParam(p, "scale") is double scale && !SameName(pattern, "SOLID")) h.PatternScale = scale;
        h.Associative = keepBoundary;
        h.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { boundaryId });
        h.EvaluateHatch(true);
        if (!keepBoundary) boundary.Erase();
        return new { handle = H(id), pattern, area = R3(area), boundaryHandle = keepBoundary ? H(boundaryId) : null };
    }

    // ---------------------------------------------------------------- annotation

    private static object DrawText(EditContext c, JsonElement p)
    {
        var t = new DBText
        {
            TextString = NeedStr(p, "text"),
            Position = NeedPt(p, "at", "draw_text"),
            Height = PositiveParam(p, "height") ?? DefaultTextHeight,
            Rotation = Rad(DblParam(p, "rotation_deg") ?? 0),
        };
        var id = c.Append(t, p);
        if (StrParam(p, "style") is string style) t.TextStyleId = c.Symbol(c.Db.TextStyleTableId, style, "Text style");
        return new { handle = H(id) };
    }

    private static object DrawMText(EditContext c, JsonElement p)
    {
        var t = new MText
        {
            Contents = NeedStr(p, "text"),
            Location = NeedPt(p, "at", "draw_mtext"),
            TextHeight = PositiveParam(p, "height") ?? DefaultTextHeight,
            Rotation = Rad(DblParam(p, "rotation_deg") ?? 0),
        };
        if (PositiveParam(p, "width") is double width) t.Width = width;
        var id = c.Append(t, p);
        if (StrParam(p, "style") is string style) t.TextStyleId = c.Symbol(c.Db.TextStyleTableId, style, "Text style");
        return new { handle = H(id) };
    }

    private static object DrawDimension(EditContext c, JsonElement p)
    {
        var a = NeedPt(p, "from", "draw_dimension");
        var b = NeedPt(p, "to", "draw_dimension");
        if (a.DistanceTo(b) < 1e-9) throw new ArgumentException("draw_dimension: 'from' and 'to' are the same point.");
        var lineAt = NeedPt(p, "line_at", "draw_dimension");
        string text = StrParam(p, "text") ?? "";
        string type = Choice(p, "type", "aligned", "aligned", "horizontal", "vertical");
        var dimStyle = StrParam(p, "style") is string ds ? c.Symbol(c.Db.DimStyleTableId, ds, "Dimension style") : c.Db.Dimstyle;
        var (dim, measured) = type switch
        {
            "horizontal" => ((Dimension)new RotatedDimension(0, a, b, lineAt, text, dimStyle), Math.Abs(b.X - a.X)),
            "vertical" => (new RotatedDimension(Math.PI / 2, a, b, lineAt, text, dimStyle), Math.Abs(b.Y - a.Y)),
            _ => (new AlignedDimension(a, b, lineAt, text, dimStyle), a.DistanceTo(b)),
        };
        var id = c.Append(dim, p);
        dim.DimensionStyle = dimStyle;   // SetDatabaseDefaults sets the current style; keep the one asked for
        return new { handle = H(id), type, measurement = R3(measured) };
    }

    private static object DrawLeader(EditContext c, JsonElement p)
    {
        var arrow = NeedPt(p, "arrow_at", "draw_leader");
        var textAt = NeedPt(p, "text_at", "draw_leader");
        if (arrow.DistanceTo(textAt) < 1e-9) throw new ArgumentException("draw_leader: arrow_at and text_at are the same point.");
        double? height = PositiveParam(p, "height");

        var ml = new MLeader();
        var id = c.Append(ml, p);
        if (StrParam(p, "style") is string mls) ml.MLeaderStyle = c.MLeaderStyleId(mls);
        int line = ml.AddLeaderLine(ml.AddLeader());
        ml.AddFirstVertex(line, arrow);
        ml.AddLastVertex(line, textAt);
        var mt = new MText { Contents = NeedStr(p, "text"), TextHeight = height ?? DefaultTextHeight };
        mt.SetDatabaseDefaults(c.Db);
        ml.ContentType = ContentType.MTextContent;
        ml.MText = mt;
        ml.TextLocation = textAt;
        if (height != null) ml.TextHeight = height.Value;   // otherwise the multileader style decides
        return new { handle = H(id) };
    }

    private static object DrawTable(EditContext c, JsonElement p)
    {
        var data = NeedArr(p, "rows", 2000).Select((row, i) => row.ValueKind == JsonValueKind.Array
            ? row.EnumerateArray().Select(Text).ToList()
            : throw new ArgumentException($"draw_table: row {i + 1} must be a list of cell values.")).ToList();
        int cols = data.Max(r => r.Count);
        if (cols == 0 || cols > 100) throw new ArgumentException("draw_table: 1 to 100 columns are supported.");
        double textHeight = PositiveParam(p, "text_height") ?? DefaultTextHeight;
        string? title = StrParam(p, "title");
        int titleRows = title != null ? 1 : 0;

        var t = new Table { TableStyle = c.Db.Tablestyle, Position = NeedPt(p, "at", "draw_table") };
        t.SetSize(data.Count + titleRows, cols);
        t.SetRowHeight(PositiveParam(p, "row_height") ?? textHeight * 2);
        t.SetColumnWidth(PositiveParam(p, "column_width") ?? textHeight * 12);
        if (title != null)
        {
            t.MergeCells(CellRange.Create(t, 0, 0, 0, cols - 1));
            t.Cells[0, 0].TextString = title;
            t.Cells[0, 0].TextHeight = textHeight * 1.2;
        }
        else
        {
            // Without a title, stop the table style from treating row 0 as a merged title row.
            try { t.Rows[0].Style = "Data"; } catch { /* style without that row type */ }
        }
        for (int r = 0; r < data.Count; r++)
            for (int col = 0; col < cols; col++)
            {
                var cell = t.Cells[r + titleRows, col];
                cell.TextString = col < data[r].Count ? data[r][col] : "";
                cell.TextHeight = textHeight;
            }
        t.GenerateLayout();
        return new { handle = H(c.Append(t, p)), rows = data.Count + titleRows, columns = cols };
    }
}
