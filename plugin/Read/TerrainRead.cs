namespace C3dMcpBridge;

/// <summary>Surfaces, COGO points, feature lines and parcels (read only).</summary>
internal static class TerrainRead
{
    public static object ListSurfaces(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = new List<object>();
        foreach (ObjectId id in Civ.GetSurfaceIds())
        {
            var s = (CivilDb.Surface)tr.GetObject(id, OpenMode.ForRead);
            // One call for all three elevations: computing the general properties walks the whole surface.
            CivilDb.GeneralSurfaceProperties? g = null;
            string? problem = null;
            try { g = s.GetGeneralProperties(); } catch (Exception ex) { problem = ex.Message; }
            rows.Add(new
            {
                name = s.Name,
                handle = H(id),
                type = s.GetType().Name,
                style = Safe(() => s.StyleName),
                layer = s.Layer,
                description = s.Description,
                minElevation = g == null ? (double?)null : R3(g.MinimumElevation),
                maxElevation = g == null ? (double?)null : R3(g.MaximumElevation),
                meanElevation = g == null ? (double?)null : R3(g.MeanElevation),
                elevationsUnavailable = problem,
            });
        }
        return new { count = rows.Count, surfaces = rows };
    });

    public static object GetSurfaceElevation(JsonElement p)
    {
        string name = NeedStr(p, "surface_name");
        double x = NeedDbl(p, "x"), y = NeedDbl(p, "y");
        return WithRead((db, tr) =>
        {
            var s = FindSurface(Civ, tr, name);
            if (s is not CivilDb.TinSurface tin)
                throw new ArgumentException($"Surface '{s.Name}' is a {s.GetType().Name}; only TIN surfaces are supported.");
            double? elevation = null;
            string? note = null;
            try { elevation = R3(tin.FindElevationAtXY(x, y)); }
            catch (Exception ex) { note = "No elevation at this point (outside the surface or in a hole): " + ex.Message; }
            return new { surface = s.Name, x, y, elevation, note };
        });
    }

    public static object GetSurfaceVolume(JsonElement p)
    {
        string name = NeedStr(p, "surface_name");
        return WithRead((db, tr) =>
        {
            var s = FindSurface(Civ, tr, name) as CivilDb.TinVolumeSurface
                    ?? throw new ArgumentException($"Surface '{name}' is not a TIN volume surface (create one with surface_volume_create).");
            var v = s.GetVolumeProperties();
            return new
            {
                surface = s.Name,
                baseSurface = NameOf(tr, () => v.BaseSurface),
                comparisonSurface = NameOf(tr, () => v.ComparisonSurface),
                cut = R3(v.UnadjustedCutVolume), fill = R3(v.UnadjustedFillVolume), net = R3(v.UnadjustedNetVolume),
                cutFactor = v.CutFactor, fillFactor = v.FillFactor,
                adjustedCut = R3(v.AdjustedCutVolume), adjustedFill = R3(v.AdjustedFillVolume), adjustedNet = R3(v.AdjustedNetVolume),
            };
        });
    }

    public static object ListCogoPoints(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("points", Civ.CogoPoints, limit, id =>
        {
            var c = (CivilDb.CogoPoint)tr.GetObject(id, OpenMode.ForRead);
            return new
            {
                number = c.PointNumber,
                name = Safe(() => c.PointName),
                easting = R3(c.Easting),
                northing = R3(c.Northing),
                elevation = R3(c.Elevation),
                description = Safe(() => c.RawDescription),
                handle = H(id),
            };
        }));
    }

    public static object ListFeatureLines(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("featureLines", ModelSpaceIdsByDxf(db, tr, "AECC_FEATURE_LINE"), limit, id =>
        {
            var fl = (CivilDb.FeatureLine)tr.GetObject(id, OpenMode.ForRead);
            return new
            {
                name = Safe(() => fl.Name), handle = H(id), layer = fl.Layer,
                length2d = Safe(() => R3(fl.Length2D)), length3d = Safe(() => R3(fl.Length3D)),
                minElevation = Safe(() => R3(fl.MinElevation)), maxElevation = Safe(() => R3(fl.MaxElevation)),
                points = Safe(() => fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints).Cast<Point3d>().Take(200).Select(P3).ToList()),
            };
        }));
    }

    public static object ListParcels(JsonElement p)
    {
        int limit = IntParam(p, "limit", 500, 1, 5000);
        return WithRead((db, tr) =>
        {
            var parcels = Civ.GetSiteIds().Cast<ObjectId>()
                .Select(id => (CivilDb.Site)tr.GetObject(id, OpenMode.ForRead))
                .SelectMany(site => site.GetParcelIds().Cast<ObjectId>().Select(id => (site, id)));
            var reply = Paged("parcels", parcels, limit, x =>
            {
                var par = (CivilDb.Parcel)tr.GetObject(x.id, OpenMode.ForRead);
                return new
                {
                    site = x.site.Name, name = Safe(() => par.Name), number = Safe(() => par.Number), handle = H(x.id),
                    centroid = Safe(() => P3(par.Centroid)), address = Safe(() => par.Address), extents = Extents(par),
                };
            });
            reply["note"] = "Read only: the Civil 3D .NET API cannot create parcels.";
            return reply;
        });
    }
}
