namespace C3dMcpBridge;

/// <summary>Blocks: insert a block (with attribute values), define a block from entities, change attribute values.</summary>
internal static class BlockOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["draw_block"] = DrawBlock;
        ops["block_define"] = BlockDefine;
        ops["attribute_set"] = AttributeSet;
    }

    /// <summary>The 'attributes' parameter: {TAG: value}; tags are case-insensitive.</summary>
    private static Dictionary<string, string> AttributeValues(JsonElement p)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Has(p, "attributes")) return values;
        var a = p.GetProperty("attributes");
        if (a.ValueKind != JsonValueKind.Object) throw new ArgumentException("attributes must be an object of TAG: value.");
        foreach (var kv in a.EnumerateObject()) values[kv.Name] = Text(kv.Value);
        return values;
    }

    /// <summary>Create the attribute references of a freshly appended block reference; returns the tags filled.</summary>
    private static List<string> FillAttributes(EditContext c, BlockReference br, BlockTableRecord def, Dictionary<string, string> values)
    {
        var filled = new List<string>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in def)
        {
            if (c.Tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition ad || ad.Constant) continue;
            known.Add(ad.Tag);
            var ar = new AttributeReference();
            ar.SetAttributeFromBlock(ad, br.BlockTransform);
            if (values.TryGetValue(ad.Tag, out var v)) { ar.TextString = v; filled.Add(ad.Tag); }
            br.AttributeCollection.AppendAttribute(ar);
            c.Tr.AddNewlyCreatedDBObject(ar, true);
        }
        foreach (var tag in values.Keys.Where(k => !known.Contains(k)))
            c.Warn($"block '{def.Name}' has no attribute '{tag}' (available: {string.Join(", ", known)}).");
        return filled;
    }

    private static object DrawBlock(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var bt = (BlockTable)c.Tr.GetObject(c.Db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(name)) throw new ArgumentException($"Block '{name}' is not defined in this drawing. Use list_blocks.");
        var def = (BlockTableRecord)c.Tr.GetObject(bt[name], OpenMode.ForRead);
        if (def.IsLayout) throw new ArgumentException($"'{name}' is a layout, not a block.");
        double scale = DblParam(p, "scale") ?? 1.0;
        if (Math.Abs(scale) < 1e-12) throw new ArgumentException("draw_block: scale cannot be zero.");
        var br = new BlockReference(NeedPt(p, "at", "draw_block"), def.ObjectId)
        {
            ScaleFactors = new Scale3d(scale),
            Rotation = Rad(DblParam(p, "rotation_deg") ?? 0),
        };
        var id = c.Append(br, p);
        var filled = def.HasAttributeDefinitions ? FillAttributes(c, br, def, AttributeValues(p)) : new List<string>();
        return new { handle = H(id), block = name, attributesFilled = filled };
    }

    private static object BlockDefine(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        CheckSymbolName(name, "block");
        var bt = (BlockTable)c.Tr.GetObject(c.Db.BlockTableId, OpenMode.ForWrite);
        if (bt.Has(name)) throw new ArgumentException($"Block '{name}' already exists.");
        var ents = c.OpenEntities(p, plainOnlyFor: "block_define");
        var basePoint = NeedPt(p, "base", "block_define");

        var def = new BlockTableRecord { Name = name, Origin = basePoint };
        var defId = bt.Add(def);
        c.Tr.AddNewlyCreatedDBObject(def, true);
        foreach (var e in ents)
        {
            var copy = (Entity)e.Clone();
            def.AppendEntity(copy);
            c.Tr.AddNewlyCreatedDBObject(copy, true);
        }

        string? inserted = null;
        if (BoolParam(p, "replace_originals") ?? false)
        {
            foreach (var e in ents) e.Erase();
            inserted = H(c.Append(new BlockReference(basePoint, defId), p));
        }
        return new { block = name, entities = ents.Count, insertedHandle = inserted };
    }

    private static object AttributeSet(EditContext c, JsonElement p)
    {
        string handle = NeedStr(p, "handle");
        var br = c.OpenByHandle<BlockReference>(p, "a block reference");
        var values = AttributeValues(p);
        if (values.Count == 0) throw new ArgumentException("attribute_set: give attributes {TAG: value}.");
        var changed = new List<string>();
        foreach (ObjectId id in br.AttributeCollection)
        {
            var ar = (AttributeReference)c.Tr.GetObject(id, OpenMode.ForWrite);
            if (!values.TryGetValue(ar.Tag, out var v)) continue;
            ar.TextString = v;
            changed.Add(ar.Tag);
        }
        var missing = values.Keys.Except(changed, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) throw new ArgumentException($"attribute_set: block reference {handle} has no attribute(s): {string.Join(", ", missing)}.");
        return new { handle, changed };
    }
}
