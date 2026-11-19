namespace C3dMcpBridge;

/// <summary>Gravity pipe networks: networks, pipes, structures and the parts lists they draw from (read only).</summary>
internal static class GravityRead
{
    private static IEnumerable<CivilDb.Network> Networks(Transaction tr, string? only = null) =>
        Civ.GetPipeNetworkIds().Cast<ObjectId>().Select(id => (CivilDb.Network)tr.GetObject(id, OpenMode.ForRead))
           .Where(n => only == null || SameName(n.Name, only));

    /// <summary>(network, part id) for every pipe or structure of the wanted networks. No part is opened here.</summary>
    private static IEnumerable<(CivilDb.Network net, ObjectId id)> PartIds(
        Transaction tr, JsonElement p, Func<CivilDb.Network, ObjectIdCollection> partsOf) =>
        Networks(tr, StrParam(p, "network_name")).SelectMany(net => partsOf(net).Cast<ObjectId>().Select(id => (net, id)));

    public static object ListPipeNetworks(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = Networks(tr).Select(n => new
        {
            name = n.Name,
            handle = H(n.ObjectId),
            partsList = Safe(() => n.PartsListName),
            referenceSurface = NameOf(tr, () => n.ReferenceSurfaceId),
            referenceAlignment = NameOf(tr, () => n.ReferenceAlignmentId),
            pipeCount = n.GetPipeIds().Count,
            structureCount = n.GetStructureIds().Count,
        }).ToList();
        return new { count = rows.Count, pipeNetworks = rows };
    });

    public static object ListPipes(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("pipes", PartIds(tr, p, n => n.GetPipeIds()), limit, x =>
        {
            var pipe = (CivilDb.Pipe)tr.GetObject(x.id, OpenMode.ForRead);
            return new
            {
                network = x.net.Name,
                name = pipe.Name,
                handle = H(x.id),
                partFamily = Safe(() => pipe.PartFamilyName),
                partSize = Safe(() => pipe.PartSizeName),
                innerDiameterOrWidth = Safe(() => pipe.InnerDiameterOrWidth),   // API units of the catalog (metres in metric templates)
                slope = Safe(() => pipe.Slope),   // ratio (m/m), not percent
                length3d = Safe(() => R3(pipe.Length3D)),
                start = P3(pipe.StartPoint),
                end = P3(pipe.EndPoint),
                startStructure = Safe(() => pipe.StartStructureId.IsNull ? null : H(pipe.StartStructureId)),
                endStructure = Safe(() => pipe.EndStructureId.IsNull ? null : H(pipe.EndStructureId)),
            };
        }));
    }

    public static object ListStructures(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("structures", PartIds(tr, p, n => n.GetStructureIds()), limit, x =>
        {
            var s = (CivilDb.Structure)tr.GetObject(x.id, OpenMode.ForRead);
            return new
            {
                network = x.net.Name,
                name = s.Name,
                handle = H(x.id),
                partFamily = Safe(() => s.PartFamilyName),
                partSize = Safe(() => s.PartSizeName),
                location = Safe(() => P3(s.Location)),
                rimElevation = Safe(() => R3(s.RimElevation)),
                sumpElevation = Safe(() => R3(s.SumpElevation)),
                rotationRad = Safe(() => s.Rotation),
                connectedPipes = Safe(() => s.ConnectedPipesCount),
            };
        }));
    }

    public static object ListGravityParts(JsonElement p)
    {
        string? listName = StrParam(p, "parts_list");
        return WithRead<object>((db, tr) =>
        {
            if (listName == null)
                return new
                {
                    partsLists = Civ.Styles.PartsListSet.Cast<ObjectId>()
                        .Select(id => (CivilStyles.PartsList)tr.GetObject(id, OpenMode.ForRead))
                        .Select(pl => new { name = Styles.NameOf(pl), handle = H(pl.ObjectId), partFamilies = pl.PartFamilyCount }).ToList(),
                    hint = "pass parts_list (name or handle) to see its families and sizes",
                };
            var list = GravityParts.FindList(Civ, db, tr, listName);
            return new
            {
                partsList = listName,
                pipeFamilies = GravityParts.Families(tr, list, CivilDb.DomainType.Pipe),
                structureFamilies = GravityParts.Families(tr, list, CivilDb.DomainType.Structure),
                note = "Dimensions are in the units shown. An edit selects a part with 'family' (text contained in the family " +
                       "description) plus 'inner_diameter' (same units) or 'size_index'.",
            };
        });
    }
}
