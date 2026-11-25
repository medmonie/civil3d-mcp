namespace C3dMcpBridge;

/// <summary>Read actions for sheets and drawing set-up: viewports, named views, xrefs, AutoCAD styles, block properties.</summary>
internal static class SheetRead
{
    public static object ListViewports(JsonElement p) => WithRead((db, tr) =>
    {
        string? only = StrParam(p, "layout");
        var layouts = PaperLayouts(db, tr).Where(l => only == null || SameName(l.LayoutName, only)).ToList();
        if (only != null && layouts.Count == 0) throw new ArgumentException($"Layout '{only}' not found. Use list_layouts.");
        var rows = new List<object>();
        foreach (var l in layouts)
        {
            var space = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
            var all = space.Cast<ObjectId>().Where(id => id.ObjectClass.DxfName == "VIEWPORT" && !id.IsErased)
                .Select(id => (Viewport)tr.GetObject(id, OpenMode.ForRead)).ToList();
            // paper space itself: viewport number 1 when the layout has been shown, else the first one
            var paper = all.FirstOrDefault(v => Safe(() => v.Number) is int n && n == 1) ?? all.FirstOrDefault();
            foreach (var vp in all)
            {
                var id = vp.ObjectId;
                bool paperViewport = vp == paper;
                if (paperViewport && !(BoolParam(p, "include_paper") ?? false)) continue;
                rows.Add(new
                {
                    layout = l.LayoutName, handle = H(id), paperViewport,
                    at = new[] { R3(vp.CenterPoint.X), R3(vp.CenterPoint.Y) }, width = R3(vp.Width), height = R3(vp.Height),
                    scale = ScaleText(db, vp.CustomScale), customScale = vp.CustomScale,
                    viewCenter = new[] { R3(vp.ViewTarget.X + vp.ViewCenter.X), R3(vp.ViewTarget.Y + vp.ViewCenter.Y) },
                    twistDeg = Deg(vp.TwistAngle), locked = vp.Locked, on = vp.On, layer = vp.Layer,
                    frozenLayers = Safe(() => vp.GetFrozenLayers().Cast<ObjectId>().Select(x => ((LayerTableRecord)tr.GetObject(x, OpenMode.ForRead)).Name).ToList()),
                });
            }
        }
        return new { count = rows.Count, viewports = rows };
    });

    public static object ListViews(JsonElement p) => WithRead((db, tr) =>
    {
        var table = (ViewTable)tr.GetObject(db.ViewTableId, OpenMode.ForRead);
        var rows = table.Cast<ObjectId>().Select(id => (ViewTableRecord)tr.GetObject(id, OpenMode.ForRead)).Select(v => new
        {
            name = v.Name, paperSpace = v.IsPaperspaceView,
            target = new[] { R3(v.Target.X), R3(v.Target.Y) }, center = new[] { R3(v.CenterPoint.X), R3(v.CenterPoint.Y) },
            width = R3(v.Width), height = R3(v.Height), twistDeg = Deg(v.ViewTwist),
        }).ToList();
        return new { count = rows.Count, views = rows };
    });

