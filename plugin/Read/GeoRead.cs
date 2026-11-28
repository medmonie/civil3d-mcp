using CivilSettings = Autodesk.Civil.Settings;

namespace C3dMcpBridge;

/// <summary>
/// The drawing's coordinate system (roadmap #39), read only: the zone Civil 3D's Drawing Settings carry, the same
/// system as AutoCAD defines it (EPSG code, WKT), the transformation settings, the AutoCAD geographic location, and
/// conversion of points between drawing coordinates and longitude / latitude. Facts only: choosing a system is the
/// client's job.
/// </summary>
internal static class GeoRead
{
    public static object GetCoordinateSystem(JsonElement p) =>
        WithRead((db, tr) => Describe(Civ, db, tr, BoolParam(p, "wkt") ?? false));

    /// <summary>Everything get_coordinate_system reports; also the receipt of coordinate_system_set.</summary>
    internal static object Describe(CivilDoc civ, Database db, Transaction tr, bool wkt)
    {
        var drawing = civ.Settings.DrawingSettings;
        var zone = drawing.UnitZoneSettings;
        string code = Code(zone);
        return new
        {
            code = code.Length > 0 ? code : null,
            assigned = code.Length > 0,
            system = code.Length > 0 ? Definition(code, wkt) : null,
            units = new
            {
                drawingUnits = Safe(() => zone.DrawingUnits.ToString()),
                angularUnits = Safe(() => zone.AngularUnits.ToString()),
                drawingScale = Safe(() => zone.DrawingScale),
                imperialToMetric = Safe(() => zone.ImperialToMetricConversion.ToString()),
                scaleObjectsFromOtherDrawings = Safe(() => zone.ScaleObjectsFromOtherDrawings),
                matchAutoCADVariables = Safe(() => zone.MatchAutoCADVariables),
            },
            transformation = Section(() => Transformation(drawing)),
            geoLocation = Section(() => GeoLocation(db, tr)),
            // AutoCAD's own variable for the same thing; it differs from 'code' when only one side was set.
            cgeocs = Safe(() => CoreApp.GetSystemVariable("CGEOCS") as string),
        };
    }

    /// <summary>One part of the reply; a part Civil 3D cannot give in this drawing reports its error, not the whole read.</summary>
    private static object Section(Func<object> part)
    {
        try { return part(); }
        catch (Exception ex) { return new { error = ErrorText(ex) }; }
    }

    /// <summary>Civil 3D's code for "No Datum, No Projection": a drawing with it has no coordinate system.</summary>
    internal const string NoSystemCode = ".";

    /// <summary>The zone code of the drawing settings; "" when none is assigned.</summary>
    internal static string Code(CivilSettings.SettingsUnitZone zone)
    {
        try
        {
            string code = (zone.CoordinateSystemCode ?? "").Trim();
            return code == NoSystemCode ? "" : code;
        }
        catch { return ""; }
    }

    /// <summary>Set the zone code; "" clears it (Civil 3D refuses an empty code in some releases and wants "." instead).</summary>
    internal static void Assign(CivilSettings.SettingsUnitZone zone, string code)
    {
        if (code.Length > 0) { zone.CoordinateSystemCode = code; return; }
        Exception? first = null;
        foreach (string none in new[] { "", NoSystemCode })
        {
            try
            {
                zone.CoordinateSystemCode = none;
                if (Code(zone).Length == 0) return;
            }
            catch (Exception ex) { first ??= ex; }
        }
        throw first ?? new InvalidOperationException("Civil 3D kept the coordinate system.");
    }

