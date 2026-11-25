namespace C3dMcpBridge;

/// <summary>Gravity parts lists: listing families and sizes, and choosing ONE part (family + size) for an edit.</summary>
internal static class GravityParts
{
    public sealed record Size(int Index, ObjectId Id, double? Dimension, string Units, string Label);

    /// <summary>A chosen part: what Network.AddLinePipe / AddStructure need, plus a label for the reply.</summary>
    public sealed record Choice(ObjectId FamilyId, ObjectId SizeId, string Label);

    public static CivilStyles.PartsList FindList(CivilDoc civ, Database db, Transaction tr, string nameOrHandle)
    {
        ObjectId id = ObjectId.Null;
        try { id = civ.Styles.PartsListSet[nameOrHandle]; } catch { /* not a name */ }
        if (id.IsNull)
        {
            try { id = IdFromHandle(db, nameOrHandle); } catch { /* not a handle either */ }
        }
        if (!id.IsNull && tr.GetObject(id, OpenMode.ForRead) is CivilStyles.PartsList pl) return pl;
        throw new ArgumentException($"Gravity parts list '{nameOrHandle}' not found. Use list_gravity_parts.");
    }

    /// <summary>The list named in the request, otherwise the network's own.</summary>
    public static CivilStyles.PartsList ListFor(CivilDoc civ, Database db, Transaction tr, CivilDb.Network net, string? nameOrHandle)
    {
        if (nameOrHandle != null) return FindList(civ, db, tr, nameOrHandle);
        if (net.PartsListId.IsNull) throw new ArgumentException($"Network '{net.Name}' has no parts list; pass parts_list.");
        return (CivilStyles.PartsList)tr.GetObject(net.PartsListId, OpenMode.ForRead);
    }

    // The size-table fields that hold "the" dimension of a part, in order of preference.
    public static CivilDb.PartContextType[] DimensionFields(CivilDb.DomainType domain) =>
        domain == CivilDb.DomainType.Pipe ? PipeDims : StructDims;

    private static readonly CivilDb.PartContextType[] PipeDims =
        { CivilDb.PartContextType.PipeInnerDiameter, CivilDb.PartContextType.PipeInnerWidth };
    private static readonly CivilDb.PartContextType[] StructDims =
    {
        CivilDb.PartContextType.StructInnerDiameter, CivilDb.PartContextType.StructDiameter,
        CivilDb.PartContextType.StructInnerWidth, CivilDb.PartContextType.StructWidth,
    };

    public static List<Size> Sizes(Transaction tr, CivilStyles.PartFamily fam)
    {
        var dims = fam.Domain == CivilDb.DomainType.Pipe ? PipeDims : StructDims;
        var sizes = new List<Size>();
        for (int i = 0; i < fam.PartSizeCount; i++)
        {
            ObjectId sid = fam[i];
            double? dim = null;
            string units = "", label = $"size {i}";
            try
            {
                var rec = ((CivilStyles.PartSize)tr.GetObject(sid, OpenMode.ForRead)).SizeDataRecord;
                foreach (var ctx in dims)
                {
                    try
                    {
                        var f = rec.GetDataFieldBy(ctx);
                        if (f?.Value == null) continue;
                        dim = Convert.ToDouble(f.Value, System.Globalization.CultureInfo.InvariantCulture);
                        units = f.Units ?? "";
                        label = $"{f.Name} = {dim:0.####} {units}".Trim();
                        break;
                    }
                    catch { /* this field is not part of the record */ }
                }
            }
            catch { /* unreadable size: keep the index so it can still be chosen by size_index */ }
            sizes.Add(new Size(i, sid, dim, units, label));
        }
        return sizes;
    }

    public static IEnumerable<CivilStyles.PartFamily> FamiliesOf(Transaction tr, CivilStyles.PartsList list, CivilDb.DomainType domain) =>
        list.GetPartFamilyIdsByDomain(domain).Cast<ObjectId>().Select(id => (CivilStyles.PartFamily)tr.GetObject(id, OpenMode.ForRead));

    /// <summary>Rows for list_gravity_parts.</summary>
    public static List<object> Families(Transaction tr, CivilStyles.PartsList list, CivilDb.DomainType domain) =>
        FamiliesOf(tr, list, domain).Select(fam => (object)new
        {
            family = fam.Description,
            partType = Safe(() => fam.PartType.ToString()),
            sizes = Sizes(tr, fam).Select(s => new { size_index = s.Index, s.Label, inner_diameter = s.Dimension, s.Units }).ToList(),
        }).ToList();

    /// <summary>
    /// Chooses parts for one batch operation. A network is usually built from a handful of distinct parts, so each
    /// distinct request (family + size) is resolved against the parts list once and then reused.
    /// </summary>
    public sealed class Picker(Transaction tr, CivilStyles.PartsList list)
    {
        private readonly Dictionary<string, Choice> _chosen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Exactly one family whose description contains every word of 'family', then one size of it.</summary>
        public Choice Pick(CivilDb.DomainType domain, JsonElement spec, string what)
        {
            string family = NeedStr(spec, "family");
            string key = $"{domain}|{family}|{SizeParam(spec, "size_index")}|{SizeParam(spec, "inner_diameter")}";
            if (!_chosen.TryGetValue(key, out var choice)) _chosen[key] = choice = Resolve(domain, family, spec, what);
            return choice;
        }

        private Choice Resolve(CivilDb.DomainType domain, string family, JsonElement spec, string what)
        {
            var all = FamiliesOf(tr, list, domain).ToList();
            var words = family.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            // An exact description wins; otherwise every word must occur in it.
            var hits = all.Where(f => SameName(f.Description, family)).Take(1).ToList();
            if (hits.Count == 0)
                hits = all.Where(f => words.All(w => (f.Description ?? "").Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hits.Count != 1)
                throw new ArgumentException(hits.Count == 0
                    ? $"{what}: no {domain} family matches '{family}'. Use list_gravity_parts."
                    : $"{what}: {hits.Count} families match '{family}' ({string.Join(" | ", hits.Take(6).Select(h => h.Description))}); be more specific.");

            var fam = hits[0];
            var sizes = Sizes(tr, fam);
            if (sizes.Count == 0) throw new ArgumentException($"{what}: family '{fam.Description}' has no sizes in this parts list.");
            Size size;
            if (Has(spec, "size_index"))
            {
                int i = IndexParam(spec, "size_index", -1, int.MaxValue);
                size = sizes.FirstOrDefault(s => s.Index == i)
                       ?? throw new ArgumentException($"{what}: size_index {i} is out of range (0..{sizes.Count - 1}).");
            }
            else if (DblParam(spec, "inner_diameter") is double want)
            {
                // Nearest catalogue size, accepted within 1 %.
                var best = sizes.Where(s => s.Dimension.HasValue).MinBy(s => Math.Abs(s.Dimension!.Value - want));
                if (best == null || Math.Abs(best.Dimension!.Value - want) > Math.Max(1e-9, Math.Abs(want) * 0.01))
                    throw new ArgumentException($"{what}: no size of '{fam.Description}' has inner_diameter {want}. Available: {string.Join(", ", sizes.Select(s => s.Label))}");
                size = best;
            }
            else if (sizes.Count == 1) size = sizes[0];
            else throw new ArgumentException($"{what}: give inner_diameter or size_index. Available: {string.Join(", ", sizes.Select(s => $"[{s.Index}] {s.Label}"))}");

            return new Choice(fam.ObjectId, size.Id, $"{fam.Description} / {size.Label}");
        }
    }
}
