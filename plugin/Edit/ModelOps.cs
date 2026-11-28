namespace C3dMcpBridge;

/// <summary>
/// Civil 3D additions of 0.19 (roadmap #68): corridor feature-line exports, corridor COGO points, targets, slope patterns
/// and solids; surface definitions (contours, DEM, point groups, drawing objects, wall breaklines, breakline files),
/// surfaces from LandXML / DEM / TIN files / cropping / corridor surfaces, snapshots and surface solids; catchments.
/// </summary>
internal static class ModelOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["corridor_feature_line_export"] = CorridorFeatureLineExport;
        ops["corridor_points_export"] = CorridorPointsExport;
        ops["corridor_targets_set"] = CorridorTargetsSet;
        ops["corridor_slope_pattern_add"] = CorridorSlopePatternAdd;
        ops["corridor_solids_export"] = CorridorSolidsExport;
        ops["surface_definition_add"] = SurfaceDefinitionAdd;
        ops["surface_create_from"] = SurfaceCreateFrom;
        ops["surface_snapshot"] = SurfaceSnapshot;
        ops["surface_solids_create"] = SurfaceSolidsCreate;
        ops["catchment_create"] = CatchmentCreate;
    }

    private static void Rebuild(EditContext c, CivilDb.Corridor cor)
    {
        try { cor.Rebuild(); }
        catch (Exception ex) { c.Warn($"corridor '{cor.Name}' did not rebuild: {ex.Message}"); }
        CorridorOps.RebuildCorridorSurfaces(c, cor);
    }

    // ---------------------------------------------------------------- corridor feature lines

    /// <summary>
    /// A corridor feature line of a code on a baseline's main feature lines: by 'index', or by 'side' (left / right of
    /// the baseline, from the mean offset); the first one otherwise.
    /// </summary>
    internal static CivilDb.CorridorFeatureLine FeatureLine(Transaction tr, CivilDb.Corridor cor, JsonElement p, string op)
    {
        var bl = ModelRead.Baseline(tr, cor, StrParam(p, "baseline"));
        string code = NeedStr(p, "code");
        var map = bl.MainBaselineFeatureLines.FeatureLineCollectionMap;
        string? actual = map.CodeNames().FirstOrDefault(n => SameName(n, code))
            ?? throw new ArgumentException($"{op}: code '{code}' has no feature line on baseline '{bl.Name}'. Codes: {string.Join(", ", map.CodeNames())}.");
        var col = map[actual];
        if (StrParam(p, "side") is { Length: > 0 } side)
        {
            bool left = Choice(p, "side", "", "left", "right") == "left";
            for (int i = 0; i < col.Count; i++)
            {
                var pts = col[i].FeatureLinePoints.Cast<CivilDb.FeatureLinePoint>().ToList();
                if (pts.Count > 0 && (pts.Average(x => x.Offset) < 0) == left) return col[i];
            }
            throw new ArgumentException($"{op}: code '{actual}' has no feature line on the {side} of '{bl.Name}'.");
        }
        int index = IndexParam(p, "index", 0, 10000);
        if (index >= col.Count) throw new ArgumentException($"{op}: code '{actual}' has {col.Count} feature line(s) on '{bl.Name}'; index {index} is out of range.");
        return col[index];
    }

    /// <summary>The site named by 'site' (created if missing); null when the parameter is absent.</summary>
    private static ObjectId? NamedSite(EditContext c, JsonElement p) =>
        StrParam(p, "site") is { Length: > 0 } site ? TryFindSite(c.Civ, c.Tr, site)?.ObjectId ?? CivilDb.Site.Create(c.Civ, site) : null;

    /// <summary>
    /// Site for an exported alignment. The API refuses siteless exports ("Site ID is invalid"), and objects sharing a site
    /// interact (a feature line laid over an alignment of the same site is flattened to elevation 0; parcels split), so the
    /// default is a dedicated site.
    /// </summary>
    internal const string ExportSite = "Corridor Exports";

    private static ObjectId AlignmentSite(EditContext c, JsonElement p) =>
        NamedSite(c, p) ?? TryFindSite(c.Civ, c.Tr, ExportSite)?.ObjectId ?? CivilDb.Site.Create(c.Civ, ExportSite);

    /// <summary>A corridor feature line exported as an alignment, a profile, 3D polyline(s) or a grading feature line.</summary>
    private static object CorridorFeatureLineExport(EditContext c, JsonElement p)
    {
        const string op = "corridor_feature_line_export";
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"));
        string what = Choice(p, "as", "", "alignment", "profile", "polyline3d", "grading_feature_line");
        var fl = FeatureLine(c.Tr, cor, p, op);
        var layer = c.Layer(p);
        switch (what)
        {
            case "alignment":
            {
                string name = NeedStr(p, "name");
                RequireNewName<CivilDb.Alignment>(c.Tr, AllAlignmentIds(c.Civ, c.Tr), name, "An alignment");
                var type = EnumParam(p, "alignment_type", CivilDb.AlignmentType.Centerline);
                var id = fl.ExportAsAlignment(name, AlignmentSite(c, p), layer,
                    Styles.Id(c.Civ.Styles.AlignmentStyles, StrParam(p, "style"), "alignment style"),
                    Styles.Id(c.Civ.Styles.LabelSetStyles.AlignmentLabelSetStyles, StrParam(p, "label_set"), "alignment label set"), type);
                var al = (CivilDb.Alignment)c.Tr.GetObject(id, OpenMode.ForRead);
                return new
                {
                    corridor = cor.Name, code = fl.CodeName, @as = what, alignment = al.Name, handle = H(id), type = al.AlignmentType.ToString(),
                    length = R3(al.Length), startStation = R3(al.StartingStation), endStation = R3(al.EndingStation),
                    entities = Safe(() => al.Entities.Count),
                };
            }
            case "profile":
            {
                string name = NeedStr(p, "name");
                var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
                RequireNewName<CivilDb.Profile>(c.Tr, al.GetProfileIds(), name, "A profile", $" on '{al.Name}'");
                var id = fl.ExportAsProfile(name, al.ObjectId, layer,
                    Styles.Id(c.Civ.Styles.ProfileStyles, StrParam(p, "style"), "profile style"),
                    Styles.Id(c.Civ.Styles.LabelSetStyles.ProfileLabelSetStyles, StrParam(p, "label_set"), "profile label set"));
                var pr = (CivilDb.Profile)c.Tr.GetObject(id, OpenMode.ForRead);
                return new
                {
                    corridor = cor.Name, code = fl.CodeName, @as = what, profile = pr.Name, alignment = al.Name, handle = H(id),
                    type = Safe(() => pr.ProfileType.ToString()), startStation = Safe(() => R3(pr.StartingStation)), endStation = Safe(() => R3(pr.EndingStation)),
                    minElevation = Safe(() => R3(pr.ElevationMin)), maxElevation = Safe(() => R3(pr.ElevationMax)),
                };
            }
            case "polyline3d":
            {
                var ids = BoolParam(p, "split") == true
                    ? fl.ExportAsPolyline3dCollection().Cast<ObjectId>().ToList()
                    : new List<ObjectId> { fl.ExportAsPolyline3d() };
                double len = 0;
                foreach (var id in ids)
                {
                    var e = (Entity)c.Tr.GetObject(id, OpenMode.ForWrite);
                    if (StrParam(p, "layer") is { Length: > 0 }) e.LayerId = layer;
                    if (e is Curve cv) len += Safe(() => cv.GetDistanceAtParameter(cv.EndParam)) is double d ? d : 0;
                }
                return new { corridor = cor.Name, code = fl.CodeName, @as = what, count = ids.Count, handles = ids.Select(H).ToList(), length3d = R3(len) };
            }
            default:
            {
                // Made from a 3D polyline through the feature line's own points, siteless unless 'site' is given. (In the 0.19 live
                // test ExportAsGradingFeatureLine gave elevation 0 because the feature line went into the site that already held
                // the alignment exported from the same corridor feature line; site topology flattens it.)
                string name = StrParam(p, "name") ?? $"{fl.CodeName} {cor.Name}";
                var pts = fl.FeatureLinePoints.Cast<CivilDb.FeatureLinePoint>().OrderBy(x => x.Station).Select(x => x.XYZ).ToList();
                if (pts.Count < 2) throw new ArgumentException($"{op}: the feature line has fewer than 2 points (rebuild the corridor).");
                var poly = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts.ToArray()), false);
                var polyId = c.Append(poly, StrParam(p, "layer"));
                var id = NamedSite(c, p) is ObjectId siteId ? CivilDb.FeatureLine.Create(name, polyId, siteId) : CivilDb.FeatureLine.Create(name, polyId);
                try { poly.Erase(); }
                catch (Exception ex) { c.Warn("the intermediate 3D polyline could not be removed: " + ex.Message); }
                var g = (CivilDb.FeatureLine)c.Tr.GetObject(id, OpenMode.ForWrite);
                if (StrParam(p, "layer") is { Length: > 0 }) g.LayerId = layer;
                g.StyleId = Styles.Id(c.Civ.Styles.FeatureLineStyles, StrParam(p, "style"), "feature line style");
                return new
                {
                    corridor = cor.Name, code = fl.CodeName, @as = what, featureLine = Safe(() => g.Name), handle = H(id),
                    site = NameOf(c.Tr, () => g.SiteId), length2d = Safe(() => R3(g.Length2D)), length3d = Safe(() => R3(g.Length3D)),
                    minElevation = Safe(() => R3(g.MinElevation)), maxElevation = Safe(() => R3(g.MaxElevation)),
                    note = "A static copy of the corridor feature line (not linked to the corridor).",
                };
            }
        }
    }

    /// <summary>COGO points at the corridor's feature-line points (all codes or the codes given), gathered in a new point group.</summary>
    private static object CorridorPointsExport(EditContext c, JsonElement p)
    {
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        string group = NeedStr(p, "point_group");
        foreach (ObjectId id in c.Civ.PointGroups)
            if (SameName(((CivilDb.PointGroup)c.Tr.GetObject(id, OpenMode.ForRead)).Name, group))
                throw new ArgumentException($"corridor_points_export: point group '{group}' already exists; give a new name.");
        var sel = new CivilDb.CorridorPointCodeSelector(cor);
        var codes = Has(p, "codes") ? StrList(p, "codes", 500) : null;
        if (codes == null) sel.SelectAll();
        else
        {
            sel.UnSelectAll();
            var all = sel.GetAllCodes();
            foreach (var code in codes)
            {
                string actual = all.FirstOrDefault(a => SameName(a, code))
                    ?? throw new ArgumentException($"corridor_points_export: point code '{code}' is not in '{cor.Name}'. Codes: {string.Join(", ", all)}.");
                sel[actual].Selected = true;
            }
        }
        long before = c.Civ.CogoPoints.Count;
        var gid = cor.ExportFeatureLinesAsCogoPoints(group, sel);
        var pg = (CivilDb.PointGroup)c.Tr.GetObject(gid, OpenMode.ForWrite);
        try { pg.Update(); } catch { /* counted below */ }
        return new
        {
            corridor = cor.Name, pointGroup = Safe(() => pg.Name), handle = H(gid), codes = codes ?? sel.GetAllCodes().ToList(),
            pointsAdded = c.Civ.CogoPoints.Count - before, pointsInGroup = Safe(() => (long)pg.PointsCount),
        };
    }

    // ---------------------------------------------------------------- corridor targets

    private static ObjectIdCollection TargetObjects(EditContext c, JsonElement t)
    {
        var ids = new ObjectIdCollection();
        foreach (var s in Has(t, "surfaces") ? StrList(t, "surfaces", 50) : new()) ids.Add(FindSurface(c.Civ, c.Tr, s).ObjectId);
        foreach (var a in Has(t, "alignments") ? StrList(t, "alignments", 50) : new()) ids.Add(FindAlignment(c.Civ, c.Tr, a).ObjectId);
        foreach (var e in ArrParam(t, "profiles", 50))
        {
            var al = FindAlignment(c.Civ, c.Tr, NeedStr(e, "alignment"));
            ids.Add(FindProfile(c.Tr, al, NeedStr(e, "profile")).ObjectId);
        }
        foreach (var h in Has(t, "handles") ? StrList(t, "handles", 50) : new()) ids.Add(IdFromHandle(c.Db, h));
        return ids;
    }

    /// <summary>
    /// Point subassembly targets (surface, width / offset, elevation / slope) of a baseline's regions at drawing objects.
    /// A target is matched by its display name or logical name, optionally only for subassemblies whose name contains 'subassembly'.
    /// </summary>
    private static object CorridorTargetsSet(EditContext c, JsonElement p)
    {
        const string op = "corridor_targets_set";
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        var bl = ModelRead.Baseline(c.Tr, cor, StrParam(p, "baseline"));
        var regionNames = Has(p, "regions") ? StrList(p, "regions", 500).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var specs = NeedArr(p, "targets", 200).Select(t => new
        {
            target = NeedStr(t, "target"),
            subassembly = StrParam(t, "subassembly"),
            ids = TargetObjects(c, t),
            option = StrParam(t, "option") is string _ ? (CivilDb.SubassemblyTargetToOption?)EnumParam(t, "option", CivilDb.SubassemblyTargetToOption.Nearest) : null,
        }).ToList();
        var used = new int[specs.Count];
        var done = new List<object>();
        int regions = 0;
        foreach (CivilDb.BaselineRegion rg in bl.BaselineRegions)
        {
            if (regionNames != null && !regionNames.Contains(rg.Name)) continue;
            regions++;
            var col = rg.GetTargets();
            bool changed = false;
            foreach (CivilDb.SubassemblyTargetInfo info in col)
                for (int i = 0; i < specs.Count; i++)
                {
                    var s = specs[i];
                    if (!SameName(info.DisplayName, s.target) && !SameName(info.LogicalName, s.target)) continue;
                    if (s.subassembly != null && info.SubassemblyName.IndexOf(s.subassembly, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    info.TargetIds = s.ids;
                    if (s.option is { } o) info.TargetToOption = o;
                    used[i]++;
                    changed = true;
                    done.Add(new { region = rg.Name, subassembly = info.SubassemblyName, target = info.DisplayName, type = info.TargetType.ToString(), objects = s.ids.Count });
                }
            if (changed) rg.SetTargets(col);
        }
        if (regions == 0) throw new ArgumentException($"{op}: no region of '{bl.Name}' matched 'regions'. Use list_corridors.");
        for (int i = 0; i < specs.Count; i++)
            if (used[i] == 0)
                throw new ArgumentException($"{op}: target '{specs[i].target}'{(specs[i].subassembly is string sa ? $" on '{sa}'" : "")} matched nothing in '{bl.Name}'. Use get_corridor_targets for names.");
        if (BoolParam(p, "rebuild") ?? true) Rebuild(c, cor);
        return new { corridor = cor.Name, baseline = bl.Name, regions, set = done.Count, targets = done };
    }

    // ---------------------------------------------------------------- corridor slope patterns

    private static object CorridorSlopePatternAdd(EditContext c, JsonElement p)
    {
        const string op = "corridor_slope_pattern_add";
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        if (!Has(p, "from") || !Has(p, "to")) throw new ArgumentException($"{op}: give 'from' and 'to' as {{code, side? | index?}}.");
        string? baseline = StrParam(p, "baseline");
        JsonElement WithBaseline(JsonElement e)
        {
            if (baseline == null || Has(e, "baseline")) return e;
            var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.GetRawText())!;
            d["baseline"] = JsonSerializer.SerializeToElement(baseline);
            return JsonSerializer.SerializeToElement(d);
        }
        var f1 = FeatureLine(c.Tr, cor, WithBaseline(p.GetProperty("from")), op);
        var f2 = FeatureLine(c.Tr, cor, WithBaseline(p.GetProperty("to")), op);
        var styles = c.Civ.Styles.SlopePatternStyles;
        ObjectId styleId;
        if (StrParam(p, "style") is null && styles.Count == 0)
        {
            styleId = styles.Add("Standard");
            c.Warn("the drawing had no slope pattern style; a style 'Standard' was created.");
        }
        else styleId = Styles.Id(styles, StrParam(p, "style"), "slope pattern style");
        if (BoolParam(p, "clear") == true)
            while (cor.SlopePatterns.Count > 0) cor.SlopePatterns.RemoveAt(0);
        var sp = cor.SlopePatterns.Add(f1, f2, styleId);
        if (DblParam(p, "start_station") is double s) sp.StartStation = s;
        if (DblParam(p, "end_station") is double e) sp.EndStation = e;
        return new
        {
            corridor = cor.Name, from = f1.CodeName, to = f2.CodeName, style = Safe(() => sp.StyleName),
            startStation = Safe(() => R3(sp.StartStation)), endStation = Safe(() => R3(sp.EndStation)), slopePatterns = cor.SlopePatterns.Count,
        };
    }

    // ---------------------------------------------------------------- solids

    /// <summary>Volume and count of the 3D solids among the ids (bodies have no volume through the API).</summary>
    private static (int solids, double volume, Dictionary<string, int> types) Measure(Transaction tr, IEnumerable<ObjectId> ids, ObjectId? layer)
    {
        int n = 0;
        double v = 0;
        var types = new Dictionary<string, int>();
        foreach (var id in ids)
        {
            string t = DxfOrName(id);
            types[t] = types.TryGetValue(t, out var k) ? k + 1 : 1;
            if (layer is ObjectId lid && tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.LayerId = lid;
            if (tr.GetObject(id, OpenMode.ForRead) is Solid3d s)
            {
                n++;
                try { v += s.MassProperties.Volume; } catch { /* open solid */ }
            }
        }
        return (n, v, types);
    }

    /// <summary>3D solids (or bodies) from a corridor's shapes and / or links, into this drawing.</summary>
    private static object CorridorSolidsExport(EditContext c, JsonElement p)
    {
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        var prm = new CivilDb.ExportCorridorSolidsParams
        {
            ExportShapes = BoolParam(p, "shapes") ?? true,
            ExportLinks = BoolParam(p, "links") ?? false,
            CreateSolidForShape = !(BoolParam(p, "bodies") ?? false),
            SweepSolidForShape = BoolParam(p, "sweep") ?? false,
        };
        if (Has(p, "include_codes")) prm.IncludedCodes = StrList(p, "include_codes", 200).ToArray();
        if (Has(p, "exclude_codes")) prm.ExcludedCodes = StrList(p, "exclude_codes", 200).ToArray();
        if (!prm.ExportShapes && !prm.ExportLinks) throw new ArgumentException("corridor_solids_export: shapes and links are both off.");
        ObjectIdCollection ids;
        string scope;
        if (StrParam(p, "region") is { Length: > 0 } rn)
        {
            var bl = ModelRead.Baseline(c.Tr, cor, StrParam(p, "baseline"));
            CivilDb.BaselineRegion rg;
            try { rg = bl.BaselineRegions[rn]; }
            catch { throw new ArgumentException($"Region '{rn}' not found on baseline '{bl.Name}'. Use list_corridors."); }
            ids = DblParam(p, "start_station") is double s && DblParam(p, "end_station") is double e
                ? rg.ExportSolids(prm, s, e, c.Db) : rg.ExportSolids(prm, c.Db);
            scope = $"region {rg.Name}";
        }
        else if (StrParam(p, "baseline") is { Length: > 0 })
        {
            var bl = ModelRead.Baseline(c.Tr, cor, StrParam(p, "baseline"));
            ids = bl.ExportSolids(prm, c.Db);
            scope = $"baseline {bl.Name}";
        }
        else { ids = cor.ExportSolids(prm, c.Db); scope = "corridor"; }
        var list = ids.Cast<ObjectId>().ToList();
        var (solids, volume, types) = Measure(c.Tr, list, StrParam(p, "layer") is { Length: > 0 } ? c.Layer(p) : null);
        return new
        {
            corridor = cor.Name, scope, count = list.Count, types, solids, totalVolume = R3(volume), handles = list.Take(200).Select(H).ToList(),
            note = "totalVolume: sum of the 3D solids in cubic drawing units (bodies are not measured).",
        };
    }

    /// <summary>3D solids between a TIN surface and a depth, a fixed elevation or another surface; or the same written to a DWG file.</summary>
    private static object SurfaceSolidsCreate(EditContext c, JsonElement p)
    {
        const string op = "surface_solids_create";
        var tin = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        string mode = Choice(p, "mode", "", "depth", "elevation", "surface");
        string layer = StrParam(p, "layer") ?? "0";
        LayerId(c.Db, c.Tr, layer);   // must exist
        ushort pen = (ushort)WholeParam(p, "color", 256, 0, 256);
        double value = mode == "depth" ? NeedPositive(p, "depth") : mode == "elevation" ? NeedDbl(p, "elevation") : 0;
        ObjectId bottom = mode == "surface" ? FindSurface(c.Civ, c.Tr, NeedStr(p, "bottom_surface")).ObjectId : ObjectId.Null;

        if (StrParam(p, "file") is { Length: > 0 })
        {
            string path = NeedFullPath(p, "file", op, write: true);
            if (!path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"{op}: file must be a .dwg path.");
            if (File.Exists(path) && BoolParam(p, "overwrite") != true) throw new ArgumentException($"{op}: {path} exists; pass overwrite: true.");
            if (Path.GetDirectoryName(path) is not string dir || !Directory.Exists(dir)) throw new ArgumentException($"{op}: folder not found for {path}.");
            var tinId = tin.ObjectId;
            var db = c.Db;
            c.AfterCommit.Add(() =>
            {
                // Written to a temporary file next to the target first: an existing file is replaced only when the
                // solids were made, so a failed creation never deletes the old file.
                string temp = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(path)}.mcp-{Guid.NewGuid():N}.dwg");
                try
                {
                    string f = temp;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var t = (CivilDb.TinSurface)tr.GetObject(tinId, OpenMode.ForWrite);
                        _ = mode switch
                        {
                            "depth" => t.CreateSolidsAtDepthToFile(value, layer, pen, ref f),
                            "elevation" => t.CreateSolidsAtFixedElevationToFile(value, layer, pen, ref f),
                            _ => t.CreateSolidsAtSurfaceToFile(bottom, layer, pen, ref f),
                        };
                        tr.Commit();
                    }
                    if (!File.Exists(f)) throw new InvalidOperationException($"{op}: Civil 3D reported no error but wrote no file.");
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(f, path);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            });
            return new { surface = tin.Name, mode, file = path, note = "Written after the edit commits; an existing file is replaced only when the solids are made." };
        }
        var ids = mode switch
        {
            "depth" => tin.CreateSolidsAtDepth(value, layer, pen),
            "elevation" => tin.CreateSolidsAtFixedElevation(value, layer, pen),
            _ => tin.CreateSolidsAtSurface(bottom, layer, pen),
        };
        var list = ids.Cast<ObjectId>().ToList();
        var (solids, volume, types) = Measure(c.Tr, list, null);
        return new
        {
            surface = tin.Name, mode, depth = mode == "depth" ? value : (double?)null, elevation = mode == "elevation" ? value : (double?)null,
            bottomSurface = mode == "surface" ? NeedStr(p, "bottom_surface") : null,
            count = list.Count, types, solids, totalVolume = R3(volume), handles = list.Take(200).Select(H).ToList(),
            note = "totalVolume: sum of the 3D solids in cubic drawing units.",
        };
    }

    // ---------------------------------------------------------------- surface definitions

    private static object Stats(CivilDb.Surface s)
    {
        CivilDb.GeneralSurfaceProperties g;
        try { g = s.GetGeneralProperties(); }
        catch (Exception ex) { return new { surface = s.Name, problem = ex.Message }; }
        return new
        {
            surface = s.Name, type = s.GetType().Name, points = g.NumberOfPoints,
            minElevation = R3(g.MinimumElevation), maxElevation = R3(g.MaximumElevation), meanElevation = R3(g.MeanElevation),
            hasSnapshot = Safe(() => s.HasSnapshot),
        };
    }

    private static ObjectIdCollection Handles(EditContext c, JsonElement p, string name, string op) =>
        new(StrList(p, name, 20000).Select(h => IdFromHandle(c.Db, h)).ToArray());

    private static string ExistingFile(JsonElement p, string op, params string[] extensions)
    {
        string path = NeedFullPath(p, "path", op);
        if (!File.Exists(path)) throw new ArgumentException($"{op}: file not found: {path}");
        if (extensions.Length > 0 && !extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"{op}: expected a {string.Join(" / ", extensions)} file: {path}");
        return path;
    }

    /// <summary>Add data to a TIN surface's definition: contours, a DEM file, a point group, drawing objects, wall breaklines, a breakline file.</summary>
    private static object SurfaceDefinitionAdd(EditContext c, JsonElement p)
    {
        const string op = "surface_definition_add";
        var tin = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        string kind = Choice(p, "kind", "", "contours", "dem", "point_group", "drawing_objects", "wall_breaklines", "breakline_file");
        // Contours need max_distance > weeding_distance: Civil 3D's dialog defaults (100 / 15 / 4 deg); breaklines: none.
        bool isContours = Choice(p, "kind", "", "contours", "dem", "point_group", "drawing_objects", "wall_breaklines", "breakline_file") == "contours";
        double mid = PositiveParam(p, "mid_ordinate") ?? 1.0;
        double maxDist = DblParam(p, "max_distance") ?? (isContours ? 100.0 : 0.0);
        double weedDist = DblParam(p, "weeding_distance") ?? (isContours ? 15.0 : 0.0);
        double weedAngle = DblParam(p, "weeding_angle") ?? (isContours ? 4.0 : 0.0);
        if (isContours && maxDist <= weedDist) throw new ArgumentException("surface_definition_add: max_distance must be greater than weeding_distance for contours.");
        string desc = StrParam(p, "description") ?? "";
        int items;
        switch (kind)
        {
            case "contours":
            {
                var flat = BoolParam(p, "minimize_flat_areas") == true;
                var opts = new CivilDb.SurfaceMinimizeFlatAreaOptions(true, true, true, true);
                var cd = tin.ContoursDefinition;
                items = 0;
                if (Has(p, "handles"))
                {
                    var ids = Handles(c, p, "handles", op);
                    if (flat) cd.AddContours(ids, mid, maxDist, weedDist, weedAngle, opts); else cd.AddContours(ids, mid, maxDist, weedDist, weedAngle);
                    items += ids.Count;
                }
                var lines = ArrParam(p, "lines", 5000);
                for (int i = 0; i < lines.Count; i++)
                {
                    var pts = new Point3dCollection(Points(lines[i], $"{op}: contour {i + 1}", 2).ToArray());
                    if (flat) cd.AddContours(pts, mid, maxDist, weedDist, weedAngle, opts); else cd.AddContours(pts, mid, maxDist, weedDist, weedAngle);
                    items++;
                }
                if (items == 0) throw new ArgumentException($"{op}: contours need 'handles' (polylines at their elevation) and/or 'lines'.");
                break;
            }
            case "dem":
            {
                string path = ExistingFile(p, op);
                if (DblParam(p, "null_elevation") is double ne) tin.DEMFilesDefinition.AddDEMFile(path, true, ne);
                else tin.DEMFilesDefinition.AddDEMFile(path);
                items = 1;
                break;
            }
            case "point_group":
            {
                string g = NeedStr(p, "point_group");
                ObjectId gid = ObjectId.Null;
                foreach (ObjectId id in c.Civ.PointGroups)
                    if (SameName(((CivilDb.PointGroup)c.Tr.GetObject(id, OpenMode.ForRead)).Name, g)) gid = id;
                if (gid.IsNull) throw new ArgumentException($"Point group '{g}' not found. Use list_point_groups.");
                tin.PointGroupsDefinition.AddPointGroup(gid);
                items = 1;
                break;
            }
            case "drawing_objects":
            {
                var ids = Handles(c, p, "handles", op);
                bool edges = BoolParam(p, "maintain_edges") ?? true;
                var dob = tin.DrawingObjectsDefinition;
                switch (Choice(p, "object_type", "", "points", "lines", "blocks", "texts", "faces", "polyfaces"))
                {
                    case "points": dob.AddFromPoints(ids, desc); break;
                    case "lines": dob.AddFromLines(ids, edges, desc); break;
                    case "blocks": dob.AddFromBlocks(ids, desc); break;
                    case "texts": dob.AddFromTexts(ids, desc); break;
                    case "faces": dob.AddFrom3DFaces(ids, edges, desc); break;
                    default: dob.AddFromPolyFaces(ids, edges, desc); break;
                }
                items = ids.Count;
                break;
            }
            case "wall_breaklines":
            {
                var walls = NeedArr(p, "walls", 2000);
                bool anyList = walls.Any(w => Has(w, "elevations"));
                if (anyList)
                {
                    var data = walls.Select(w => new CivilDb.WallBreaklineCreationDataEx
                    {
                        SourceId = IdFromHandle(c.Db, NeedStr(w, "handle")),
                        IsRightSideOffset = BoolParam(w, "right_side") ?? true,
                        Elevations = Has(w, "elevations") ? NumList(w, "elevations", 20000).ToArray() : new[] { NeedDbl(w, "elevation") },
                    }).ToArray();
                    tin.BreaklinesDefinition.AddWallBreaklines(data, mid, maxDist, weedDist, weedAngle);
                }
                else
                {
                    var data = walls.Select(w => new CivilDb.WallBreaklineCreationData
                    {
                        SourceId = IdFromHandle(c.Db, NeedStr(w, "handle")),
                        IsRightSideOffset = BoolParam(w, "right_side") ?? true,
                        Elevation = NeedDbl(w, "elevation"),
                    }).ToArray();
                    tin.BreaklinesDefinition.AddWallBreaklines(data, mid, maxDist, weedDist, weedAngle);
                }
                items = walls.Count;
                break;
            }
            default:
                tin.BreaklinesDefinition.AddBreaklinesFromFile(ExistingFile(p, op, ".flt"));
                items = 1;
                break;
        }
        if (BoolParam(p, "rebuild") ?? true)
        {
            try { tin.Rebuild(); } catch (Exception ex) { c.Warn($"surface '{tin.Name}' did not rebuild: {ex.Message}"); }
        }
        return new { kind, items, maxDistance = maxDist, weedingDistance = weedDist, weedingAngle = weedAngle, after = Stats(tin) };
    }

    /// <summary>The first surface name in a LandXML file (attribute name of the first Surface element).</summary>
    private static string FirstLandXmlSurface(string path)
    {
        using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null });
        while (reader.Read())
            if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "Surface" && reader.GetAttribute("name") is { Length: > 0 } n)
                return n;
        throw new ArgumentException($"surface_create_from: no <Surface name=...> in {path}.");
    }

    /// <summary>A new surface from a LandXML file, a DEM (grid surface), a TIN file, a cropped area of another surface, or a corridor surface.</summary>
    private static object SurfaceCreateFrom(EditContext c, JsonElement p)
    {
        const string op = "surface_create_from";
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Surface>(c.Tr, c.Civ.GetSurfaceIds(), name, "A surface");
        string source = Choice(p, "source", "", "landxml", "dem", "tin_file", "crop", "corridor");
        ObjectId id;
        string? from = null;
        switch (source)
        {
            case "landxml":
            {
                string path = ExistingFile(p, op, ".xml");
                from = StrParam(p, "landxml_surface") ?? FirstLandXmlSurface(path);
                id = CivilDb.TinSurface.CreateFromLandXML(c.Db, name, path, from);
                break;
            }
            case "dem":
                id = CivilDb.GridSurface.CreateFromDEM(ExistingFile(p, op), Styles.Id(c.Civ.Styles.SurfaceStyles, StrParam(p, "style"), "surface style"));
                break;
            case "tin_file":
                id = CivilDb.TinSurface.CreateFromTin(c.Db, ExistingFile(p, op, ".tin"));
                break;
            case "crop":
            {
                var src = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"));
                from = src.Name;
                var pts = PtList(p, "points", op, 3, 20000);
                if (pts[0].DistanceTo(pts[^1]) > 1e-9) pts.Add(pts[0]);
                // TinSurface.CreateByCropping only writes into another drawing; here: a new TIN with the source pasted in and an
                // outer boundary on the polygon (non-destructive, so triangles are cut at the edge).
                id = CivilDb.TinSurface.Create(c.Db, name);
                var crop = (CivilDb.TinSurface)c.Tr.GetObject(id, OpenMode.ForWrite);
                crop.PasteSurface(src.ObjectId);
                crop.BoundariesDefinition.AddBoundaries(new Point2dCollection(pts.Select(x => new Point2d(x.X, x.Y)).ToArray()), 1.0,
                                                        Autodesk.Civil.SurfaceBoundaryType.Outer, true);
                crop.Rebuild();
                break;
            }
            default:
            {
                var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"));
                string cs = NeedStr(p, "corridor_surface");
                CivilDb.CorridorSurface surf;
                try { surf = cor.CorridorSurfaces[cs]; }
                catch { throw new ArgumentException($"Corridor surface '{cs}' not found on '{cor.Name}'. Use list_corridors."); }
                from = $"{cor.Name} / {surf.Name}";
                id = CivilDb.TinSurface.CreateFromCorridorSurface(name, surf);
                break;
            }
        }
        var s = (CivilDb.Surface)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (!SameName(s.Name, name)) s.Name = name;
        if (Has(p, "style") && source != "dem") s.StyleId = Styles.Id(c.Civ.Styles.SurfaceStyles, StrParam(p, "style"), "surface style");
        if (StrParam(p, "layer") is { Length: > 0 }) s.LayerId = c.Layer(p);
        if (StrParam(p, "description") is string d) s.Description = d;
        return new { source, from, handle = H(id), created = Stats(s) };
    }

    private static object SurfaceSnapshot(EditContext c, JsonElement p)
    {
        var s = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"), OpenMode.ForWrite);
        string action = Choice(p, "action", "create", "create", "rebuild", "remove");
        switch (action)
        {
            case "create": s.CreateSnapshot(); break;
            case "rebuild": s.RebuildSnapshot(); break;
            default: s.RemoveSnapshot(); break;
        }
        return new { surface = s.Name, action, hasSnapshot = Safe(() => s.HasSnapshot), snapshotOutOfDate = Safe(() => s.IsSnapshotOutOfDate) };
    }

    // ---------------------------------------------------------------- catchments

    /// <summary>A catchment from a boundary on a surface, in a group (created if missing), with its flow path traced from the surface.</summary>
    private static object CatchmentCreate(EditContext c, JsonElement p)
    {
        const string op = "catchment_create";
        string name = NeedStr(p, "name");
        var surf = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"));
        string groupName = StrParam(p, "group") ?? "Catchments";
        var groups = c.Civ.GetCatchmentGroups();
        ObjectId gid = ObjectId.Null;
        foreach (ObjectId id in groups)
            if (SameName(((CivilDb.CatchmentGroup)c.Tr.GetObject(id, OpenMode.ForRead)).Name, groupName)) gid = id;
        bool newGroup = gid.IsNull;
        if (newGroup) gid = groups.Add(groupName);
        else
        {
            var g = (CivilDb.CatchmentGroup)c.Tr.GetObject(gid, OpenMode.ForRead);
            foreach (ObjectId cid in g.GetAllCatchmentIds())
                if (SameName(((CivilDb.Catchment)c.Tr.GetObject(cid, OpenMode.ForRead)).Name, name))
                    throw new ArgumentException($"{op}: catchment '{name}' already exists in group '{groupName}'.");
        }

        List<Point3d> boundary;
        if (StrParam(p, "handle") is { Length: > 0 } h)
        {
            var curve = c.Tr.GetObject(IdFromHandle(c.Db, h), OpenMode.ForRead) as Curve ?? throw new ArgumentException($"{op}: {h} is not a polyline.");
            boundary = new List<Point3d>();
            if (curve is Polyline pl) for (int i = 0; i < pl.NumberOfVertices; i++) boundary.Add(pl.GetPoint3dAt(i));
            else if (curve is Polyline3d p3) foreach (ObjectId v in p3) boundary.Add(((PolylineVertex3d)c.Tr.GetObject(v, OpenMode.ForRead)).Position);
            else throw new ArgumentException($"{op}: the boundary handle must be a polyline or 3D polyline.");
        }
        else boundary = PtList(p, "boundary", op, 3, 20000);
        if (boundary.Count < 3) throw new ArgumentException($"{op}: the boundary needs at least 3 points.");
        if (boundary[0].DistanceTo(boundary[^1]) > 1e-9) boundary.Add(boundary[0]);

        var style = Styles.Id(c.Civ.Styles.CatchmentStyles, StrParam(p, "style"), "catchment style");
        var catId = CivilDb.Catchment.Create(name, style, gid, surf.ObjectId, new Point3dCollection(boundary.ToArray()));
        var cat = (CivilDb.Catchment)c.Tr.GetObject(catId, OpenMode.ForWrite);
        if (StrParam(p, "layer") is { Length: > 0 }) cat.LayerId = c.Layer(p);
        if (DblParam(p, "runoff_coefficient") is double rc) cat.RunoffCoefficient = rc;
        if (DblParam(p, "impervious_percent") is double ia)
        {
            if (ia < 0 || ia > 100) throw new ArgumentException($"{op}: impervious_percent must be 0 to 100.");
            cat.ImperviousArea = ia / 100.0;   // Civil 3D keeps the impervious share as a fraction (shows it as percent)
        }
        if (Has(p, "flow_path"))
        {
            cat.SetFlowPath(new Point3dCollection(PtList(p, "flow_path", op, 2, 20000).ToArray()));
        }
        else if (OptPt(p, "flow_from", op) is Point3d start)
        {
            cat.FlowPath.SetPathFromSurface(new Point2d(start.X, start.Y));   // the property is live; SetFlowPath(FlowPath) is obsolete
        }
        if (StrParam(p, "discharge_structure") is { Length: > 0 } sn)
        {
            var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"));
            ObjectId sid = ObjectId.Null;
            foreach (ObjectId s in net.GetStructureIds())
                if (SameName(((CivilDb.Structure)c.Tr.GetObject(s, OpenMode.ForRead)).Name, sn)) sid = s;
            if (sid.IsNull) throw new ArgumentException($"{op}: structure '{sn}' not found in '{net.Name}'.");
            cat.ReferencePipeNetworkStructureId = sid;
        }
        return new { group = groupName, groupCreated = newGroup, catchment = ModelRead.Catchment(c.Tr, cat, false, true) };
    }
}
