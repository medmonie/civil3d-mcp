using Autodesk.AutoCAD.EditorInput;

namespace C3dMcpBridge;

/// <summary>Drawing-level read actions: connection, layers, entity counts, selection, objects by handle.</summary>
internal static class DrawingRead
{
    public static object Ping(JsonElement p) => new
    {
        bridgeVersion = BridgeInfo.Version,
        acadVersion = Safe(() => CoreApp.GetSystemVariable("ACADVER")?.ToString()),
        product = Safe(() => CoreApp.GetSystemVariable("PRODUCT")?.ToString()),
        activeDrawing = AcApp.DocumentManager.MdiActiveDocument?.Name,
        editingAllowed = WriteGate.Granted,
    };

    public static object GetDrawingInfo(JsonElement p)
    {
        var doc = ActiveDoc();
        return WithRead((db, tr) =>
        {
            int layerCount = ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Cast<ObjectId>().Count();
            int modelSpaceCount = ModelSpace(db, tr).Cast<ObjectId>().Count();

            object? civil = Safe(() =>
            {
                var civ = Civ;
                return new
                {
                    surfaces = Safe(() => civ.GetSurfaceIds().Count),
                    alignments = Safe(() => AllAlignmentIds(civ, tr).Count),
                    pipeNetworks = Safe(() => civ.GetPipeNetworkIds().Count),
                    pressureNetworks = Safe(() => PressureNetworkIds(civ).Count),
                    cogoPoints = Safe(() => civ.CogoPoints.Count),
                    sites = Safe(() => civ.GetSiteIds().Count),
                };
            });

            return new
            {
                name = Path.GetFileName(doc.Name),
                path = doc.Name,
                readOnly = doc.IsReadOnly,
                insertionUnits = db.Insunits.ToString(),
                civilDrawingUnits = Safe(() => Civ.Settings.DrawingSettings.UnitZoneSettings.DrawingUnits.ToString()),
                currentLayer = Safe(() => ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name),
                layerCount,
                modelSpaceEntityCount = modelSpaceCount,
                civil,
                editingAllowed = WriteGate.Granted,
            };
        });
    }

