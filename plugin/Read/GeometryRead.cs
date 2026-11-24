namespace C3dMcpBridge;

/// <summary>
/// Reads of 0.17: alignment geometry (lines, curves, spirals, station equations, design speeds), profile geometry
/// (PVIs and vertical curves), feature line vertices with grades, and material-list areas and volumes.
/// </summary>
internal static class GeometryRead
{
    // ---------------------------------------------------------------- alignment geometry

    public static object GetAlignmentGeometry(JsonElement p)
    {
        string name = NeedStr(p, "alignment");
        return WithRead((db, tr) =>
        {
            var al = FindAlignment(Civ, tr, name);
            var items = new List<object>();
            for (int i = 0; i < al.Entities.Count; i++)
            {
                var ent = al.Entities.GetEntityByOrder(i);
                var subs = new List<object>();
                for (int k = 0; k < ent.SubEntityCount; k++) subs.Add(SubEntity(ent[k]));
                items.Add(new { entity = i + 1, id = ent.EntityId, type = ent.EntityType.ToString(), constraint = Safe(() => ent.Constraint1.ToString()), parts = subs });
            }
            var eqs = new List<object>();
            for (int i = 0; i < al.StationEquations.Count; i++)
            {
                var e = al.StationEquations[i];
                eqs.Add(new { rawStationBack = R3(e.RawStationBack), stationBack = R3(e.StationBack), stationAhead = R3(e.StationAhead), type = e.EquationType.ToString() });
            }
            var speeds = new List<object>();
            for (int i = 0; i < al.DesignSpeeds.Count; i++)
            {
                var d = al.DesignSpeeds[i];
                speeds.Add(new { station = R3(d.Station), speed = R3(d.Value), comment = Safe(() => d.Comment) });
            }
            return new
            {
                alignment = al.Name, type = al.AlignmentType.ToString(), length = R3(al.Length),
                startStation = R3(al.StartingStation), endStation = R3(al.EndingStation),
                entities = items, stationEquations = eqs, designSpeeds = speeds,
                offset = al.AlignmentType == CivilDb.AlignmentType.Offset ? Safe(() => new
                {
                    parent = NameOf(tr, () => al.OffsetAlignmentInfo.ParentAlignmentId),
                    nominalOffset = R3(al.OffsetAlignmentInfo.NominalOffset),
                    side = al.OffsetAlignmentInfo.Side.ToString(),
                }) : null,
                connected = al.AlignmentType == CivilDb.AlignmentType.CurbReturn ? Safe(() => new
                {
                    incoming = NameOf(tr, () => al.ConnectedAlignmentInfo.IncomingParentAlignmentId),
                    outgoing = NameOf(tr, () => al.ConnectedAlignmentInfo.OutgoingParentAlignmentId),
                    curveGroup = al.ConnectedAlignmentInfo.CurveGroupType.ToString(),
                    offsetIn = R3(al.ConnectedAlignmentInfo.OffsetIn), offsetOut = R3(al.ConnectedAlignmentInfo.OffsetOut),
                }) : null,
                note = "Directions are bearings in degrees measured counter-clockwise from east (Civil 3D's internal angle).",
            };
        });
    }

    private static double[] P2(Point2d q) => new[] { R3(q.X), R3(q.Y) };

    private static object SubEntity(CivilDb.AlignmentSubEntity s)
    {
        var common = new Dictionary<string, object?>
        {
            ["kind"] = s.SubEntityType.ToString(),
            ["startStation"] = R3(s.StartStation), ["endStation"] = R3(s.EndStation), ["length"] = R3(s.Length),
            ["start"] = P2(s.StartPoint), ["end"] = P2(s.EndPoint),
        };
        switch (s)
        {
            case CivilDb.AlignmentSubEntityLine ln:
                common["direction"] = Deg(ln.Direction);
                break;
            case CivilDb.AlignmentSubEntityArc a:
                common["radius"] = R3(a.Radius); common["delta"] = Deg(a.Delta); common["clockwise"] = a.Clockwise;
                common["center"] = P2(a.CenterPoint); common["piStation"] = Safe(() => R3(a.PIStation)); common["pi"] = Safe(() => P2(a.PIPoint));
                common["tangent"] = Safe(() => R3(a.ExternalTangent)); common["external"] = Safe(() => R3(a.ExternalSecant));
                common["midOrdinate"] = Safe(() => R3(a.MidOrdinate)); common["chord"] = Safe(() => R3(a.ChordLength));
                break;
            case CivilDb.AlignmentSubEntitySpiral sp:
                common["a"] = Safe(() => R3(sp.A)); common["radiusIn"] = Safe(() => R3(sp.RadiusIn)); common["radiusOut"] = Safe(() => R3(sp.RadiusOut));
                common["spiralType"] = Safe(() => sp.SpiralDefinition.ToString()); common["direction"] = Safe(() => sp.Direction.ToString());
                common["spiStation"] = Safe(() => R3(sp.SPIStation)); common["p"] = Safe(() => R3(sp.P)); common["k"] = Safe(() => R3(sp.K));
                common["delta"] = Safe(() => Deg(sp.Delta));
                break;
        }
        return common;
    }

