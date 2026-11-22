namespace C3dMcpBridge;

/// <summary>
/// AutoCAD drawing set-up: text, dimension and multileader styles, linetypes, annotation scales, layer states, purge,
/// xref management (reload / detach / bind run right after the commit, when nothing has them open) and PDF / image
/// underlays.
/// </summary>
internal static class CadOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["text_style_create"] = TextStyleCreate;
        ops["dim_style_create"] = DimStyleCreate;
        ops["mleader_style_create"] = MLeaderStyleCreate;
        ops["linetype_load"] = LinetypeLoad;
        ops["annotation_scale_add"] = AnnotationScaleAdd;
        ops["layer_state_save"] = LayerStateSave;
        ops["layer_state_restore"] = LayerStateRestore;
        ops["purge"] = Purge;
        ops["xref_reload"] = (c, p) => XrefQueue(c, p, "reload");
        ops["xref_detach"] = (c, p) => XrefQueue(c, p, "detach");
        ops["xref_bind"] = (c, p) => XrefQueue(c, p, "bind");
        ops["underlay_attach"] = UnderlayAttach;
    }

    /// <summary>'if_exists': error (default), skip (keep the existing one) or replace (update it with the values given).</summary>
    private static string IfExists(JsonElement p) => Choice(p, "if_exists", "error", "error", "skip", "replace");

    /// <summary>Open a record of a symbol table for writing: the existing one (if_exists replace) or a new one.
    /// Returns null when an existing one is to be kept.</summary>
    private static T? OpenOrCreate<T>(EditContext c, ObjectId tableId, string name, string what, JsonElement p, Func<T> create, out bool created)
        where T : SymbolTableRecord
    {
        CheckSymbolName(name, what);
        var table = (SymbolTable)c.Tr.GetObject(tableId, OpenMode.ForRead);
        created = false;
        if (table.Has(name))
        {
            switch (IfExists(p))
            {
                case "skip": return null;
                case "error": throw new ArgumentException($"{what} '{name}' already exists (if_exists: skip or replace).");
            }
            return (T)c.Tr.GetObject(table[name], OpenMode.ForWrite);
        }
        var rec = create();
        rec.Name = name;
        table.UpgradeOpen();
        table.Add(rec);
        c.Tr.AddNewlyCreatedDBObject(rec, true);
        created = true;
        return rec;
    }

    // ---------------------------------------------------------------- styles

    private static object TextStyleCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var ts = OpenOrCreate(c, c.Db.TextStyleTableId, name, "Text style", p, () => new TextStyleTableRecord { FileName = "arial.ttf" }, out bool created);
        if (ts == null) return new { style = name, kept = true };
        if (StrParam(p, "font") is string font)
        {
            // .shx and .ttf file names both go into FileName; a bare name is taken as a TrueType font file.
            ts.FileName = Path.HasExtension(font) ? font : font + ".ttf";
        }
        if (DblParam(p, "height") is double h)
        {
            if (h < 0) throw new ArgumentException("text_style_create: height cannot be negative (0 = set per text).");
            ts.TextSize = h;
        }
        if (PositiveParam(p, "width_factor") is double wf) ts.XScale = wf;
        if (DblParam(p, "oblique_deg") is double ob) ts.ObliquingAngle = Rad(ob);
        if (BoolParam(p, "set_current") ?? false) c.Db.Textstyle = ts.ObjectId;
        return new { style = name, created, font = ts.FileName, height = R3(ts.TextSize) };
    }

    private static object DimStyleCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var ds = OpenOrCreate(c, c.Db.DimStyleTableId, name, "Dimension style", p, () => new DimStyleTableRecord(), out bool created);
        if (ds == null) return new { style = name, kept = true };
        if (created)
        {
            // start from 'based_on' (or the current style) so every variable has a sensible value
            var baseId = StrParam(p, "based_on") is string b ? c.Symbol(c.Db.DimStyleTableId, b, "Dimension style") : c.Db.Dimstyle;
            ds.CopyFrom(c.Tr.GetObject(baseId, OpenMode.ForRead));
            ds.Name = name;
        }
        if (StrParam(p, "text_style") is string tsName) ds.Dimtxsty = c.Symbol(c.Db.TextStyleTableId, tsName, "Text style");
        if (PositiveParam(p, "text_height") is double th) ds.Dimtxt = th;
        if (PositiveParam(p, "arrow_size") is double asz) ds.Dimasz = asz;
        if (DblParam(p, "extension_offset") is double exo) ds.Dimexo = exo;
        if (DblParam(p, "extension_beyond") is double exe) ds.Dimexe = exe;
        if (DblParam(p, "text_gap") is double gap) ds.Dimgap = gap;
        if (Has(p, "decimals")) ds.Dimdec = IntParam(p, "decimals", 2, 0, 8);
        if (PositiveParam(p, "overall_scale") is double sc) ds.Dimscale = sc;
        if (PositiveParam(p, "linear_scale") is double lf) ds.Dimlfac = lf;
        if (BoolParam(p, "text_above") is bool above)
        {
            ds.Dimtad = above ? 1 : 0;
            ds.Dimtih = !above;
            ds.Dimtoh = !above;
        }
        if (BoolParam(p, "set_current") ?? false) { c.Db.Dimstyle = ds.ObjectId; c.Db.SetDimstyleData(ds); }
        return new { style = name, created, textHeight = R3(ds.Dimtxt), arrowSize = R3(ds.Dimasz), overallScale = R3(ds.Dimscale), decimals = ds.Dimdec };
    }

    private static object MLeaderStyleCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        CheckSymbolName(name, "multileader style");
        var dict = (DBDictionary)c.Tr.GetObject(c.Db.MLeaderStyleDictionaryId, OpenMode.ForRead);
        MLeaderStyle style;
        bool created = false;
        var existing = Entries(dict).FirstOrDefault(e => SameName(e.Key, name));
        if (existing.Value.IsValid)
        {
            switch (IfExists(p))
            {
                case "skip": return new { style = name, kept = true };
                case "error": throw new ArgumentException($"Multileader style '{name}' already exists (if_exists: skip or replace).");
            }
            style = (MLeaderStyle)c.Tr.GetObject(existing.Value, OpenMode.ForWrite);
        }
        else
        {
            var baseId = StrParam(p, "based_on") is string b ? c.MLeaderStyleId(b) : c.Db.MLeaderstyle;
            style = new MLeaderStyle();
            style.CopyFrom(c.Tr.GetObject(baseId, OpenMode.ForRead));
            var id = style.PostMLeaderStyleToDb(c.Db, name);
            c.Tr.AddNewlyCreatedDBObject(style, true);
            style = (MLeaderStyle)c.Tr.GetObject(id, OpenMode.ForWrite);
            created = true;
        }
        style.ContentType = ContentType.MTextContent;
        if (StrParam(p, "text_style") is string tsName) style.TextStyleId = c.Symbol(c.Db.TextStyleTableId, tsName, "Text style");
        if (PositiveParam(p, "text_height") is double th) style.TextHeight = th;
        if (PositiveParam(p, "arrow_size") is double asz) style.ArrowSize = asz;
        if (DblParam(p, "landing_gap") is double lg) style.LandingGap = lg;
        if (DblParam(p, "dogleg_length") is double dl) { style.EnableDogleg = dl > 0; if (dl > 0) style.DoglegLength = dl; }
        if (BoolParam(p, "set_current") ?? false) c.Db.MLeaderstyle = style.ObjectId;
        return new { style = name, created, textHeight = R3(style.TextHeight), arrowSize = R3(style.ArrowSize) };
    }

    private static object LinetypeLoad(EditContext c, JsonElement p)
    {
        string file = StrParam(p, "file") ?? (c.Db.Measurement == MeasurementValue.Metric ? "acadiso.lin" : "acad.lin");
        var table = (LinetypeTable)c.Tr.GetObject(c.Db.LinetypeTableId, OpenMode.ForRead);
        var loaded = new List<string>();
        var already = new List<string>();
        foreach (var name in StrList(p, "names", 500))
        {
            if (table.Has(name)) { already.Add(name); continue; }
            try { c.Db.LoadLineTypeFile(name, file); }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new ArgumentException($"linetype_load: '{name}' could not be loaded from {file}: {ex.Message}");
            }
            loaded.Add(name);
        }
        return new { file, loaded, alreadyLoaded = already };
    }

    private static object AnnotationScaleAdd(EditContext c, JsonElement p)
    {
        double n = ScaleDenominator(p, "scale") ?? throw new ArgumentException("annotation_scale_add: give scale, e.g. \"1:500\".");
        string name = StrParam(p, "name") ?? $"1:{n:0.###}";
        var scales = c.Db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
        bool created = false;
        if (!scales.HasContext(name))
        {
            // paper millimetres to drawing units: 1:500 in a metre drawing is 1 mm = 0.5 m
            var scale = new AnnotationScale { Name = name, PaperUnits = 1, DrawingUnits = n / UnitMm(c.Db) };
            scales.AddContext(scale);
            created = true;
        }
        if (BoolParam(p, "set_current") ?? false) c.Db.Cannoscale = (AnnotationScale)scales.GetContext(name);
        var s = (AnnotationScale)scales.GetContext(name);
        return new { scale = name, created, paperUnits = s.PaperUnits, drawingUnits = R3(s.DrawingUnits), current = SameName(c.Db.Cannoscale.Name, name) };
    }

    // ---------------------------------------------------------------- layer states

    private const LayerStateMasks AllLayerProperties = LayerStateMasks.On | LayerStateMasks.Frozen | LayerStateMasks.Locked |
        LayerStateMasks.Plot | LayerStateMasks.NewViewport | LayerStateMasks.Color | LayerStateMasks.LineType |
        LayerStateMasks.LineWeight | LayerStateMasks.PlotStyle | LayerStateMasks.Transparency;

    private static object LayerStateSave(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var lsm = c.Db.LayerStateManager;
        if (lsm.HasLayerState(name))
        {
            if (IfExists(p) == "error") throw new ArgumentException($"Layer state '{name}' already exists (if_exists: replace).");
            if (IfExists(p) == "skip") return new { layerState = name, kept = true };
            lsm.DeleteLayerState(name);
        }
        lsm.SaveLayerState(name, AllLayerProperties, ObjectId.Null);
        if (StrParam(p, "description") is string d) lsm.SetLayerStateDescription(name, d);
        return new { layerState = name, saved = true };
    }

    private static object LayerStateRestore(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var lsm = c.Db.LayerStateManager;
        if (!lsm.HasLayerState(name)) throw new ArgumentException($"Layer state '{name}' not found. Use list_cad_styles kind layer_state.");
        lsm.RestoreLayerState(name, ObjectId.Null, 0, lsm.GetLayerStateMask(name));
        return new { layerState = name, restored = true };
    }

    // ---------------------------------------------------------------- purge

    private static readonly string[] PurgeKinds = { "blocks", "layers", "linetypes", "text_styles", "dim_styles", "mleader_styles", "table_styles", "regapps" };

    private static object Purge(EditContext c, JsonElement p)
    {
        var kinds = Has(p, "kinds") ? StrList(p, "kinds", 20).Select(k => k.ToLowerInvariant()).ToList() : PurgeKinds.ToList();
        var unknown = kinds.Except(PurgeKinds).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"purge: unknown kinds {string.Join(", ", unknown)}. Use: {string.Join(", ", PurgeKinds)}.");
        bool dryList = BoolParam(p, "list_only") ?? false;
        var purged = new Dictionary<string, List<string>>();

        // repeat: purging a block can free the layers and styles only it used
        for (int pass = 0; pass < 5; pass++)
        {
            var candidates = new List<(string kind, ObjectId id, string name)>();
            foreach (var kind in kinds)
                foreach (var (id, name) in Candidates(c, kind))
                    candidates.Add((kind, id, name));
            var ids = new ObjectIdCollection(candidates.Select(x => x.id).ToArray());
            c.Db.Purge(ids);
            var free = new HashSet<ObjectId>(ids.Cast<ObjectId>());
            int count = 0;
            foreach (var (kind, id, name) in candidates.Where(x => free.Contains(x.id)))
            {
                if (!purged.TryGetValue(kind, out var list)) purged[kind] = list = new List<string>();
                if (list.Contains(name)) continue;
                list.Add(name);
                count++;
                if (!dryList) c.Tr.GetObject(id, OpenMode.ForWrite).Erase();
            }
            if (count == 0 || dryList) break;
        }
        return new { purged, total = purged.Values.Sum(l => l.Count), listOnly = dryList };
    }

    /// <summary>Records that may be purged (never layer 0, Defpoints, the current ones, xref-dependent or layouts).</summary>
    private static IEnumerable<(ObjectId, string)> Candidates(EditContext c, string kind)
    {
        IEnumerable<(ObjectId, string)> Table(ObjectId tableId, Func<SymbolTableRecord, bool>? keep = null)
        {
            var table = (SymbolTable)c.Tr.GetObject(tableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                if (id.IsErased) continue;
                var r = (SymbolTableRecord)c.Tr.GetObject(id, OpenMode.ForRead);
                if (r.IsDependent || r.IsErased || (keep?.Invoke(r) ?? false)) continue;
                yield return (id, r.Name);
            }
        }
        IEnumerable<(ObjectId, string)> Dict(ObjectId dictId)
        {
            var dict = (DBDictionary)c.Tr.GetObject(dictId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dict) if (!e.Value.IsErased) yield return (e.Value, e.Key);
        }
        return kind switch
        {
            "blocks" => Table(c.Db.BlockTableId, r => r is BlockTableRecord b && (b.IsLayout || b.IsFromExternalReference || b.IsAnonymous)),
            "layers" => Table(c.Db.LayerTableId, r => r.Name == "0" || SameName(r.Name, "Defpoints") || r.ObjectId == c.Db.Clayer),
            "linetypes" => Table(c.Db.LinetypeTableId, r => r.Name is "ByLayer" or "ByBlock" or "Continuous" || r.ObjectId == c.Db.Celtype),
            "text_styles" => Table(c.Db.TextStyleTableId, r => r.ObjectId == c.Db.Textstyle),
            "dim_styles" => Table(c.Db.DimStyleTableId, r => r.ObjectId == c.Db.Dimstyle),
            "regapps" => Table(c.Db.RegAppTableId),
            "mleader_styles" => Dict(c.Db.MLeaderStyleDictionaryId).Where(x => x.Item1 != c.Db.MLeaderstyle),
            "table_styles" => Dict(c.Db.TableStyleDictionaryId).Where(x => x.Item1 != c.Db.Tablestyle),
            _ => Enumerable.Empty<(ObjectId, string)>(),
        };
    }

    // ---------------------------------------------------------------- xrefs

    /// <summary>Xref blocks by name ('names'; all xrefs when absent).</summary>
    private static List<(ObjectId id, string name)> Xrefs(EditContext c, JsonElement p, string op)
    {
        var bt = (BlockTable)c.Tr.GetObject(c.Db.BlockTableId, OpenMode.ForRead);
        var all = new List<(ObjectId, string)>();
        foreach (ObjectId id in bt)
        {
            var b = (BlockTableRecord)c.Tr.GetObject(id, OpenMode.ForRead);
            if (b.IsFromExternalReference && !b.IsDependent) all.Add((id, b.Name));
        }
        if (!Has(p, "names")) return all.Count > 0 ? all : throw new ArgumentException($"{op}: the drawing has no xrefs.");
        return StrList(p, "names", 200).Select(n => all.FirstOrDefault(x => SameName(x.Item2, n)) is var hit && hit.Item1.IsValid
            ? hit : throw new ArgumentException($"{op}: '{n}' is not an xref of this drawing. Use list_xrefs.")).ToList();
    }

    private static object XrefQueue(EditContext c, JsonElement p, string action)
    {
        var xrefs = Xrefs(c, p, "xref_" + action);
        bool insert = BoolParam(p, "insert") ?? false;
        var db = c.Db;
        var ids = new ObjectIdCollection(xrefs.Select(x => x.id).ToArray());
        c.AfterCommit.Add(() =>
        {
            switch (action)
            {
                case "reload": db.ReloadXrefs(ids); break;
                case "detach": foreach (ObjectId id in ids) db.DetachXref(id); break;
                default: db.BindXrefs(ids, insert); break;
            }
        });
        return new
        {
            queued = true, action, xrefs = xrefs.Select(x => x.name).ToList(),
            note = action == "bind" ? (insert ? "Bound as plain blocks (Insert: names without the xref prefix)." : "Bound (Bind: names keep the xref$0$ prefix).") + " Runs after the commit."
                                    : "Runs after the commit.",
        };
    }

    // ---------------------------------------------------------------- underlays

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif" };

    private static object UnderlayAttach(EditContext c, JsonElement p)
    {
        string path = NeedStr(p, "path");
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("underlay_attach: path must be a full path.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new ArgumentException($"underlay_attach: file not found: {path}");
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var at = OptPt(p, "at", "underlay_attach") ?? Point3d.Origin;
        double rotation = Rad(DblParam(p, "rotation_deg") ?? 0);
        string stem = Path.GetFileNameWithoutExtension(path);
        var nod = (DBDictionary)c.Tr.GetObject(c.Db.NamedObjectsDictionaryId, OpenMode.ForRead);

        if (ext == ".pdf")
        {
            string page = StrParam(p, "page") ?? (Has(p, "page") ? Text(p.GetProperty("page")) : "1");
            string key = UnderlayDefinition.GetDictionaryKey(typeof(PdfDefinition));
            ObjectId dictId;
            if (nod.Contains(key)) dictId = nod.GetAt(key);
            else
            {
                nod.UpgradeOpen();
                var d = new DBDictionary();
                dictId = nod.SetAt(key, d);
                c.Tr.AddNewlyCreatedDBObject(d, true);
            }
            var defs = (DBDictionary)c.Tr.GetObject(dictId, OpenMode.ForWrite);
            string defName = UniqueKey(defs, $"{stem} - {page}");
            var def = new PdfDefinition { SourceFileName = path, ItemName = page };
            var defId = defs.SetAt(defName, def);
            c.Tr.AddNewlyCreatedDBObject(def, true);
            try { def.Load(null); }
            catch (Exception ex) { throw new ArgumentException($"underlay_attach: page '{page}' of {Path.GetFileName(path)} could not be loaded: {ex.Message}"); }
            var r = new PdfReference { DefinitionId = defId, Position = at, Rotation = rotation, ScaleFactors = new Scale3d(DblParam(p, "scale") ?? 1.0) };
            var id = c.Append(r, p);
            return new { handle = H(id), kind = "pdf", definition = defName, page };
        }
        if (ImageExtensions.Contains(ext))
        {
            var dictId = RasterImageDef.GetImageDictionary(c.Db);
            if (dictId.IsNull) dictId = RasterImageDef.CreateImageDictionary(c.Db);
            var defs = (DBDictionary)c.Tr.GetObject(dictId, OpenMode.ForWrite);
            string defName = UniqueKey(defs, stem);
            var def = new RasterImageDef { SourceFileName = path };
            def.Load();
            var defId = defs.SetAt(defName, def);
            c.Tr.AddNewlyCreatedDBObject(def, true);
            // width in drawing units (default: the image's own size in pixels x its resolution, else 1 unit per pixel)
            var size = def.Size;
            double width = PositiveParam(p, "width") ?? size.X;
            double height = width * size.Y / size.X;
            var u = new Vector3d(Math.Cos(rotation), Math.Sin(rotation), 0);
            var v = new Vector3d(-Math.Sin(rotation), Math.Cos(rotation), 0);
            var img = new RasterImage { ImageDefId = defId, ShowImage = true, Orientation = new CoordinateSystem3d(at, u * width, v * height) };
            var id = c.Append(img, p);
            img.AssociateRasterDef(def);
            return new { handle = H(id), kind = "image", definition = defName, width = R3(width), height = R3(height) };
        }
        throw new ArgumentException($"underlay_attach: use a .pdf or an image ({string.Join(", ", ImageExtensions)}); DWG files go through xref_attach.");
    }

    private static string UniqueKey(DBDictionary dict, string name)
    {
        string key = name;
        for (int i = 2; dict.Contains(key); i++) key = $"{name} ({i})";
        return key;
    }
}