    public static object ListLayers(JsonElement p)
    {
        int limit = IntParam(p, "limit", 500, 1, 20000);
        return WithRead((db, tr) => Paged("layers", ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Cast<ObjectId>(), limit, id =>
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            return new
            {
                name = l.Name,
                on = !l.IsOff,
                frozen = l.IsFrozen,
                locked = l.IsLocked,
                plot = l.IsPlottable,
                color = l.Color.ToString(),
                xref = l.IsDependent,
            };
        }));
    }

    public static object CountEntities(JsonElement p)
    {
        string groupBy = Choice(p, "group_by", "type", "type", "layer");
        string? layerFilter = StrParam(p, "layer");

        return WithRead((db, tr) =>
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            bool needEntity = groupBy == "layer" || layerFilter != null;

            foreach (ObjectId id in ModelSpace(db, tr))
            {
                string key;
                if (needEntity)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    if (layerFilter != null && !SameName(ent.Layer, layerFilter)) continue;
                    key = groupBy == "layer" ? ent.Layer : DxfOrName(id);
                }
                else
                {
                    key = DxfOrName(id);   // no need to open the object, much faster on big drawings
                }
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
                total++;
            }

            var groups = counts.OrderByDescending(kv => kv.Value).Take(200)
                               .Select(kv => new { key = kv.Key, count = kv.Value }).ToList();
            return new { groupBy, layerFilter, total, groups, groupsTruncated = counts.Count > groups.Count };
        });
    }

    private static object Describe(Transaction tr, ObjectId id)
    {
        var obj = tr.GetObject(id, OpenMode.ForRead);
        var ent = obj as Entity;
        return new
        {
            handle = H(id),
            type = DxfOrName(id),
            layer = ent?.Layer,
            civilName = obj is CivilDb.Entity ce ? Safe(() => ce.Name) : null,
            extents = ent == null ? null : Extents(ent),
        };
    }

    public static object GetSelection(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 2000);
        var doc = ActiveDoc();
        var psr = doc.Editor.SelectImplied();
        if (psr.Status != PromptStatus.OK)
            return new
            {
                count = 0,
                objects = new List<object>(),
                note = "Nothing is pre-selected. Select objects in Civil 3D first, then ask again.",
            };

        var ids = psr.Value.GetObjectIds();
        return WithRead((db, tr) =>
        {
            var rows = ids.Take(limit).Select(id => Describe(tr, id)).ToList();
            return new { count = ids.Length, returned = rows.Count, truncated = ids.Length > rows.Count, objects = rows };
        });
    }

    public static object GetObjects(JsonElement p)
    {
        var handles = StrList(p, "handles", 500);
        return WithRead((db, tr) =>
        {
            var rows = new List<object>();
            foreach (var h in handles)
            {
                try { rows.Add(Describe(tr, IdFromHandle(db, h))); }
                catch (Exception ex) { rows.Add(new { handle = h, error = ex.Message }); }
            }
            return new { count = rows.Count, objects = rows };
        });
    }

    /// <summary>Geometry of a plain AutoCAD entity, in a shape that is easy to reason about.</summary>
    private static object? Geometry(Transaction tr, Entity ent) => ent switch
    {
        Line l => new { start = P3(l.StartPoint), end = P3(l.EndPoint), length = R3(l.Length) },
        Polyline pl => new
        {
            closed = pl.Closed, length = R3(pl.Length), elevation = R3(pl.Elevation), vertexCount = pl.NumberOfVertices,
            vertices = Enumerable.Range(0, Math.Min(pl.NumberOfVertices, 200)).Select(i => { var v = pl.GetPoint2dAt(i); return new[] { R3(v.X), R3(v.Y) }; }).ToList(),
            hasArcs = pl.HasBulges,
        },
        Circle ci => new { center = P3(ci.Center), radius = R3(ci.Radius) },
        Arc a => new { center = P3(a.Center), radius = R3(a.Radius), startDeg = Deg(a.StartAngle), endDeg = Deg(a.EndAngle), length = R3(a.Length) },
        DBText t => new { text = t.TextString, at = P3(t.Position), height = R3(t.Height), rotationDeg = Deg(t.Rotation) },
        MText mt => new { text = mt.Text, at = P3(mt.Location), height = R3(mt.TextHeight), rotationDeg = Deg(mt.Rotation) },
        DBPoint pt => new { at = P3(pt.Position) },
        BlockReference br => new
        {
            block = Safe(() => ((BlockTableRecord)tr.GetObject(br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord, OpenMode.ForRead)).Name),
            at = P3(br.Position), scale = R3(br.ScaleFactors.X), rotationDeg = Deg(br.Rotation),
            attributes = Safe(() =>
            {
                var d = new Dictionary<string, string>();
                foreach (ObjectId aid in br.AttributeCollection)
                    if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar) d[ar.Tag] = ar.TextString;
                return d;
            }),
        },
        Hatch h => new { pattern = h.PatternName, area = Safe(() => R3(h.Area)) },
        Dimension dm => new { measurement = Safe(() => R3(dm.Measurement)), text = dm.DimensionText },
        Curve cu => new { start = Safe(() => P3(cu.StartPoint)), end = Safe(() => P3(cu.EndPoint)), length = Safe(() => R3(cu.GetDistanceAtParameter(cu.EndParam))) },
        _ => null,
    };

    public static object ListEntities(JsonElement p)
    {
        string? type = StrParam(p, "type");
        string? layer = StrParam(p, "layer");
        int limit = IntParam(p, "limit", 100, 1, 20000);
        var win = NumList(p, "window", 4);
        if (Has(p, "window") && win.Count != 4) throw new ArgumentException("window must be [xmin, ymin, xmax, ymax].");

        bool InWindow(Entity ent)
        {
            if (win.Count == 0) return true;
            try
            {
                var e = ent.GeometricExtents;
                return e.MaxPoint.X >= win[0] && e.MinPoint.X <= win[2] && e.MaxPoint.Y >= win[1] && e.MinPoint.Y <= win[3];
            }
            catch { return false; }   // no extents: cannot be inside a window
        }

        return WithRead((db, tr) =>
        {
            // The DXF type is checked on the id, so entities of other types are never opened.
            var matches = ModelSpace(db, tr).Cast<ObjectId>()
                .Where(id => type == null || SameName(DxfOrName(id), type))
                .Select(id => tr.GetObject(id, OpenMode.ForRead) as Entity)
                .Where(ent => ent != null && (layer == null || SameName(ent.Layer, layer)) && InWindow(ent));
            return Paged("entities", matches, limit, ent => new
            {
                handle = H(ent!.ObjectId), type = DxfOrName(ent.ObjectId), layer = ent.Layer, color = ent.Color.ToString(),
                civilName = ent is CivilDb.Entity ce ? Safe(() => ce.Name) : null,
                geometry = Safe(() => Geometry(tr, ent)),
            });
        });
    }

    public static object ListBlocks(JsonElement p)
    {
        int limit = IntParam(p, "limit", 500, 1, 5000);
        return WithRead((db, tr) =>
        {
            var blocks = ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)).Cast<ObjectId>()
                .Select(id => (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead))
                .Where(b => !b.IsLayout && !b.IsAnonymous && !b.IsFromExternalReference && !b.Name.StartsWith('*'));
            return Paged("blocks", blocks, limit, b => new
            {
                name = b.Name, hasAttributes = b.HasAttributeDefinitions, references = Safe(() => b.GetBlockReferenceIds(true, false).Count),
            });
        });
    }

    public static object ListLayouts(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = new List<object>();
        var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry e in dict)
        {
            var l = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
            rows.Add(new { name = l.LayoutName, tabOrder = l.TabOrder, isModel = l.ModelType, plotDevice = Safe(() => l.PlotConfigurationName), paper = Safe(() => l.CanonicalMediaName) });
        }
        return new { count = rows.Count, layouts = rows };
    });
}