    // ---------------------------------------------------------------- profile geometry

    public static object GetProfileGeometry(JsonElement p)
    {
        string alName = NeedStr(p, "alignment"), prName = NeedStr(p, "profile");
        return WithRead((db, tr) =>
        {
            var al = FindAlignment(Civ, tr, alName);
            var pr = FindProfile(tr, al, prName);
            var pvis = new List<object>();
            for (int i = 0; i < pr.PVIs.Count; i++)
            {
                var v = pr.PVIs[i];
                pvis.Add(new
                {
                    station = R3(v.Station), elevation = R3(v.Elevation), type = v.PVIType.ToString(),
                    gradeIn = i > 0 ? Safe(() => R3(v.GradeIn * 100)) : null,
                    gradeOut = i + 1 < pr.PVIs.Count ? Safe(() => R3(v.GradeOut * 100)) : null,
                });
            }
            var ents = new List<object>();
            for (int i = 0; i < pr.Entities.Count; i++)
            {
                var e = pr.Entities[i];
                var row = new Dictionary<string, object?>
                {
                    ["type"] = e.EntityType.ToString(),
                    ["startStation"] = R3(e.StartStation), ["endStation"] = R3(e.EndStation),
                    ["startElevation"] = R3(e.StartElevation), ["endElevation"] = R3(e.EndElevation), ["length"] = R3(e.Length),
                };
                switch (e)
                {
                    case CivilDb.ProfileTangent t: row["grade"] = R3(t.Grade * 100); break;
                    case CivilDb.ProfileParabolaSymmetric s:
                        Curve(row, s.CurveType, s.GradeIn, s.GradeOut, s.K, s.HighLowPointStation, s.HighLowPointElevation, s.PVIStation, s.PVIElevation);
                        break;
                    case CivilDb.ProfileParabolaAsymmetric a:
                        Curve(row, a.CurveType, a.GradeIn, a.GradeOut, a.K, a.HighLowPointStation, a.HighLowPointElevation, a.PVIStation, a.PVIElevation);
                        row["lengths"] = new[] { R3(a.AsymmetricLength1), R3(a.AsymmetricLength2) };
                        break;
                    case CivilDb.ProfileCircular c:
                        Curve(row, c.CurveType, c.GradeIn, c.GradeOut, c.K, c.HighLowPointStation, c.HighLowPointElevation, c.PVIStation, c.PVIElevation);
                        row["radius"] = R3(c.Radius);
                        break;
                }
                ents.Add(row);
            }
            return new
            {
                alignment = al.Name, profile = pr.Name, type = pr.ProfileType.ToString(),
                pvis, entities = ents,
                note = "Grades in percent; K = curve length / grade change in percent.",
            };
        });
    }

    private static void Curve(Dictionary<string, object?> row, CivilDb.VerticalCurveType type, double gIn, double gOut, double k,
                              double hlSt, double hlEl, double pviSt, double pviEl)
    {
        row["curve"] = type.ToString();
        row["gradeIn"] = R3(gIn * 100); row["gradeOut"] = R3(gOut * 100); row["gradeChange"] = R3((gOut - gIn) * 100);
        row["k"] = R3(k); row["pviStation"] = R3(pviSt); row["pviElevation"] = R3(pviEl);
        row["highLowStation"] = R3(hlSt); row["highLowElevation"] = R3(hlEl);
    }

    // ---------------------------------------------------------------- feature lines

