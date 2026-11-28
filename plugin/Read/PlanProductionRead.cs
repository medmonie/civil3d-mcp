namespace C3dMcpBridge;

/// <summary>
/// Roadmap #78: what Plan Production and Civil 3D tables already exist in the drawing (creating them is not in the
/// .NET API): view frame groups with their frames (station range, sheet) and match lines; point, parcel, alignment,
/// legend and parcel segment tables.
/// </summary>
internal static class PlanProductionRead
{
    public static object ListViewFrames(JsonElement p) => WithRead<object>((db, tr) =>
    {
        var groups = new List<object>();
        foreach (ObjectId gid in Civ.GetViewFrameGroupIds())
        {
            var g = (CivilDb.ViewFrameGroup)tr.GetObject(gid, OpenMode.ForRead);
            var frames = g.GetViewFrameIds().Cast<ObjectId>().Select(id => (CivilDb.ViewFrame)tr.GetObject(id, OpenMode.ForRead))
                .OrderBy(f => Safe(() => f.StartStation)).Select(f => new
                {
                    name = f.Name, handle = H(f.ObjectId), alignment = NameOf(tr, () => f.AlignmentId),
                    startStation = Safe(() => R3(f.StartStation)), endStation = Safe(() => R3(f.EndStation)),
                    sheet = Safe(() => f.Sheet), sheetSet = Safe(() => f.SheetSet),
                    extents = Safe(() => new[] { P3(f.GeometricExtents.MinPoint), P3(f.GeometricExtents.MaxPoint) }),
                }).ToList();
            var lines = g.GetMatchLineIds().Cast<ObjectId>().Select(id => (CivilDb.MatchLine)tr.GetObject(id, OpenMode.ForRead))
                .Select(m => new { name = m.Name, handle = H(m.ObjectId), number = Safe(() => m.Number), station = Safe(() => R3(m.Station)),
                                   alignment = NameOf(tr, () => m.AlignmentId) }).ToList();
            groups.Add(new { name = g.Name, handle = H(gid), viewFrames = frames, matchLines = lines });
        }
        return new { count = groups.Count, viewFrameGroups = groups,
                     note = groups.Count == 0 ? "No view frame groups. Plan Production (Create View Frames) is a Civil 3D command; the API can only read them." : null };
    });

    public static object ListCivilTables(JsonElement p) => WithRead<object>((db, tr) =>
    {
        object Rows(Func<ObjectIdCollection> ids) => Safe(() => ids().Cast<ObjectId>().Select(id =>
        {
            var o = tr.GetObject(id, OpenMode.ForRead);
            return new
            {
                handle = H(id), type = o.GetType().Name, layer = (o as Entity)?.Layer,
                extents = Safe(() => o is Entity e ? new[] { P3(e.GeometricExtents.MinPoint), P3(e.GeometricExtents.MaxPoint) } : null),
            };
        }).ToList());
        return new
        {
            pointTables = Rows(() => Civ.GetPointTableIds()),
            parcelTables = Rows(() => Civ.GetParcelTableIds()),
            parcelSegmentTables = Rows(() => Civ.GetParcelSegmentTableIds()),
            alignmentTables = Rows(() => Civ.GetAlignmentTableIds()),
            legendTables = Rows(() => Civ.GetLegendTableIds()),
            note = "Civil 3D's own tables (creating them is not in the .NET API; network_table draws an AutoCAD table instead).",
        };
    });
}
