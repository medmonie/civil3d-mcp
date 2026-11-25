namespace C3dMcpBridge;

/// <summary>
/// Raw data reads of 0.16 (facts only, for design checks and quantities done by the client): catalogue part data,
/// user-defined properties of points and parcels, sample-line sections, corridor sections, surface sampling and the
/// ground along pipes.
/// </summary>
internal static class DataRead
{
    private static object? Plain(object? v) => v switch
    {
        null => null,
        double d => R3(d),
        float f => R3(f),
        string or bool or int or long or short or uint => v,
        _ => v.ToString(),
    };

    // ---------------------------------------------------------------- part data

    /// <summary>Every catalogue field of the parts of a gravity or pressure network (wall thickness, material, ...).</summary>
    public static object GetPartData(JsonElement p) => WithRead((db, tr) =>
    {
        string network = NeedStr(p, "network");
        string kind = Choice(p, "kind", "all", "all", "pipes", "structures", "fittings", "appurtenances");
        int limit = IntParam(p, "limit", 200, 1, 5000);
        var only = NameSet(p, "parts", 5000);
        var fieldFilter = NameSet(p, "fields", 200);
        bool Want(string name) => only.Count == 0 || only.Contains(name);
        bool WantField(params string?[] names) => fieldFilter.Count == 0 || names.Any(n => n != null && fieldFilter.Contains(n));
        var rows = new List<object>();
        int total = 0;

        if (TryFindPressureNetwork(Civ, tr, network) is { } pnet)
        {
            IEnumerable<(string type, ObjectId id)> ids = Enumerable.Empty<(string, ObjectId)>();
            if (kind is "all" or "pipes") ids = ids.Concat(pnet.GetPipeIds().Cast<ObjectId>().Select(i => ("pipe", i)));
            if (kind is "all" or "fittings") ids = ids.Concat(pnet.GetFittingIds().Cast<ObjectId>().Select(i => ("fitting", i)));
            if (kind is "all" or "appurtenances") ids = ids.Concat(pnet.GetAppurtenanceIds().Cast<ObjectId>().Select(i => ("appurtenance", i)));
            foreach (var (type, id) in ids)
            {
                var part = (CivilDb.PressurePart)tr.GetObject(id, OpenMode.ForRead);
                if (!Want(part.Name)) continue;
                if (++total > limit) continue;
                var fields = new List<object>();
                foreach (CivilDb.PressurePartProperty prop in part.PartData)
                {
                    if (prop.IsHidden || !WantField(prop.Name, prop.DisplayName)) continue;
                    fields.Add(new { name = prop.Name, displayName = prop.DisplayName, value = prop.HasValue ? Plain(Safe(() => prop.Value)) : null });
                }
                rows.Add(new { type, name = part.Name, handle = H(id), description = Safe(() => part.PartDescription), fields });
            }
            return new { network = pnet.Name, networkType = "pressure", count = rows.Count, total, truncated = total > limit, parts = rows };
        }

        var net = FindPipeNetwork(Civ, tr, network);
        IEnumerable<(string type, ObjectId id)> gids = Enumerable.Empty<(string, ObjectId)>();
        if (kind is "all" or "pipes") gids = gids.Concat(net.GetPipeIds().Cast<ObjectId>().Select(i => ("pipe", i)));
        if (kind is "all" or "structures") gids = gids.Concat(net.GetStructureIds().Cast<ObjectId>().Select(i => ("structure", i)));
        foreach (var (type, id) in gids)
        {
            var part = (CivilDb.Part)tr.GetObject(id, OpenMode.ForRead);
            if (!Want(part.Name)) continue;
            if (++total > limit) continue;
            var fields = new List<object>();
            var record = part.PartData;
            foreach (var f in record.GetAllDataFields())
            {
                if (!WantField(f.Name, f.ContextString, f.Description)) continue;
                fields.Add(new
                {
                    name = f.Name, context = f.ContextString, description = f.Description, units = f.Units,
                    value = Plain(Safe(() => f.Value)), readOnly = f.IsReadOnly,
                });
            }
            rows.Add(new { type, name = part.Name, handle = H(id), family = Safe(() => part.PartFamilyName), size = Safe(() => part.PartSizeName), fields });
        }
        return new { network = net.Name, networkType = "gravity", count = rows.Count, total, truncated = total > limit, parts = rows };
    });

