using System.Text.RegularExpressions;

namespace C3dMcpBridge;

/// <summary>Pressure parts lists: listing them, and choosing ONE part size by its description for an edit.</summary>
internal static class PressureParts
{
    public sealed record Entry(CivilStyles.PressurePartList List, string? Name);

    /// <summary>Every pressure parts list of the drawing, with its name.</summary>
    public static List<Entry> Lists(Database db, Transaction tr) =>
        CivilStyles.PressurePartList.GetAvailablePartLists(db).Cast<ObjectId>()
            .Select(id => (CivilStyles.PressurePartList)tr.GetObject(id, OpenMode.ForRead))
            .Select(list => new Entry(list, Styles.NameOf(list)))
            .ToList();

    public static CivilStyles.PressurePartList FindList(Database db, Transaction tr, string nameOrHandle)
    {
        var lists = Lists(db, tr);
        var hit = lists.FirstOrDefault(l => SameName(l.Name, nameOrHandle)) ?? lists.FirstOrDefault(l => SameName(H(l.List.ObjectId), nameOrHandle));
        return hit?.List ?? throw new ArgumentException(
            $"Pressure parts list '{nameOrHandle}' not found. Available: {string.Join(", ", lists.Select(l => l.Name ?? H(l.List.ObjectId)))}. Use list_pressure_part_sizes.");
    }

    /// <summary>The list named in the request, otherwise the network's own.</summary>
    public static CivilStyles.PressurePartList ListFor(Database db, Transaction tr, CivilDb.PressurePipeNetwork net, string? nameOrHandle)
    {
        if (nameOrHandle != null) return FindList(db, tr, nameOrHandle);
        if (net.PartsListId.IsNull) throw new ArgumentException($"Pressure network '{net.Name}' has no parts list; pass parts_list.");
        return (CivilStyles.PressurePartList)tr.GetObject(net.PartsListId, OpenMode.ForRead);
    }

    public static string Describe(CivilStyles.PressurePartSize s)
    {
        try { return s.Description ?? ""; } catch { return ""; }
    }

    /// <summary>type: Pipe, Fitting, Appurtenance (whole domains) or a part type such as Elbow, Tee, Reducer, Valve.</summary>
    public static List<CivilStyles.PressurePartSize> Candidates(CivilStyles.PressurePartList pl, string type)
    {
        if (Enum.TryParse<CivilDb.PressurePartDomainType>(type, true, out var domain) && !int.TryParse(type, out _)) return pl.GetParts(domain);
        if (Enum.TryParse<CivilDb.PressurePartType>(type, true, out var pt) && !int.TryParse(type, out _)) return pl.GetParts(pt);
        throw new ArgumentException($"Unknown part type '{type}'. Use Pipe, Fitting, Appurtenance or one of: {string.Join(", ", Enum.GetNames<CivilDb.PressurePartType>())}.");
    }

    /// <summary>Exactly one part whose description contains the size as a whole number and every word of 'match'.</summary>
    public static CivilStyles.PressurePartSize Resolve(IReadOnlyList<CivilStyles.PressurePartSize> candidates, string size, string match, string what)
    {
        var tests = new List<Regex>();
        if (!string.IsNullOrWhiteSpace(size))
            tests.Add(new Regex($@"(?<![\d.]){Regex.Escape(size.Trim())}(?![\d.])", RegexOptions.IgnoreCase));   // 150 must not match 1500 or 0.150
        foreach (var word in match.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            tests.Add(new Regex(Regex.Escape(word), RegexOptions.IgnoreCase));
        var hits = candidates.Where(s => { var d = Describe(s); return tests.All(r => r.IsMatch(d)); }).ToList();
        if (hits.Count == 1) return hits[0];
        throw new ArgumentException(hits.Count == 0
            ? $"{what}: no part matches size='{size}' match='{match}' ({candidates.Count} parts checked). Use list_pressure_part_sizes."
            : $"{what}: {hits.Count} parts match size='{size}' match='{match}'; refine 'match'. First: {string.Join(" | ", hits.Take(8).Select(Describe))}");
    }

    /// <summary>
    /// Chooses parts for one batch operation. A network is usually built from a handful of distinct parts, so the
    /// candidates of each part type are read once and each distinct request (type + size + match) is resolved once.
    /// </summary>
    public sealed class Picker(CivilStyles.PressurePartList list)
    {
        private readonly Dictionary<string, List<CivilStyles.PressurePartSize>> _candidates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CivilStyles.PressurePartSize> _chosen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>spec supplies 'size' and 'match'.</summary>
        public CivilStyles.PressurePartSize Pick(string type, JsonElement spec, string what)
        {
            string size = SizeParam(spec, "size"), match = StrParam(spec, "match") ?? "";
            string key = $"{type}|{size}|{match}";
            if (_chosen.TryGetValue(key, out var part)) return part;
            if (!_candidates.TryGetValue(type, out var candidates)) _candidates[type] = candidates = Candidates(list, type);
            return _chosen[key] = Resolve(candidates, size, match, what);
        }
    }
}
