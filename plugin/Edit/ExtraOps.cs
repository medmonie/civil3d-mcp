namespace C3dMcpBridge;

/// <summary>
/// Civil 3D additions of 0.18: profiles derived from other objects, corridor baselines / regions / surface boundaries,
/// surface extraction, curved gravity pipes and part swaps, pressure run bends and merges, section view volume tables.
/// </summary>
internal static class ExtraOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["profile_derive"] = ProfileDerive;
        ops["corridor_baseline_add"] = CorridorBaselineAdd;
        ops["corridor_region_edit"] = CorridorRegionEdit;
        ops["corridor_surface_set"] = CorridorSurfaceSet;
        ops["surface_extract"] = SurfaceExtract;
        ops["pipe_curved_add"] = PipeCurvedAdd;
        ops["part_swap"] = PartSwap;
        ops["parts_to_section_views"] = PartsToSectionViews;
        ops["pressure_run_edit"] = PressureRunEdit;
        ops["section_volume_tables"] = SectionVolumeTables;
    }

    private static void Rebuild(EditContext c, CivilDb.Corridor cor)
    {
        try { cor.Rebuild(); }
        catch (Exception ex) { c.Warn($"corridor '{cor.Name}' did not rebuild: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- profiles

    /// <summary>A profile from a corridor feature line, an offset profile at a slope, or a static copy of a profile.</summary>
    private static object ProfileDerive(EditContext c, JsonElement p)
    {
        string kind = Choice(p, "kind", "", "corridor_feature_line", "offset_slope", "static_copy");
        string name = NeedStr(p, "name");
        var layer = c.Layer(p);
        var style = Styles.Id(c.Civ.Styles.ProfileStyles, StrParam(p, "style"), "profile style");
        var labels = Styles.Id(c.Civ.Styles.LabelSetStyles.ProfileLabelSetStyles, StrParam(p, "label_set"), "profile label set");
        ObjectId id;
        string on;
        switch (kind)
        {
            case "corridor_feature_line":
            {
                var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"));
                var fl = CorridorFeatureLine(cor, StrParam(p, "baseline"), NeedStr(p, "code"), IntParam(p, "index", 0, 0, 1000));
                var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
                id = CivilDb.Profile.CreateFromFeatureLine(name, fl, al.ObjectId, layer, style, labels);
                on = al.Name;
                break;
            }
            case "offset_slope":
            {
                // the offset alignment's parent profile, projected at a cross slope
                var off = FindAlignment(c.Civ, c.Tr, NeedStr(p, "offset_alignment"));
                if (off.AlignmentType != CivilDb.AlignmentType.Offset)
                    throw new ArgumentException($"profile_derive: '{off.Name}' is not an offset alignment.");
                var parent = (CivilDb.Alignment)c.Tr.GetObject(off.OffsetAlignmentInfo.ParentAlignmentId, OpenMode.ForRead);
                var src = FindProfile(c.Tr, parent, NeedStr(p, "profile"), OpenMode.ForWrite);
                id = src.CreateOffsetProfileBySlope(name, off.ObjectId, style, NeedDbl(p, "slope_percent") / 100.0);
                on = off.Name;
                break;
            }
            default:
            {
                var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
                var src = FindProfile(c.Tr, al, NeedStr(p, "profile"));
                try { id = CivilDb.Profile.CreateStaticFGFromProfile(name, src.ObjectId, layer, style, labels); }
                catch (Exception ex)
                {
                    throw new ArgumentException($"profile_derive: no static copy of '{src.Name}' ({src.ProfileType}): {ex.Message} " +
                                                "Static copies are made from corridor and offset profiles (not from surface or layout profiles).");
                }
                on = al.Name;
                break;
            }
        }
        var pr = (CivilDb.Profile)c.Tr.GetObject(id, OpenMode.ForRead);
        return new
        {
            kind, profile = name, alignment = on, handle = H(id), type = Safe(() => pr.ProfileType.ToString()),
            startStation = Safe(() => R3(pr.StartingStation)), endStation = Safe(() => R3(pr.EndingStation)),
            minElevation = Safe(() => R3(pr.ElevationMin)), maxElevation = Safe(() => R3(pr.ElevationMax)),
        };
    }

    /// <summary>A corridor feature line by code on a baseline (main baseline feature lines; the index-th one for that code).</summary>
    internal static CivilDb.CorridorFeatureLine CorridorFeatureLine(CivilDb.Corridor cor, string? baseline, string code, int index)
    {
        var bl = baseline == null ? cor.Baselines[0] : cor.Baselines[baseline];
        var map = bl.MainBaselineFeatureLines.FeatureLineCollectionMap;
        if (!map.CodeNames().Any(n => SameName(n, code)))
            throw new ArgumentException($"Code '{code}' has no feature line on baseline '{bl.Name}'. Codes: {string.Join(", ", map.CodeNames())}.");
        var col = map[map.CodeNames().First(n => SameName(n, code))];
        if (index >= col.Count) throw new ArgumentException($"Code '{code}' has {col.Count} feature line(s) on '{bl.Name}'; index {index} is out of range.");
        return col[index];
    }

    // ---------------------------------------------------------------- corridors

    private static CivilDb.Baseline Baseline(CivilDb.Corridor cor, string? name)
    {
        if (name == null) return cor.Baselines[0];
        try { return cor.Baselines[name]; }
        catch { throw new ArgumentException($"Baseline '{name}' not found on corridor '{cor.Name}'. Use list_corridors."); }
    }

    private static CivilDb.BaselineRegion Region(CivilDb.Baseline bl, string name)
    {
        try { return bl.BaselineRegions[name]; }
        catch { throw new ArgumentException($"Region '{name}' not found on baseline '{bl.Name}'. Use list_corridors."); }
    }

    /// <summary>Another baseline (alignment + profile) with a first region.</summary>
    private static object CorridorBaselineAdd(EditContext c, JsonElement p)
    {
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var pr = FindProfile(c.Tr, al, NeedStr(p, "profile"));
        string name = StrParam(p, "name") ?? $"BL-{cor.Baselines.Count + 1}";
        var bl = cor.Baselines.Add(name, al.ObjectId, pr.ObjectId);
        object? region = null;
        if (StrParam(p, "assembly") is { Length: > 0 } asm)
        {
            var a = FindAssembly(c.Civ, c.Tr, asm);
            string rn = StrParam(p, "region") ?? "RG-1";
            var rg = DblParam(p, "start_station") is double s && DblParam(p, "end_station") is double e
                ? bl.BaselineRegions.Add(rn, a.ObjectId, s, e) : bl.BaselineRegions.Add(rn, a.ObjectId);
            region = new { name = rg.Name, start = R3(rg.StartStation), end = R3(rg.EndStation) };
        }
        if (BoolParam(p, "rebuild") ?? true) Rebuild(c, cor);
        return new { corridor = cor.Name, baseline = bl.Name, alignment = al.Name, profile = pr.Name, region };
    }

    /// <summary>
    /// Regions of a baseline: add one, split one at a station, merge a run of regions, move a region's start / end,
    /// add or delete extra stations, and set the assembly frequency.
    /// </summary>
    private static object CorridorRegionEdit(EditContext c, JsonElement p)
    {
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        var bl = Baseline(cor, StrParam(p, "baseline"));
        var done = new List<string>();

        if (Has(p, "add"))
        {
            var e = p.GetProperty("add");
            var a = FindAssembly(c.Civ, c.Tr, NeedStr(e, "assembly"));
            var rg = bl.BaselineRegions.Add(NeedStr(e, "name"), a.ObjectId, NeedDbl(e, "start_station"), NeedDbl(e, "end_station"));
            done.Add($"added {rg.Name} {R3(rg.StartStation)}-{R3(rg.EndStation)}");
        }
        if (Has(p, "merge"))
        {
            var names = StrList(p, "merge", 2);
            if (names.Count != 2) throw new ArgumentException("corridor_region_edit: merge is [first region, last region].");
            var first = Region(bl, names[0]); var last = Region(bl, names[1]);
            first.Merge(first, last);
            done.Add($"merged {names[0]}..{names[1]}");
        }
        if (StrParam(p, "region") is { Length: > 0 } rname)
        {
            var rg = Region(bl, rname);
            if (DblParam(p, "start_station") is double s) { rg.StartStation = s; done.Add($"start {s}"); }
            if (DblParam(p, "end_station") is double e) { rg.EndStation = e; done.Add($"end {e}"); }
            foreach (var st in ArrParam(p, "add_stations", 1000))
            {
                if (st.ValueKind == JsonValueKind.Number) rg.AddStation(st.GetDouble(), "");
                else rg.AddStation(NeedDbl(st, "station"), StrParam(st, "description") ?? "");
            }
            if (Has(p, "add_stations")) done.Add("stations added");
            foreach (double st in Has(p, "delete_stations") ? NumList(p, "delete_stations", 1000) : new List<double>())
                rg.DeleteStation(st);
            if (Has(p, "delete_stations")) done.Add("stations deleted");
            if (Has(p, "frequency"))
            {
                var f = p.GetProperty("frequency");
                var set = rg.AppliedAssemblySetting;
                if (DblParam(f, "tangents") is double t) set.FrequencyAlongTangents = t;
                if (DblParam(f, "curves") is double cu) set.FrequencyAlongCurves = cu;
                if (DblParam(f, "spirals") is double sp) set.FrequencyAlongSpirals = sp;
                if (DblParam(f, "profile_curves") is double pc) set.FrequencyAlongProfileCurves = pc;
                done.Add("frequency");
            }
            if (DblParam(p, "split_at") is double at)
            {
                var second = rg.Split(at);
                done.Add($"split at {at}: new region {second.Name}");
            }
        }
        if (BoolParam(p, "rebuild") ?? true) Rebuild(c, cor);
        var regions = new List<object>();
        foreach (CivilDb.BaselineRegion r in bl.BaselineRegions)
            regions.Add(new { name = r.Name, start = R3(r.StartStation), end = R3(r.EndStation), extraStations = Safe(() => r.AdditionalStations().Select(R3).ToList()) });
        return new { corridor = cor.Name, baseline = bl.Name, done, regions };
    }

    /// <summary>Boundaries, masks and feature-line codes of a corridor surface.</summary>
    private static object CorridorSurfaceSet(EditContext c, JsonElement p)
    {
        var cor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        string sname = NeedStr(p, "surface");
        CivilDb.CorridorSurface cs;
        try { cs = cor.CorridorSurfaces[sname]; }
        catch { throw new ArgumentException($"Corridor surface '{sname}' not found on '{cor.Name}'. Use list_corridors."); }
        var done = new List<string>();
        foreach (var code in Has(p, "feature_line_codes") ? StrList(p, "feature_line_codes", 100) : new List<string>())
        {
            cs.AddFeatureLineCode(code);
            done.Add($"breakline code {code}");
        }
        foreach (var b in ArrParam(p, "boundaries", 50))
        {
            string bn = NeedStr(b, "name");
            CivilDb.CorridorSurfaceBoundary bd =
                BoolParam(b, "extents") == true ? cs.Boundaries.AddCorridorExtentsBoundary(bn)
                : StrParam(b, "feature_line_code") is { Length: > 0 } code ? cs.Boundaries.Add(bn, code)
                : cs.Boundaries.Add(bn, new Point3dCollection(PtList(b, "points", "boundary", 3, 10000).ToArray()));
            if (Choice(b, "type", "outside", "outside", "inside") == "inside")
                bd.BoundaryType = CivilDb.CorridorSurfaceBoundaryType.InsideBoundary;
            done.Add($"boundary {bn}");
        }
        foreach (var m in ArrParam(p, "masks", 50))
        {
            string mn = NeedStr(m, "name");
            if (StrParam(m, "feature_line_code") is { Length: > 0 } code) cs.Masks.Add(mn, code);
            else cs.Masks.Add(mn, new Point3dCollection(PtList(m, "points", "mask", 3, 10000).ToArray()));
            done.Add($"mask {mn}");
        }
        if (BoolParam(p, "rebuild") ?? true) Rebuild(c, cor);
        return new { corridor = cor.Name, surface = cs.Name, done, boundaries = cs.Boundaries.Count, masks = cs.Masks.Count };
    }

    // ---------------------------------------------------------------- surfaces

    /// <summary>Drawing objects from a TIN surface: contours (interval or given levels), border, watersheds, grid lines.</summary>
    private static object SurfaceExtract(EditContext c, JsonElement p)
    {
        var tin = FindTinForWrite(c.Civ, c.Tr, NeedStr(p, "surface"));
        string what = Choice(p, "what", "", "contours", "contours_at", "major_contours", "minor_contours", "border", "watershed", "gridded");
        var mode = Choice(p, "mode", "plan", "plan", "model") == "model" ? Autodesk.Civil.SurfaceExtractionSettingsType.Model : Autodesk.Civil.SurfaceExtractionSettingsType.Plan;
        var ids = new List<ObjectId>();
        void Take(ObjectIdCollection col) { foreach (ObjectId id in col) ids.Add(id); }
        switch (what)
        {
            case "contours":
                double iv = NeedPositive(p, "interval");
                if (DblParam(p, "low") is double lo && DblParam(p, "high") is double hi) Take(tin.ExtractContours(lo, hi, iv));
                else Take(tin.ExtractContours(iv));
                break;
            case "contours_at":
                foreach (double z in NumList(p, "elevations", 500)) Take(tin.ExtractContoursAt(z));
                break;
            case "major_contours": Take(tin.ExtractMajorContours(mode)); break;
            case "minor_contours": Take(tin.ExtractMinorContours(mode)); break;
            case "border": Take(tin.ExtractBorder(mode)); break;
            case "watershed": Take(tin.ExtractWatershed(mode)); break;
            default: Take(tin.ExtractGridded(mode)); break;
        }
        if (StrParam(p, "layer") is { Length: > 0 })
        {
            var layer = c.Layer(p);
            foreach (var id in ids) ((Entity)c.Tr.GetObject(id, OpenMode.ForWrite)).LayerId = layer;
        }
        var types = ids.GroupBy(DxfOrName).ToDictionary(g => g.Key, g => g.Count());
        return new { surface = tin.Name, what, count = ids.Count, types, handles = ids.Take(200).Select(H).ToList() };
    }

    // ---------------------------------------------------------------- gravity networks

    /// <summary>A curved gravity pipe between two points: radius (with clockwise) or a point it passes through.</summary>
    private static object PipeCurvedAdd(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"), OpenMode.ForWrite);
        var picker = new GravityParts.Picker(c.Tr, GravityParts.ListFor(c.Civ, c.Db, c.Tr, net, StrParam(p, "parts_list")));
        var part = picker.Pick(CivilDb.DomainType.Pipe, p, "pipe_curved_add");
        var a = NeedPt(p, "start", "pipe_curved_add");
        var b = NeedPt(p, "end", "pipe_curved_add");
        CivilDb.PipeCurveGeometry geom = Has(p, "through")
            ? new CivilDb.PipeCurveGeometry(a, new Point2d(NeedPt(p, "through", "through").X, NeedPt(p, "through", "through").Y), b)
            : new CivilDb.PipeCurveGeometry(a, b, NeedPositive(p, "radius"), BoolParam(p, "clockwise") ?? false, false);
        var id = net.AddCurvePipe(part.FamilyId, part.SizeId, geom, BoolParam(p, "apply_rules") ?? false);
        var pipe = (CivilDb.Pipe)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(p, "name") is { Length: > 0 } n) pipe.Name = n;
        foreach (var (key, pos) in new[] { ("start_structure", CivilDb.ConnectorPositionType.Start), ("end_structure", CivilDb.ConnectorPositionType.End) })
            if (StrParam(p, key) is { Length: > 0 } sn)
            {
                ObjectId sid = ObjectId.Null;
                foreach (ObjectId s in net.GetStructureIds())
                    if (SameName(((CivilDb.Structure)c.Tr.GetObject(s, OpenMode.ForRead)).Name, sn)) sid = s;
                if (sid.IsNull) throw new ArgumentException($"pipe_curved_add: structure '{sn}' not found in '{net.Name}'.");
                pipe.ConnectToStructure(pos, sid, true);
            }
        return new
        {
            network = net.Name, pipe = pipe.Name, handle = H(id), part = part.Label, type = pipe.SubEntityType.ToString(),
            length2d = Safe(() => R3(pipe.Length2D)), radius = Safe(() => R3(pipe.Radius)),
        };
    }

    private static List<CivilDb.Part> GravityParts_(EditContext c, CivilDb.Network net, JsonElement p, string op)
    {
        var parts = new List<CivilDb.Part>();
        var names = Has(p, "names") ? StrList(p, "names", 5000).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        if (Has(p, "handles"))
            foreach (var h in StrList(p, "handles", 5000))
                parts.Add(c.Tr.GetObject(IdFromHandle(c.Db, h), OpenMode.ForWrite) as CivilDb.Part ?? throw new ArgumentException($"{op}: {h} is not a network part."));
        else
            foreach (ObjectId id in net.GetPipeIds().Cast<ObjectId>().Concat(net.GetStructureIds().Cast<ObjectId>()))
            {
                var part = (CivilDb.Part)c.Tr.GetObject(id, OpenMode.ForWrite);
                if (names == null || names.Contains(part.Name)) parts.Add(part);
            }
        return parts;
    }

    /// <summary>Swap pipes or structures to another family and size of the parts list.</summary>
    private static object PartSwap(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"), OpenMode.ForWrite);
        var picker = new GravityParts.Picker(c.Tr, GravityParts.ListFor(c.Civ, c.Db, c.Tr, net, StrParam(p, "parts_list")));
        var parts = GravityParts_(c, net, p, "part_swap");
        if (parts.Count == 0) throw new ArgumentException("part_swap: no parts matched 'names' / 'handles'.");
        var done = new List<object>();
        foreach (var part in parts)
        {
            var domain = part is CivilDb.Pipe ? CivilDb.DomainType.Pipe : CivilDb.DomainType.Structure;
            var choice = picker.Pick(domain, p, $"part_swap {part.Name}");
            part.SwapPartFamilyAndSize(choice.FamilyId, choice.SizeId);
            done.Add(new { name = part.Name, handle = H(part.ObjectId), part = choice.Label });
        }
        return new { network = net.Name, swapped = done.Count, parts = done };
    }

    /// <summary>Show network parts in every section view of a sample line group (or the section views named).</summary>
    private static object PartsToSectionViews(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"));
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string group = NeedStr(p, "group");
        var g = TryFindSampleLineGroup(c.Tr, al, group) ?? throw new ArgumentException($"Sample line group '{group}' not found on '{al.Name}'.");
        var views = new List<ObjectId>();
        foreach (var sl in AlignmentRead.SampleLines(c.Tr, g))
            foreach (ObjectId v in sl.GetSectionViewIds()) views.Add(v);
        if (views.Count == 0) throw new ArgumentException($"parts_to_section_views: '{g.Name}' has no section views (section_views_create).");
        var parts = GravityParts_(c, net, p, "parts_to_section_views");
        int added = 0;
        foreach (var part in parts)
            foreach (var v in views)
            {
                try { part.AddToSectionView(v); added++; }
                catch (Exception ex) { c.Warn($"{part.Name} in section view {H(v)}: {ex.Message}"); }
            }
        return new { network = net.Name, sectionViews = views.Count, parts = parts.Count, added };
    }

    // ---------------------------------------------------------------- pressure networks

    /// <summary>Bends at plan points (PIs), vertical bends at station/elevation, or merging another run into this one.</summary>
    private static object PressureRunEdit(EditContext c, JsonElement p)
    {
        var net = FindPressureNetwork(c.Civ, c.Tr, NeedStr(p, "network"), OpenMode.ForWrite);
        string runName = NeedStr(p, "run");
        CivilDb.PressurePipeRun? run = null, other = null;
        string? otherName = StrParam(p, "merge");
        foreach (CivilDb.PressurePipeRun r in net.PipeRuns)
        {
            if (SameName(r.Name, runName)) run = r;
            if (otherName != null && SameName(r.Name, otherName)) other = r;
        }
        if (run == null) throw new ArgumentException($"Pipe run '{runName}' not found in '{net.Name}'. Use list_pipe_runs.");
        var bends = new List<string>();
        foreach (var pt in Has(p, "bends") ? PtList(p, "bends", "pressure_run_edit", 1, 500) : new())
            bends.Add(H(run.AddBendByPI(new Point2d(pt.X, pt.Y))));
        var vbends = new List<string>();
        foreach (var e in ArrParam(p, "vertical_bends", 500))
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2) throw new ArgumentException("pressure_run_edit: vertical_bends are [station, elevation].");
            vbends.Add(H(run.AddVerticalBendByPVI(e[0].GetDouble(), e[1].GetDouble())));
        }
        if (otherName != null)
        {
            if (other == null) throw new ArgumentException($"Pipe run '{otherName}' not found in '{net.Name}'.");
            run.Merge(other);
        }
        return new { network = net.Name, run = runName, bends, verticalBends = vbends, merged = otherName };
    }

    // ---------------------------------------------------------------- section view tables

    /// <summary>A total-volume or material table on every section view of a sample line group, for one material list.</summary>
    private static object SectionVolumeTables(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string group = NeedStr(p, "group");
        var g = TryFindSampleLineGroup(c.Tr, al, group) ?? throw new ArgumentException($"Sample line group '{group}' not found on '{al.Name}'.");
        string listName = NeedStr(p, "material_list");
        CivilDb.QTOMaterialList? list = null;
        foreach (CivilDb.QTOMaterialList l in g.MaterialLists) if (SameName(l.Name, listName)) list = l;
        if (list == null) throw new ArgumentException($"Material list '{listName}' not found on '{g.Name}' (material_list_create).");
        var type = Choice(p, "type", "total", "total", "material") == "material" ? CivilDb.VolumeTableType.Material : CivilDb.VolumeTableType.TotalVolume;
        var tables = new List<string>();
        int views = 0;
        foreach (var sl in AlignmentRead.SampleLines(c.Tr, g))
            foreach (ObjectId vid in sl.GetSectionViewIds())
            {
                var sv = (CivilDb.SectionView)c.Tr.GetObject(vid, OpenMode.ForWrite);
                views++;
                var vt = sv.VolumeTables;
                if (DblParam(p, "offset_x") is double ox) vt.OffsetX = ox;
                if (DblParam(p, "offset_y") is double oy) vt.OffsetY = oy;
                try { tables.Add(H(vt.CreateVolumeTable(type, list.Guid))); }
                catch (Exception ex) { c.Warn($"section view {sv.Name}: {ex.Message}"); }
            }
        if (views == 0) throw new ArgumentException($"section_volume_tables: '{g.Name}' has no section views (section_views_create).");
        return new { alignment = al.Name, group = g.Name, materialList = list.Name, type = type.ToString(), sectionViews = views, tables = tables.Count,
                     note = "Civil 3D fills the tables when it has computed the material sections." };
    }
}