    /// <summary>
    /// Civil 3D's library entry for a code, or null when the code is not in the library. The library's own lookup is
    /// case-sensitive, so a code typed in another case is matched against the list of codes first.
    /// </summary>
    internal static CivilSettings.SettingsCoordinateSystem? Lookup(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == NoSystemCode) return null;
        var found = Exact(code);
        if (found != null) return found;
        try
        {
            string? spelled = (CivilSettings.SettingsUnitZone.GetAllCodes() ?? Array.Empty<string>()).FirstOrDefault(c => SameName(c, code));
            return spelled != null && spelled != code ? Exact(spelled) : null;
        }
        catch { return null; }
    }

    private static CivilSettings.SettingsCoordinateSystem? Exact(string code)
    {
        try
        {
            var cs = CivilSettings.SettingsUnitZone.GetCoordinateSystemByCode(code);
            return cs != null && !string.IsNullOrEmpty(cs.Code) ? cs : null;
        }
        catch { return null; }
    }

    /// <summary>One coordinate system: Civil 3D's library entry, and AutoCAD's definition of the same code.</summary>
    internal static object Definition(string code, bool wkt)
    {
        var cs = Lookup(code);
        GeoCoordinateSystem? geo = null;
        try { geo = GeoCoordinateSystem.Create(code); }
        catch { /* not known to AutoCAD's library under this code */ }
        try
        {
            return new
            {
                code = cs != null ? cs.Code : code,
                known = cs != null || geo != null,
                category = Safe(() => cs?.Category),
                description = Safe(() => cs?.Description ?? geo?.Description),   // AutoCAD's text when Civil 3D has none
                projection = Safe(() => cs?.Projection),
                datum = Safe(() => cs?.Datum),
                unit = Safe(() => cs?.Unit),
                epsg = geo == null ? null : Safe(() => geo!.EPSGcode > 0 ? geo!.EPSGcode : (int?)null),
                type = geo == null ? null : Safe(() => geo!.Type.ToString()),
                projectionCode = geo == null ? null : Safe(() => geo!.ProjectionCode.ToString()),
                wkt = wkt && geo != null ? Safe(() => geo!.WktRepresentation) : null,
            };
        }
        finally { geo?.Dispose(); }
    }

    private static object Transformation(CivilSettings.SettingsDrawing drawing)
    {
        object? applied = Safe(() => drawing.ApplyTransformSettings);
        CivilSettings.SettingsTransformation t;
        try { t = drawing.TransformationSettings; }
        catch (Exception ex)
        {
            // Live on Civil 3D 2027: the getter throws for a drawing whose transformation tab was never filled in.
            return new { applied, available = false, reason = "Civil 3D does not give this drawing's transformation settings: " + ex.Message };
        }
        return new
        {
            applied, available = true,
            applySeaLevelScaleFactor = Safe(() => t.ApplySeaLevelScaleFactor),
            seaLevelScaleElevation = Safe(() => t.SeaLevelScaleElevation),
            spheroidRadius = Safe(() => t.SpheroidRadius),
            gridScaleFactorComputation = Safe(() => t.GridScaleFactorComputation.ToString()),
            gridScaleFactor = Safe(() => t.GridScaleFactor),
            rotationType = Safe(() => t.SpecifyRotationType.ToString()),
            rotationToGridNorthDeg = Safe(() => Deg(t.RotationToGridNorth)),
            rotationToGridAzimuthDeg = Safe(() => Deg(t.RotationToGridAzimuth)),
            localReferencePoint = Safe(() => P2(t.LocalReferencePoint)),
            gridReferencePoint = Safe(() => P2(t.GridReferencePoint)),
            localRotationPoint = Safe(() => P2(t.LocalRotationPoint)),
            gridRotationPoint = Safe(() => P2(t.GridRotationPoint)),
        };
    }

    private static double[] P2(Point2d q) => new[] { R3(q.X), R3(q.Y) };

    /// <summary>Longitude and latitude need more than the 3 decimals used for drawing coordinates (1e-9 deg is 0.1 mm).</summary>
    private static double[] LonLat(Point3d q) => new[] { Math.Round(q.X, 9), Math.Round(q.Y, 9), R3(q.Z) };

    /// <summary>The drawing's AutoCAD geographic location object, or null when it has none.</summary>
    private static GeoLocationData? GeoData(Database db, Transaction tr)
    {
        ObjectId id;
        try { id = db.GeoDataObject; }
        catch { return null; }   // AutoCAD throws (eKeyNotFound) when the drawing has no geographic location
        if (id.IsNull || id.IsErased) return null;
        return tr.GetObject(id, OpenMode.ForRead) as GeoLocationData;
    }

    /// <summary>The ID of a coordinate system given as an ID, WKT or XML (GeoLocationData stores the XML definition).</summary>
    private static string? SystemId(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition)) return null;
        try
        {
            using var cs = GeoCoordinateSystem.Create(definition);
            return cs.ID;
        }
        catch { return definition!.Length > 80 ? definition.Substring(0, 80) + "..." : definition; }
    }

    private static bool HasSystem(GeoLocationData g)
    {
        try { return !string.IsNullOrWhiteSpace(g.CoordinateSystem); }
        catch { return false; }
    }

    private static object GeoLocation(Database db, Transaction tr)
    {
        var g = GeoData(db, tr);
        if (g == null) return new { present = false };
        return new
        {
            present = true,
            // Clearing the zone leaves this object in the drawing with no system: its numbers then mean nothing.
            hasCoordinateSystem = HasSystem(g),
            coordinateSystem = Safe(() => SystemId(g.CoordinateSystem)),
            typeOfCoordinates = Safe(() => g.TypeOfCoordinates.ToString()),
            designPoint = Safe(() => P3(g.DesignPoint)),
            referencePoint = Safe(() => LonLat(g.ReferencePoint)),
            referencePointIs = "[longitude, latitude, elevation] for a grid or geographic system",
            northDirectionDeg = Safe(() => Deg(g.NorthDirection)),
            horizontalUnits = Safe(() => g.HorizontalUnits.ToString()),
            verticalUnits = Safe(() => g.VerticalUnits.ToString()),
            scaleEstimation = Safe(() => g.ScaleEstimationMethod.ToString()),
            scaleFactor = Safe(() => g.ScaleFactor),
            seaLevelCorrection = Safe(() => g.DoSeaLevelCorrection),
            seaLevelElevation = Safe(() => g.SeaLevelElevation),
        };
    }

    // ---------------------------------------------------------------- library

    /// <summary>Codes of Civil 3D's coordinate system library, filtered by text; each returned row is looked up.</summary>
    public static object ListCoordinateSystems(JsonElement p)
    {
        string? contains = StrParam(p, "contains");
        bool inDescription = BoolParam(p, "in_description") ?? false;
        int limit = IntParam(p, "limit", 50, 1, 500);

        string[] codes = CivilSettings.SettingsUnitZone.GetAllCodes() ?? Array.Empty<string>();
        bool Hit(string text) => text.IndexOf(contains!, StringComparison.OrdinalIgnoreCase) >= 0;
        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // the library lists some codes more than once
        foreach (string code in codes)
        {
            if (string.IsNullOrEmpty(code) || code == NoSystemCode || !seen.Add(code)) continue;
            if (contains == null || Hit(code)) { matches.Add(code); continue; }
            // Opt-in: every non-matching code costs one library lookup.
            if (inDescription && Lookup(code) is { } cs)
            {
                string text;
                try { text = (cs.Description ?? "") + " " + (cs.Category ?? "") + " " + (cs.Datum ?? ""); }
                catch { continue; }
                if (Hit(text)) matches.Add(code);
            }
        }
        matches.Sort(StringComparer.OrdinalIgnoreCase);
        var rows = matches.Take(limit).Select(code =>
        {
            var cs = Lookup(code);
            return (object)new
            {
                code,
                category = Safe(() => cs?.Category), description = Safe(() => cs?.Description),
                projection = Safe(() => cs?.Projection), datum = Safe(() => cs?.Datum), unit = Safe(() => cs?.Unit),
            };
        }).ToList();
        return new
        {
            librarySize = seen.Count, matched = matches.Count, returned = rows.Count, truncated = matches.Count > limit,
            contains, inDescription, coordinateSystems = rows,
            hint = matches.Count == 0 && contains != null && !inDescription
                ? "No code contains that text. Pass in_description: true to search descriptions, categories and datums as well (slower)."
                : null,
        };
    }

    // ---------------------------------------------------------------- points

    /// <summary>
    /// Points between drawing coordinates and longitude / latitude, through the drawing's geographic location (so the
    /// result is what AutoCAD's own GEO commands give for this drawing, design point and scale included).
    /// </summary>
    public static object TransformCoordinates(JsonElement p) => WithRead((db, tr) =>
    {
        string to = Choice(p, "to", "lonlat", "lonlat", "drawing");
        const string none = "transform_coordinates: the drawing has no geographic location with a coordinate system. Assign one first " +
                            "(coordinate_system_set, or Civil 3D's Drawing Settings / GEOGRAPHICLOCATION), then read get_coordinate_system.";
        var g = GeoData(db, tr) ?? throw new InvalidOperationException(none);
        // A cleared zone leaves the location object behind without a system; it would still return numbers.
        if (!HasSystem(g)) throw new InvalidOperationException(none);
        var rows = new List<double[]>();
        if (to == "lonlat")
        {
            foreach (var q in PtList(p, "points", "transform_coordinates", 1, 5000)) rows.Add(LonLat(g.TransformToLonLatAlt(q)));
        }
        else
        {
            // Input is [longitude, latitude] or [longitude, latitude, elevation], in degrees.
            int i = 0;
            foreach (var e in NeedArr(p, "points", 5000))
            {
                i++;
                var q = Pt(e, $"transform_coordinates point {i}");
                if (Math.Abs(q.X) > 180 || Math.Abs(q.Y) > 90)
                    throw new ArgumentException($"transform_coordinates point {i}: expected [longitude, latitude] in degrees, got ({q.X}, {q.Y}).");
                rows.Add(P3(g.TransformFromLonLatAlt(q)));
            }
        }
        return new
        {
            from = to == "lonlat" ? "drawing" : "lonlat", to,
            coordinateSystem = Safe(() => SystemId(g.CoordinateSystem)),
            count = rows.Count, points = rows,
            note = "lonlat points are [longitude, latitude, elevation] in decimal degrees; drawing points are [x, y, z] in drawing units.",
        };
    });
}
