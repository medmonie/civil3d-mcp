namespace C3dMcpBridge;

/// <summary>
/// Editing a gravity network after it was drawn or imported (roadmap #69): break a pipe at a structure, move parts to
/// another network, connect / disconnect pipe ends, regrade holding one end, pull a pipe end to a structure wall,
/// resize structures by the depth of their pipes, set flow direction. Parts are named by handle, or by name with
/// 'network'.
/// </summary>
internal static class NetworkEditOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["pipe_break"] = PipeBreak;
        ops["parts_move"] = PartsMove;
        ops["pipe_connect"] = PipeConnect;
        ops["pipe_regrade"] = PipeRegrade;
        ops["pipe_end_adjust"] = PipeEndAdjust;
        ops["structure_resize"] = StructureResize;
        ops["pipe_flow_direction"] = PipeFlowDirection;
    }

    // ---------------------------------------------------------------- finding parts

    /// <summary>A gravity pipe or structure by handle, or by name inside 'network' (or any network when unique).</summary>
    internal static ObjectId PartId(Transaction tr, Database db, CivilDoc civ, string text, string? network, bool pipe, string what)
    {
        if (TryHandle(db, text) is ObjectId byHandle)
        {
            var o = tr.GetObject(byHandle, OpenMode.ForRead);
            if (pipe ? o is CivilDb.Pipe : o is CivilDb.Structure) return byHandle;
        }
        var hits = new List<ObjectId>();
        foreach (ObjectId nid in civ.GetPipeNetworkIds())
        {
            var net = (CivilDb.Network)tr.GetObject(nid, OpenMode.ForRead);
            if (network != null && !SameName(net.Name, network)) continue;
            foreach (ObjectId id in pipe ? net.GetPipeIds() : net.GetStructureIds())
                if (SameName(((CivilDb.Part)tr.GetObject(id, OpenMode.ForRead)).Name, text)) hits.Add(id);
        }
        if (hits.Count == 1) return hits[0];
        string kind = pipe ? "pipe" : "structure";
        if (hits.Count == 0)
            throw new ArgumentException($"{what}: no gravity {kind} '{text}'" + (network != null ? $" in network '{network}'." : ". Give its handle, or its name with 'network'."));
        throw new ArgumentException($"{what}: {hits.Count} {kind}s are named '{text}'; give 'network' or the handle.");
    }

    private static ObjectId? TryHandle(Database db, string text)
    {
        if (text.Length == 0 || text.Length > 16 || !text.All(Uri.IsHexDigit)) return null;
        try { var id = IdFromHandle(db, text); return id.IsNull || id.IsErased ? null : id; }
        catch { return null; }
    }

    private static CivilDb.Pipe Pipe(EditContext c, JsonElement p, string key = "pipe") =>
        (CivilDb.Pipe)c.Tr.GetObject(PartId(c.Tr, c.Db, c.Civ, NeedStr(p, key), StrParam(p, "network"), true, key), OpenMode.ForWrite);

    private static CivilDb.Structure Structure(EditContext c, JsonElement p, string key = "structure") =>
        (CivilDb.Structure)c.Tr.GetObject(PartId(c.Tr, c.Db, c.Civ, NeedStr(p, key), StrParam(p, "network"), false, key), OpenMode.ForWrite);

    private static CivilDb.ConnectorPositionType End(JsonElement p, string key = "end") =>
        Choice(p, key, "", "start", "end") == "start" ? CivilDb.ConnectorPositionType.Start : CivilDb.ConnectorPositionType.End;

    private static object PipeFacts(Transaction tr, CivilDb.Pipe pipe) => new
    {
        name = pipe.Name, handle = H(pipe.ObjectId),
        start = P3(pipe.StartPoint), end = P3(pipe.EndPoint),
        slope = Safe(() => Math.Round(pipe.Slope, 6)), length2d = Safe(() => R3(pipe.Length2DCenterToCenter)),
        startStructure = NameOf(tr, () => pipe.StartStructureId), endStructure = NameOf(tr, () => pipe.EndStructureId),
        flowDirection = Safe(() => pipe.FlowDirection.ToString()),
    };

    // ---------------------------------------------------------------- operations

    /// <summary>Split a pipe at a point with a structure: an existing one, or a new one from the parts list.</summary>
    private static object PipeBreak(EditContext c, JsonElement p)
    {
        var pipe = Pipe(c, p);
        var net = (CivilDb.Network)c.Tr.GetObject(pipe.NetworkId, OpenMode.ForWrite);
        var a = pipe.StartPoint; var b = pipe.EndPoint;
        // The break point: 'at' [x, y] projected onto the pipe in plan, or 'distance' from the start (plan).
        double len2d = new Vector2d(b.X - a.X, b.Y - a.Y).Length;
        double t;
        if (DblParam(p, "distance") is double dist)
        {
            if (dist <= 0 || dist >= len2d) throw new ArgumentException($"pipe_break: distance must lie inside the pipe (0 - {R3(len2d)} m in plan).");
            t = dist / len2d;
        }
        else
        {
            var at = NeedPt(p, "at", "pipe_break");
            t = ((at.X - a.X) * (b.X - a.X) + (at.Y - a.Y) * (b.Y - a.Y)) / (len2d * len2d);
            if (t <= 0.001 || t >= 0.999) throw new ArgumentException("pipe_break: 'at' is at or beyond an end of the pipe; break it inside.");
            double off = Math.Abs((b.X - a.X) * (at.Y - a.Y) - (b.Y - a.Y) * (at.X - a.X)) / len2d;
            if (off > (DblParam(p, "tolerance") ?? 0.5))
                throw new ArgumentException($"pipe_break: 'at' is {R3(off)} m off the pipe in plan (tolerance {DblParam(p, "tolerance") ?? 0.5} m).");
        }
        var point = new Point3d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        ObjectId structureId;
        bool created = false;
        string? partLabel = null;
        if (Has(p, "structure"))
        {
            var st = Structure(c, p);
            if (st.NetworkId != pipe.NetworkId) throw new ArgumentException("pipe_break: the structure is in another network (parts_move it first).");
            structureId = st.ObjectId;
        }
        else
        {
            var picker = new GravityParts.Picker(c.Tr, GravityParts.ListFor(c.Civ, c.Db, c.Tr, net, StrParam(p, "parts_list")));
            var part = picker.Pick(CivilDb.DomainType.Structure, p, "pipe_break structure");
            structureId = ObjectId.Null;
            net.AddStructure(part.FamilyId, part.SizeId, point, Rad(DblParam(p, "rotation_deg") ?? 0), ref structureId, false);
            var st = (CivilDb.Structure)c.Tr.GetObject(structureId, OpenMode.ForWrite);
            if (StrParam(p, "structure_name") is string sn) st.Name = sn;
            created = true;
            partLabel = part.Label;
        }
        ObjectId newPipeId = ObjectId.Null;
        net.BreakPipe(pipe.ObjectId, point, structureId, ref newPipeId);
        if (newPipeId.IsNull) throw new InvalidOperationException("pipe_break: Civil 3D did not split the pipe.");
        var newPipe = (CivilDb.Pipe)c.Tr.GetObject(newPipeId, OpenMode.ForWrite);
        if (StrParam(p, "new_pipe_name") is string pn) newPipe.Name = pn;
        var st2 = (CivilDb.Structure)c.Tr.GetObject(structureId, OpenMode.ForRead);
        return new
        {
            at = P3(point), structure = new { name = st2.Name, handle = H(structureId), created, part = partLabel },
            pipe = PipeFacts(c.Tr, (CivilDb.Pipe)c.Tr.GetObject(pipe.ObjectId, OpenMode.ForRead)),
            newPipe = PipeFacts(c.Tr, newPipe),
        };
    }

    /// <summary>Move pipes and structures to another network (same drawing).</summary>
    private static object PartsMove(EditContext c, JsonElement p)
    {
        var to = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "to_network"), OpenMode.ForWrite);
        string? from = StrParam(p, "network");
        var ids = new ObjectIdCollection();
        var names = new List<object>();
        foreach (var (key, pipe) in new[] { ("pipes", true), ("structures", false) })
        {
            if (!Has(p, key)) continue;
            foreach (var e in ArrParam(p, key, 5000))
            {
                string text = e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ArgumentException($"parts_move: {key} holds names or handles.");
                var id = PartId(c.Tr, c.Db, c.Civ, text, from, pipe, $"parts_move {key}");
                var part = (CivilDb.Part)c.Tr.GetObject(id, OpenMode.ForRead);
                if (part.NetworkId == to.ObjectId) { c.Warn($"{part.Name} is already in '{to.Name}'."); continue; }
                ids.Add(id);
                names.Add(new { kind = pipe ? "pipe" : "structure", name = part.Name, handle = H(id), from = NameOf(c.Tr, () => part.NetworkId) });
            }
        }
        if (ids.Count == 0 && names.Count == 0) throw new ArgumentException("parts_move: give pipes and/or structures.");
        if (ids.Count > 0)
        {
            var source = (CivilDb.Network)c.Tr.GetObject(((CivilDb.Part)c.Tr.GetObject(ids[0], OpenMode.ForRead)).NetworkId, OpenMode.ForWrite);
            source.MoveParts(ids, to.ObjectId);
        }
        int moved = ids.Cast<ObjectId>().Count(id => ((CivilDb.Part)c.Tr.GetObject(id, OpenMode.ForRead)).NetworkId == to.ObjectId);
        if (moved != ids.Count) throw new InvalidOperationException($"parts_move: only {moved} of {ids.Count} parts reached '{to.Name}'.");
        return new { toNetwork = to.Name, moved, parts = names };
    }

    /// <summary>Connect one end of a pipe to a structure, or disconnect it.</summary>
    private static object PipeConnect(EditContext c, JsonElement p)
    {
        var pipe = Pipe(c, p);
        var end = End(p);
        bool disconnect = BoolParam(p, "disconnect") ?? false;
        if (disconnect == Has(p, "structure"))
            throw new ArgumentException("pipe_connect: give 'structure' to connect, or disconnect: true.");
        var before = end == CivilDb.ConnectorPositionType.Start ? pipe.StartStructureId : pipe.EndStructureId;
        if (disconnect)
        {
            if (before.IsNull) c.Warn($"The {end.ToString().ToLowerInvariant()} of '{pipe.Name}' was not connected.");
            else pipe.Disconnect(end);
        }
        else
        {
            var st = Structure(c, p);
            if (st.NetworkId != pipe.NetworkId) throw new ArgumentException("pipe_connect: the structure is in another network (parts_move it first).");
            pipe.ConnectToStructure(end, st.ObjectId, BoolParam(p, "force") ?? false);
        }
        var after = end == CivilDb.ConnectorPositionType.Start ? pipe.StartStructureId : pipe.EndStructureId;
        if (disconnect ? !after.IsNull : after.IsNull)
            throw new InvalidOperationException($"pipe_connect: Civil 3D did not {(disconnect ? "disconnect" : "connect")} the pipe end.");
        return new { end = end.ToString().ToLowerInvariant(), before = NameOf(c.Tr, () => before), pipe = PipeFacts(c.Tr, pipe) };
    }

    /// <summary>Set a pipe's slope holding its start or end invert.</summary>
    private static object PipeRegrade(EditContext c, JsonElement p)
    {
        var pipe = Pipe(c, p);
        double slope = NeedDbl(p, "slope");
        if (Math.Abs(slope) > 1) throw new ArgumentException("pipe_regrade: slope is m/m (0.005 = 0.5 %), at most 1.");
        string hold = Choice(p, "hold", "start", "start", "end");
        var before = PipeFacts(c.Tr, pipe);
        if (hold == "start") pipe.SetSlopeHoldStart(slope); else pipe.SetSlopeHoldEnd(slope);
        return new { hold, requestedSlope = slope, before, after = PipeFacts(c.Tr, pipe) };
    }

    /// <summary>Pull a pipe end to the centre, inner wall or outer wall of its structure.</summary>
    private static object PipeEndAdjust(EditContext c, JsonElement p)
    {
        var pipe = Pipe(c, p);
        var end = End(p);
        var where = Choice(p, "location", "inner_wall", "center", "inner_wall", "outer_wall") switch
        {
            "center" => CivilDb.PipeEndLocation.StructureCenter,
            "outer_wall" => CivilDb.PipeEndLocation.StructureOuterWall,
            _ => CivilDb.PipeEndLocation.StructureInnerWall,
        };
        var before = PipeFacts(c.Tr, pipe);
        bool ok = pipe.AdjustEndpoint(end, where, DblParam(p, "offset") ?? 0);
        if (!ok) throw new InvalidOperationException("pipe_end_adjust: Civil 3D did not move the end (is it connected to a structure?).");
        return new { end = end.ToString().ToLowerInvariant(), location = where.ToString(), before, after = PipeFacts(c.Tr, pipe) };
    }

    /// <summary>Resize structures to the depth of their connected pipes (Civil 3D's own rule).</summary>
    private static object StructureResize(EditContext c, JsonElement p)
    {
        var results = new List<object>();
        var list = Has(p, "structures") ? ArrParam(p, "structures", 2000).Select(e => e.GetString() ?? "").ToList() : new List<string> { NeedStr(p, "structure") };
        foreach (var text in list)
        {
            var st = (CivilDb.Structure)c.Tr.GetObject(PartId(c.Tr, c.Db, c.Civ, text, StrParam(p, "network"), false, "structure_resize"), OpenMode.ForWrite);
            double rim = st.RimElevation, sump = st.SumpElevation;
            bool ok = st.ResizeByPipeDepths();
            results.Add(new { name = st.Name, handle = H(st.ObjectId), resized = ok, before = new { rim = R3(rim), sump = R3(sump) },
                              after = new { rim = R3(st.RimElevation), sump = R3(st.SumpElevation) } });
        }
        return new { structures = results };
    }

    private static object PipeFlowDirection(EditContext c, JsonElement p)
    {
        var method = Choice(p, "method", "", "start_to_end", "end_to_start", "by_slope", "bidirectional") switch
        {
            "start_to_end" => CivilDb.FlowDirectionMethodType.StartToEnd,
            "end_to_start" => CivilDb.FlowDirectionMethodType.EndToStart,
            "by_slope" => CivilDb.FlowDirectionMethodType.BySlope,
            _ => CivilDb.FlowDirectionMethodType.Bidirectional,
        };
        var list = Has(p, "pipes") ? ArrParam(p, "pipes", 5000).Select(e => e.GetString() ?? "").ToList() : new List<string> { NeedStr(p, "pipe") };
        var rows = new List<object>();
        foreach (var text in list)
        {
            var pipe = (CivilDb.Pipe)c.Tr.GetObject(PartId(c.Tr, c.Db, c.Civ, text, StrParam(p, "network"), true, "pipe_flow_direction"), OpenMode.ForWrite);
            pipe.FlowDirectionMethod = method;
            rows.Add(new { name = pipe.Name, method = method.ToString(), flowDirection = pipe.FlowDirection.ToString() });
        }
        return new { pipes = rows };
    }

    // ---------------------------------------------------------------- read

    /// <summary>network_trace: the shortest chain of parts between two parts of a gravity network.</summary>
    public static object NetworkTrace(JsonElement p) => WithRead<object>((db, tr) =>
    {
        string? network = StrParam(p, "network");
        ObjectId Find(string key)
        {
            string text = NeedStr(p, key);
            try { return PartId(tr, db, Civ, text, network, false, key); }
            catch (ArgumentException) { return PartId(tr, db, Civ, text, network, true, key); }
        }
        var from = Find("from");
        var to = Find("to");
        double length = 0;
        var ids = CivilDb.Network.FindShortestNetworkPath(from, to, ref length);
        if (ids == null || ids.Count == 0)
            return new { from = NeedStr(p, "from"), to = NeedStr(p, "to"), connected = false, parts = new List<object>(),
                         note = "No path: the two parts are not connected through pipes and structures." };
        // Civil 3D returns the path without the end part: add it, so the chain reads from 'from' to 'to'.
        var chain = ids.Cast<ObjectId>().ToList();
        if (chain.Count > 0 && chain[0] != from && !chain.Contains(from)) chain.Insert(0, from);
        if (!chain.Contains(to)) chain.Add(to);
        var parts = chain.Select(id =>
        {
            var o = tr.GetObject(id, OpenMode.ForRead);
            return o switch
            {
                CivilDb.Pipe pp => (object)new { kind = "pipe", name = pp.Name, handle = H(id), length2d = Safe(() => R3(pp.Length2DCenterToCenter)),
                                                  slope = Safe(() => Math.Round(pp.Slope, 6)), flowDirection = Safe(() => pp.FlowDirection.ToString()) },
                CivilDb.Structure s => new { kind = "structure", name = s.Name, handle = H(id), rim = Safe(() => R3(s.RimElevation)), sump = Safe(() => R3(s.SumpElevation)) },
                _ => new { kind = o.GetType().Name, handle = H(id) },
            };
        }).ToList();
        return new { from = NeedStr(p, "from"), to = NeedStr(p, "to"), connected = true, length = R3(length), count = parts.Count, parts };
    });
}
