namespace C3dMcpBridge;

/// <summary>Corridors and assemblies (read only).</summary>
internal static class CorridorRead
{
    public static object ListCorridors(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = new List<object>();
        foreach (ObjectId id in Civ.CorridorCollection)
        {
            var c = (CivilDb.Corridor)tr.GetObject(id, OpenMode.ForRead);
            var baselines = c.Baselines.Select(b => Baseline(tr, b)).ToList();
            var surfaces = new List<object?>();
            try { foreach (CivilDb.CorridorSurface cs in c.CorridorSurfaces) surfaces.Add(Safe(() => cs.Name)); } catch { /* none */ }
            rows.Add(new
            {
                name = c.Name,
                handle = H(id),
                description = c.Description,
                outOfDate = Safe(() => c.IsOutOfDate),
                baselineCount = baselines.Count,
                baselines,
                corridorSurfaces = surfaces,
            });
        }
        return new { count = rows.Count, corridors = rows };
    });

    private static object Baseline(Transaction tr, CivilDb.Baseline b)
    {
        var regions = new List<object>();
        try
        {
            foreach (CivilDb.BaselineRegion r in b.BaselineRegions)
                regions.Add(new
                {
                    name = Safe(() => r.Name), startStation = Safe(() => R3(r.StartStation)), endStation = Safe(() => R3(r.EndStation)),
                    assembly = NameOf(tr, () => r.AssemblyId),
                });
        }
        catch { /* baseline without readable regions */ }
        return new
        {
            name = Safe(() => b.Name),
            alignment = NameOf(tr, () => b.AlignmentId),
            profile = NameOf(tr, () => b.ProfileId),
            regionCount = regions.Count, regions,
        };
    }

    public static object ListAssemblies(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = new List<object>();
        foreach (ObjectId id in Civ.AssemblyCollection)
        {
            var a = (CivilDb.Assembly)tr.GetObject(id, OpenMode.ForRead);
            var subs = new List<object?>();
            try
            {
                foreach (CivilDb.AssemblyGroup g in a.Groups)
                    foreach (ObjectId sid in g.GetSubassemblyIds())
                        subs.Add(Safe(() => { var s = (CivilDb.Subassembly)tr.GetObject(sid, OpenMode.ForRead); return $"{s.Name} ({s.Side})"; }));
            }
            catch { /* empty assembly */ }
            rows.Add(new { name = a.Name, handle = H(id), type = Safe(() => a.Type.ToString()), location = Safe(() => P3(a.Location)), subassemblies = subs });
        }
        return new { count = rows.Count, assemblies = rows };
    });
}
