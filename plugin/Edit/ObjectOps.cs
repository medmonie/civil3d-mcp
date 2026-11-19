namespace C3dMcpBridge;

/// <summary>Layers and existing objects: change properties, move, copy, rotate, scale, erase.</summary>
internal static class ObjectOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["layer_create"] = LayerCreate;
        ops["layer_set"] = LayerSet;
        ops["entity_set"] = EntitySet;
        ops["entity_move"] = EntityMove;
        ops["entity_copy"] = EntityCopy;
        ops["entity_rotate"] = EntityRotate;
        ops["entity_scale"] = EntityScale;
        ops["entity_erase"] = EntityErase;
        ops["text_set"] = TextSet;
        ops["object_set"] = ObjectSet;
    }

    // ---------------------------------------------------------------- layers

    private static void ApplyLayerProps(EditContext c, LayerTableRecord l, JsonElement p)
    {
        if (Has(p, "color")) l.Color = EditContext.Aci(p);
        if (BoolParam(p, "on") is bool on) l.IsOff = !on;
        if (BoolParam(p, "frozen") is bool frozen)
        {
            if (frozen && l.ObjectId == c.Db.Clayer) throw new ArgumentException($"Layer '{l.Name}' is the current layer and cannot be frozen.");
            l.IsFrozen = frozen;
        }
        if (BoolParam(p, "locked") is bool locked) l.IsLocked = locked;
        if (BoolParam(p, "plot") is bool plot) l.IsPlottable = plot;
    }

    private static object LayerCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        CheckSymbolName(name, "layer");
        var lt = (LayerTable)c.Tr.GetObject(c.Db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
        {
            if (SameName(StrParam(p, "if_exists"), "skip")) return new { layer = name, created = false, note = "already exists" };
            throw new ArgumentException($"Layer '{name}' already exists (pass if_exists:'skip' to accept that, or use layer_set).");
        }
        lt.UpgradeOpen();
        var l = new LayerTableRecord { Name = name };
        ApplyLayerProps(c, l, p);
        lt.Add(l);
        c.Tr.AddNewlyCreatedDBObject(l, true);
        return new { layer = name, created = true };
    }

    private static object LayerSet(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var l = (LayerTableRecord)c.Tr.GetObject(LayerId(c.Db, c.Tr, name), OpenMode.ForWrite);
        if (l.IsDependent) throw new InvalidOperationException($"Layer '{name}' belongs to an xref and cannot be changed here.");
        ApplyLayerProps(c, l, p);
        if (StrParam(p, "new_name") is string newName)
        {
            if (name == "0") throw new ArgumentException("Layer 0 cannot be renamed.");
            CheckSymbolName(newName, "layer");
            l.Name = newName;
        }
        return new { layer = l.Name, updated = true };
    }

    // ---------------------------------------------------------------- any entity

    /// <summary>A linetype by name, loaded from the standard linetype files when the drawing does not have it yet.</summary>
    private static ObjectId LinetypeId(EditContext c, string name)
    {
        var table = (LinetypeTable)c.Tr.GetObject(c.Db.LinetypeTableId, OpenMode.ForRead);
        if (table.Has(name)) return table[name];
        foreach (var file in new[] { "acadiso.lin", "acad.lin" })
        {
            try { c.Db.LoadLineTypeFile(name, file); break; } catch { /* not in this file */ }
        }
        table = (LinetypeTable)c.Tr.GetObject(c.Db.LinetypeTableId, OpenMode.ForRead);
        return table.Has(name) ? table[name] : throw new ArgumentException($"Linetype '{name}' is not loaded and was not found in acadiso.lin / acad.lin.");
    }

    private static object EntitySet(EditContext c, JsonElement p)
    {
        var ents = c.OpenEntities(p);
        ObjectId? layer = StrParam(p, "layer") is string ? c.Layer(p) : null;
        var color = Has(p, "color") ? EditContext.Aci(p) : null;
        ObjectId? linetype = StrParam(p, "linetype") is string lt ? LinetypeId(c, lt) : null;
        if (layer == null && color == null && linetype == null) throw new ArgumentException("entity_set: give 'layer', 'color' and/or 'linetype'.");
        foreach (var e in ents)
        {
            if (layer != null) e.LayerId = layer.Value;
            if (color != null) e.Color = color;
            if (linetype != null) e.LinetypeId = linetype.Value;
        }
        return new { updated = ents.Count };
    }

    private static object EntityMove(EditContext c, JsonElement p)
    {
        var v = Displacement(p);
        if (v.Length < 1e-12) throw new ArgumentException("entity_move: give a non-zero dx, dy and/or dz.");
        var ents = c.OpenEntities(p);
        var m = Matrix3d.Displacement(v);
        foreach (var e in ents) e.TransformBy(m);
        return new { moved = ents.Count, by = new[] { v.X, v.Y, v.Z } };
    }

    private static object EntityErase(EditContext c, JsonElement p)
    {
        var ents = c.OpenEntities(p);
        var types = ents.GroupBy(e => DxfOrName(e.ObjectId)).Select(g => new { type = g.Key, count = g.Count() }).ToList();
        foreach (var e in ents) e.Erase();
        return new { erased = ents.Count, types };
    }

    // ---------------------------------------------------------------- plain AutoCAD entities only

    private static object EntityCopy(EditContext c, JsonElement p)
    {
        var ents = c.OpenEntities(p, plainOnlyFor: "entity_copy");
        var m = Matrix3d.Displacement(Displacement(p));
        var ms = ModelSpace(c.Db, c.Tr, OpenMode.ForWrite);
        var handles = new List<string>();
        foreach (var e in ents)
        {
            var copy = (Entity)e.Clone();
            copy.TransformBy(m);
            handles.Add(H(ms.AppendEntity(copy)));
            c.Tr.AddNewlyCreatedDBObject(copy, true);
        }
        return new { copied = handles.Count, handles };
    }

    private static object EntityRotate(EditContext c, JsonElement p)
    {
        var ents = c.OpenEntities(p, plainOnlyFor: "entity_rotate");
        var m = Matrix3d.Rotation(Rad(NeedDbl(p, "angle_deg")), Vector3d.ZAxis, NeedPt(p, "base", "entity_rotate"));
        foreach (var e in ents) e.TransformBy(m);
        return new { rotated = ents.Count };
    }

    private static object EntityScale(EditContext c, JsonElement p)
    {
        var ents = c.OpenEntities(p, plainOnlyFor: "entity_scale");
        var m = Matrix3d.Scaling(NeedPositive(p, "factor"), NeedPt(p, "base", "entity_scale"));
        foreach (var e in ents) e.TransformBy(m);
        return new { scaled = ents.Count };
    }

    private static object TextSet(EditContext c, JsonElement p)
    {
        string handle = NeedStr(p, "handle");
        string? text = StrParam(p, "text");
        double? height = PositiveParam(p, "height");
        if (text == null && height == null) throw new ArgumentException("text_set: give text and/or height.");
        switch (c.OpenByHandle<Entity>(p, "a drawing entity"))
        {
            case DBText t:
                if (text != null) t.TextString = text;
                if (height != null) t.Height = height.Value;
                break;
            case MText mt:
                if (text != null) mt.Contents = text;
                if (height != null) mt.TextHeight = height.Value;
                break;
            default:
                throw new ArgumentException($"Object {handle} is not text (TEXT or MTEXT).");
        }
        return new { handle, updated = true };
    }

    // ---------------------------------------------------------------- Civil 3D objects: rename / describe / restyle

    private static CivilDb.Entity FindCivil(EditContext c, JsonElement p, string kind, string name) => kind switch
    {
        "alignment" => FindAlignment(c.Civ, c.Tr, name, OpenMode.ForWrite),
        "profile" => FindProfile(c.Tr, FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment")), name, OpenMode.ForWrite),
        "surface" => FindSurface(c.Civ, c.Tr, name, OpenMode.ForWrite),
        "corridor" => FindCorridor(c.Civ, c.Tr, name, OpenMode.ForWrite),
        "assembly" => FindAssembly(c.Civ, c.Tr, name, OpenMode.ForWrite),
        "pipe_network" => FindPipeNetwork(c.Civ, c.Tr, name, OpenMode.ForWrite),
        _ => FindPressureNetwork(c.Civ, c.Tr, name, OpenMode.ForWrite),
    };

    private static object ObjectSet(EditContext c, JsonElement p)
    {
        string kind = Choice(p, "kind", "", "alignment", "profile", "surface", "corridor", "assembly", "pipe_network", "pressure_network");
        var e = FindCivil(c, p, kind, NeedStr(p, "name"));
        var changed = new List<string>();
        if (StrParam(p, "description") is string d) { e.Description = d; changed.Add("description"); }
        if (StrParam(p, "style") is string s) { e.StyleName = s; changed.Add("style"); }
        if (StrParam(p, "layer") is string) { e.LayerId = c.Layer(p); changed.Add("layer"); }
        if (StrParam(p, "new_name") is { Length: > 0 } newName) { e.Name = newName; changed.Add("name"); }
        if (changed.Count == 0) throw new ArgumentException("object_set: give new_name, description, style and/or layer.");
        return new { kind, name = e.Name, handle = H(e.ObjectId), changed };
    }
}
