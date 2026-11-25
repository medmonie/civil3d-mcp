namespace C3dMcpBridge;

/// <summary>Alignments, profiles, profile views, sample lines and section views.</summary>
internal static class AlignmentOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["alignment_create"] = AlignmentCreate;
        ops["profile_create"] = ProfileCreate;
        ops["profile_from_surface"] = ProfileFromSurface;
        ops["profile_view_create"] = ProfileViewCreate;
        ops["profile_view_add_parts"] = ProfileViewAddParts;
        ops["sample_lines_create"] = SampleLinesCreate;
        ops["section_views_create"] = SectionViewsCreate;
        ops["sample_sources_set"] = SampleSourcesSet;
        ops["profile_pvis_set"] = ProfilePvisSet;
    }

    private const double StationTolerance = 1e-6;

    private static string StationRange(CivilDb.Alignment a) => $"{R3(a.StartingStation)} to {R3(a.EndingStation)}";

    private static bool OnAlignment(CivilDb.Alignment a, double station) =>
        station >= a.StartingStation - StationTolerance && station <= a.EndingStation + StationTolerance;

    // ---------------------------------------------------------------- alignments

    private static object AlignmentCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var pts = PtList(p, "points", "alignment_create", 2, 5000).Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        for (int i = 1; i < pts.Count; i++)
            if (pts[i].DistanceTo(pts[i - 1]) < 1e-6) throw new ArgumentException($"alignment_create: points {i} and {i + 1} are the same.");
        double? radius = PositiveParam(p, "radius");
        RequireNewName<CivilDb.Alignment>(c.Tr, AllAlignmentIds(c.Civ, c.Tr), name, "An alignment");

        ObjectId siteId = StrParam(p, "site") is { Length: > 0 } site
            ? TryFindSite(c.Civ, c.Tr, site)?.ObjectId ?? throw new ArgumentException($"Site '{site}' not found.")
            : ObjectId.Null;   // siteless
        var id = CivilDb.Alignment.Create(c.Civ, name, siteId, c.Layer(p),
            Styles.Id(c.Civ.Styles.AlignmentStyles, StrParam(p, "style"), "alignment style"),
            Styles.Id(c.Civ.Styles.LabelSetStyles.AlignmentLabelSetStyles, StrParam(p, "label_set"), "alignment label set"));
        var al = (CivilDb.Alignment)c.Tr.GetObject(id, OpenMode.ForWrite);

        // Fixed tangents between the PIs, then a free curve between each pair of tangents.
        var tangents = new List<CivilDb.AlignmentLine>();
        for (int i = 0; i + 1 < pts.Count; i++) tangents.Add(al.Entities.AddFixedLine(pts[i], pts[i + 1]));
        // Curve at each interior PI: 'curves' (one entry per interior PI) overrides 'radius' / 'spiral_in' / 'spiral_out'.
        var perPi = ArrParam(p, "curves", 5000);
        if (perPi.Count > 0 && perPi.Count != tangents.Count - 1)
            throw new ArgumentException($"alignment_create: 'curves' needs one entry per interior PI ({tangents.Count - 1}), got {perPi.Count}.");
        double spIn = DblParam(p, "spiral_in") ?? 0, spOut = DblParam(p, "spiral_out") ?? spIn;
        var spType = SpiralTypeParam(p);
        int curves = 0, spirals = 0;
        for (int i = 0; i + 1 < tangents.Count; i++)
        {
            double? rr = radius; double s1 = spIn, s2 = spOut;
            if (perPi.Count > 0)
            {
                var e = perPi[i];
                if (e.ValueKind == JsonValueKind.Null) continue;
                rr = e.ValueKind == JsonValueKind.Number ? e.GetDouble() : PositiveParam(e, "radius");
                s1 = DblParam(e, "spiral_in") ?? 0; s2 = DblParam(e, "spiral_out") ?? s1;
            }
            if (rr is not double r || r <= 0) continue;
            try
            {
                if (s1 > 0 || s2 > 0)
                {
                    al.Entities.AddFreeSCS(tangents[i].EntityId, tangents[i + 1].EntityId, s1, s2, CivilDb.SpiralParamType.Length, r, false, spType);
                    spirals += (s1 > 0 ? 1 : 0) + (s2 > 0 ? 1 : 0);
                }
                else al.Entities.AddFreeCurve(tangents[i].EntityId, tangents[i + 1].EntityId, r,
                        CivilDb.CurveParamType.Radius, false, CivilDb.CurveType.Compound);
                curves++;
            }
            catch (Exception ex) { c.Warn($"no curve at vertex {i + 2} (radius {r}, spirals {s1}/{s2} do not fit): {ex.Message}"); }
        }
        if (StrParam(p, "description") is string d) al.Description = d;
        return new { alignment = name, handle = H(id), tangents = tangents.Count, curves, spirals, length = Safe(() => R3(al.Length)) };
    }

    internal static Autodesk.Civil.SpiralType SpiralTypeParam(JsonElement p) =>
        EnumParam(p, "spiral_type", Autodesk.Civil.SpiralType.Clothoid);

    // ---------------------------------------------------------------- profiles

    /// <summary>A PVI as given: [station, elevation] or {station, elevation, curve?, length?, k?, radius?, lengths?}.</summary>
    internal sealed record PviSpec(double Station, double? Elevation, string Curve, double? Length, double? K, double? Radius, double[]? Lengths);

    internal static List<PviSpec> ParsePvis(JsonElement p, string name, string op, bool needElevation = true) =>
        ArrParam(p, name, 5000).Select((e, i) =>
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                if (e.GetArrayLength() != 2 || e[0].ValueKind != JsonValueKind.Number || e[1].ValueKind != JsonValueKind.Number)
                    throw new ArgumentException($"{op}: pvi {i + 1} must be [station, elevation] or an object.");
                return new PviSpec(e[0].GetDouble(), e[1].GetDouble(), "none", null, null, null, null);
            }
            if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{op}: pvi {i + 1} must be [station, elevation] or an object.");
            double st = NeedDbl(e, "station");
            double? el = DblParam(e, "elevation");
            if (needElevation && el == null) throw new ArgumentException($"{op}: pvi {i + 1} needs 'elevation'.");
            double[]? lens = Has(e, "lengths") ? NumList(e, "lengths", 2).ToArray() : null;
            if (lens != null && lens.Length != 2) throw new ArgumentException($"{op}: pvi {i + 1}: 'lengths' is [length before, length after].");
            string curve = Choice(e, "curve", lens != null ? "asymmetric" : Has(e, "radius") ? "circular" : (Has(e, "length") || Has(e, "k")) ? "parabola" : "none",
                                  "none", "parabola", "circular", "asymmetric");
            var spec = new PviSpec(st, el, curve, PositiveParam(e, "length"), PositiveParam(e, "k"), PositiveParam(e, "radius"), lens);
            if (curve == "parabola" && spec.Length == null && spec.K == null) throw new ArgumentException($"{op}: pvi {i + 1}: a parabola needs 'length' or 'k'.");
            if (curve == "circular" && spec.Radius == null && spec.Length == null) throw new ArgumentException($"{op}: pvi {i + 1}: a circular curve needs 'radius'.");
            if (curve == "asymmetric" && lens == null) throw new ArgumentException($"{op}: pvi {i + 1}: an asymmetric parabola needs 'lengths'.");
            return spec;
        }).ToList();

    /// <summary>Puts the vertical curves on PVIs that already exist (their neighbours give the grades).</summary>
    internal static List<object> ApplyCurves(EditContext c, CivilDb.Profile pr, IEnumerable<PviSpec> specs)
    {
        var done = new List<object>();
        foreach (var v in specs.Where(v => v.Curve != "none"))
        {
            var pvi = FindPvi(pr, v.Station) ?? throw new ArgumentException($"No PVI at station {v.Station}.");
            double st = pvi.Station, el = pvi.Elevation;
            double gIn = pvi.GradeIn, gOut = pvi.GradeOut;
            double a = Math.Abs(gOut - gIn) * 100.0;              // grade change in percent
            try
            {
                pr.PVIs.RemoveAt(st, el);
                switch (v.Curve)
                {
                    case "parabola":
                        double len = v.Length ?? v.K!.Value * a;
                        if (len <= 0) throw new ArgumentException("no grade change at this PVI");
                        pr.PVIs.AddPVISymParabola(st, el, len);
                        done.Add(new { station = R3(st), curve = v.Curve, length = R3(len), k = a > 0 ? R3(len / a) : (double?)null });
                        break;
                    case "circular":
                        double rad = v.Radius ?? (a > 0 ? v.Length!.Value / (a / 100.0) : 0);
                        pr.PVIs.AddPVIArc(st, el, rad);
                        done.Add(new { station = R3(st), curve = v.Curve, radius = R3(rad) });
                        break;
                    default:
                        pr.PVIs.AddPVIAsymParabola(st, el, v.Lengths![0], v.Lengths[1]);
                        done.Add(new { station = R3(st), curve = v.Curve, lengths = v.Lengths });
                        break;
                }
            }
            catch (Exception ex)
            {
                if (FindPvi(pr, st) == null) pr.PVIs.AddPVI(st, el);   // put the plain PVI back
                c.Warn($"no {v.Curve} curve at station {R3(st)}: {ex.Message}");
            }
        }
        return done;
    }

    internal static CivilDb.ProfilePVI? FindPvi(CivilDb.Profile pr, double station)
    {
        for (int i = 0; i < pr.PVIs.Count; i++)
            if (Math.Abs(pr.PVIs[i].Station - station) < 1e-3) return pr.PVIs[i];
        return null;
    }

    private static ObjectId ProfileStyle(EditContext c, JsonElement p) =>
        Styles.Id(c.Civ.Styles.ProfileStyles, StrParam(p, "style"), "profile style");

    private static ObjectId ProfileLabelSet(EditContext c, JsonElement p) =>
        Styles.Id(c.Civ.Styles.LabelSetStyles.ProfileLabelSetStyles, StrParam(p, "label_set"), "profile label set");

    private static object ProfileCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var pvis = ParsePvis(p, "pvis", "profile_create").OrderBy(v => v.Station).ToList();
        if (pvis.Count < 2) throw new ArgumentException("profile_create: at least 2 PVIs are needed.");
        if (!OnAlignment(al, pvis[0].Station) || !OnAlignment(al, pvis[^1].Station))
            throw new ArgumentException($"profile_create: PVI stations must lie within the alignment ({StationRange(al)}).");

        var id = CivilDb.Profile.CreateByLayout(name, al.ObjectId, c.Layer(p), ProfileStyle(c, p), ProfileLabelSet(c, p));
        var pr = (CivilDb.Profile)c.Tr.GetObject(id, OpenMode.ForWrite);
        foreach (var v in pvis) pr.PVIs.AddPVI(v.Station, v.Elevation!.Value);
        var curves = ApplyCurves(c, pr, pvis);
        return new { alignment = al.Name, profile = name, handle = H(id), pvis = pvis.Count, curves };
    }

    /// <summary>Add, change or remove PVIs of a layout profile, with or without vertical curves.</summary>
    private static object ProfilePvisSet(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var pr = FindProfile(c.Tr, al, NeedStr(p, "profile"), OpenMode.ForWrite);
        if (pr.ProfileType != CivilDb.ProfileType.FG)
            throw new ArgumentException($"profile_pvis_set: '{pr.Name}' is a {pr.ProfileType} profile; only layout profiles have editable PVIs.");
        var removed = new List<double>();
        foreach (double st in Has(p, "remove") ? NumList(p, "remove", 5000) : new List<double>())
        {
            var pvi = FindPvi(pr, st) ?? throw new ArgumentException($"profile_pvis_set: no PVI at station {st}.");
            pr.PVIs.RemoveAt(pvi.Station, pvi.Elevation);
            removed.Add(R3(st));
        }
        var specs = ParsePvis(p, "pvis", "profile_pvis_set", needElevation: false);
        int added = 0, changed = 0;
        foreach (var v in specs)
        {
            if (!OnAlignment(al, v.Station)) throw new ArgumentException($"profile_pvis_set: station {v.Station} is off the alignment ({StationRange(al)}).");
            var pvi = FindPvi(pr, v.Station);
            if (pvi != null)
            {
                double el = v.Elevation ?? pvi.Elevation;
                pr.PVIs.RemoveAt(pvi.Station, pvi.Elevation);   // also drops any curve; ApplyCurves puts the requested one back
                pr.PVIs.AddPVI(v.Station, el);
                changed++;
            }
            else
            {
                if (v.Elevation == null) throw new ArgumentException($"profile_pvis_set: new PVI at {v.Station} needs 'elevation'.");
                pr.PVIs.AddPVI(v.Station, v.Elevation.Value);
                added++;
            }
        }
        var curves = ApplyCurves(c, pr, specs);
        return new { alignment = al.Name, profile = pr.Name, added, changed, removed, curves, pvis = pr.PVIs.Count };
    }

    private static object ProfileFromSurface(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var surf = FindSurface(c.Civ, c.Tr, NeedStr(p, "surface"));
        var id = CivilDb.Profile.CreateFromSurface(name, al.ObjectId, surf.ObjectId, c.Layer(p), ProfileStyle(c, p), ProfileLabelSet(c, p));
        return new { alignment = al.Name, surface = surf.Name, profile = name, handle = H(id) };
    }

    // ---------------------------------------------------------------- profile views

    private static object ProfileViewCreate(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string name = StrParam(p, "name") ?? al.Name + " - Profile View";
        var style = Styles.Id(c.Civ.Styles.ProfileViewStyles, StrParam(p, "style"), "profile view style");
        string? bandName = StrParam(p, "band_set");
        var bandSet = BandSetFor(c, bandName);
        var id = CivilDb.ProfileView.Create(al.ObjectId, NeedPt(p, "at", "profile_view_create"), name, bandSet, style);
        var pv = (CivilDb.ProfileView)c.Tr.GetObject(id, OpenMode.ForWrite);
        var bands = FillBandData(c, pv, al);
        if (bands.Count == 0 && bandName != null)
            c.Warn($"profile_view_create: '{pv.Name}' has no bands (band set '{Styles.NameOf(c.Tr, bandSet)}' is empty). Pass band_set, or add bands with profile_view_bands.");
        return new { profileView = pv.Name, handle = H(id), alignment = al.Name, style = Styles.NameOf(c.Tr, style),
                     bandSet = Styles.NameOf(c.Tr, bandSet), bands, profiles = al.GetProfileIds().Count };
    }

    /// <summary>
    /// The band set to use: the one named; with none named, 'Standard' (or the first set); with "auto", 'Standard'
    /// when it has bands, else the band set with "Elevation" in its name and the fewest bands, else the first with any. A template's default can be an empty set, which gives
    /// a profile view with no bands at all.
    /// </summary>
    internal static ObjectId BandSetFor(EditContext c, string? name)
    {
        var sets = c.Civ.Styles.ProfileViewBandSetStyles;
        if (!string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase)) return Styles.Id(sets, name, "profile view band set");
        int Count(ObjectId id)
        {
            var set = (CivilStyles.ProfileViewBandSetStyle)c.Tr.GetObject(id, OpenMode.ForRead);
            return set.GetBottomBandSetItems().Count + set.GetTopBandSetItems().Count;
        }
        try { var standard = sets[Styles.StandardStyle]; if (Count(standard) > 0) return standard; } catch { /* no Standard set */ }
        // Prefer a set that writes elevations (Civil 3D's metric template has no 'Standard' set, and its first set,
        // 'Stations Only', shows nothing but stations).
        ObjectId best = ObjectId.Null, any = ObjectId.Null;
        int bestCount = int.MaxValue;
        for (int i = 0; i < sets.Count; i++)
        {
            int n = Count(sets[i]);
            if (n == 0) continue;
            if (any.IsNull) any = sets[i];
            string setName = Styles.NameOf(c.Tr, sets[i]) ?? "";
            if (setName.IndexOf("Elevation", StringComparison.OrdinalIgnoreCase) >= 0 && n < bestCount) { best = sets[i]; bestCount = n; }
        }
        if (!best.IsNull) return best;
        return any.IsNull ? Styles.Id(sets, null, "profile view band set") : any;
    }

    /// <summary>
    /// Gives every profile data band without a profile the alignment's first profile (the ground) and, when there is
    /// one, its second profile as profile 2, so the band shows values instead of an empty strip. Returns the bands now on the view.
    /// </summary>
    internal static List<object> FillBandData(EditContext c, CivilDb.ProfileView pv, CivilDb.Alignment al)
    {
        var report = new List<object>();
        var profiles = al.GetProfileIds();
        var first = profiles.Count > 0 ? profiles[0] : ObjectId.Null;
        foreach (var location in new[] { Autodesk.Civil.BandLocationType.Bottom, Autodesk.Civil.BandLocationType.Top })
        {
            var items = location == Autodesk.Civil.BandLocationType.Bottom ? pv.Bands.GetBottomBandItems() : pv.Bands.GetTopBandItems();
            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.BandType == Autodesk.Civil.BandType.ProfileData && item.Profile1Id.IsNull && !first.IsNull)
                {
                    item.Profile1Id = first;
                    if (item.Profile2Id.IsNull && profiles.Count > 1) item.Profile2Id = profiles[1];
                    changed = true;
                }
                report.Add(new { location = location.ToString().ToLowerInvariant(), type = item.BandType.ToString(),
                                 style = Styles.NameOf(c.Tr, item.BandStyleId) });
            }
            if (!changed) continue;
            if (location == Autodesk.Civil.BandLocationType.Bottom) pv.Bands.SetBottomBandItems(items);
            else pv.Bands.SetTopBandItems(items);
        }
        return report;
    }

    private static object ProfileViewAddParts(EditContext c, JsonElement p)
    {
        var pv = FindProfileView(c.Civ, c.Tr, NeedStr(p, "profile_view"));
        var ids = new List<ObjectId>();
        void AddAll(ObjectIdCollection more) => ids.AddRange(more.Cast<ObjectId>());

        if (StrParam(p, "pipe_network") is string gravity)
        {
            var net = FindPipeNetwork(c.Civ, c.Tr, gravity);
            AddGravity(net.GetPipeIds(), "pipe_names");
            AddGravity(net.GetStructureIds(), "structure_names");
        }
        else if (Has(p, "pipe_names") || Has(p, "structure_names"))
            throw new ArgumentException("pipe_names and structure_names require pipe_network.");

        void AddGravity(ObjectIdCollection all, string key)
        {
            if (!Has(p, key)) { AddAll(all); return; }
            var names = ArrParam(p, key, 5000).Select(Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in all)
                if (c.Tr.GetObject(id, OpenMode.ForRead) is CivilDb.Part part && names.Remove(part.Name)) ids.Add(id);
            if (names.Count > 0) throw new ArgumentException($"Parts in '{key}' were not found: {string.Join(", ", names.Take(10))}.");
        }
        if (StrParam(p, "pressure_network") is string pressure)
        {
            var net = FindPressureNetwork(c.Civ, c.Tr, pressure);
            AddAll(net.GetPipeIds());
            AddAll(net.GetFittingIds());
            AddAll(net.GetAppurtenanceIds());
        }
        if (Has(p, "handles")) ids.AddRange(StrList(p, "handles", 5000).Select(h => IdFromHandle(c.Db, h)));
        if (ids.Count == 0) throw new ArgumentException("profile_view_add_parts: give pipe_network, pressure_network and/or handles.");

        int added = 0;
        foreach (var id in ids.Distinct())
        {
            switch (c.Tr.GetObject(id, OpenMode.ForWrite))
            {
                case CivilDb.Part part: part.AddToProfileView(pv.ObjectId); added++; break;
                case CivilDb.PressurePart part: part.AddToProfileView(pv.ObjectId); added++; break;
                default: c.Warn($"{H(id)} is not a network part; skipped."); break;
            }
        }
        return new { profileView = pv.Name, partsAdded = added };
    }

    // ---------------------------------------------------------------- sample lines and section views

    private static object SampleLinesCreate(EditContext c, JsonElement p)
    {
        const int maxLines = 5000;
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string group = NeedStr(p, "group");
        var stations = NumList(p, "stations", maxLines);
        if (PositiveParam(p, "interval") is double step)
        {
            for (double s = al.StartingStation; s <= al.EndingStation + StationTolerance && stations.Count < maxLines; s += step)
                stations.Add(Math.Min(s, al.EndingStation));
            if (stations.Count >= maxLines && stations[^1] < al.EndingStation - StationTolerance)
                c.Warn($"sample_lines_create: stopped at {maxLines} sample lines (station {R3(stations[^1])}); use a larger interval or several calls.");
        }
        if (stations.Count == 0) throw new ArgumentException("sample_lines_create: give stations and/or interval.");
        if (stations.Where(s => !OnAlignment(al, s)).Select(s => (double?)s).FirstOrDefault() is double outside)
            throw new ArgumentException($"sample_lines_create: station {outside} is outside the alignment ({StationRange(al)}).");

        var existing = TryFindSampleLineGroup(c.Tr, al, group);
        var groupId = existing?.ObjectId ?? CivilDb.SampleLineGroup.Create(group, al.ObjectId);
        string prefix = StrParam(p, "prefix") ?? "SL-";
        // Sample lines are drawn across the alignment, left_width to the left and right_width to the right; a line made
        // from a station alone has no width and samples nothing.
        double left = PositiveParam(p, "left_width") ?? 20.0, right = PositiveParam(p, "right_width") ?? 20.0;
        foreach (var s in stations.Distinct().OrderBy(x => x))
        {
            double le = 0, ln = 0, re = 0, rn = 0;
            al.PointLocation(s, -left, ref le, ref ln);
            al.PointLocation(s, right, ref re, ref rn);
            var pts = new Point2dCollection { new Point2d(le, ln), new Point2d(re, rn) };
            var id = CivilDb.SampleLine.Create($"{prefix}{s:0.###}", groupId, pts);
            var sl = (CivilDb.SampleLine)c.Tr.GetObject(id, OpenMode.ForWrite);
            if (Math.Abs(sl.Station - s) > StationTolerance) c.Warn($"sample_lines_create: '{sl.Name}' landed at station {R3(sl.Station)} instead of {s}.");
        }
        var g = (CivilDb.SampleLineGroup)c.Tr.GetObject(groupId, OpenMode.ForRead);
        return new { alignment = al.Name, group, groupCreated = existing == null, leftWidth = left, rightWidth = right,
                     sampleLinesNow = g.GetSampleLineIds().Count };
    }

    /// <summary>Choose which sources (surfaces, corridors, networks) a sample line group samples, so sections exist.</summary>
    private static object SampleSourcesSet(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string group = NeedStr(p, "group");
        var g = TryFindSampleLineGroup(c.Tr, al, group, OpenMode.ForWrite)
                ?? throw new ArgumentException($"Sample line group '{group}' not found on alignment '{al.Name}'. Use list_sample_lines.");
        var names = NameSet(p, "sources", 200);
        bool sampled = BoolParam(p, "sampled") ?? true;
        var changed = new List<string>();
        var available = new List<object>();
        var sourceNames = new List<string>();
        foreach (CivilDb.SectionSource source in g.GetSectionSources())
        {
            sourceNames.Add(source.SourceName);
            if (names.Count > 0 && !names.Contains(source.SourceName)) continue;
            try
            {
                if (source.IsSampled != sampled) { source.IsSampled = sampled; changed.Add(source.SourceName); }
            }
            catch (Exception ex) { c.Warn($"sample_sources_set: '{source.SourceName}': {ex.Message}"); }
            available.Add(new { name = source.SourceName, type = source.SourceType.ToString(), sampled = Safe(() => source.IsSampled) });
        }
        var missing = names.Where(n => !sourceNames.Any(a => SameName(a, n))).ToList();
        if (missing.Count > 0) c.Warn($"sample_sources_set: not a source of '{group}': {string.Join(", ", missing)}.");
        return new { alignment = al.Name, group = g.Name, sampled, changed, sources = available };
    }

    private static object SectionViewsCreate(EditContext c, JsonElement p)
    {
        var al = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        string group = NeedStr(p, "group");
        var g = TryFindSampleLineGroup(c.Tr, al, group, OpenMode.ForWrite)
                ?? throw new ArgumentException($"Sample line group '{group}' not found on alignment '{al.Name}'. Use list_sample_lines.");

        // Sample every source (surfaces, corridors, networks), otherwise the views would be empty.
        var sampled = new List<string>();
        foreach (CivilDb.SectionSource source in g.GetSectionSources())
        {
            try
            {
                if (!source.IsSampled) source.IsSampled = true;
                sampled.Add($"{source.SourceType}: {source.SourceName}");
            }
            catch (Exception ex) { c.Warn($"could not sample '{source.SourceName}': {ex.Message}"); }
        }

        var origin = NeedPt(p, "at", "section_views_create");
        int columns = IntParam(p, "columns", 5, 1, 100);
        double dx = DblParam(p, "spacing_x") ?? 80.0, dy = DblParam(p, "spacing_y") ?? 50.0;
        var lines = g.GetSampleLineIds().Cast<ObjectId>()
            .Select(id => (CivilDb.SampleLine)c.Tr.GetObject(id, OpenMode.ForRead))
            .OrderBy(sl => sl.Station).ToList();
        if (lines.Count == 0) throw new ArgumentException($"Sample line group '{group}' has no sample lines.");

        // One view per sample line, in station order, laid out left to right and then upwards.
        for (int i = 0; i < lines.Count; i++)
        {
            var at = new Point3d(origin.X + (i % columns) * dx, origin.Y + (i / columns) * dy, 0);
            CivilDb.SectionView.Create(lines[i].Name, lines[i].ObjectId, at);
        }
        return new { alignment = al.Name, group, sectionViews = lines.Count, sampledSources = sampled };
    }
}
