namespace C3dMcpBridge;

/// <summary>Corridor modelling: assemblies from stock subassemblies, corridors, corridor surfaces.</summary>
internal static class CorridorOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["assembly_create"] = AssemblyCreate;
        ops["corridor_create"] = CorridorCreate;
        ops["corridor_surface_add"] = CorridorSurfaceAdd;
        ops["corridor_rebuild"] = CorridorRebuild;
    }

    private static void Rebuild(EditContext c, CivilDb.Corridor corridor)
    {
        try { corridor.Rebuild(); }
        catch (Exception ex) { c.Warn($"corridor '{corridor.Name}' did not rebuild: {ex.Message}"); }
        RebuildCorridorSurfaces(c, corridor);
    }

    /// <summary>
    /// A corridor surface's TIN stays empty ("point outside surface") until the surface itself is rebuilt after the
    /// corridor rebuild has been committed, so each one is rebuilt after the commit.
    /// </summary>
    internal static void RebuildCorridorSurfaces(EditContext c, CivilDb.Corridor corridor)
    {
        var ids = new List<(ObjectId Id, string Name)>();
        foreach (CivilDb.CorridorSurface cs in corridor.CorridorSurfaces) ids.Add((cs.SurfaceId, cs.Name));
        if (ids.Count == 0) return;
        var db = c.Db;
        c.AfterCommit.Add(() =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (id, name) in ids)
            {
                try { ((CivilDb.TinSurface)tr.GetObject(id, OpenMode.ForWrite)).Rebuild(); }
                catch (Exception ex) { c.Warn($"corridor surface '{name}' did not rebuild: {ex.Message}"); }
            }
            tr.Commit();
        });
    }

    private static object AssemblyCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Assembly>(c.Tr, c.Civ.AssemblyCollection, name, "An assembly");
        var at = NeedPt(p, "at", "assembly_create");
        var type = EnumParam(p, "type", CivilDb.AssemblyType.UndividedCrownedRoad);

        var assemblyId = c.Civ.AssemblyCollection.Add(name, type, at);
        var assembly = (CivilDb.Assembly)c.Tr.GetObject(assemblyId, OpenMode.ForWrite);
        var added = new List<object>();
        var specs = ArrParam(p, "subassemblies", 50);
        var lastOnSide = new Dictionary<CivilDb.SubassemblySideType, ObjectId>();
        for (int i = 0; i < specs.Count; i++)
        {
            // Stock subassemblies are addressed by their .NET class, e.g. Subassembly.BasicLane.
            string cls = NeedStr(specs[i], "class");
            if (!cls.Contains('.')) cls = "Subassembly." + cls;
            string subName = StrParam(specs[i], "name") ?? $"{name} - {cls.Split('.').Last()} {i + 1}";
            ObjectId subId;
            try { subId = c.Civ.SubassemblyCollection.ImportStockSubassembly(subName, cls, at); }
            catch (Exception ex)
            {
                throw new ArgumentException($"assembly_create: stock subassembly '{cls}' could not be imported ({ex.Message}). Example classes: " +
                    "LaneSuperelevationAOR, BasicLane, BasicShoulder, BasicSideSlopeCutDitch, LinkSlopeToSurface.");
            }
            var sub = (CivilDb.Subassembly)c.Tr.GetObject(subId, OpenMode.ForWrite);
            if (Has(specs[i], "side"))
            {
                var side = EnumParam(specs[i], "side", CivilDb.SubassemblySideType.Right);
                try { sub.Side = side; } catch (Exception ex) { c.Warn($"subassembly {i + 1}: side could not be set: {ex.Message}"); }
            }
            // Chain on each side, as when built by hand: a subassembly is attached to the outermost point of the
            // previous subassembly on the same side (a shoulder at the lane edge), not to the assembly baseline.
            string? attachedTo = null;
            var sideNow = Safe(() => sub.Side);
            if (sideNow is CivilDb.SubassemblySideType s0 && lastOnSide.TryGetValue(s0, out var prevId))
            {
                try
                {
                    var prev = (CivilDb.Subassembly)c.Tr.GetObject(prevId, OpenMode.ForRead);
                    double Out(CivilDb.Point pt) => s0 == CivilDb.SubassemblySideType.Left ? -pt.Offset : pt.Offset;
                    var hook = prev.Points.Cast<CivilDb.Point>().OrderByDescending(Out).ThenByDescending(pt => pt.Elevation).First();   // outer, top edge
                    assembly.AddSubassembly(subId, hook);
                    attachedTo = $"{prev.Name} (point {Safe(() => string.Join("/", hook.Codes))})";
                }
                catch (Exception ex)
                {
                    c.Warn($"subassembly {i + 1}: could not be attached to the previous one on the {s0} side ({ex.Message}); it starts at the baseline.");
                    attachedTo = null;
                }
            }
            if (attachedTo == null) assembly.AddSubassembly(subId);
            if (sideNow is CivilDb.SubassemblySideType s1 && (s1 == CivilDb.SubassemblySideType.Left || s1 == CivilDb.SubassemblySideType.Right)) lastOnSide[s1] = subId;
            added.Add(new { name = subName, @class = cls, side = Safe(() => sub.Side.ToString()), attachedTo = attachedTo ?? "baseline" });
        }
        return new { assembly = name, handle = H(assemblyId), type = type.ToString(), subassemblies = added };
    }

    private static object CorridorCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Corridor>(c.Tr, c.Civ.CorridorCollection, name, "A corridor");
        var alignment = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var profile = FindProfile(c.Tr, alignment, NeedStr(p, "profile"));
        var assembly = FindAssembly(c.Civ, c.Tr, NeedStr(p, "assembly"));

        var id = c.Civ.CorridorCollection.Add(name, StrParam(p, "baseline") ?? "BL-1", alignment.ObjectId, profile.ObjectId,
            StrParam(p, "region") ?? "RG-1", assembly.ObjectId);
        var corridor = (CivilDb.Corridor)c.Tr.GetObject(id, OpenMode.ForWrite);
        double? start = DblParam(p, "start_station"), end = DblParam(p, "end_station");
        if (start != null || end != null)
            foreach (var region in corridor.Baselines.SelectMany(b => b.BaselineRegions))
            {
                if (start != null) region.StartStation = start.Value;
                if (end != null) region.EndStation = end.Value;
            }
        Rebuild(c, corridor);
        return new
        {
            corridor = name, handle = H(id), alignment = alignment.Name, profile = profile.Name, assembly = assembly.Name,
            outOfDate = Safe(() => corridor.IsOutOfDate),
        };
    }

    private static object CorridorSurfaceAdd(EditContext c, JsonElement p)
    {
        var corridor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "corridor"), OpenMode.ForWrite);
        string name = NeedStr(p, "name");
        var codes = Has(p, "link_codes") ? StrList(p, "link_codes", 20) : new List<string> { "Top" };
        var surface = corridor.CorridorSurfaces.Add(name);
        foreach (var code in codes) surface.AddLinkCode(code, true);
        surface.IsBuild = true;   // otherwise Civil 3D keeps the surface empty ("no point or triangle")
        Rebuild(c, corridor);
        return new { corridor = corridor.Name, corridorSurface = name, linkCodes = codes };
    }

    private static object CorridorRebuild(EditContext c, JsonElement p)
    {
        var corridor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "name"), OpenMode.ForWrite);
        corridor.Rebuild();
        RebuildCorridorSurfaces(c, corridor);
        return new { corridor = corridor.Name, rebuilt = true };
    }
}
