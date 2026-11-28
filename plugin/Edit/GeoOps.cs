namespace C3dMcpBridge;

/// <summary>The drawing's coordinate system (roadmap #39): assign or clear the zone of Civil 3D's Drawing Settings.</summary>
internal static class GeoOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["coordinate_system_set"] = CoordinateSystemSet;
    }

    /// <summary>
    /// Set the zone by its library code, or clear it with "" / "none". The code must exist in this Civil 3D's library
    /// (list_coordinate_systems): a mistyped code is refused instead of leaving the drawing with a zone nobody chose.
    /// Objects are not moved or scaled; only the drawing's statement of which system its coordinates are in changes.
    /// </summary>
    private static object CoordinateSystemSet(EditContext c, JsonElement p)
    {
        const string op = "coordinate_system_set";
        string wanted = (StrParam(p, "code") ?? throw new ArgumentException(
            $"{op}: give 'code' (find it with list_coordinate_systems), or \"\" to clear the coordinate system.")).Trim();
        if (SameName(wanted, "none")) wanted = "";

        var zone = c.Civ.Settings.DrawingSettings.UnitZoneSettings;
        string before = GeoRead.Code(zone);

        if (wanted.Length > 0)
        {
            var cs = GeoRead.Lookup(wanted)
                ?? throw new ArgumentException($"{op}: '{wanted}' is not a coordinate system code of this Civil 3D. " +
                                               "Find the code with list_coordinate_systems (contains: part of the code, or in_description: true).");
            wanted = cs.Code;   // the library's own spelling
            string unit = "";
            try { unit = cs.Unit ?? ""; } catch { /* reported below only when readable */ }
            string drawingUnits = "";
            try { drawingUnits = zone.DrawingUnits.ToString(); } catch { /* same */ }
            bool zoneMetric = unit.IndexOf("met", StringComparison.OrdinalIgnoreCase) >= 0;
            bool zoneFeet = unit.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0 || unit.IndexOf("feet", StringComparison.OrdinalIgnoreCase) >= 0;
            if ((zoneMetric && SameName(drawingUnits, "Feet")) || (zoneFeet && SameName(drawingUnits, "Meters")))
                c.Warn($"the coordinate system's unit is {unit} but the drawing units are {drawingUnits}; nothing was scaled.");
        }

        if (SameName(before, wanted))
            return new { unchanged = true, previous = Null(before), code = Null(wanted), coordinateSystem = GeoRead.Describe(c.Civ, c.Db, c.Tr, false) };

        // Applied after the commit, like saving and plotting. Drawing settings take no part in the transaction: set
        // inside it, a dry run or a failed batch brought the code back but left AutoCAD's geographic location
        // converting with the system that was rolled back (found live on Civil 3D 2027). After the commit there is
        // nothing to roll back; a dry run only validates.
        var db = c.Db;
        c.AfterCommit.Add(() =>
        {
            var z = CivilDoc.GetCivilDocument(db).Settings.DrawingSettings.UnitZoneSettings;
            try { GeoRead.Assign(z, wanted); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(wanted.Length == 0
                    ? $"{op}: Civil 3D did not clear the coordinate system ({ex.Message})."
                    : $"{op}: Civil 3D did not accept '{wanted}' ({ex.Message}).");
            }
            string after = GeoRead.Code(z);
            if (!SameName(after, wanted))
                throw new InvalidOperationException($"{op}: Civil 3D kept '{after}' instead of '{wanted}'.");
        });
        return new
        {
            queued = true, previous = Null(before), code = Null(wanted),
            system = wanted.Length > 0 ? GeoRead.Definition(wanted, false) : null,
            note = "Assigned after the batch is committed (afterCommitSucceeded says whether it took); read get_coordinate_system " +
                   "for the result. Only the assignment changes: no object is moved, rotated or scaled.",
        };
    }

    private static string? Null(string s) => s.Length > 0 ? s : null;
}
