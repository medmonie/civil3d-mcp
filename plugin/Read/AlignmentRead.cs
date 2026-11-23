namespace C3dMcpBridge;

/// <summary>Alignments and what hangs off them: profiles, profile views, sample lines, section views (read only).</summary>
internal static class AlignmentRead
{
    public static object ListAlignments(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = Alignments(Civ, tr).Select(a => new
        {
            name = a.Name,
            handle = H(a.ObjectId),
            type = a.AlignmentType.ToString(),
            site = Safe(() => a.SiteName),
            style = Safe(() => a.StyleName),
            layer = a.Layer,
            length = R3(a.Length),
            startStation = R3(a.StartingStation),
            endStation = R3(a.EndingStation),
            entityCount = Safe(() => a.Entities.Count),
            profileCount = a.GetProfileIds().Count,
            extents = Extents(a),
        }).ToList();
        return new { count = rows.Count, alignments = rows };
    });

    public static object StationToPoint(JsonElement p)
    {
        string name = NeedStr(p, "alignment_name");
        double station = NeedDbl(p, "station");
        double offset = DblParam(p, "offset") ?? 0.0;
        return WithRead((db, tr) =>
        {
            var a = FindAlignment(Civ, tr, name);
            double e = 0, n = 0;
            a.PointLocation(station, offset, ref e, ref n);
            return new
            {
                alignment = a.Name, station, offset, easting = R3(e), northing = R3(n),
                stationRange = new[] { R3(a.StartingStation), R3(a.EndingStation) },
            };
        });
    }

    public static object ProfileSheetWindow(JsonElement p) => WithRead((db, tr) =>
    {
        var a = FindAlignment(Civ, tr, NeedStr(p, "alignment_name"));
        string name = NeedStr(p, "profile_view");
        var view = TryFind<CivilDb.ProfileView>(tr, a.GetProfileViewIds(), name)
            ?? throw new ArgumentException($"Profile view '{name}' does not belong to '{a.Name}'.");
        double left = NeedDbl(p, "x_min"), right = NeedDbl(p, "x_max");
        if (left >= right) throw new ArgumentException("x_min must be less than x_max.");

        // Map drawing X to raw station using the graph itself. Entity extents also include bands and labels.
        double start = view.StationStart, end = view.StationEnd;
        double e = (view.ElevationMin + view.ElevationMax) / 2;
        double x0 = 0, x1 = 0, y = 0;
        if (!view.FindXYAtStationAndElevation(start, e, ref x0, ref y) ||
            !view.FindXYAtStationAndElevation(end, e, ref x1, ref y) || Math.Abs(x1 - x0) < 1e-9)
            throw new ArgumentException($"Cannot map the station axis of profile view '{name}'.");
        double Station(double x) => Math.Max(a.StartingStation, Math.Min(a.EndingStation,
            start + Math.Max(0, Math.Min(1, (x - x0) / (x1 - x0))) * (end - start)));
        double s0 = Station(left), s1 = Station(right);
        double lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
        // Every point on the curve is within half a station step of a sampled point, including on bends.
        const int intervals = 64;
        double step = (hi - lo) / intervals;
        double xmin = double.PositiveInfinity, ymin = xmin, xmax = double.NegativeInfinity, ymax = xmax;
        for (int i = 0; i <= intervals; i++)
        {
            double east = 0, north = 0;
            a.PointLocation(i == intervals ? hi : lo + i * step, 0, ref east, ref north);
            xmin = Math.Min(xmin, east); xmax = Math.Max(xmax, east);
            ymin = Math.Min(ymin, north); ymax = Math.Max(ymax, north);
        }
        return new { stationRange = new[] { lo, hi },
                     window = new[] { xmin - step / 2, ymin - step / 2, xmax + step / 2, ymax + step / 2 } };
    });

    public static object PointToStation(JsonElement p)
    {
        string name = NeedStr(p, "alignment_name");
        double x = NeedDbl(p, "x"), y = NeedDbl(p, "y");
        return WithRead((db, tr) =>
        {
            var a = FindAlignment(Civ, tr, name);
            double station = 0, offset = 0;
            a.StationOffset(x, y, ref station, ref offset);
            return new
            {
                alignment = a.Name, x, y, station = R3(station), offset = R3(offset),
                note = "Offset is positive to the right of the alignment direction, negative to the left.",
            };
        });
    }

    public static object ListProfiles(JsonElement p)
    {
        string? only = StrParam(p, "alignment_name");
        return WithRead((db, tr) =>
        {
            var rows = new List<object>();
            foreach (var a in Alignments(Civ, tr, only))
                foreach (ObjectId id in a.GetProfileIds())
                {
                    var pr = (CivilDb.Profile)tr.GetObject(id, OpenMode.ForRead);
                    rows.Add(new
                    {
                        alignment = a.Name,
                        name = pr.Name,
                        handle = H(id),
                        type = pr.ProfileType.ToString(),
                        description = pr.Description,
                        pviCount = Safe(() => pr.PVIs.Count),
                        startStation = Safe(() => R3(pr.StartingStation)),
                        endStation = Safe(() => R3(pr.EndingStation)),
                        minElevation = Safe(() => R3(pr.ElevationMin)),
                        maxElevation = Safe(() => R3(pr.ElevationMax)),
                    });
                }
            return new { count = rows.Count, profiles = rows };
        });
    }

    public static object GetProfileElevation(JsonElement p)
    {
        string alignment = NeedStr(p, "alignment_name"), profile = NeedStr(p, "profile_name");
        double station = NeedDbl(p, "station");
        return WithRead((db, tr) =>
        {
            var a = FindAlignment(Civ, tr, alignment);
            var pr = FindProfile(tr, a, profile);
            return new
            {
                alignment = a.Name, profile = pr.Name, station,
                elevation = R3(pr.ElevationAt(station)),
                stationRange = new[] { R3(pr.StartingStation), R3(pr.EndingStation) },
            };
        });
    }

    public static object ListProfileViews(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = new List<object>();
        foreach (var a in Alignments(Civ, tr))
            foreach (ObjectId id in a.GetProfileViewIds())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(id, OpenMode.ForRead);
                rows.Add(new
                {
                    name = pv.Name, handle = H(id), alignment = a.Name, style = Safe(() => pv.StyleName),
                    stationStart = Safe(() => R3(pv.StationStart)), stationEnd = Safe(() => R3(pv.StationEnd)),
                    elevationMin = Safe(() => R3(pv.ElevationMin)), elevationMax = Safe(() => R3(pv.ElevationMax)),
                    extents = Extents(pv),
                });
            }
        return new { count = rows.Count, profileViews = rows };
    });

    private static IEnumerable<CivilDb.SampleLine> SampleLines(Transaction tr, CivilDb.SampleLineGroup g) =>
        g.GetSampleLineIds().Cast<ObjectId>().Select(id => (CivilDb.SampleLine)tr.GetObject(id, OpenMode.ForRead));

    public static object ListSampleLines(JsonElement p)
    {
        string? only = StrParam(p, "alignment_name");
        return WithRead((db, tr) =>
        {
            var rows = new List<object>();
            foreach (var a in Alignments(Civ, tr, only))
                foreach (var g in SampleLineGroups(tr, a))
                {
                    var stations = SampleLines(tr, g).Select(sl => R3(sl.Station)).ToList();
                    rows.Add(new { alignment = a.Name, group = g.Name, handle = H(g.ObjectId), sampleLineCount = stations.Count, stations = stations.Take(500).ToList() });
                }
            return new { count = rows.Count, sampleLineGroups = rows };
        });
    }

    public static object ListSectionViews(JsonElement p)
    {
        int limit = IntParam(p, "limit", 500, 1, 5000);
        return WithRead((db, tr) =>
        {
            var views =
                from a in Alignments(Civ, tr)
                from g in SampleLineGroups(tr, a)
                from sl in SampleLines(tr, g)
                from ObjectId viewId in sl.GetSectionViewIds()
                select (a, g, sl, viewId);
            return Paged("sectionViews", views, limit, x => new
            {
                alignment = x.a.Name, group = x.g.Name, sampleLine = x.sl.Name, station = R3(x.sl.Station),
                name = ((CivilDb.SectionView)tr.GetObject(x.viewId, OpenMode.ForRead)).Name, handle = H(x.viewId),
                sections = Safe(() => x.sl.GetSectionIds().Count),
            });
        });
    }
}
