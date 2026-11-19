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
            assembly.AddSubassembly(subId);
            added.Add(new { name = subName, @class = cls, side = Safe(() => sub.Side.ToString()) });
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
        Rebuild(c, corridor);
        return new { corridor = corridor.Name, corridorSurface = name, linkCodes = codes };
    }

    private static object CorridorRebuild(EditContext c, JsonElement p)
    {
        var corridor = FindCorridor(c.Civ, c.Tr, NeedStr(p, "name"), OpenMode.ForWrite);
        corridor.Rebuild();
        return new { corridor = corridor.Name, rebuilt = true };
    }
}
