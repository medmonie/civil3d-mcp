namespace C3dMcpBridge;

/// <summary>
/// Civil 3D reads of 0.15: point groups, point file formats and description keys, surface analysis ranges,
/// superelevation, intersections, hydraulic grade lines and a pipe interference / clearance check.
/// </summary>
internal static class CivilRead
{
    public static object ListPointGroups(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = Civ.PointGroups.Cast<ObjectId>().Select(id => (CivilDb.PointGroup)tr.GetObject(id, OpenMode.ForRead)).Select(g => new
        {
            name = g.Name, handle = H(g.ObjectId), description = Safe(() => g.Description), points = Safe(() => g.PointsCount),
            locked = Safe(() => g.IsLocked), query = Safe(() => g.GetQuery().QueryString),
            pointStyle = g.IsPointStyleOverridden ? Styles.NameOf(tr, g.PointStyleId) : null,
            labelStyle = g.IsPointLabelStyleOverridden ? Styles.NameOf(tr, g.PointLabelStyleId) : null,
        }).ToList();
        return new { count = rows.Count, pointGroups = rows };
    });

    public static object ListPointFileFormats(JsonElement p) => WithRead((db, tr) =>
    {
        using var formats = CivilDb.PointFileFormatCollection.GetPointFileFormats(db);
        var rows = new List<object>();
        foreach (CivilDb.PointFileFormat f in formats)
            rows.Add(new { name = f.Name, extension = Safe(() => f.FileExtension), delimiter = Safe(() => f.Delimiter),
                           type = Safe(() => f.FileFormatType.ToString()) });
        return new { count = rows.Count, formats = rows };
    });

    public static object ListDescriptionKeys(JsonElement p) => WithRead((db, tr) =>
    {
        var sets = CivilDb.PointDescriptionKeySetCollection.GetPointDescriptionKeySets(db);
        var rows = new List<object>();
        for (int i = 0; i < sets.Count; i++)
        {
            ObjectId sid;
            try { sid = sets[i]; } catch (Exception ex) { rows.Add(new { index = i, error = ErrorText(ex) }); continue; }
            if (sid.IsNull || sid.IsErased) continue;
            CivilDb.PointDescriptionKeySet set;
            try { set = (CivilDb.PointDescriptionKeySet)tr.GetObject(sid, OpenMode.ForRead); }
            catch (Exception ex) { rows.Add(new { index = i, error = ErrorText(ex) }); continue; }
            var keyIds = new List<ObjectId>();
            try { keyIds = set.GetPointDescriptionKeyIds().Cast<ObjectId>().ToList(); }
            catch (Exception ex) { rows.Add(new { keySet = Styles.NameOf(set), error = ErrorText(ex) }); continue; }
            var keys = keyIds.Where(k => !k.IsNull && !k.IsErased)
                .Select(k => (CivilDb.PointDescriptionKey)tr.GetObject(k, OpenMode.ForRead))
                .Select(k => new
                {
                    code = Safe(() => k.Code), format = Safe(() => k.Format),
                    pointStyle = Safe(() => k.ApplyStyleId && !k.StyleId.IsNull ? Styles.NameOf(tr, k.StyleId) : null),
                    labelStyle = Safe(() => k.ApplyLabelStyleId && !k.LabelStyleId.IsNull ? Styles.NameOf(tr, k.LabelStyleId) : null),
                    layer = Safe(() => k.ApplyLayerId && !k.LayerId.IsNull ? ((LayerTableRecord)tr.GetObject(k.LayerId, OpenMode.ForRead)).Name : null),
                }).ToList();
            rows.Add(new { keySet = Styles.NameOf(set), keys });
        }
        return new { count = rows.Count, keySets = rows };
    });

    public static object GetSurfaceAnalysis(JsonElement p) => WithRead((db, tr) =>
    {
        var surf = FindSurface(Civ, tr, NeedStr(p, "surface"));
        string Col(Autodesk.AutoCAD.Colors.Color col) => col.IsByAci ? col.ColorIndex.ToString() : col.ColorNameForDisplay;
        var props = surf.GetGeneralProperties();
        return new
        {
            surface = surf.Name, minElevation = R3(props.MinimumElevation), maxElevation = R3(props.MaximumElevation),
            meanElevation = R3(props.MeanElevation),
            elevations = Safe(() => surf.Analysis.GetElevationData().Select(d => new { min = R3(d.MinimumElevation), max = R3(d.MaximumElevation), color = Col(d.Scheme) }).ToList()),
            slopesPercent = Safe(() => surf.Analysis.GetSlopeData().Select(d => new { min = R3(d.MinimumSlope * 100), max = R3(d.MaximumSlope * 100), color = Col(d.Scheme) }).ToList()),
            slopeArrowsPercent = Safe(() => surf.Analysis.GetSlopeArrowData().Select(d => new { min = R3(d.MinimumSlope * 100), max = R3(d.MaximumSlope * 100), color = Col(d.Scheme) }).ToList()),
            watersheds = Safe(() => surf.Analysis.GetWatershedData().Cast<CivilDb.SurfaceAnalysisWatershedData>()
                .Select(w => new { id = w.AreaID, type = w.Type.ToString(), description = w.Description, drainsInto = w.DrainsInto }).ToList()),
        };
    });

    public static object GetSuperelevation(JsonElement p) => WithRead((db, tr) =>
    {
        var al = FindAlignment(Civ, tr, NeedStr(p, "alignment"));
        var segments = Enum.GetValues(typeof(Autodesk.Civil.SuperelevationCrossSegmentType)).Cast<Autodesk.Civil.SuperelevationCrossSegmentType>().ToList();
        var curves = Safe(() => al.SuperelevationCurves.Cast<CivilDb.SuperelevationCurve>().Select(sc => new
        {
            name = sc.Name, startStation = R3(sc.StartStation), endStation = R3(sc.EndStation),
        }).ToList());
        var stations = Safe(() => al.SuperelevationCriticalStations.Cast<CivilDb.SuperelevationCriticalStation>().Select(s => new
        {
            station = R3(s.Station), type = s.StationType.ToString(), region = Safe(() => s.TransitionRegionType.ToString()),
            curve = Safe(() => s.SuperelevationCurveName),
            slopesPercent = segments.ToDictionary(g => g.ToString(), g => Safe(() => R3(s.GetSlope(g) * 100))),
        }).ToList());
        return new
        {
            alignment = al.Name, type = Safe(() => al.SuperelevationType.ToString()), curves, criticalStations = stations,
            note = "Superelevation is calculated with Civil 3D's Calculate/Edit Superelevation command (not exposed to .NET); the slopes of existing critical stations can be read and changed.",
        };
    });

    public static object ListIntersections(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = Civ.GetIntersectionIds().Cast<ObjectId>().Select(id => (CivilDb.Intersection)tr.GetObject(id, OpenMode.ForRead)).Select(x => new
        {
            name = x.Name, handle = H(x.ObjectId), location = Safe(() => P3(x.Location)),
            corridor = NameOf(tr, () => x.CorridorId), roads = Safe(() => x.IntersectionRoads.Count),
        }).ToList();
        return new { count = rows.Count, intersections = rows,
                     note = "Intersections are created with Civil 3D's Create Intersection command (not exposed to .NET)." };
    });

    // ---------------------------------------------------------------- hydraulics

    /// <summary>HGL / EGL of every pipe and structure, with flags: HGL above the rim (flooding) or above a pipe's crown (surcharge).</summary>
    public static object NetworkHgl(JsonElement p) => WithRead((db, tr) =>
    {
        var net = FindPipeNetwork(Civ, tr, NeedStr(p, "network"));
        double freeboard = DblParam(p, "freeboard") ?? 0.0;
        var flags = new List<object>();
        var pipes = net.GetPipeIds().Cast<ObjectId>().Select(id => (CivilDb.Pipe)tr.GetObject(id, OpenMode.ForRead)).Select(x =>
        {
            double halfHeight = GravityRead.InnerHalfHeight(x);
            bool reversed = x.FlowDirection == CivilDb.FlowDirectionType.EndToStart;
            double crownUp = (reversed ? x.EndPoint.Z : x.StartPoint.Z) + halfHeight;
            double crownDown = (reversed ? x.StartPoint.Z : x.EndPoint.Z) + halfHeight;
            double hu = x.HydraulicGradeLineUp, hd = x.HydraulicGradeLineDown;
            if (hu > crownUp + 1e-6 || hd > crownDown + 1e-6)
                flags.Add(new { part = x.Name, issue = "surcharged", hglUp = R3(hu), hglDown = R3(hd), crownUp = R3(crownUp), crownDown = R3(crownDown) });
            return new { name = x.Name, hglUp = R3(hu), hglDown = R3(hd), eglUp = R3(x.EnergyGradeLineUp), eglDown = R3(x.EnergyGradeLineDown), flow = R3(x.FlowRate) };
        }).ToList();
        var structures = net.GetStructureIds().Cast<ObjectId>().Select(id => (CivilDb.Structure)tr.GetObject(id, OpenMode.ForRead)).Select(s =>
        {
            if (s.HydraulicGradeLine > s.RimElevation - freeboard + 1e-6)
                flags.Add(new { part = s.Name, issue = freeboard > 0 ? "less than the freeboard below the rim" : "above the rim (flooding)",
                                hgl = R3(s.HydraulicGradeLine), rim = R3(s.RimElevation) });
            return new { name = s.Name, hgl = R3(s.HydraulicGradeLine), egl = R3(s.EnergyGradeLine), rim = R3(s.RimElevation) };
        }).ToList();
        return new { network = net.Name, pipes, structures, flags,
                     note = "Values are what the pipes and structures hold: set them with hgl_set (e.g. from SewerGEMS) or Civil 3D's analysis." };
    });

    private sealed record Seg(string Network, string Name, string Kind, Point3d A, Point3d B, double Radius, ObjectId StartNode, ObjectId EndNode);

    private static CivilDb.Entity ResolveNetwork(Transaction tr, string name) =>
        (CivilDb.Entity?)TryFindPressureNetwork(Civ, tr, name) ?? FindPipeNetwork(Civ, tr, name);

    private static IEnumerable<Seg> Segments(Transaction tr, CivilDb.Entity network, List<object> excluded)
    {
        if (network is CivilDb.PressurePipeNetwork pnet)
        {
            foreach (ObjectId id in pnet.GetPipeIds())
            {
                var pp = (CivilDb.PressurePipe)tr.GetObject(id, OpenMode.ForRead);
                if (pp.IsCurve || pp.HasVerticalSegments)
                {
                    excluded.Add(new { network = pnet.Name, pipe = pp.Name, handle = H(id), reason = "Curved or vertically segmented pressure pipe" });
                    continue;
                }
                yield return new Seg(pnet.Name, pp.Name, "pressure pipe", pp.StartPoint, pp.EndPoint, pp.OuterDiameter / 2, ObjectId.Null, ObjectId.Null);
            }
            yield break;
        }
        var net = (CivilDb.Network)network;
        foreach (ObjectId id in net.GetPipeIds())
        {
            var pipe = (CivilDb.Pipe)tr.GetObject(id, OpenMode.ForRead);
            if (pipe.SubEntityType != CivilDb.PipeSubEntityType.Straight || pipe.CrossSectionalShape != CivilDb.SweptShapeType.Circular)
            {
                excluded.Add(new { network = net.Name, pipe = pipe.Name, handle = H(id), reason = "Clearance requires a straight circular pipe" });
                continue;
            }
            yield return new Seg(net.Name, pipe.Name, "pipe", pipe.StartPoint, pipe.EndPoint, pipe.OuterDiameterOrWidth / 2, pipe.StartStructureId, pipe.EndStructureId);
        }
    }

    /// <summary>Closest points of two 3D segments (standard clamped segment-segment distance).</summary>
    private static (double dist, Point3d onA, Point3d onB) Closest(Point3d p1, Point3d q1, Point3d p2, Point3d q2)
    {
        Vector3d d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        double a = d1.DotProduct(d1), e = d2.DotProduct(d2), f = d2.DotProduct(r);
        double s, t;
        if (a < 1e-12 && e < 1e-12) { s = t = 0; }
        else if (a < 1e-12) { s = 0; t = Math.Clamp(f / e, 0, 1); }
        else
        {
            double cc = d1.DotProduct(r);
            if (e < 1e-12) { t = 0; s = Math.Clamp(-cc / a, 0, 1); }
            else
            {
                double b = d1.DotProduct(d2), denom = a * e - b * b;
                s = denom > 1e-12 ? Math.Clamp((b * f - cc * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-cc / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - cc) / a, 0, 1); }
            }
        }
        var ca = p1 + d1 * s;
        var cb = p2 + d2 * t;
        return (ca.DistanceTo(cb), ca, cb);
    }

    /// <summary>
    /// Pipes of two networks (gravity or pressure) that come closer than 'clearance' outside to outside, with the
    /// gap, where it happens and which pipe is above. Civil 3D's interference check object cannot be created through
    /// .NET; this check works on the pipe geometry directly. With one network, its own pipes are checked against
    /// each other, except pipes that share a structure.
    /// </summary>
    public static object NetworkInterference(JsonElement p) => WithRead((db, tr) =>
    {
        string n1 = NeedStr(p, "network1");
        string? n2 = StrParam(p, "network2");
        double clearance = DblParam(p, "clearance") ?? 0.3;
        int limit = IntParam(p, "limit", 500, 1, 5000);
        var first = ResolveNetwork(tr, n1);
        var second = n2 == null ? first : ResolveNetwork(tr, n2);
        bool sameNetwork = first.ObjectId == second.ObjectId;
        var excluded = new List<object>();
        var a = Segments(tr, first, excluded).ToList();
        int excludedA = excluded.Count;
        var b = sameNetwork ? a : Segments(tr, second, excluded).ToList();
        var hits = new List<(double gap, object row)>();
        long tested = 0;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            double xMinX = Math.Min(x.A.X, x.B.X) - x.Radius - clearance, xMaxX = Math.Max(x.A.X, x.B.X) + x.Radius + clearance;
            double xMinY = Math.Min(x.A.Y, x.B.Y) - x.Radius - clearance, xMaxY = Math.Max(x.A.Y, x.B.Y) + x.Radius + clearance;
            for (int j = sameNetwork ? i + 1 : 0; j < b.Count; j++)
            {
                var y = b[j];
                if (Math.Max(y.A.X, y.B.X) + y.Radius < xMinX || Math.Min(y.A.X, y.B.X) - y.Radius > xMaxX ||
                    Math.Max(y.A.Y, y.B.Y) + y.Radius < xMinY || Math.Min(y.A.Y, y.B.Y) - y.Radius > xMaxY) continue;
                bool SharesNode(ObjectId node) => !node.IsNull && (node == y.StartNode || node == y.EndNode);
                if (sameNetwork && (SharesNode(x.StartNode) || SharesNode(x.EndNode))) continue;
                tested++;
                var (dist, pa, pb) = Closest(x.A, x.B, y.A, y.B);
                double gap = dist - x.Radius - y.Radius;
                if (gap >= clearance) continue;
                hits.Add((gap, new
                {
                    a = new { network = x.Network, pipe = x.Name, kind = x.Kind }, b = new { network = y.Network, pipe = y.Name, kind = y.Kind },
                    gap = R3(gap), clash = gap < 0, at = P3(new Point3d((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, (pa.Z + pb.Z) / 2)),
                    above = pa.Z >= pb.Z ? x.Name : y.Name, verticalSeparation = R3(Math.Abs(pa.Z - pb.Z)),
                }));
            }
        }
        var rows = hits.OrderBy(h => h.gap).Take(limit).Select(h => h.row).ToList();
        return new
        {
            network1 = n1, network2 = n2 ?? n1, clearance, pipesA = a.Count + excludedA,
            pipesB = b.Count + (sameNetwork ? excludedA : excluded.Count - excludedA), pairsTested = tested,
            complete = excluded.Count == 0, excludedPipes = excluded,
            found = hits.Count, clashes = hits.Count(h => h.gap < 0), truncated = hits.Count > limit, conflicts = rows,
            note = "Gap is the minimum centreline distance minus both outer radii. Counts cover eligible straight circular pipes only. See excludedPipes for omitted geometry. Structures are outside the scope.",
        };
    });
}
