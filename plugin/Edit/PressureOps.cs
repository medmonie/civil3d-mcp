namespace C3dMcpBridge;

/// <summary>Pressure pipe networks: create, extend, pipe runs, move / resize one part, label, erase.</summary>
internal static class PressureOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["pressure_network_create"] = NetworkCreate;
        ops["pressure_network_set"] = NetworkSet;
        ops["pressure_network_erase"] = NetworkErase;
        ops["pressure_add"] = Add;
        ops["pressure_pipe_run"] = PipeRun;
        ops["pressure_part_set"] = PartSet;
        ops["pressure_labels_add"] = LabelsAdd;
    }

    private const string PipeType = "Pipe";

    private static CivilDb.PressurePipeNetwork Network(EditContext c, JsonElement p, string param) =>
        FindPressureNetwork(c.Civ, c.Tr, NeedStr(p, param), OpenMode.ForWrite);

    private static CivilStyles.PressurePartList PartsList(EditContext c, CivilDb.PressurePipeNetwork net, JsonElement p) =>
        PressureParts.ListFor(c.Db, c.Tr, net, StrParam(p, "parts_list"));

    // ---------------------------------------------------------------- networks

    private static void ApplyNetworkProps(EditContext c, CivilDb.PressurePipeNetwork net, JsonElement p)
    {
        if (StrParam(p, "parts_list") is string pl) net.PartsListId = PressureParts.FindList(c.Db, c.Tr, pl).ObjectId;
        if (StrParam(p, "reference_surface") is string s) net.ReferenceSurfaceId = FindSurface(c.Civ, c.Tr, s).ObjectId;
        if (StrParam(p, "reference_alignment") is string a) net.ReferenceAlignmentId = FindAlignment(c.Civ, c.Tr, a).ObjectId;
        if (StrParam(p, "description") is string d) net.Description = d;
    }

    private static void RequireNewNetworkName(EditContext c, string name, string hint = "") =>
        RequireNewName<CivilDb.PressurePipeNetwork>(c.Tr, PressureNetworkIds(c.Civ), name, "A pressure network", hint);

    private static object NetworkCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewNetworkName(c, name, " (use pressure_add to extend it)");
        NeedStr(p, "parts_list");   // required: a network without a parts list cannot take parts
        var id = CivilDb.PressurePipeNetwork.Create(c.Db, name);
        var net = (CivilDb.PressurePipeNetwork)c.Tr.GetObject(id, OpenMode.ForWrite);
        ApplyNetworkProps(c, net, p);
        return new { network = name, handle = H(id), partsList = Safe(() => net.PartsListName) };
    }

    private static object NetworkSet(EditContext c, JsonElement p)
    {
        var net = Network(c, p, "name");
        ApplyNetworkProps(c, net, p);
        if (StrParam(p, "new_name") is { Length: > 0 } newName && !SameName(newName, net.Name))
        {
            RequireNewNetworkName(c, newName);
            net.Name = newName;
        }
        return new { network = net.Name, handle = H(net.ObjectId), updated = true };
    }

    private static object NetworkErase(EditContext c, JsonElement p)
    {
        var net = Network(c, p, "name");
        string name = net.Name;
        int pipes = net.GetPipeIds().Count, fittings = net.GetFittingIds().Count, appurtenances = net.GetAppurtenanceIds().Count;
        net.Erase();
        return new { network = name, erased = true, pipes, fittings, appurtenances };
    }

    // ---------------------------------------------------------------- add pipes / fittings / appurtenances

    private static object Add(EditContext c, JsonElement p)
    {
        var net = Network(c, p, "network");
        var picker = new PressureParts.Picker(PartsList(c, net, p));
        double tolerance = DblParam(p, "connect_tolerance") ?? 0.01;
        var pipeSpecs = ArrParam(p, "pipes", 5000);
        var fittingSpecs = ArrParam(p, "fittings", 5000);
        var appurtenanceSpecs = ArrParam(p, "appurtenances", 5000);
        if (pipeSpecs.Count + fittingSpecs.Count + appurtenanceSpecs.Count == 0)
            throw new ArgumentException("pressure_add: give pipes, fittings and/or appurtenances.");

        var created = new List<object>();
        double totalLength = 0;
        for (int i = 0; i < pipeSpecs.Count; i++)
        {
            var s = pipeSpecs[i];
            string what = $"pipe {StrParam(s, "id") ?? (i + 1).ToString()}";
            var a = NeedPt(s, "start", what);
            var b = NeedPt(s, "end", what);
            if (a.DistanceTo(b) < 0.01) throw new ArgumentException($"{what}: start and end are the same point.");
            var part = picker.Pick(PipeType, s, what);
            ObjectId id;
            if (OptPt(s, "through", what) is Point3d mid)
            {
                // Curved pipe: an arc from start to end passing through the given point.
                using var arc = new CivilDb.PipeCurveGeometry(a, new Point2d(mid.X, mid.Y), b);
                id = net.AddCurvePipe(arc, part);
                totalLength += ((CivilDb.PressurePipe)c.Tr.GetObject(id, OpenMode.ForRead)).Length2DCenterToCenter;
            }
            else
            {
                try { id = net.AddLinePipe(new LineSegment3d(a, b), part); }
                catch (Exception ex)
                {
                    throw new ArgumentException($"{what} ({a.DistanceTo(b):0.###} m, {PressureParts.Describe(part)}): {ex.Message}");
                }
                totalLength += a.DistanceTo(b);
            }
            Describe(c, id, s, what, p);
            created.Add(new { kind = "pipe", id = StrParam(s, "id"), handle = H(id), part = PressureParts.Describe(part) });
        }

        // Pipe ends a fitting or appurtenance can connect to: every pipe of the network, including the ones just added.
        var pipeEnds = new PointIndex<(ObjectId pipe, int port)>(tolerance);
        if (fittingSpecs.Count + appurtenanceSpecs.Count > 0)
            foreach (ObjectId id in net.GetPipeIds())
            {
                var pipe = (CivilDb.PressurePipe)c.Tr.GetObject(id, OpenMode.ForRead);
                pipeEnds.Add(pipe.StartPoint, (id, 0));
                pipeEnds.Add(pipe.EndPoint, (id, 1));
            }

        int connections = 0;
        void Place(List<JsonElement> specs, string kind, string defaultType, Func<Point3d, CivilStyles.PressurePartSize, ObjectId> addToNetwork)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                string what = $"{kind} {StrParam(s, "id") ?? (i + 1).ToString()}";
                var at = NeedPt(s, "at", what);
                string type = StrParam(s, "type") ?? defaultType;
                if (SameName(type, PipeType) || SameName(type, "PressurePipe")) throw new ArgumentException($"{what}: type cannot be a pipe.");
                var part = picker.Pick(type, s, $"{what} ({type})");
                ObjectId id;
                try { id = addToNetwork(at, part); }
                catch (Exception ex) { throw new ArgumentException($"{what} ({PressureParts.Describe(part)}): {ex.Message}"); }
                Describe(c, id, s, what, p);

                // Connect the new part's ports, in order, to the pipe ends that meet at its location. Only a call that
                // returns counts as a connection; a failed one is reported and the next pipe end gets the same port.
                int made = 0, tried = 0;
                var placed = (CivilDb.PressurePart)c.Tr.GetObject(id, OpenMode.ForWrite);
                foreach (var (pipe, port) in pipeEnds.Near(at, planOnly: false))
                {
                    tried++;
                    try { placed.ConnectToPipe(made, pipe, port); made++; }
                    catch (Exception ex) { c.Warn($"{what}: connecting port {made} to a pipe end failed: {ex.Message}"); }
                }
                if (tried == 0) c.Warn($"{what}: no pipe end within {tolerance} of it, so it is not connected.");
                else if (made < tried) c.Warn($"{what}: {made} of {tried} pipe ends connected.");
                connections += made;
                created.Add(new { kind, id = StrParam(s, "id"), handle = H(id), part = PressureParts.Describe(part), connections = made });
            }
        }
        Place(fittingSpecs, "fitting", "Elbow", (at, part) => net.AddFitting(at, part));
        Place(appurtenanceSpecs, "appurtenance", "Valve", (at, part) => net.AddAppurtenance(at, part));

        return new
        {
            network = net.Name, pipes = pipeSpecs.Count, fittings = fittingSpecs.Count, appurtenances = appurtenanceSpecs.Count,
            connections, totalPipeLength = R3(totalLength), created,
        };
    }

    /// <summary>Optional 'name' and 'description' of a part just added.</summary>
    private static void Describe(EditContext c, ObjectId id, JsonElement s, string what, JsonElement op = default)
    {
        if (op.ValueKind == JsonValueKind.Object && Provenance.FromSpec(s, op) is { } tag)
            Provenance.Write(c.Tr, c.Db, c.Tr.GetObject(id, OpenMode.ForWrite), tag);
        if (StrParam(s, "name") is null && StrParam(s, "description") is null) return;
        var part = (CivilDb.PressurePart)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(s, "name") is { Length: > 0 } name)
        {
            try { part.Name = name; }
            catch (Exception ex) { c.Warn($"{what}: name '{name}' was not accepted ({ex.Message}); kept '{part.Name}'."); }
            if (part.Name != name) c.Warn($"{what}: Civil 3D named it '{part.Name}' instead of '{name}' (is the name already used?).");
        }
        if (StrParam(s, "description") is string d) part.Description = d;
    }

    // ---------------------------------------------------------------- pipe run from a path

    private static object PipeRun(EditContext c, JsonElement p)
    {
        var net = Network(c, p, "network");
        string name = NeedStr(p, "name");
        var pts = PtList(p, "points", "pressure_pipe_run", 2, 5000);
        double cover = DblParam(p, "cover") ?? 1.0;
        if (cover < 0) throw new ArgumentException("pressure_pipe_run: cover cannot be negative.");
        bool autoBends = BoolParam(p, "auto_bends") ?? true;
        bool keepPath = BoolParam(p, "keep_path") ?? false;
        var part = new PressureParts.Picker(PartsList(c, net, p)).Pick(PipeType, p, $"pipe run '{name}'");
        if (net.PipeRuns.Any(r => SameName(r.Name, name)))
            throw new ArgumentException($"Pipe run '{name}' already exists in network '{net.Name}'.");

        // The API takes the path as a polyline: draw it, hand it over, and remove it again unless asked to keep it.
        var path = FlatPolyline(pts);
        path.Elevation = 0;
        var pathId = c.Append(path, StrParam(p, "layer"));
        int pipesBefore = net.GetPipeIds().Count;
        net.PipeRuns.createPipeRun(name, path, part, cover, autoBends);
        if (!keepPath)
        {
            try { path.Erase(); } catch (Exception ex) { c.Warn("the path polyline could not be removed: " + ex.Message); }
        }
        if (net.ReferenceSurfaceId.IsNull)
            c.Warn("the network has no reference surface, so 'cover' has nothing to measure from; set reference_surface for depth control.");
        return new
        {
            network = net.Name, pipeRun = name, part = PressureParts.Describe(part), cover, autoBends,
            pipesAdded = net.GetPipeIds().Count - pipesBefore, fittingsNow = net.GetFittingIds().Count,
            pathHandle = keepPath ? H(pathId) : null,
        };
    }

    // ---------------------------------------------------------------- change one part

    private static object PartSet(EditContext c, JsonElement p)
    {
        string handle = NeedStr(p, "handle");
        var part = c.OpenByHandle<CivilDb.PressurePart>(p, "a pressure pipe, fitting or appurtenance");
        var changed = new List<string>();

        if (Has(p, "start") || Has(p, "end"))
        {
            var pipe = part as CivilDb.PressurePipe ?? throw new ArgumentException($"{handle}: start/end apply to pipes only (use 'location' for fittings).");
            if (OptPt(p, "start", handle) is Point3d a) { pipe.StartPoint = a; changed.Add("start"); }
            if (OptPt(p, "end", handle) is Point3d b) { pipe.EndPoint = b; changed.Add("end"); }
        }
        if (OptPt(p, "location", handle) is Point3d at)
        {
            switch (part)
            {
                case CivilDb.PressureFitting f: f.Location = at; break;
                case CivilDb.PressureAppurtenance ap: ap.Location = at; break;
                default: throw new ArgumentException($"{handle}: 'location' applies to fittings and appurtenances (use start/end for pipes).");
            }
            changed.Add("location");
        }
        if (Has(p, "size") || Has(p, "match"))
        {
            var net = (CivilDb.PressurePipeNetwork)c.Tr.GetObject(part.NetworkId, OpenMode.ForRead);
            string type = part is CivilDb.PressurePipe ? PipeType : part.PartType.ToString();
            var size = new PressureParts.Picker(PartsList(c, net, p)).Pick(type, p, handle);
            SwapSize(part, size);
            changed.Add("size -> " + PressureParts.Describe(size));
        }
        if (StrParam(p, "description") is string d) { part.Description = d; changed.Add("description"); }
        if (changed.Count == 0) throw new ArgumentException("pressure_part_set: give start/end, location, size/match and/or description.");
        return new { handle, name = part.Name, type = part.PartType.ToString(), changed };
    }