    // ---------------------------------------------------------------- user-defined properties (points, parcels)

    private static CivilDb.UDPClassificationCollection Classifications(string kind) =>
        kind == "parcel" ? Civ.ParcelUDPClassifications : Civ.PointUDPClassifications;

    private static string UdpType(CivilDb.UDP u) => u switch
    {
        CivilDb.UDPDouble => "double", CivilDb.UDPInteger => "integer", CivilDb.UDPBoolean => "boolean",
        CivilDb.UDPEnumeration => "enumeration", CivilDb.UDPString => "string", _ => u.GetType().Name,
    };

    public static object ListUserProperties(JsonElement p) => WithRead((db, tr) =>
    {
        string kind = Choice(p, "kind", "point", "point", "parcel");
        var rows = new List<object>();
        foreach (CivilDb.UDPClassification c in Classifications(kind))
            rows.Add(new
            {
                classification = c.Name,
                properties = c.UDPs.Cast<CivilDb.UDP>().Select(u => new
                {
                    name = u.Name, type = UdpType(u), description = Safe(() => u.Description),
                    defaultValue = Plain(Safe(() => u.DefaultValue)), inUse = Safe(() => u.IsInUsed),
                }).ToList(),
            });
        return new { kind, count = rows.Count, classifications = rows };
    });

    private static object? UdpValueOf(CivilDb.CogoPoint pt, CivilDb.UDP u) => u switch
    {
        CivilDb.UDPDouble d => R3(pt.GetUDPValue(d)),
        CivilDb.UDPInteger i => pt.GetUDPValue(i),
        CivilDb.UDPBoolean b => pt.GetUDPValue(b),
        CivilDb.UDPEnumeration e => pt.GetUDPValue(e),
        CivilDb.UDPString s => pt.GetUDPValue(s),
        _ => null,
    };

    private static object? UdpValueOf(CivilDb.Parcel pc, CivilDb.UDP u) => u switch
    {
        CivilDb.UDPDouble d => R3(pc.GetUDPValue(d)),
        CivilDb.UDPInteger i => pc.GetUDPValue(i),
        CivilDb.UDPBoolean b => pc.GetUDPValue(b),
        CivilDb.UDPEnumeration e => pc.GetUDPValue(e),
        CivilDb.UDPString s => pc.GetUDPValue(s),
        _ => null,
    };

