namespace C3dMcpBridge;

/// <summary>
/// AutoCAD extras (roadmap #79): splines, ellipses, wipeouts, text with fields (file name, dates, sheet info that update
/// themselves), groups. Read: list_groups.
/// </summary>
internal static class CadExtraOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["draw_spline"] = DrawSpline;
        ops["draw_ellipse"] = DrawEllipse;
        ops["draw_wipeout"] = DrawWipeout;
        ops["draw_field_text"] = DrawFieldText;
        ops["group_create"] = GroupCreate;
        ops["group_edit"] = GroupEdit;
    }

    private static object DrawSpline(EditContext c, JsonElement p)
    {
        var pts = PtList(p, "points", "draw_spline", 2, 5000);
        int degree = WholeParam(p, "degree", 3, 1, 3);
        var spline = new Spline(new Point3dCollection(pts.ToArray()), degree, 0.0);
        if (BoolParam(p, "closed") ?? false)
        {
            if (pts.Count < 3) throw new ArgumentException("draw_spline: a closed spline needs at least 3 points.");
            spline = new Spline(new Point3dCollection(pts.Append(pts[0]).ToArray()), degree, 0.0);
        }
        var id = c.Append(spline, p);
        return new { handle = H(id), points = pts.Count, length = Safe(() => R3(spline.GetDistanceAtParameter(spline.EndParam))) };
    }

    private static object DrawEllipse(EditContext c, JsonElement p)
    {
        var center = NeedPt(p, "center", "draw_ellipse");
        var axis = NeedPt(p, "major_axis", "draw_ellipse");   // vector from the centre to the end of the major axis
        var major = new Vector3d(axis.X, axis.Y, 0);
        if (major.Length < 1e-9) throw new ArgumentException("draw_ellipse: major_axis is a vector [dx, dy] from the centre and cannot be zero.");
        double ratio = NeedDbl(p, "ratio");
        if (ratio <= 0 || ratio > 1) throw new ArgumentException("draw_ellipse: ratio (minor / major) is above 0 and at most 1.");
        double a0 = Rad(DblParam(p, "start_deg") ?? 0), a1 = Rad(DblParam(p, "end_deg") ?? 360);
        var e = new Ellipse(center, Vector3d.ZAxis, major, ratio, a0, a1);
        var id = c.Append(e, p);
        return new { handle = H(id), majorRadius = R3(major.Length), minorRadius = R3(major.Length * ratio) };
    }

    private static object DrawWipeout(EditContext c, JsonElement p)
    {
        var pts = PtList(p, "points", "draw_wipeout", 3, 5000);
        var w = new Wipeout();
        var ring = new Point2dCollection(pts.Select(q => new Point2d(q.X, q.Y)).Append(new Point2d(pts[0].X, pts[0].Y)).ToArray());
        w.SetFrom(ring, Vector3d.ZAxis);
        var id = c.Append(w, p);
        return new { handle = H(id), vertices = pts.Count, note = "Draw order: a wipeout covers what is under it; draw it before the text on top." };
    }

    /// <summary>Field codes for the common cases; 'code' takes any AutoCAD field expression instead.</summary>
    private static string FieldCode(string preset, JsonElement p) => preset switch
    {
        "filename" => @"%<\AcVar Filename \f ""%fn2"">%",
        "date" => @"%<\AcVar Date \f """ + (StrParam(p, "format") ?? "dd.MM.yyyy") + @""">%",
        "savedate" => @"%<\AcVar SaveDate \f """ + (StrParam(p, "format") ?? "dd.MM.yyyy") + @""">%",
        "plotdate" => @"%<\AcVar PlotDate \f """ + (StrParam(p, "format") ?? "dd.MM.yyyy") + @""">%",
        "author" => @"%<\AcVar Author>%",
        "title" => @"%<\AcVar Title>%",
        "layout" => @"%<\AcVar ctab>%",
        _ => throw new ArgumentException("draw_field_text: field is filename | date | savedate | plotdate | author | title | layout, or give 'code'."),
    };

    private static object DrawFieldText(EditContext c, JsonElement p)
    {
        string code = StrParam(p, "code") ?? FieldCode(NeedStr(p, "field").ToLowerInvariant(), p);
        if (!code.Contains("%<") || !code.Contains(">%")) throw new ArgumentException("draw_field_text: code must be an AutoCAD field expression %<...>%.");
        string prefix = StrParam(p, "prefix") ?? "", suffix = StrParam(p, "suffix") ?? "";
        var t = new MText
        {
            Location = NeedPt(p, "at", "draw_field_text"),
            TextHeight = PositiveParam(p, "height") ?? 2.5,
            Rotation = Rad(DblParam(p, "rotation_deg") ?? 0),
            Contents = prefix + code + suffix,
        };
        var id = c.Append(t, p);
        var field = new Field(prefix + code + suffix);
        t.SetField(field);
        c.Tr.AddNewlyCreatedDBObject(field, true);
        field.Evaluate();
        return new { handle = H(id), code, value = Safe(() => t.Text), state = Safe(() => field.EvaluationStatus.Status.ToString()) };
    }

    private static DBDictionary Groups(EditContext c, OpenMode mode) => (DBDictionary)c.Tr.GetObject(c.Db.GroupDictionaryId, mode);

    private static ObjectIdCollection Ids(EditContext c, JsonElement p, string key)
    {
        var ids = new ObjectIdCollection();
        foreach (var e in ArrParam(p, key, 20000))
        {
            var id = IdFromHandle(c.Db, e.GetString() ?? "");
            if (c.Tr.GetObject(id, OpenMode.ForRead) is not Entity) throw new ArgumentException($"{key}: {e.GetString()} is not a drawing object.");
            ids.Add(id);
        }
        return ids;
    }

    private static object GroupCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var dict = Groups(c, OpenMode.ForWrite);
        if (dict.Contains(name)) throw new ArgumentException($"group_create: a group '{name}' exists (group_edit).");
        var ids = Ids(c, p, "handles");
        if (ids.Count == 0) throw new ArgumentException("group_create: give handles.");
        var g = new Group(StrParam(p, "description") ?? "", BoolParam(p, "selectable") ?? true);
        dict.SetAt(name, g);
        c.Tr.AddNewlyCreatedDBObject(g, true);
        g.Append(ids);
        return new { group = name, handle = H(g.ObjectId), members = g.NumEntities };
    }

    private static object GroupEdit(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var dict = Groups(c, OpenMode.ForRead);
        if (!dict.Contains(name)) throw new ArgumentException($"group_edit: no group '{name}' (list_groups).");
        var g = (Group)c.Tr.GetObject(dict.GetAt(name), OpenMode.ForWrite);
        if (BoolParam(p, "ungroup") ?? false)
        {
            int n = g.NumEntities;
            g.Erase();   // the objects stay; only the group goes
            return new { group = name, ungrouped = true, members = n };
        }
        if (Has(p, "add")) g.Append(Ids(c, p, "add"));
        if (Has(p, "remove")) g.Remove(Ids(c, p, "remove"));
        if (StrParam(p, "description") is string d) g.Description = d;
        if (BoolParam(p, "selectable") is bool sel) g.Selectable = sel;
        return new { group = name, members = g.NumEntities };
    }

    public static object ListGroups(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var dict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        var rows = new List<object>();
        foreach (DBDictionaryEntry e in dict)
        {
            var g = (Group)tr.GetObject(e.Value, OpenMode.ForRead);
            rows.Add(new { name = e.Key, description = g.Description, selectable = g.Selectable, members = g.NumEntities,
                           handles = g.GetAllEntityIds().Take(200).Select(H).ToList() });
        }
        return new { count = rows.Count, groups = rows };
    });
}
