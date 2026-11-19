namespace C3dMcpBridge;

/// <summary>Pressure pipe networks (AeccPressurePipesMgd): networks, pipes, fittings, appurtenances, pipe runs, part sizes (read only).</summary>
internal static class PressureRead
{
    private static IEnumerable<CivilDb.PressurePipeNetwork> Networks(Transaction tr, string? only = null) =>
        PressureNetworkIds(Civ).Select(id => (CivilDb.PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead))
                               .Where(n => only == null || SameName(n.Name, only));

    /// <summary>(network, part id) for every pipe, fitting or appurtenance of the wanted networks. No part is opened here.</summary>
    private static IEnumerable<(CivilDb.PressurePipeNetwork net, ObjectId id)> PartIds(
        Transaction tr, JsonElement p, Func<CivilDb.PressurePipeNetwork, ObjectIdCollection> partsOf) =>
        Networks(tr, StrParam(p, "network_name")).SelectMany(net => partsOf(net).Cast<ObjectId>().Select(id => (net, id)));

    public static object ListPressureNetworks(JsonElement p) => WithRead((db, tr) =>
    {
        var rows = Networks(tr).Select(n => new
        {
            name = n.Name,
            handle = H(n.ObjectId),
            partsList = Safe(() => n.PartsListName),
            partsListHandle = Safe(() => n.PartsListId.IsNull ? null : H(n.PartsListId)),
            referenceSurface = NameOf(tr, () => n.ReferenceSurfaceId),
            referenceAlignment = NameOf(tr, () => n.ReferenceAlignmentId),
            pipeCount = Safe(() => n.GetPipeIds().Count),
            fittingCount = Safe(() => n.GetFittingIds().Count),
            appurtenanceCount = Safe(() => n.GetAppurtenanceIds().Count),
            pipeRunCount = Safe(() => n.PipeRuns.Count),
            minElevation = Safe(() => R3(n.MinimumElevation)),
            maxElevation = Safe(() => R3(n.MaximumElevation)),
        }).ToList();
        return new { count = rows.Count, pressureNetworks = rows };
    });

    public static object ListPressurePipes(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("pipes", PartIds(tr, p, n => n.GetPipeIds()), limit, x =>
        {
            var pipe = (CivilDb.PressurePipe)tr.GetObject(x.id, OpenMode.ForRead);
            return new
            {
                network = x.net.Name,
                name = pipe.Name,
                handle = H(x.id),
                partFamily = Safe(() => pipe.PartFamilyName),
                description = Safe(() => pipe.PartDescription),
                style = Safe(() => pipe.StyleName),
                layer = pipe.Layer,
                // Metres (verified live: DN150 reads 0.15).
                nominalDiameter = Safe(() => pipe.NominalDiameter),
                innerDiameter = Safe(() => pipe.InnerDiameter),
                outerDiameter = Safe(() => pipe.OuterDiameter),
                wallThickness = Safe(() => pipe.WallThickness),
                isCurve = Safe(() => pipe.IsCurve),
                length2d = Safe(() => R3(pipe.Length2DCenterToCenter)),
                length3d = Safe(() => R3(pipe.Length3DCenterToCenter)),
                slope = Safe(() => pipe.Slope),   // ratio (m/m), not percent
                startElevation = Safe(() => R3(pipe.StartElevation)),
                endElevation = Safe(() => R3(pipe.EndElevation)),
                minCover = Safe(() => R3(pipe.MinimumCover)),
                maxCover = Safe(() => R3(pipe.MaximumCover)),
                start = Safe(() => P3(pipe.StartPoint)),
                end = Safe(() => P3(pipe.EndPoint)),
            };
        }));
    }

    /// <summary>The fields fittings and appurtenances share.</summary>
    private static Dictionary<string, object?> PointPart(CivilDb.PressurePipeNetwork net, CivilDb.PressurePart part, Func<Point3d> location) => new()
    {
        ["network"] = net.Name,
        ["name"] = part.Name,
        ["handle"] = H(part.ObjectId),
        ["partFamily"] = Safe(() => part.PartFamilyName),
        ["description"] = Safe(() => part.PartDescription),
        ["partType"] = Safe(() => part.PartType.ToString()),
        ["connections"] = Safe(() => part.ConnectionCount),
        ["location"] = Safe(() => P3(location())),
    };

    public static object ListPressureFittings(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("fittings", PartIds(tr, p, n => n.GetFittingIds()), limit, x =>
        {
            var f = (CivilDb.PressureFitting)tr.GetObject(x.id, OpenMode.ForRead);
            var row = PointPart(x.net, f, () => f.Location);
            row["horizontalAngleRad"] = Safe(() => f.ResultantHorizontalAngle);
            return row;
        }));
    }

    public static object ListPressureAppurtenances(JsonElement p)
    {
        int limit = IntParam(p, "limit", 200, 1, 5000);
        return WithRead((db, tr) => Paged("appurtenances", PartIds(tr, p, n => n.GetAppurtenanceIds()), limit, x =>
        {
            var a = (CivilDb.PressureAppurtenance)tr.GetObject(x.id, OpenMode.ForRead);
            return PointPart(x.net, a, () => a.Location);
        }));
    }

    public static object ListPipeRuns(JsonElement p)
    {
        string? only = StrParam(p, "network_name");
        return WithRead((db, tr) =>
        {
            var rows = new List<object>();
            foreach (var net in Networks(tr, only))
                foreach (CivilDb.PressurePipeRun run in net.PipeRuns)
                    rows.Add(new
                    {
                        network = net.Name,
                        name = Safe(() => run.Name),
                        alignment = NameOf(tr, () => run.AlignmentId),
                        profile = NameOf(tr, () => run.ProfileId),
                        partCount = Safe(() => run.GetPartIds().Count),
                        verticalOffset = Safe(() => R3(run.VerticalOffset)),
                    });
            return new { count = rows.Count, pipeRuns = rows };
        });
    }

    public static object ListPressurePartSizes(JsonElement p)
    {
        string? listName = StrParam(p, "parts_list");
        string type = StrParam(p, "part_type") ?? "Pipe";
        string? contains = StrParam(p, "contains");
        int limit = IntParam(p, "limit", 100, 1, 1000);
        return WithRead<object>((db, tr) =>
        {
            if (listName == null)
                return new
                {
                    partsLists = PressureParts.Lists(db, tr).Select(l => new
                    {
                        handle = H(l.List.ObjectId),
                        name = l.Name,
                        description = Safe(() => l.List.Description),
                        pipeSizes = Safe(() => l.List.GetParts(CivilDb.PressurePartDomainType.Pipe).Count),
                    }).ToList(),
                    hint = "pass parts_list (name or handle) to see its sizes",
                };
            var sizes = PressureParts.Candidates(PressureParts.FindList(db, tr, listName), type)
                .Select(s => new { description = PressureParts.Describe(s), partType = s.PartType.ToString() })
                .Where(r => contains == null || r.description.Contains(contains, StringComparison.OrdinalIgnoreCase));
            var reply = Paged("sizes", sizes, limit, s => s);
            reply["partsList"] = listName;
            reply["type"] = type;
            return reply;
        });
    }
}
