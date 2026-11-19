namespace C3dMcpBridge;

/// <summary>
/// Finds the items placed within a tolerance of a point without comparing against every item: points are bucketed
/// in a plan grid whose cells are one tolerance wide, so a query looks at 9 cells. Used to connect pipe ends to
/// structures and fittings when thousands of parts are added in one batch.
/// </summary>
internal sealed class PointIndex<T>(double tolerance)
{
    private readonly double _cell = Math.Max(tolerance, 1e-6);
    private readonly Dictionary<(long, long), List<(int order, Point3d at, T item)>> _cells = new();
    private int _count;

    private (long, long) Cell(Point3d p) => ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell));

    public void Add(Point3d at, T item)
    {
        var key = Cell(at);
        if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new();
        list.Add((_count++, at, item));
    }

    /// <summary>Items within the tolerance of 'at', in the order they were added. planOnly ignores elevation.</summary>
    public List<T> Near(Point3d at, bool planOnly)
    {
        var (cx, cy) = Cell(at);
        var hits = new List<(int order, T item)>();
        for (long x = cx - 1; x <= cx + 1; x++)
            for (long y = cy - 1; y <= cy + 1; y++)
            {
                if (!_cells.TryGetValue((x, y), out var list)) continue;
                foreach (var e in list)
                {
                    double d = planOnly ? Math.Sqrt(Math.Pow(e.at.X - at.X, 2) + Math.Pow(e.at.Y - at.Y, 2)) : e.at.DistanceTo(at);
                    if (d <= tolerance) hits.Add((e.order, e.item));
                }
            }
        return hits.OrderBy(h => h.order).Select(h => h.item).ToList();
    }
}
