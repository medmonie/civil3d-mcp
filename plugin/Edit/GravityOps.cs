namespace C3dMcpBridge;

/// <summary>Gravity pipe networks: create a network, add structures and pipes, adjust one part, label.</summary>
internal static class GravityOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["pipe_network_create"] = NetworkCreate;
        ops["pipe_network_add"] = Add;
        ops["pipe_set"] = PipeSet;
        ops["structure_set"] = StructureSet;
        ops["labels_add"] = LabelsAdd;
        ops["parts_list_add_family"] = PartsListAddFamily;
    }

    // ---------------------------------------------------------------- parts lists

    /// <summary>
    /// Adds a part family from the Civil 3D catalogue to a gravity parts list, with the sizes asked for. A network
    /// should take all its parts from its own parts list; this is how a family from another list is made available.
    /// </summary>
    private static object PartsListAddFamily(EditContext c, JsonElement p)
    {
        var list = GravityParts.FindList(c.Civ, c.Db, c.Tr, NeedStr(p, "parts_list"));
        list.UpgradeOpen();
        var domain = Choice(p, "domain", "", "pipe", "structure") == "pipe" ? CivilDb.DomainType.Pipe : CivilDb.DomainType.Structure;
        string family = NeedStr(p, "family");

        CivilStyles.PartFamily? Find() => GravityParts.FamiliesOf(c.Tr, list, domain).FirstOrDefault(f => SameName(f.Description, family));
        bool added = false;
        var fam = Find();
        if (fam == null)
        {
            try { list.AddPartFamilyByDescription(domain, family); }
            catch (Exception ex) { throw new ArgumentException($"parts_list_add_family: '{family}' is not a {domain} family of the part catalogue ({ex.Message}). Use the exact family description, e.g. as list_gravity_parts shows it for another parts list."); }
            fam = Find() ?? throw new InvalidOperationException($"'{family}' was added but cannot be found in the parts list.");
            added = true;
        }
        fam.UpgradeOpen();

        var have = GravityParts.Sizes(c.Tr, fam).Where(s => s.Dimension.HasValue).Select(s => s.Dimension!.Value).ToList();
        var newSizes = new List<object>();
        foreach (double want in NumList(p, "sizes", 50))
        {
            if (have.Any(h => Math.Abs(h - want) < 0.5)) continue;
            using var filter = new CivilStyles.SizeFilterRecord(fam);
            var field = GravityParts.DimensionFields(domain)
                .Select(ctx => { try { return filter.GetParamByContextAndIndex(ctx, 0); } catch { return null; } })
                .FirstOrDefault(f => f != null)
                ?? throw new InvalidOperationException($"Family '{family}' has no diameter / width field to choose a size by.");
            object value = want;
            if (field.IsFromList)
            {
                var options = Enumerable.Range(0, field.ValueList.Count).Select(i => Convert.ToDouble(field.ValueList[i], System.Globalization.CultureInfo.InvariantCulture)).ToList();
                double nearest = options.MinBy(o => Math.Abs(o - want));
                if (Math.Abs(nearest - want) > Math.Max(0.5, want * 0.01))
                    throw new ArgumentException($"parts_list_add_family: '{family}' has no {field.Name} of {want} {field.Units}. Catalogue values: {string.Join(", ", options)}");
                value = nearest;
            }
            field.Value = value;
            fam.AddPartSize(filter);
            newSizes.Add(new { field = field.Name, value, units = field.Units });
        }
        if (!added && newSizes.Count == 0 && NumList(p, "sizes", 50).Count == 0 && fam.PartSizeCount > 0)
            c.Warn($"'{family}' was already in '{p.GetProperty("parts_list")}' with {fam.PartSizeCount} size(s); nothing changed.");
        if (fam.PartSizeCount == 0)
            throw new ArgumentException($"parts_list_add_family: give 'sizes' - '{family}' has no sizes in this parts list.");
        return new
        {
            partsList = NeedStr(p, "parts_list"), family = fam.Description, familyAdded = added, sizesAdded = newSizes,
            sizes = GravityParts.Sizes(c.Tr, fam).Select(s => s.Label).ToList(),
        };
    }

    private static object NetworkCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        RequireNewName<CivilDb.Network>(c.Tr, c.Civ.GetPipeNetworkIds(), name, "A gravity pipe network", " (use pipe_network_add to extend it)");
        var id = CivilDb.Network.Create(c.Civ, ref name);
        var net = (CivilDb.Network)c.Tr.GetObject(id, OpenMode.ForWrite);
        if (StrParam(p, "parts_list") is string pl) net.PartsListId = GravityParts.FindList(c.Civ, c.Db, c.Tr, pl).ObjectId;
        if (StrParam(p, "reference_surface") is string s) net.ReferenceSurfaceId = FindSurface(c.Civ, c.Tr, s).ObjectId;
        if (StrParam(p, "reference_alignment") is string a) net.ReferenceAlignmentId = FindAlignment(c.Civ, c.Tr, a).ObjectId;
        if (StrParam(p, "description") is string d) net.Description = d;
        return new { network = net.Name, handle = H(id), partsList = Safe(() => net.PartsListName) };
    }

    // ---------------------------------------------------------------- add structures and pipes

    private static object Add(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"), OpenMode.ForWrite);
        var picker = new GravityParts.Picker(c.Tr, GravityParts.ListFor(c.Civ, c.Db, c.Tr, net, StrParam(p, "parts_list")));
        bool applyRules = BoolParam(p, "apply_rules") ?? false;
        double tolerance = DblParam(p, "connect_tolerance") ?? 0.01;
        var structureSpecs = ArrParam(p, "structures", 5000);
        var pipeSpecs = ArrParam(p, "pipes", 5000);
        if (structureSpecs.Count + pipeSpecs.Count == 0) throw new ArgumentException("pipe_network_add: give structures and/or pipes.");

        var created = new List<object>();

        // Structures a new pipe end can connect to: the existing ones plus the ones added below.
        var structures = new PointIndex<ObjectId>(tolerance);
        var named = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
        void IndexStructure(CivilDb.Structure structure)
        {
            structures.Add(structure.Location, structure.ObjectId);
            if (!named.TryGetValue(structure.Name, out var ids)) named[structure.Name] = ids = new();
            ids.Add(structure.ObjectId);
        }
        foreach (ObjectId id in net.GetStructureIds())
            IndexStructure((CivilDb.Structure)c.Tr.GetObject(id, OpenMode.ForRead));

        for (int i = 0; i < structureSpecs.Count; i++)
        {
            var s = structureSpecs[i];
            string what = $"structure {StrParam(s, "id") ?? (i + 1).ToString()}";
            var at = NeedPt(s, "at", what);
            var part = picker.Pick(CivilDb.DomainType.Structure, s, what);
            ObjectId id = ObjectId.Null;
            net.AddStructure(part.FamilyId, part.SizeId, at, Rad(DblParam(s, "rotation_deg") ?? 0), ref id, applyRules);
            var st = (CivilDb.Structure)c.Tr.GetObject(id, OpenMode.ForWrite);
            Describe(st, s, what);
            SetLevels(st, DblParam(s, "rim"), DblParam(s, "sump"));
            IndexStructure(st);
            created.Add(new
            {
                kind = "structure", id = StrParam(s, "id"), name = st.Name, handle = H(id), part = part.Label,
                rim = Safe(() => R3(st.RimElevation)), sump = Safe(() => R3(st.SumpElevation)),
            });
        }

        int connections = 0;
        double totalLength = 0;
        for (int i = 0; i < pipeSpecs.Count; i++)
        {
            var s = pipeSpecs[i];
            string what = $"pipe {StrParam(s, "id") ?? (i + 1).ToString()}";
            var a = NeedPt(s, "start", what);
            var b = NeedPt(s, "end", what);
            if (a.DistanceTo(b) < 0.01) throw new ArgumentException($"{what}: start and end are the same point.");
            var part = picker.Pick(CivilDb.DomainType.Pipe, s, what);
            ObjectId id = ObjectId.Null;
            net.AddLinePipe(part.FamilyId, part.SizeId, new LineSegment3d(a, b), ref id, applyRules);
            totalLength += a.DistanceTo(b);

            // Connect each end to the structure standing at it (in plan).
            var pipe = (CivilDb.Pipe)c.Tr.GetObject(id, OpenMode.ForWrite);
            Describe(pipe, s, what);
            int made = 0;
            foreach (var (end, position, key) in new[]
            {
                (a, CivilDb.ConnectorPositionType.Start, "start_structure"),
                (b, CivilDb.ConnectorPositionType.End, "end_structure"),
            })
            {
                if (Has(s, key))
                {
                    var name = NeedStr(s, key);
                    if (!named.TryGetValue(name, out var matches) || matches.Count != 1)
                        throw new ArgumentException($"{what}: '{key}' must name one structure in the network. Found '{name}' {matches?.Count ?? 0} time(s).");
                    var structure = matches[0];
                    pipe.ConnectToStructure(position, structure, true);
                    var connected = position == CivilDb.ConnectorPositionType.Start ? pipe.StartStructureId : pipe.EndStructureId;
                    if (connected != structure) throw new InvalidOperationException($"{what}: no connection to '{name}' was recorded.");
                    made++;
                    continue;
                }
                var near = structures.Near(end, planOnly: true);
                if (near.Count == 0) continue;
                if (near.Count > 1)
                {
                    c.Warn($"{what}: {near.Count} structures match its {position}. Specify '{key}' to choose one.");
                    continue;
                }
                try { pipe.ConnectToStructure(position, near[0], true); made++; }
                catch (Exception ex) { c.Warn($"{what}: could not connect its {position} to structure {H(near[0])}: {ex.Message}"); }
            }
            if (PositiveParam(s, "verify_bore_mm") is double boreMm)
            {
                double bore = boreMm / 1000;
                double actual = pipe.InnerDiameterOrWidth;
                double actualBore = Math.Abs(actual - bore) <= Math.Abs(actual / 1000 - bore) ? actual : actual / 1000;
                if (!double.IsFinite(actualBore) || Math.Abs(actualBore - bore) > 0.001 ||
                    Math.Abs(pipe.StartPoint.Z - actualBore / 2 - (a.Z - bore / 2)) > 0.005 ||
                    Math.Abs(pipe.EndPoint.Z - actualBore / 2 - (b.Z - bore / 2)) > 0.005)
                    throw new InvalidOperationException($"{what}: the placed pipe did not preserve the requested bore or inverts.");
            }
            connections += made;
            created.Add(new { kind = "pipe", id = StrParam(s, "id"), name = pipe.Name, handle = H(id), part = part.Label, connections = made });
        }

        return new
        {
            network = net.Name, structures = structureSpecs.Count, pipes = pipeSpecs.Count, connections,
            totalPipeLength = R3(totalLength), created,
        };
    }

    /// <summary>The optional 'name' and 'description' of a new structure or pipe. A name already used in the drawing
    /// fails the batch (Civil 3D would otherwise rename silently).</summary>
    private static void Describe(CivilDb.Part part, JsonElement spec, string what)
    {
        if (StrParam(spec, "name") is { Length: > 0 } name)
        {
            try { part.Name = name; }
            catch (Exception ex) { throw new ArgumentException($"{what}: the name '{name}' cannot be used ({ex.Message}); is it already taken?"); }
            if (part.Name != name) throw new ArgumentException($"{what}: the name '{name}' is already used in this drawing.");
        }
        if (StrParam(spec, "description") is string description) part.Description = description;
    }

    // ---------------------------------------------------------------- change one part

    /// <summary>An explicit rim or sump level only sticks when Civil 3D's automatic control of it is switched off
    /// (rim follows the reference surface; sump follows a depth below the lowest pipe). Verified live.</summary>
    private static void SetLevels(CivilDb.Structure st, double? rim, double? sump)
    {
        if (rim is double r)
        {
            st.AutomaticRimSurfaceAdjustment = false;
            st.RimElevation = r;
        }
        if (sump is double s)
        {
            st.ControlSumpBy = CivilDb.StructureControlSumpType.ByElevation;
            st.SumpElevation = s;
        }
    }

    private static object PipeSet(EditContext c, JsonElement p)
    {
        string handle = NeedStr(p, "handle");
        var pipe = c.OpenByHandle<CivilDb.Pipe>(p, "a gravity pipe");
        var changed = new List<string>();
        if (OptPt(p, "start", handle) is Point3d a) { pipe.StartPoint = a; changed.Add("start"); }
        if (OptPt(p, "end", handle) is Point3d b) { pipe.EndPoint = b; changed.Add("end"); }
        if (DblParam(p, "inner_diameter") is double d)
        {
            pipe.ResizeByInnerDiameterOrWidth(d, true);
            changed.Add($"inner diameter -> {pipe.InnerDiameterOrWidth}");
        }
        if (StrParam(p, "description") is string description) { pipe.Description = description; changed.Add("description"); }
        if (changed.Count == 0) throw new ArgumentException("pipe_set: give start, end, inner_diameter and/or description.");
        return new { handle, name = pipe.Name, changed, slope = Safe(() => pipe.Slope) };
    }

    private static object StructureSet(EditContext c, JsonElement p)
    {
        string handle = NeedStr(p, "handle");
        var st = c.OpenByHandle<CivilDb.Structure>(p, "a structure");
        var changed = new List<string>();
        if (OptPt(p, "location", handle) is Point3d at) { st.Location = at; changed.Add("location"); }
        if (Has(p, "rim")) changed.Add("rim");
        if (Has(p, "sump")) changed.Add("sump");
        SetLevels(st, DblParam(p, "rim"), DblParam(p, "sump"));
        if (DblParam(p, "rotation_deg") is double rotation) { st.Rotation = Rad(rotation); changed.Add("rotation"); }
        if (StrParam(p, "description") is string description) { st.Description = description; changed.Add("description"); }
        if (changed.Count == 0) throw new ArgumentException("structure_set: give location, rim, sump, rotation_deg and/or description.");
        return new { handle, name = st.Name, changed, rim = Safe(() => R3(st.RimElevation)), sump = Safe(() => R3(st.SumpElevation)) };
    }

    // ---------------------------------------------------------------- labels

    private static object LabelsAdd(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "pipe_network"));
        int pipes = 0, structures = 0, skipped = 0;
        // A part that already has a plan label is skipped, so running this again does not stack a second label.
        if (BoolParam(p, "pipes") ?? true)
        {
            var labelled = c.LabelsByPart<CivilDb.PipeLabel>();
            foreach (ObjectId id in net.GetPipeIds())
                if (labelled.ContainsKey(id)) skipped++; else { CivilDb.PipeLabel.Create(id, 0.5); pipes++; }
        }
        if (BoolParam(p, "structures") ?? true)
        {
            var labelled = c.LabelsByPart<CivilDb.StructureLabel>();
            foreach (ObjectId id in net.GetStructureIds())
                if (labelled.ContainsKey(id)) skipped++; else { CivilDb.StructureLabel.Create(id); structures++; }
        }
        return new { network = net.Name, pipeLabels = pipes, structureLabels = structures, alreadyLabelled = skipped,
                     note = "Labels use the drawing's default label styles. Parts that already had a plan label were skipped." };
    }
}
