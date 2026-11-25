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
                note = "bearing = azimuth in degrees clockwise from north (grid north of the drawing); angleFromEast = degrees "
                     + "counter-clockwise from east (+X), as AutoCAD angles. Both are computed from the coordinates. Curves give "
                     + "the bearing of travel at their start and end; a spiral's turn is Left or Right.",
            };
        });
    }

    private static double[] P2(Point2d q) => new[] { R3(q.X), R3(q.Y) };

    /// <summary>Direction of a plan vector as bearing (clockwise from north), angle from east and a quadrant bearing.</summary>
    internal static void Dir(Dictionary<string, object?> row, string prefix, Vector2d v)
    {
        if (v.Length < 1e-12) return;
        double east = Math.Atan2(v.Y, v.X) * 180 / Math.PI;
        if (east < 0) east += 360;
        double az = (450 - east) % 360;
        string Key(string k) => prefix.Length == 0 ? k : prefix + char.ToUpperInvariant(k[0]) + k.Substring(1);
        row[Key("bearing")] = Math.Round(az, 6);
        row[Key("angleFromEast")] = Math.Round(east, 6);
        row[Key("quadrantBearing")] = Quadrant(az);
    }

    internal static string Quadrant(double az)
    {
        string ns = az <= 90 || az >= 270 ? "N" : "S", ew = az < 180 ? "E" : "W";
        double a = az <= 90 ? az : az < 180 ? 180 - az : az < 270 ? az - 180 : 360 - az;
        if (a < 1e-9) return ns == "N" ? "N" : "S";
        if (Math.Abs(a - 90) < 1e-9) return ew;
        int d = (int)a; double mf = (a - d) * 60; int m = (int)mf; double sec = Math.Round((mf - m) * 60, 1);
        if (sec >= 60) { sec = 0; m++; } if (m >= 60) { m = 0; d++; }
        return $"{ns} {d}\u00b0{m:00}'{sec:00.0}\" {ew}";
    }

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
                Dir(common, "", s.EndPoint - s.StartPoint);
                break;
            case CivilDb.AlignmentSubEntityArc a:
                common["radius"] = R3(a.Radius); common["delta"] = Deg(a.Delta); common["clockwise"] = a.Clockwise;
                common["center"] = P2(a.CenterPoint);
                {
                    // Travel direction = radius vector turned 90 degrees: left (+90) for counter-clockwise arcs, right for clockwise.
                    Vector2d rs = a.StartPoint - a.CenterPoint, re = a.EndPoint - a.CenterPoint;
                    Vector2d ts = a.Clockwise ? new Vector2d(rs.Y, -rs.X) : new Vector2d(-rs.Y, rs.X);
                    Vector2d te = a.Clockwise ? new Vector2d(re.Y, -re.X) : new Vector2d(-re.Y, re.X);
                    Dir(common, "start", ts); Dir(common, "end", te);
                }
                common["piStation"] = Safe(() => R3(a.PIStation)); common["pi"] = Safe(() => P2(a.PIPoint));
                common["tangent"] = Safe(() => R3(a.ExternalTangent)); common["external"] = Safe(() => R3(a.ExternalSecant));
                common["midOrdinate"] = Safe(() => R3(a.MidOrdinate)); common["chord"] = Safe(() => R3(a.ChordLength));
                break;
            case CivilDb.AlignmentSubEntitySpiral sp:
                common["a"] = Safe(() => R3(sp.A)); common["radiusIn"] = Safe(() => R3(sp.RadiusIn)); common["radiusOut"] = Safe(() => R3(sp.RadiusOut));
                common["spiralType"] = Safe(() => sp.SpiralDefinition.ToString()); common["turn"] = Safe(() => sp.Direction.ToString());
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
        double? from = DblParam(p, "from_station"), to = DblParam(p, "to_station");
        if (from is double f0 && to is double t0 && t0 <= f0)
            throw new ArgumentException("get_material_volumes: 'to_station' must be greater than 'from_station'.");
        bool InRange(double st) => (from == null || st >= from.Value - 1e-6) && (to == null || st <= to.Value + 1e-6);
        return WithRead((db, tr) =>
        {
            var al = FindAlignment(Civ, tr, alName);
            var g = TryFindSampleLineGroup(tr, al, groupName)
                    ?? throw new ArgumentException($"Sample line group '{groupName}' not found on alignment '{al.Name}'. Use list_sample_lines.");
            // With a station range only the sample lines inside it count: volumes between consecutive lines in the range.
            var lines = AlignmentRead.SampleLines(tr, g).Where(l => InRange(l.Station)).OrderBy(l => l.Station).ToList();
            if (lines.Count < 2 && (from != null || to != null))
                throw new ArgumentException($"get_material_volumes: fewer than two sample lines of '{g.Name}' lie between stations " +
                                            $"{from?.ToString() ?? "start"} and {to?.ToString() ?? "end"}; widen the range or add sample lines.");
            var lists = new List<object>();
            foreach (CivilDb.QTOMaterialList list in g.MaterialLists)
            {
                if (only != null && !SameName(list.Name, only)) continue;
                CivilDb.QTOSectionalResult[] civil = Array.Empty<CivilDb.QTOSectionalResult>();
                try { civil = g.GetTotalVolumeResultDataForMaterialList(list.Guid).GetResultsAlongSampleLines() ?? civil; } catch { /* not computed */ }
                civil = civil.Where(r => InRange(r.Station)).ToArray();
                if (civil.Length > 0)
                {
                    var rows = civil.OrderBy(r => r.Station).Select(r => new
                    {
                        sampleLine = r.SampleLineName, station = R3(r.Station),
                        cutArea = R3(r.AreaResult.CutArea), fillArea = R3(r.AreaResult.FillArea),
                        cutVolume = R3(r.VolumeResult.IncrementalCutVolume), fillVolume = R3(r.VolumeResult.IncrementalFillVolume),
                        cumulativeCut = R3(r.VolumeResult.CumulativeCutVolume), cumulativeFill = R3(r.VolumeResult.CumulativeFillVolume),
                    }).ToList();
                    // Totals over the rows shown: the first row's incremental volume belongs to the segment before it.
                    double cut = rows.Skip(1).Sum(r => r.cutVolume), fill = rows.Skip(1).Sum(r => r.fillVolume);
                    lists.Add(new
                    {
                        materialList = list.Name, source = "civil3d",
                        totals = new { cut = R3(cut), fill = R3(fill), net = R3(cut - fill) },
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
                alignment = al.Name, group = g.Name,
                range = from != null || to != null ? new { fromStation = lines[0].Station, toStation = lines[^1].Station, sampleLines = lines.Count } : null,
                materialLists = lists,
                note = "source 'civil3d': Civil 3D's quantity takeoff. source 'computed': each surface sampled across every sample line " +
                       $"every {step} (trapezoids), volumes by average end area between consecutive sample lines. Areas in drawing units², volumes in units³.",
            };
        });
    }

    /// <summary>
    /// Elevation sampler for a material item. A corridor surface item is named "&lt;corridor&gt; &lt;surface&gt;" in the material
    /// list: it is found through the corridor and sampled on its TIN. Anything else is a TIN surface by name.
    /// </summary>
    private static Func<double, double, double?> MaterialSurface(Transaction tr, string itemName)
    {
        foreach (ObjectId cid in Civ.CorridorCollection)
        {
            var cor = (CivilDb.Corridor)tr.GetObject(cid, OpenMode.ForRead);
            foreach (CivilDb.CorridorSurface cs in cor.CorridorSurfaces)
                if (SameName(itemName, cs.Name) || SameName(itemName, cor.Name + " " + cs.Name))
                {
                    var ctin = (CivilDb.TinSurface)tr.GetObject(cs.SurfaceId, OpenMode.ForRead);
                    return (x, y) => DataRead.Elev(ctin, x, y);
                }
        }
        var tin = DataRead.Tin(tr, itemName);   // the error names the surface
        return (x, y) => DataRead.Elev(tin, x, y);
    }

    /// <summary>One surface material: area at each sample line between its surfaces, and average-end-area volumes.</summary>
    private static object ComputeMaterial(Transaction tr, CivilDb.QTOMaterial m, List<CivilDb.SampleLine> lines, CivilDb.Alignment al, double step)
    {
        var items = new List<(string name, CivilDb.MaterialConditionType cond, CivilDb.MaterialItemType type)>();
        foreach (CivilDb.QTOMaterialItem it in m) items.Add((it.Name, it.Condition, it.Type));
        if (items.Any(i => i.type != CivilDb.MaterialItemType.Surface))
            return new { name = m.Name, type = m.QuantityType.ToString(), computed = false,
                         reason = "Has corridor shapes: run Compute Materials in Civil 3D, or read shape areas with get_corridor_section." };
        var tins = items.Select(i => i.name).Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(n => n, n => MaterialSurface(tr, n), StringComparer.OrdinalIgnoreCase);
        bool earthwork = m.QuantityType is CivilDb.MaterialQuantityType.Earthwork or CivilDb.MaterialQuantityType.CutFill;
        string? baseS = items.FirstOrDefault(i => i.cond == CivilDb.MaterialConditionType.Base).name;
        string? compS = items.FirstOrDefault(i => i.cond == CivilDb.MaterialConditionType.Compare).name;
        if (earthwork && (baseS == null || compS == null))
            return new { name = m.Name, type = m.QuantityType.ToString(), computed = false, reason = "An earthwork material needs a base and a compare surface." };

        var hits = tins.Keys.ToDictionary(k => k, _ => 0, StringComparer.OrdinalIgnoreCase);
        var sampled = tins.ToDictionary(kv => kv.Key, kv => (Func<double, double, double?>)((x, y) =>
        {
            var z = kv.Value(x, y);
            if (z != null) hits[kv.Key]++;
            return z;
        }), StringComparer.OrdinalIgnoreCase);
        tins = sampled;
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
                        var zb = tins[baseS!](x, y); var zc = tins[compS!](x, y);
                        h1 = zb != null && zc != null ? Math.Max(zb.Value - zc.Value, 0) : null;   // cut: existing above design
                        h2 = zb != null && zc != null ? Math.Max(zc.Value - zb.Value, 0) : null;   // fill
                    }
                    else
                    {
                        // between every 'above' surface and every 'below' surface
                        double lo = double.NegativeInfinity, hi = double.PositiveInfinity; bool ok = true;
                        foreach (var it in items)
                        {
                            var z = tins[it.name](x, y);
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
        var empty = hits.Where(h => h.Value == 0).Select(h => h.Key).ToList();
        return new
        {
            name = m.Name, type = m.QuantityType.ToString(), computed = true,
            warning = empty.Count == 0 ? null
                : $"No elevation found on {string.Join(", ", empty)} along any sample line: the surface is empty or out of date " +
                  "(a corridor surface needs surface_rebuild after the corridor changes), so these volumes are 0.",
            surfaces = items.Select(i => new { surface = i.name, condition = i.cond.ToString() }).ToList(),
            totals = earthwork ? (object)new { cut = R3(c1), fill = R3(c2), net = R3(c1 - c2) } : new { volume = R3(c1) },
            stations = outRows,
        };
    }
}