    /// <summary>Values of the user-defined properties of COGO points or parcels (one classification or all).</summary>
    public static object GetUserPropertyValues(JsonElement p) => WithRead((db, tr) =>
    {
        string kind = Choice(p, "kind", "point", "point", "parcel");
        string? classification = StrParam(p, "classification");
        int limit = IntParam(p, "limit", 500, 1, 20000);
        var udps = new List<CivilDb.UDP>();
        foreach (CivilDb.UDPClassification c in Classifications(kind))
            if (classification == null || SameName(c.Name, classification)) udps.AddRange(c.UDPs.Cast<CivilDb.UDP>());
        if (classification != null && udps.Count == 0)
            throw new ArgumentException($"get_user_property_values: no {kind} classification '{classification}' with properties. Use list_user_properties.");
        var rows = new List<object>();
        int total = 0;
        if (kind == "point")
        {
            var numbers = ArrParam(p, "numbers", 20000).Select((e, i) => e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out var n) ? n
                : throw new ArgumentException($"numbers item {i + 1} must be a point number.")).ToHashSet();
            foreach (ObjectId id in Civ.CogoPoints)
            {
                var pt = (CivilDb.CogoPoint)tr.GetObject(id, OpenMode.ForRead);
                if (numbers.Count > 0 && !numbers.Contains(pt.PointNumber)) continue;
                if (++total > limit) continue;
                rows.Add(new { number = pt.PointNumber, name = Safe(() => pt.PointName),
                               values = udps.ToDictionary(u => $"{u.ClassificationName}/{u.Name}", u => Safe(() => UdpValueOf(pt, u))) });
            }
        }
        else
        {
            var names = NameSet(p, "names", 20000);
            foreach (ObjectId sid in Civ.GetSiteIds())
            {
                var site = (CivilDb.Site)tr.GetObject(sid, OpenMode.ForRead);
                foreach (ObjectId pid in site.GetParcelIds())
                {
                    var pc = (CivilDb.Parcel)tr.GetObject(pid, OpenMode.ForRead);
                    if (names.Count > 0 && !names.Contains(pc.Name)) continue;
                    if (++total > limit) continue;
                    rows.Add(new { name = pc.Name, site = site.Name,
                                   values = udps.ToDictionary(u => $"{u.ClassificationName}/{u.Name}", u => Safe(() => UdpValueOf(pc, u))) });
                }
            }
        }
        return new { kind, properties = udps.Count, count = rows.Count, total, truncated = total > limit, items = rows };
    });

    // ---------------------------------------------------------------- sections

    /// <summary>
    /// Ground sections at the sample lines of an alignment: the TIN surface sampled along each sample line (every
    /// surface breakpoint), as [offset, elevation] with offsets measured from the alignment (negative = left).
    /// Computed from the surface directly, so it needs no sampled sources or section views. Corridor sections:
    /// get_corridor_section.
    /// </summary>
    public static object GetSections(JsonElement p) => WithRead((db, tr) =>
    {
        var al = FindAlignment(Civ, tr, NeedStr(p, "alignment"));
        string? groupName = StrParam(p, "group");
        double from = DblParam(p, "from_station") ?? double.MinValue, to = DblParam(p, "to_station") ?? double.MaxValue;
        var wanted = ArrParam(p, "surfaces", 100).Select(Text).ToList();
        int limit = IntParam(p, "limit", 50, 1, 2000);
        int maxPoints = IntParam(p, "max_points", 500, 2, 10000);
        var rows = new List<object>();
        int total = 0;
        foreach (var g in SampleLineGroups(tr, al))
        {
            if (groupName != null && !SameName(g.Name, groupName)) continue;
            // surfaces: the ones named, else the TIN surfaces the group's sample lines already have sections of.
            // (SampleLineGroup.GetSectionSources needs the group open for write, which a read must not do.)
            var tins = new List<CivilDb.TinSurface>();
            if (wanted.Count > 0) tins.AddRange(wanted.Select(n => Tin(tr, n)));
            else
            {
                var seen = new HashSet<ObjectId>();
                foreach (var line in AlignmentRead.SampleLines(tr, g))
                    foreach (ObjectId sid in line.GetSectionIds())
                        if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Section sec && Safe(() => sec.SourceId) is ObjectId src
                            && !src.IsNull && seen.Add(src) && tr.GetObject(src, OpenMode.ForRead) is CivilDb.TinSurface t)
                            tins.Add(t);
                if (tins.Count == 0)
                    throw new ArgumentException($"get_sections: group '{g.Name}' samples no TIN surface yet; pass 'surfaces' (or sample one with sample_sources_set).");
            }
            foreach (var sl in AlignmentRead.SampleLines(tr, g).Where(s => s.Station >= from - 1e-6 && s.Station <= to + 1e-6).OrderBy(s => s.Station))
            {
                if (++total > limit) continue;
                var verts = sl.Vertices.Cast<CivilDb.SampleLineVertex>().Select(v => v.Location).ToList();
                var sections = new List<object>();
                foreach (var tin in tins)
                {
                    var pts = new List<double[]>();
                    for (int i = 0; i + 1 < verts.Count; i++)
                    {
                        Point3dCollection got;
                        try { got = tin.SampleElevations(new Point3d(verts[i].X, verts[i].Y, 0), new Point3d(verts[i + 1].X, verts[i + 1].Y, 0)); }
                        catch { continue; }
                        foreach (Point3d pt in got)
                        {
                            double sta = 0, off = 0;
                            try { al.StationOffset(pt.X, pt.Y, ref sta, ref off); } catch { continue; }
                            pts.Add(new[] { R3(off), R3(pt.Z) });
                        }
                    }
                    pts = pts.GroupBy(x => x[0]).Select(x => x.First()).OrderBy(x => x[0]).ToList();
                    int n = pts.Count, step = Math.Max(1, (int)Math.Ceiling(n / (double)maxPoints));
                    var kept = step == 1 ? pts : pts.Where((x, i) => i % step == 0 || i == n - 1).ToList();
                    sections.Add(new { surface = tin.Name, points = kept, pointCount = n, thinned = step > 1,
                                       minElevation = n > 0 ? R3(pts.Min(x => x[1])) : (double?)null, maxElevation = n > 0 ? R3(pts.Max(x => x[1])) : (double?)null });
                }
                rows.Add(new
                {
                    group = g.Name, sampleLine = sl.Name, station = R3(sl.Station),
                    vertices = verts.Select(v => new[] { R3(v.X), R3(v.Y) }).ToList(), sections,
                });
            }
        }
        return new { alignment = al.Name, count = rows.Count, total, truncated = total > limit, sampleLines = rows,
                     note = "points are [offset, elevation] from the TIN surface along the sample line; offsets negative to the left. Corridor sections: get_corridor_section." };
    });

    /// <summary>Corridor section at a station: points (codes, offset, elevation, x/y/z), links and shapes (codes, area).</summary>
    public static object GetCorridorSection(JsonElement p) => WithRead((db, tr) =>
    {
        var cor = FindCorridor(Civ, tr, NeedStr(p, "corridor"));
        string? baselineName = StrParam(p, "baseline");
        double station = NeedDbl(p, "station");
        var codes = NameSet(p, "codes", 200);
        CivilDb.Baseline? bl = null;
        foreach (CivilDb.Baseline b in cor.Baselines)
            if (baselineName == null || SameName(b.Name, baselineName) || SameName((NameOf(tr, () => b.AlignmentId) as string) ?? "", baselineName)) { bl = b; break; }
        if (bl == null) throw new ArgumentException($"get_corridor_section: corridor '{cor.Name}' has no baseline '{baselineName}'. Use list_corridors.");
        var stations = bl.SortedStations();
        if (stations.Length == 0) throw new ArgumentException($"get_corridor_section: baseline '{bl.Name}' has no stations (rebuild the corridor).");
        double used = stations.OrderBy(s => Math.Abs(s - station)).First();
        var aa = bl.GetAppliedAssemblyAtStation(used);
        static List<string> Codes(CivilDb.CorridorCodeCollection c) => c.Cast<string>().ToList();
        bool Keep(CivilDb.CorridorCodeCollection c) => codes.Count == 0 || c.Cast<string>().Any(codes.Contains);
        var points = new List<object>();
        foreach (CivilDb.CalculatedPoint cp in aa.Points)
        {
            if (!Keep(cp.CorridorCodes)) continue;
            var soe = cp.StationOffsetElevationToBaseline;   // (station, offset, elevation relative to the baseline profile)
            points.Add(new { codes = Codes(cp.CorridorCodes), offset = R3(soe.Y), elevation = R3(cp.XYZ.Z), aboveProfile = R3(soe.Z), xyz = P3(cp.XYZ) });
        }
        var links = new List<object>();
        foreach (CivilDb.CalculatedLink cl in aa.Links)
        {
            if (!Keep(cl.CorridorCodes)) continue;
            links.Add(new
            {
                codes = Codes(cl.CorridorCodes),
                points = cl.CalculatedPoints.Cast<CivilDb.CalculatedPoint>().Select(x => new[] { R3(x.StationOffsetElevationToBaseline.Y), R3(x.XYZ.Z) }).ToList(),
            });
        }
        var shapes = new List<object>();
        foreach (CivilDb.CalculatedShape sh in aa.Shapes)
            if (Keep(sh.CorridorCodes)) shapes.Add(new { codes = Codes(sh.CorridorCodes), area = R3(sh.Area) });
        return new
        {
            corridor = cor.Name, baseline = bl.Name, requestedStation = station, station = R3(used),
            assembly = NameOf(tr, () => aa.AssemblyId), points, links, shapes,
            note = "Only the corridor's frequency stations hold sections; the nearest one is used. offset: to the baseline (negative = left); elevation: absolute; aboveProfile: relative to the baseline profile; link points: [offset, elevation].",
        };
    });

    // ---------------------------------------------------------------- surface sampling

    internal static CivilDb.TinSurface Tin(Transaction tr, string name)
    {
        var s = FindSurface(Civ, tr, name);
        return s as CivilDb.TinSurface ?? throw new ArgumentException($"Surface '{s.Name}' is a {s.GetType().Name}; only TIN surfaces are supported.");
    }

    internal static double? Elev(CivilDb.TinSurface tin, double x, double y)
    {
        try { return tin.FindElevationAtXY(x, y); } catch { return null; }
    }

    /// <summary>
    /// Surface elevations in one call: at a list of points, or along a polyline / line / two points (the surface's own
    /// breakpoints, or every 'interval' along it).
    /// </summary>
    public static object SampleSurface(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var tin = Tin(tr, NeedStr(p, "surface"));
        double? interval = DblParam(p, "interval");
        if (Has(p, "points"))
        {
            var pts = PtList(p, "points", "sample_surface", 1, 20000);
            var rows = pts.Select(pt => { var z = Elev(tin, pt.X, pt.Y); return new { x = R3(pt.X), y = R3(pt.Y), elevation = z is double v ? R3(v) : (double?)null }; }).ToList();
            return new { surface = tin.Name, count = rows.Count, outside = rows.Count(r => r.elevation == null), points = rows };
        }
        Curve curve;
        bool temp = false;
        if (StrParam(p, "handle") is string h)
        {
            var id = IdFromHandle(db, h);
            curve = tr.GetObject(id, OpenMode.ForRead) as Curve ?? throw new ArgumentException("sample_surface: handle is not a line, arc or polyline.");
        }
        else
        {
            var line = PtList(p, "line", "sample_surface", 2, 2);
            curve = new Line(new Point3d(line[0].X, line[0].Y, 0), new Point3d(line[1].X, line[1].Y, 0));
            temp = true;
        }
        try
        {
            double length = curve.GetDistanceAtParameter(curve.EndParam);
            var samples = new List<object>();
            if (interval is double step && step > 0)
            {
                int n = (int)Math.Min(20000, Math.Floor(length / step));
                if (Math.Floor(length / step) > 20000)
                    throw new ArgumentException($"sample_surface: interval {step} gives more than 20000 samples over {R3(length)}; use a larger interval.");
                var chain = Enumerable.Range(0, n + 1).Select(i => i * step).ToList();
                if (length - chain[chain.Count - 1] > 1e-6) chain.Add(length);
                foreach (var d in chain)
                {
                    var pt = curve.GetPointAtDist(d);
                    var z = Elev(tin, pt.X, pt.Y);
                    samples.Add(new { chainage = R3(d), x = R3(pt.X), y = R3(pt.Y), elevation = z is double v ? R3(v) : (double?)null });
                }
            }
            else
            {
                Point3dCollection got;
                if (temp) got = tin.SampleElevations(curve.StartPoint, curve.EndPoint);
                else got = tin.SampleElevations(curve.ObjectId);
                foreach (Point3d pt in got)
                {
                    double d;
                    try { d = curve.GetDistAtPoint(curve.GetClosestPointTo(new Point3d(pt.X, pt.Y, curve.StartPoint.Z), false)); }
                    catch { d = double.NaN; }
                    samples.Add(new { chainage = R3(d), x = R3(pt.X), y = R3(pt.Y), elevation = (double?)R3(pt.Z) });
                }
            }
            return new { surface = tin.Name, length = R3(length), mode = interval is > 0 ? $"every {interval}" : "surface breakpoints",
                         count = samples.Count, samples };
        }
        finally { if (temp) curve.Dispose(); }
    });

    /// <summary>
    /// Ground along the pipes of a network: at both ends and every 'interval', the surface elevation, the pipe's
    /// invert / centre / outside top, and the cover to the outside top. Facts only.
    /// </summary>
    private const int MaxSamplesPerPipe = 2000;

    public static object PipeGroundProfile(JsonElement p) => WithRead((db, tr) =>
    {
        var tin = Tin(tr, NeedStr(p, "surface"));
        string network = NeedStr(p, "network");
        double step = DblParam(p, "interval") ?? 5.0;
        if (step <= 0) throw new ArgumentException("pipe_ground_profile: interval must be > 0.");
        int limit = IntParam(p, "limit", 200, 1, 5000);
        var only = NameSet(p, "pipes", 5000);
        var segs = new List<(string name, Point3d a, Point3d b, double innerHalf, double outerHalf)>();
        if (TryFindPressureNetwork(Civ, tr, network) is { } pnet)
        {
            foreach (ObjectId id in pnet.GetPipeIds())
            {
                var pp = (CivilDb.PressurePipe)tr.GetObject(id, OpenMode.ForRead);
                segs.Add((pp.Name, pp.StartPoint, pp.EndPoint, pp.InnerDiameter / 2, pp.OuterDiameter / 2));
            }
        }
        else
        {
            var net = FindPipeNetwork(Civ, tr, network);
            foreach (ObjectId id in net.GetPipeIds())
            {
                var pipe = (CivilDb.Pipe)tr.GetObject(id, OpenMode.ForRead);
                bool circular = pipe.CrossSectionalShape == CivilDb.SweptShapeType.Circular;
                segs.Add((pipe.Name, pipe.StartPoint, pipe.EndPoint, GravityRead.InnerHalfHeight(pipe),
                          (circular ? pipe.OuterDiameterOrWidth : pipe.OuterHeight) / 2));
            }
        }
        var rows = new List<object>();
        int total = 0;
        foreach (var s in segs)
        {
            if (only.Count > 0 && !only.Contains(s.name)) continue;
            if (++total > limit) continue;
            double len = new Point2d(s.a.X, s.a.Y).GetDistanceTo(new Point2d(s.b.X, s.b.Y));
            // at most MaxSamplesPerPipe samples per pipe: a tiny interval on long pipes would otherwise build millions of rows
            double pipeStep = Math.Max(step, len / MaxSamplesPerPipe);
            var chain = new List<double>();
            for (double d = 0; d < len - 1e-6; d += pipeStep) chain.Add(d);
            chain.Add(len);
            var samples = chain.Select(d =>
            {
                double t = len > 0 ? d / len : 0;
                double x = s.a.X + (s.b.X - s.a.X) * t, y = s.a.Y + (s.b.Y - s.a.Y) * t, zc = s.a.Z + (s.b.Z - s.a.Z) * t;
                var g = Elev(tin, x, y);
                return new
                {
                    chainage = R3(d), x = R3(x), y = R3(y), ground = g is double gv ? R3(gv) : (double?)null,
                    invert = R3(zc - s.innerHalf), centre = R3(zc), outsideTop = R3(zc + s.outerHalf),
                    cover = g is double gv2 ? R3(gv2 - (zc + s.outerHalf)) : (double?)null,
                };
            }).ToList();
            var covers = samples.Where(x => x.cover != null).Select(x => x.cover!.Value).ToList();
            rows.Add(new
            {
                pipe = s.name, length2d = R3(len), samples,
                minCover = covers.Count > 0 ? R3(covers.Min()) : (double?)null, maxCover = covers.Count > 0 ? R3(covers.Max()) : (double?)null,
                outside = samples.Count(x => x.ground == null),
            });
        }
        return new { network, surface = tin.Name, interval = step, count = rows.Count, total, truncated = total > limit, pipes = rows,
                     note = "cover = ground - outside top of pipe (gravity: outside height for non-circular pipes; pressure: outer diameter). Straight line between the pipe ends." };
    });
}
