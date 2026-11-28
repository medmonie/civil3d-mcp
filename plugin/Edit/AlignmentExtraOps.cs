namespace C3dMcpBridge;

/// <summary>
/// Alignment extras (roadmap #77): reverse, copy to a site, polyline from the alignment; reads of stations at intervals /
/// geometry points and of the distance to another alignment. Starting chainage, profile update mode and split profile
/// views are property_set settings.
/// </summary>
internal static class AlignmentExtraOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["alignment_reverse"] = Reverse;
        ops["alignment_copy_to_site"] = CopyToSite;
        ops["alignment_to_polyline"] = ToPolyline;
    }

    private static object Reverse(EditContext c, JsonElement p)
    {
        var a = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"), OpenMode.ForWrite);
        var start = a.StartPoint; var end = a.EndPoint;
        if (a.GetProfileIds().Count > 0 && !(BoolParam(p, "confirm") ?? false))
            throw new ArgumentException($"alignment_reverse: '{a.Name}' has {a.GetProfileIds().Count} profile(s); reversing changes their stationing. Add confirm: true.");
        a.Reverse();
        return new { alignment = a.Name, startBefore = P3(start), endBefore = P3(end), start = P3(a.StartPoint), end = P3(a.EndPoint),
                     length = R3(a.Length) };
    }

    private static object CopyToSite(EditContext c, JsonElement p)
    {
        var a = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"), OpenMode.ForWrite);
        string site = NeedStr(p, "site");
        var before = c.Civ.GetAlignmentIds().Count + c.Civ.GetSitelessAlignmentIds().Count;
        if (TryFindSite(c.Civ, c.Tr, site) is { } s) a.CopyToSite(s.ObjectId);
        else throw new ArgumentException($"alignment_copy_to_site: no site '{site}' (site_create).");
        return new { alignment = a.Name, site, alignmentsBefore = before };
    }

    private static object ToPolyline(EditContext c, JsonElement p)
    {
        var a = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var id = a.GetPolyline();
        if (id.IsNull) throw new InvalidOperationException("alignment_to_polyline: Civil 3D made no polyline.");
        var pl = (Entity)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(p, "layer") is { Length: > 0 }) pl.LayerId = c.Layer(p);
        return new { alignment = a.Name, handle = H(id), type = DxfOrName(id), layer = pl.Layer, length = Safe(() => R3(((Curve)pl).GetDistanceAtParameter(((Curve)pl).EndParam))) };
    }

    // ---------------------------------------------------------------- reads

    public static object AlignmentStations(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var a = FindAlignment(Civ, tr, NeedStr(p, "alignment"));
        string kind = Choice(p, "kind", "geometry", "geometry", "interval", "pi", "equation", "all");
        int limit = IntParam(p, "limit", 1000, 1, 20000);
        CivilDb.Station[] set = kind switch
        {
            "interval" => a.GetStationSet(CivilDb.StationTypes.Major, PositiveParam(p, "interval") ?? throw new ArgumentException("alignment_stations: interval is required for kind interval.")),
            "pi" => a.GetStationSet(CivilDb.StationTypes.PIPoint),
            "equation" => a.GetStationSet(CivilDb.StationTypes.Equation),
            "all" => a.GetStationSet(CivilDb.StationTypes.All, PositiveParam(p, "interval") ?? 20, (PositiveParam(p, "interval") ?? 20) / 2),
            _ => a.GetStationSet(CivilDb.StationTypes.GeometryPoint),
        };
        var rows = set.Take(limit).Select(st => new
        {
            station = R3(st.RawStation), label = Safe(() => a.GetStationStringWithEquations(st.RawStation)),
            at = new[] { R3(st.Location.X), R3(st.Location.Y) }, type = st.StationType.ToString(),
            geometry = kind == "geometry" ? Safe(() => st.GeometryStationType.ToString()) : null,
        }).ToList();
        return new { alignment = a.Name, kind, total = set.Length, returned = rows.Count, truncated = set.Length > rows.Count, stations = rows };
    });

    public static object AlignmentDistance(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var a = FindAlignment(Civ, tr, NeedStr(p, "alignment"));
        var other = FindAlignment(Civ, tr, NeedStr(p, "other"));
        var stations = NumList(p, "stations", 2000);
        if (stations.Count == 0) throw new ArgumentException("alignment_distance: give stations [..].");
        var rows = new List<object>();
        foreach (double st in stations)
        {
            double d = 0, so = 0;
            try { a.DistanceToAlignment(st, other, ref d, ref so); rows.Add(new { station = R3(st), distance = R3(d), otherStation = R3(so) }); }
            catch (Exception ex) { rows.Add(new { station = R3(st), distance = (double?)null, otherStation = (double?)null, error = ex.Message }); }
        }
        return new { alignment = a.Name, other = other.Name, rows };
    });
}