    public static object GetFeatureLine(JsonElement p) => WithRead((db, tr) =>
    {
        var fl = GeometryOps.FindFeatureLine(db, tr, p, OpenMode.ForRead);
        var pis = fl.GetPoints(Autodesk.Civil.FeatureLinePointType.PIPoint).Cast<Point3d>().ToList();
        var all = fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints).Cast<Point3d>().ToList();
        var piSet = new HashSet<(double, double)>(pis.Select(q => (Math.Round(q.X, 4), Math.Round(q.Y, 4))));
        var rows = new List<object>();
        double run = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var q = all[i];
            if (i > 0) run += Math.Sqrt(Math.Pow(q.X - all[i - 1].X, 2) + Math.Pow(q.Y - all[i - 1].Y, 2));
            rows.Add(new
            {
                index = i, point = P3(q), distance = R3(run),
                kind = piSet.Contains((Math.Round(q.X, 4), Math.Round(q.Y, 4))) ? "PI" : "elevation",
                gradeIn = i > 0 ? Safe(() => R3(fl.GetGradeInAtPoint(q) * 100)) : null,
                gradeOut = i + 1 < all.Count ? Safe(() => R3(fl.GetGradeOutAtPoint(q) * 100)) : null,
                radius = Safe(() => { double r = fl.GetCurveRadius(i); return r > 0 && double.IsFinite(r) ? R3(r) : (double?)null; }),
            });
        }
        return new
        {
            featureLine = Safe(() => fl.Name), handle = H(fl.ObjectId), layer = fl.Layer,
            length2d = Safe(() => R3(fl.Length2D)), length3d = Safe(() => R3(fl.Length3D)),
            minElevation = Safe(() => R3(fl.MinElevation)), maxElevation = Safe(() => R3(fl.MaxElevation)),
            minGrade = Safe(() => R3(fl.MinGrade * 100)), maxGrade = Safe(() => R3(fl.MaxGrade * 100)),
            piCount = pis.Count, points = rows,
            note = "Grades in percent along the line; distance is the plan distance from the first point.",
        };
    });

    // ---------------------------------------------------------------- material volumes

    /// <summary>
    /// Areas at each sample line and average-end-area volumes, per material list. Civil 3D's own quantity results are
    /// returned when it has computed them (Compute Materials); otherwise the surface materials are computed here from the
    /// TIN surfaces sampled across each sample line ('source' says which).
    /// </summary>
    public static object GetMaterialVolumes(JsonElement p)
    {
        string alName = NeedStr(p, "alignment"), groupName = NeedStr(p, "group");
        string? only = StrParam(p, "material_list");
        double step = PositiveParam(p, "step") ?? 0.25;
        return WithRead((db, tr) =>
        {
            var al = FindAlignment(Civ, tr, alName);
            var g = TryFindSampleLineGroup(tr, al, groupName)
                    ?? throw new ArgumentException($"Sample line group '{groupName}' not found on alignment '{al.Name}'. Use list_sample_lines.");
            var lines = AlignmentRead.SampleLines(tr, g).OrderBy(l => l.Station).ToList();
            var lists = new List<object>();
            foreach (CivilDb.QTOMaterialList list in g.MaterialLists)
            {
                if (only != null && !SameName(list.Name, only)) continue;
                CivilDb.QTOSectionalResult[] civil = Array.Empty<CivilDb.QTOSectionalResult>();
                try { civil = g.GetTotalVolumeResultDataForMaterialList(list.Guid).GetResultsAlongSampleLines() ?? civil; } catch { /* not computed */ }
                if (civil.Length > 0)
                {
                    var rows = civil.OrderBy(r => r.Station).Select(r => new
                    {
                        sampleLine = r.SampleLineName, station = R3(r.Station),
                        cutArea = R3(r.AreaResult.CutArea), fillArea = R3(r.AreaResult.FillArea),
                        cutVolume = R3(r.VolumeResult.IncrementalCutVolume), fillVolume = R3(r.VolumeResult.IncrementalFillVolume),
                        cumulativeCut = R3(r.VolumeResult.CumulativeCutVolume), cumulativeFill = R3(r.VolumeResult.CumulativeFillVolume),
                    }).ToList();
                    lists.Add(new
                    {
                        materialList = list.Name, source = "civil3d",
                        totals = new { cut = rows[^1].cumulativeCut, fill = rows[^1].cumulativeFill, net = R3(rows[^1].cumulativeCut - rows[^1].cumulativeFill) },
                        stations = rows,
                    });
                    continue;
                }
                var materials = new List<object>();
                foreach (CivilDb.QTOMaterial m in list) materials.Add(ComputeMaterial(tr, m, lines, al, step));
                lists.Add(new { materialList = list.Name, source = "computed", materials });
            }
            return new
            {
                alignment = al.Name, group = g.Name, materialLists = lists,
                note = "source 'civil3d': Civil 3D's quantity takeoff. source 'computed': each surface sampled across every sample line " +
                       $"every {step} (trapezoids), volumes by average end area between consecutive sample lines. Areas in drawing units², volumes in units³.",
            };
        });
    }

    /// <summary>One surface material: area at each sample line between its surfaces, and average-end-area volumes.</summary>
    private static object ComputeMaterial(Transaction tr, CivilDb.QTOMaterial m, List<CivilDb.SampleLine> lines, CivilDb.Alignment al, double step)
    {
        var items = new List<(string name, CivilDb.MaterialConditionType cond, CivilDb.MaterialItemType type)>();
        foreach (CivilDb.QTOMaterialItem it in m) items.Add((it.Name, it.Condition, it.Type));
        if (items.Any(i => i.type != CivilDb.MaterialItemType.Surface))
            return new { name = m.Name, type = m.QuantityType.ToString(), computed = false,
                         reason = "Has corridor shapes: run Compute Materials in Civil 3D, or read shape areas with get_corridor_section." };
        var tins = items.ToDictionary(i => i.name, i => DataRead.Tin(tr, i.name), StringComparer.OrdinalIgnoreCase);
        bool earthwork = m.QuantityType is CivilDb.MaterialQuantityType.Earthwork or CivilDb.MaterialQuantityType.CutFill;
        string? baseS = items.FirstOrDefault(i => i.cond == CivilDb.MaterialConditionType.Base).name;
        string? compS = items.FirstOrDefault(i => i.cond == CivilDb.MaterialConditionType.Compare).name;
        if (earthwork && (baseS == null || compS == null))
            return new { name = m.Name, type = m.QuantityType.ToString(), computed = false, reason = "An earthwork material needs a base and a compare surface." };

        var rows = new List<(string line, double station, double a1, double a2)>();
        foreach (var sl in lines)
        {
            var v = sl.Vertices.Cast<CivilDb.SampleLineVertex>().Select(x => x.Location).ToList();
            double a1 = 0, a2 = 0;
            for (int k = 0; k + 1 < v.Count; k++)
            {
                double len = Math.Sqrt(Math.Pow(v[k + 1].X - v[k].X, 2) + Math.Pow(v[k + 1].Y - v[k].Y, 2));
                int n = Math.Max(1, (int)Math.Ceiling(len / step));
                double? prev1 = null, prev2 = null;
                for (int i = 0; i <= n; i++)
                {
                    double t = (double)i / n, x = v[k].X + t * (v[k + 1].X - v[k].X), y = v[k].Y + t * (v[k + 1].Y - v[k].Y);
                    double? h1, h2 = null;
                    if (earthwork)
                    {
                        var zb = DataRead.Elev(tins[baseS!], x, y); var zc = DataRead.Elev(tins[compS!], x, y);
                        h1 = zb != null && zc != null ? Math.Max(zb.Value - zc.Value, 0) : null;   // cut: existing above design
                        h2 = zb != null && zc != null ? Math.Max(zc.Value - zb.Value, 0) : null;   // fill
                    }
                    else
                    {
                        // between every 'above' surface and every 'below' surface
                        double lo = double.NegativeInfinity, hi = double.PositiveInfinity; bool ok = true;
                        foreach (var it in items)
                        {
                            var z = DataRead.Elev(tins[it.name], x, y);
                            if (z == null) { ok = false; break; }
                            if (it.cond == CivilDb.MaterialConditionType.Above) lo = Math.Max(lo, z.Value);
                            else if (it.cond == CivilDb.MaterialConditionType.Below) hi = Math.Min(hi, z.Value);
                        }
                        h1 = ok && double.IsFinite(lo) && double.IsFinite(hi) ? Math.Max(hi - lo, 0) : null;
                    }
                    double dx = len / n;
                    if (i > 0 && h1 != null && prev1 != null) a1 += (h1.Value + prev1.Value) / 2 * dx;
                    if (i > 0 && h2 != null && prev2 != null) a2 += (h2.Value + prev2.Value) / 2 * dx;
                    prev1 = h1; prev2 = h2;
                }
            }
            rows.Add((sl.Name, sl.Station, a1, a2));
        }
        var outRows = new List<object>();
        double c1 = 0, c2 = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            double v1 = 0, v2 = 0;
            if (i > 0)
            {
                double d = rows[i].station - rows[i - 1].station;
                v1 = (rows[i].a1 + rows[i - 1].a1) / 2 * d; v2 = (rows[i].a2 + rows[i - 1].a2) / 2 * d;
            }
            c1 += v1; c2 += v2;
            outRows.Add(earthwork
                ? new { sampleLine = rows[i].line, station = R3(rows[i].station), cutArea = R3(rows[i].a1), fillArea = R3(rows[i].a2),
                        cutVolume = R3(v1), fillVolume = R3(v2), cumulativeCut = R3(c1), cumulativeFill = R3(c2) }
                : (object)new { sampleLine = rows[i].line, station = R3(rows[i].station), area = R3(rows[i].a1), volume = R3(v1), cumulative = R3(c1) });
        }
        return new
        {
            name = m.Name, type = m.QuantityType.ToString(), computed = true,
            surfaces = items.Select(i => new { surface = i.name, condition = i.cond.ToString() }).ToList(),
            totals = earthwork ? (object)new { cut = R3(c1), fill = R3(c2), net = R3(c1 - c2) } : new { volume = R3(c1) },
            stations = outRows,
        };
    }
}
