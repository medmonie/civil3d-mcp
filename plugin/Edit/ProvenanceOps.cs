namespace C3dMcpBridge;

/// <summary>source_tag_set / get_source_tags: write and read import provenance (see Core/Provenance.cs).</summary>
internal static class ProvenanceOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["source_tag_set"] = SourceTagSet;
    }

    private static object SourceTagSet(EditContext c, JsonElement p)
    {
        bool clear = BoolParam(p, "clear") ?? false;
        var rows = new List<object>();
        foreach (var item in ArrParam(p, "items", 20000))
        {
            string handle = NeedStr(item, "handle");
            var obj = c.Tr.GetObject(IdFromHandle(c.Db, handle), OpenMode.ForWrite);
            if (clear) { Provenance.Clear(obj); rows.Add(new { handle, cleared = true }); continue; }
            var tag = Provenance.FromSpec(item, p) ?? throw new ArgumentException($"source_tag_set: item {handle} needs source_id.");
            Provenance.Write(c.Tr, c.Db, obj, tag);
            rows.Add(new { handle, source = tag.Source, sourceId = tag.Id, run = tag.Run });
        }
        if (rows.Count == 0) throw new ArgumentException("source_tag_set: give items [{handle, source_id}, ...].");
        return new { count = rows.Count, items = rows };
    }

    /// <summary>Objects carrying a source tag: by handle, or every tagged object of model space (filtered).</summary>
    public static object GetSourceTags(JsonElement p) => WithRead<object>((db, tr) =>
    {
        int limit = IntParam(p, "limit", 500, 1, 20000);
        string? source = StrParam(p, "source"), id = StrParam(p, "source_id");
        IEnumerable<ObjectId> ids;
        if (Has(p, "handles"))
            ids = ArrParam(p, "handles", 20000).Select(h => IdFromHandle(db, h.GetString() ?? "")).ToList();
        else
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            ids = ((BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead)).Cast<ObjectId>();
        }
        var rows = new List<object>();
        int total = 0;
        foreach (var oid in ids)
        {
            var obj = tr.GetObject(oid, OpenMode.ForRead);
            var tag = Provenance.Read(obj);
            if (tag == null) continue;
            if (source != null && !tag.Source.Contains(source, StringComparison.OrdinalIgnoreCase)) continue;
            if (id != null && !SameName(tag.Id, id)) continue;
            total++;
            if (rows.Count >= limit) continue;
            rows.Add(new
            {
                handle = H(oid), type = DxfOrName(oid), name = Safe(() => (obj as CivilDb.Entity)?.Name),
                source = tag.Source, sourceId = tag.Id, run = tag.Run,
            });
        }
        return new { total, returned = rows.Count, truncated = total > rows.Count, objects = rows };
    });
}