    public static object ListXrefs(JsonElement p) => WithRead((db, tr) =>
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var rows = new List<object>();
        foreach (ObjectId id in bt)
        {
            var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!b.IsFromExternalReference) continue;
            rows.Add(new
            {
                name = b.Name, path = b.PathName, status = b.XrefStatus.ToString(), nested = b.IsDependent,
                overlay = Safe(() => b.IsFromOverlayReference), unloaded = Safe(() => b.IsUnloaded),
                references = Safe(() => b.GetBlockReferenceIds(true, false).Count),
            });
        }
        return new { count = rows.Count, xrefs = rows };
    });

    private static readonly string[] Kinds = { "text_style", "dim_style", "mleader_style", "table_style", "linetype", "layer_state", "annotation_scale", "page_setup" };

    public static object ListCadStyles(JsonElement p)
    {
        string kind = Choice(p, "kind", "text_style", Kinds);
        return WithRead((db, tr) =>
        {
            List<object> rows = new();
            string? current = null;
            switch (kind)
            {
                case "text_style":
                    current = Name(tr, db.Textstyle);
                    foreach (ObjectId id in (SymbolTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead))
                    {
                        var t = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (t.IsShapeFile || t.Name.Length == 0) continue;
                        rows.Add(new { name = t.Name, font = t.FileName, typeface = Safe(() => t.Font.TypeFace), height = R3(t.TextSize), widthFactor = R3(t.XScale), xref = t.IsDependent });
                    }
                    break;
                case "dim_style":
                    current = Name(tr, db.Dimstyle);
                    foreach (ObjectId id in (SymbolTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead))
                    {
                        var d = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        rows.Add(new { name = d.Name, textStyle = Name(tr, d.Dimtxsty), textHeight = R3(d.Dimtxt), arrowSize = R3(d.Dimasz), overallScale = R3(d.Dimscale), decimals = d.Dimdec, xref = d.IsDependent });
                    }
                    break;
                case "linetype":
                    current = Name(tr, db.Celtype);
                    foreach (ObjectId id in (SymbolTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))
                    {
                        var l = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        rows.Add(new { name = l.Name, description = l.AsciiDescription, patternLength = R3(l.PatternLength), xref = l.IsDependent });
                    }
                    break;
                case "mleader_style":
                    current = DictName(tr, db.MLeaderStyleDictionaryId, db.MLeaderstyle);
                    foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead))
                    {
                        var m = (MLeaderStyle)tr.GetObject(e.Value, OpenMode.ForRead);
                        rows.Add(new { name = e.Key, textStyle = Name(tr, m.TextStyleId), textHeight = R3(m.TextHeight), arrowSize = R3(m.ArrowSize) });
                    }
                    break;
                case "table_style":
                    current = DictName(tr, db.TableStyleDictionaryId, db.Tablestyle);
                    foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead))
                        rows.Add(new { name = e.Key });
                    break;
                case "layer_state":
                    var lsm = db.LayerStateManager;
                    current = Safe(() => lsm.LastRestoredLayerState) as string;
                    foreach (var n in lsm.GetLayerStateNames(false, false).Cast<string>())
                        rows.Add(new { name = n, description = Safe(() => lsm.GetLayerStateDescription(n)) });
                    break;
                case "annotation_scale":
                    current = db.Cannoscale.Name;
                    var scales = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
                    foreach (var ctx in scales)
                        if (ctx is AnnotationScale s) rows.Add(new { name = s.Name, paperUnits = s.PaperUnits, drawingUnits = R3(s.DrawingUnits) });
                    break;
                case "page_setup":
                    foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForRead))
                    {
                        var ps = (PlotSettings)tr.GetObject(e.Value, OpenMode.ForRead);
                        rows.Add(new { name = e.Key, device = ps.PlotConfigurationName, paper = ps.CanonicalMediaName, plotStyle = ps.CurrentStyleSheet, modelSpace = ps.ModelType });
                    }
                    break;
            }
            return new { kind, current, count = rows.Count, items = rows };
        });
    }

    private static string? Name(Transaction tr, ObjectId id) =>
        id.IsNull ? null : (tr.GetObject(id, OpenMode.ForRead) as SymbolTableRecord)?.Name;

    private static string? DictName(Transaction tr, ObjectId dictId, ObjectId id)
    {
        foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead))
            if (e.Value == id) return e.Key;
        return null;
    }

    private static Dictionary<string, string> Attributes(Transaction tr, BlockReference br)
    {
        var d = new Dictionary<string, string>();
        foreach (ObjectId id in br.AttributeCollection)
            if (tr.GetObject(id, OpenMode.ForRead) is AttributeReference a) d[a.Tag] = a.TextString;
        return d;
    }

    public static object GetBlockProperties(JsonElement p) => WithRead((db, tr) =>
    {
        string handle = NeedStr(p, "handle");
        var br = tr.GetObject(IdFromHandle(db, handle), OpenMode.ForRead) as BlockReference
                 ?? throw new ArgumentException($"Object {handle} is not a block reference.");
        var def = (BlockTableRecord)tr.GetObject(br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord, OpenMode.ForRead);
        var attributes = new Dictionary<string, string>();
        foreach (ObjectId id in br.AttributeCollection)
        {
            var ar = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
            attributes[ar.Tag] = ar.TextString;
        }
        var props = br.IsDynamicBlock
            ? br.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                .Where(x => x.Show || !(BoolParam(p, "visible_only") ?? true))
                .Select(x => (object)new
                {
                    name = x.PropertyName, value = x.Value?.ToString(), type = x.Value?.GetType().Name, readOnly = x.ReadOnly,
                    allowed = x.GetAllowedValues().Select(a => a?.ToString()).ToList(), units = x.UnitsType.ToString(), description = x.Description,
                }).ToList()
            : new List<object>();
        return new
        {
            handle, block = def.Name, dynamic = br.IsDynamicBlock, layer = br.Layer,
            at = P3(br.Position), rotationDeg = Deg(br.Rotation), scale = R3(br.ScaleFactors.X), attributes, properties = props,
        };
    });

    // ---------------------------------------------------------------- a drawing file on disk

    /// <summary>
    /// Read a .dwg / .dwt / .dxf from disk without opening it in the editor: layouts (paper, scale, viewports with
    /// their scale and model area), what is drawn on each layout (block references with attributes, text), xrefs,
    /// block definitions with their attribute tags, named views, page setups, and a summary of model space.
    /// </summary>
    public static object InspectFile(JsonElement p)
    {
        string path = NeedFullPath(p, "path", "inspect_file");
        // 'limit' is one budget for the whole reply: layout entities, blocks, views and page setups together.
        int limit = IntParam(p, "limit", 60, 1, 2000), budget = limit;
        string? onlyLayout = StrParam(p, "layout");
        var truncated = new List<string>();
        List<T> Take<T>(IEnumerable<T> items, int total, string what)
        {
            var list = items.Take(Math.Max(0, budget)).ToList();
            budget -= list.Count;
            if (list.Count < total) truncated.Add($"{what}: {list.Count} of {total}");
            return list;
        }
        using var side = BlockOps.ReadSide(path, "inspect_file");
        using var tr = side.TransactionManager.StartTransaction();
        object Ent(Entity e) => new
        {
            handle = H(e.ObjectId), type = DxfOrName(e.ObjectId), layer = e.Layer,
            block = e is BlockReference br ? Safe(() => ((BlockTableRecord)tr.GetObject(br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord, OpenMode.ForRead)).Name) : null,
            // title blocks often repeat a tag: ToDictionary would throw and fail the whole inspection; the last one wins
            attributes = e is BlockReference b2 ? Attributes(tr, b2) : null,
            geometry = Safe(() => DrawingRead.Geometry(tr, e)),
        };

        var layouts = new List<object>();
        var allLayouts = PaperLayouts(side, tr).ToList();
        if (onlyLayout != null && !allLayouts.Any(l => l.LayoutName.Equals(onlyLayout, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"inspect_file: no layout '{onlyLayout}' in the file. Layouts: {string.Join(", ", allLayouts.Select(l => l.LayoutName))}");
        foreach (var l in allLayouts)
        {
            if (onlyLayout != null && !l.LayoutName.Equals(onlyLayout, StringComparison.OrdinalIgnoreCase)) continue;
            var space = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
            var ents = space.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead) as Entity).Where(e => e != null).Cast<Entity>().ToList();
            var vps = ents.OfType<Viewport>().ToList();
            var paper = vps.FirstOrDefault(v => Safe(() => v.Number) is int n && n == 1) ?? vps.FirstOrDefault();
            layouts.Add(new
            {
                name = l.LayoutName, tabOrder = l.TabOrder, device = l.PlotConfigurationName, paper = l.CanonicalMediaName,
                paperSize = new[] { R3(l.PlotPaperSize.X), R3(l.PlotPaperSize.Y) }, rotation = l.PlotRotation.ToString(), plotStyle = l.CurrentStyleSheet,
                viewports = vps.Where(v => v != paper).Select(vp => new
                {
                    handle = H(vp.ObjectId), at = new[] { R3(vp.CenterPoint.X), R3(vp.CenterPoint.Y) }, width = R3(vp.Width), height = R3(vp.Height),
                    scale = ScaleText(side, vp.CustomScale), customScale = vp.CustomScale,
                    viewCenter = new[] { R3(vp.ViewTarget.X + vp.ViewCenter.X), R3(vp.ViewTarget.Y + vp.ViewCenter.Y) },
                    viewTarget = new[] { R3(vp.ViewTarget.X), R3(vp.ViewTarget.Y) }, viewCenterDcs = new[] { R3(vp.ViewCenter.X), R3(vp.ViewCenter.Y) },
                    viewHeight = R3(vp.ViewHeight), twistDeg = Deg(vp.TwistAngle), locked = vp.Locked, layer = vp.Layer,
                    clipped = vp.NonRectClipOn,
                    frozenLayers = Safe(() => vp.GetFrozenLayers().Cast<ObjectId>().Select(x => ((LayerTableRecord)tr.GetObject(x, OpenMode.ForRead)).Name).ToList()),
                }).ToList(),
                entities = Take(ents.Where(e => e is not Viewport), ents.Count(e => e is not Viewport), $"layout '{l.LayoutName}' entities").Select(Ent).ToList(),
                entityCount = ents.Count,
            });
        }

        var bt = (BlockTable)tr.GetObject(side.BlockTableId, OpenMode.ForRead);
        var xrefs = new List<object>();
        var blocks = new List<object>();
        foreach (ObjectId id in bt)
        {
            var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (b.IsLayout || b.IsAnonymous) continue;
            if (b.IsFromExternalReference) { xrefs.Add(new { name = b.Name, path = b.PathName, overlay = Safe(() => b.IsFromOverlayReference) }); continue; }
            if (b.IsDependent) continue;
            var tags = b.Cast<ObjectId>().Where(x => x.ObjectClass.DxfName == "ATTDEF")
                .Select(x => (AttributeDefinition)tr.GetObject(x, OpenMode.ForRead)).Select(a => new { tag = a.Tag, prompt = a.Prompt, value = a.TextString }).ToList();
            blocks.Add(new { name = b.Name, attributes = tags, dynamic = b.IsDynamicBlock, references = Safe(() => b.GetBlockReferenceIds(true, false).Count) });
        }
        var views = ((ViewTable)tr.GetObject(side.ViewTableId, OpenMode.ForRead)).Cast<ObjectId>()
            .Select(id => (ViewTableRecord)tr.GetObject(id, OpenMode.ForRead))
            .Select(v => new { name = v.Name, target = new[] { R3(v.Target.X), R3(v.Target.Y) }, width = R3(v.Width), height = R3(v.Height), twistDeg = Deg(v.ViewTwist) }).ToList();
        var pageSetups = Entries((DBDictionary)tr.GetObject(side.PlotSettingsDictionaryId, OpenMode.ForRead)).Select(e => e.Key).ToList();
        var model = ModelSpace(side, tr).Cast<ObjectId>().ToList();
        var byType = model.GroupBy(DxfOrName).OrderByDescending(g => g.Count()).Take(30).ToDictionary(g => g.Key, g => g.Count());
        tr.Commit();
        return new
        {
            path, units = side.Insunits.ToString(), limit, layouts, xrefs,
            blocks = Take(blocks, blocks.Count, "blocks"), views = Take(views, views.Count, "views"),
            pageSetups = Take(pageSetups, pageSetups.Count, "page setups"),
            modelSpace = new { count = model.Count, byType },
            truncated = truncated.Count > 0 ? truncated : null,
            note = truncated.Count > 0 ? "limit is shared by layout entities, blocks, views and page setups; raise it or pass layout to see more." : null,
        };
    }
}