#if NETFRAMEWORK
    // The Civil 3D 2024 .NET API has no way to change the size of an existing pressure part.
    private static void SwapSize(CivilDb.PressurePart part, CivilStyles.PressurePartSize size) =>
        throw new NotSupportedException($"pressure_part_set: changing size is not available in Civil 3D 2024 ({PressureParts.Describe(size)} was requested). Delete the part and create it again with the new size, or swap it in Civil 3D.");
#else
    private static void SwapSize(CivilDb.PressurePart part, CivilStyles.PressurePartSize size) => part.SwapPartSize(size);
#endif

    // ---------------------------------------------------------------- labels

    private static object LabelsAdd(EditContext c, JsonElement p)
    {
        const double middle = 0.5;   // label position along the part
        var net = FindPressureNetwork(c.Civ, c.Tr, NeedStr(p, "pressure_network"));
        var used = new Dictionary<string, object?>();
        HashSet<string>? only = Has(p, "handles") ? StrList(p, "handles", 20000).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

        int skipped = 0;

        // Labels every part of one kind with one style; returns how many were labelled. A part that already has a
        // plan label of that kind is skipped, so running this again does not stack a second label.
        int Label(string kind, ObjectIdCollection parts, CivilStyles.StyleCollectionBase styles,
                  Dictionary<ObjectId, List<ObjectId>> labelled, Action<ObjectId, ObjectId> create)
        {
            if (!(BoolParam(p, kind + "s") ?? true)) return 0;
            var style = Styles.Id(styles, StrParam(p, kind + "_style"), $"pressure {kind} label style");
            used[kind] = Styles.NameOf(c.Tr, style);
            int made = 0;
            foreach (ObjectId id in parts)
            {
                if (only != null && !only.Contains(H(id))) continue;
                if (labelled.ContainsKey(id)) { skipped++; continue; }
                create(id, style);
                made++;
            }
            return made;
        }

        int pipes = Label("pipe", net.GetPipeIds(), Styles.PressurePipeLabels(c.Civ), c.LabelsByPart<CivilDb.PressurePipeLabel>(),
            (id, style) => CivilDb.PressurePipeLabel.Create(id, middle, style));
        int fittings = Label("fitting", net.GetFittingIds(), Styles.PressureFittingLabels(c.Civ), c.LabelsByPart<CivilDb.PressureFittingLabel>(),
            (id, style) => CivilDb.PressureFittingLabel.Create(id, style, middle, Vector3d.XAxis));
        int appurtenances = Label("appurtenance", net.GetAppurtenanceIds(), Styles.PressureAppurtenanceLabels(c.Civ), c.LabelsByPart<CivilDb.PressureAppurtenanceLabel>(),
            (id, style) => CivilDb.PressureAppurtenanceLabel.Create(id, style, middle, Vector3d.XAxis));
        return new { network = net.Name, pipeLabels = pipes, fittingLabels = fittings, appurtenanceLabels = appurtenances,
                     alreadyLabelled = skipped, stylesUsed = used };
    }
}
