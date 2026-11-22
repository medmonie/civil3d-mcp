namespace C3dMcpBridge;

/// <summary>Blocks: insert a block (with attribute values), define a block from entities, change attribute values,
/// bring blocks in from another drawing (title blocks), set dynamic block properties.</summary>
internal static class BlockOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["draw_block"] = DrawBlock;
        ops["block_define"] = BlockDefine;
        ops["attribute_set"] = AttributeSet;
        ops["block_import"] = BlockImport;
        ops["title_block_insert"] = TitleBlockInsert;
        ops["dynamic_block_set"] = DynamicBlockSet;
    }

    /// <summary>The 'attributes' parameter: {TAG: value}; tags are case-insensitive.</summary>
    internal static Dictionary<string, string> AttributeValues(JsonElement p)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Has(p, "attributes")) return values;
        var a = p.GetProperty("attributes");
        if (a.ValueKind != JsonValueKind.Object) throw new ArgumentException("attributes must be an object of TAG: value.");
        foreach (var kv in a.EnumerateObject()) values[kv.Name] = Text(kv.Value);
        return values;
    }

    /// <summary>Create the attribute references of a freshly appended block reference; returns the tags filled.</summary>
    internal static List<string> FillAttributes(EditContext c, BlockReference br, BlockTableRecord def, Dictionary<string, string> values)
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
        return Insert(c, p, name, NeedPt(p, "at", "draw_block"));
    }

    /// <summary>Insert a defined block into model space or the operation's 'layout', with 'scale', 'rotation_deg' and
    /// 'attributes'.</summary>
    internal static object Insert(EditContext c, JsonElement p, string name, Point3d at)
    {
        var bt = (BlockTable)c.Tr.GetObject(c.Db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(name)) throw new ArgumentException($"Block '{name}' is not defined in this drawing. Use list_blocks, or block_import.");
        var def = (BlockTableRecord)c.Tr.GetObject(bt[name], OpenMode.ForRead);
        if (def.IsLayout) throw new ArgumentException($"'{name}' is a layout, not a block.");
        double scale = DblParam(p, "scale") ?? 1.0;
        if (Math.Abs(scale) < 1e-12) throw new ArgumentException("scale cannot be zero.");
        var br = new BlockReference(at, def.ObjectId)
        {
            ScaleFactors = new Scale3d(scale),
            Rotation = Rad(DblParam(p, "rotation_deg") ?? 0),
        };
        var id = c.Append(br, p);
        var filled = def.HasAttributeDefinitions ? FillAttributes(c, br, def, AttributeValues(p)) : new List<string>();
        return new { handle = H(id), block = name, layout = StrParam(p, "layout") ?? "Model", attributesFilled = filled };
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

    // ---------------------------------------------------------------- blocks from other drawings

    /// <summary>A drawing file read into a side database (never opened in the editor). Caller disposes.</summary>
    internal static Database ReadSide(string path, string op)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException($"{op}: path must be a full path such as C:\\Standards\\A1 title.dwg.");
        if (!new[] { ".dwg", ".dwt", ".dxf" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{op}: the file must be a .dwg, .dwt or .dxf.");
        if (!File.Exists(path)) throw new ArgumentException($"{op}: file not found: {path}");
        var side = new Database(false, true);
        try
        {
            if (Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase)) side.DxfIn(path, null);
            else side.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            side.CloseInput(true);
            return side;
        }
        catch { side.Dispose(); throw; }
    }

    /// <summary>Copy block definitions (or the whole file as one block) into this drawing; returns the names brought in.</summary>
    internal static List<string> Import(EditContext c, JsonElement p, string op, out List<string> skipped)
    {
        string path = Path.GetFullPath(NeedStr(p, "path"));
        bool replace = BoolParam(p, "replace") ?? false;
        var bt = (BlockTable)c.Tr.GetObject(c.Db.BlockTableId, OpenMode.ForRead);
        var names = Has(p, "blocks") ? StrList(p, "blocks", 200) : Has(p, "block") ? new List<string> { NeedStr(p, "block") } : new List<string>();
        var imported = new List<string>();
        skipped = new List<string>();
        using var side = ReadSide(path, op);
        if (names.Count == 0)
        {
            // the whole file becomes one block named after the file (or 'as_block')
            string name = StrParam(p, "as_block") ?? Path.GetFileNameWithoutExtension(path);
            CheckSymbolName(name, "block");
            if (bt.Has(name) && !replace) { skipped.Add(name); return imported; }
            BlockTableRecord def;
            if (bt.Has(name))
            {
                def = (BlockTableRecord)c.Tr.GetObject(bt[name], OpenMode.ForWrite);
                if (def.IsLayout || def.IsFromExternalReference) throw new ArgumentException($"{op}: '{name}' is a layout or an xref, not a block.");
                foreach (ObjectId id in def) c.Tr.GetObject(id, OpenMode.ForWrite).Erase();
            }
            else
            {
                def = new BlockTableRecord { Name = name };
                bt.UpgradeOpen();
                bt.Add(def);
                c.Tr.AddNewlyCreatedDBObject(def, true);
            }
            def.Origin = side.Insbase;
            // the file's model space becomes the block's content (with the blocks, layers and styles it uses)
            var content = new ObjectIdCollection();
            using (var st = side.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ModelSpace(side, st)) content.Add(id);
                st.Commit();
            }
            if (content.Count > 0)
                side.WblockCloneObjects(content, def.ObjectId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
            imported.Add(name);
            return imported;
        }
        var ids = new ObjectIdCollection();
        using (var st = side.TransactionManager.StartTransaction())
        {
            var sbt = (BlockTable)st.GetObject(side.BlockTableId, OpenMode.ForRead);
            foreach (var name in names)
            {
                if (!sbt.Has(name)) throw new ArgumentException($"{op}: block '{name}' is not in {Path.GetFileName(path)}.");
                var rec = (BlockTableRecord)st.GetObject(sbt[name], OpenMode.ForRead);
                if (rec.IsLayout) throw new ArgumentException($"{op}: '{name}' is a layout, not a block.");
                if (bt.Has(rec.Name) && !replace) { skipped.Add(rec.Name); continue; }
                ids.Add(rec.ObjectId);
                imported.Add(rec.Name);
            }
            st.Commit();
        }
        if (ids.Count > 0)
            side.WblockCloneObjects(ids, c.Db.BlockTableId, new IdMapping(),
                replace ? DuplicateRecordCloning.Replace : DuplicateRecordCloning.Ignore, false);
        return imported;
    }

    private static object BlockImport(EditContext c, JsonElement p)
    {
        var imported = Import(c, p, "block_import", out var skipped);
        if (skipped.Count > 0) c.Warn($"block_import: already defined, kept as they are (pass replace: true to redefine): {string.Join(", ", skipped)}");
        return new { imported, alreadyDefined = skipped };
    }

    /// <summary>Title block on a layout: bring it in from a file when 'path' is given, insert it, fill its attributes.</summary>
    private static object TitleBlockInsert(EditContext c, JsonElement p)
    {
        NeedStr(p, "layout");
        string name;
        if (Has(p, "path"))
        {
            var imported = Import(c, p, "title_block_insert", out var skipped);
            name = imported.Concat(skipped).FirstOrDefault()
                   ?? throw new ArgumentException("title_block_insert: nothing was imported.");
        }
        else name = NeedStr(p, "block");
        return Insert(c, p, name, OptPt(p, "at", "title_block_insert") ?? Point3d.Origin);
    }

    // ---------------------------------------------------------------- dynamic blocks

    private static object DynamicBlockSet(EditContext c, JsonElement p)
    {
        string handle = NeedStr(p, "handle");
        var br = c.OpenByHandle<BlockReference>(p, "a block reference");
        if (!br.IsDynamicBlock) throw new ArgumentException($"dynamic_block_set: {handle} is not a dynamic block.");
        if (!Has(p, "properties") || p.GetProperty("properties").ValueKind != JsonValueKind.Object)
            throw new ArgumentException("dynamic_block_set: give properties {name: value}.");
        var props = br.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().ToList();
        var changed = new Dictionary<string, object?>();
        foreach (var kv in p.GetProperty("properties").EnumerateObject())
        {
            var prop = props.FirstOrDefault(x => SameName(x.PropertyName, kv.Name))
                       ?? throw new ArgumentException($"dynamic_block_set: {handle} has no property '{kv.Name}'. Available: {string.Join(", ", props.Where(x => !x.ReadOnly).Select(x => x.PropertyName))}.");
            if (prop.ReadOnly) throw new ArgumentException($"dynamic_block_set: property '{prop.PropertyName}' is read-only.");
            object value = ToPropertyValue(prop, kv.Value);
            var allowed = prop.GetAllowedValues();
            if (allowed.Length > 0 && !allowed.Any(a => Equals(a, value) || SameName(a?.ToString(), value.ToString())))
                throw new ArgumentException($"dynamic_block_set: '{prop.PropertyName}' accepts only: {string.Join(", ", allowed)}.");
            if (allowed.Length > 0) value = allowed.First(a => Equals(a, value) || SameName(a?.ToString(), value.ToString()));
            prop.Value = value;
            changed[prop.PropertyName] = prop.Value;
        }
        return new { handle, changed };
    }

    /// <summary>A JSON value as the type the property already holds (double, short, int or text).</summary>
    private static object ToPropertyValue(DynamicBlockReferenceProperty prop, JsonElement v)
    {
        var current = prop.Value;
        string text = Text(v);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            return current switch
            {
                double => double.Parse(text, inv),
                short => short.Parse(text, inv),
                int => int.Parse(text, inv),
                _ => text,
            };
        }
        catch (FormatException)
        {
            throw new ArgumentException($"dynamic_block_set: '{prop.PropertyName}' needs a {current?.GetType().Name ?? "value"}, not '{text}'.");
        }
    }
}
