namespace C3dMcpBridge;

/// <summary>
/// Writing data onto existing Civil 3D objects (roadmap #70): user-defined property values of COGO points and parcels,
/// parcel number / address / tax ID / style, and point group actions (lock, unlock, update, delete its points).
/// </summary>
internal static class DataOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["udp_set"] = UdpSet;
        ops["parcel_set"] = ParcelSet;
        ops["point_group_action"] = PointGroupAction;
    }

    private static CivilDb.UDP FindUdp(EditContext c, string kind, string? classification, string property)
    {
        var classes = kind == "parcel" ? c.Civ.ParcelUDPClassifications : c.Civ.PointUDPClassifications;
        var hits = new List<CivilDb.UDP>();
        foreach (CivilDb.UDPClassification cls in classes)
        {
            if (classification != null && !SameName(cls.Name, classification)) continue;
            foreach (CivilDb.UDP u in cls.UDPs) if (SameName(u.Name, property)) hits.Add(u);
        }
        if (hits.Count == 1) return hits[0];
        if (hits.Count == 0)
            throw new ArgumentException($"udp_set: no {kind} property '{property}'" + (classification != null ? $" in classification '{classification}'" : "") + ". Use list_user_properties (or udp_create).");
        throw new ArgumentException($"udp_set: {hits.Count} classifications have a {kind} property '{property}'; give 'classification'.");
    }

    /// <summary>Convert a JSON value to what the property's type takes; refuses anything that would be changed silently.</summary>
    private static object Value(CivilDb.UDP u, JsonElement v, string what)
    {
        switch (u)
        {
            case CivilDb.UDPDouble:
                if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
                break;
            case CivilDb.UDPInteger:
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
                break;
            case CivilDb.UDPBoolean:
                if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
                break;
            case CivilDb.UDPEnumeration e:
                if (v.ValueKind == JsonValueKind.String)
                {
                    var options = e.GetEnumValues();
                    var match = options.FirstOrDefault(o => SameName(o, v.GetString()!));
                    if (match == null) throw new ArgumentException($"{what}: '{v.GetString()}' is not one of {string.Join(", ", options)}.");
                    return match;
                }
                break;
            case CivilDb.UDPString:
                if (v.ValueKind == JsonValueKind.String) return v.GetString()!;
                break;
        }
        throw new ArgumentException($"{what}: property '{u.Name}' is {u.GetType().Name.Replace("UDP", "").ToLowerInvariant()}; {v.ValueKind} value {v} does not fit.");
    }

    private static void SetOn(CivilDb.CogoPoint pt, CivilDb.UDP u, object val)
    {
        switch (u)
        {
            case CivilDb.UDPDouble d: pt.SetUDPValue(d, (double)val); break;
            case CivilDb.UDPInteger i: pt.SetUDPValue(i, (int)val); break;
            case CivilDb.UDPBoolean b: pt.SetUDPValue(b, (bool)val); break;
            case CivilDb.UDPEnumeration e: pt.SetUDPValue(e, (string)val); break;
            case CivilDb.UDPString s: pt.SetUDPValue(s, (string)val); break;
        }
    }

    private static void SetOn(CivilDb.Parcel pc, CivilDb.UDP u, object val)
    {
        switch (u)
        {
            case CivilDb.UDPDouble d: pc.SetUDPValue(d, (double)val); break;
            case CivilDb.UDPInteger i: pc.SetUDPValue(i, (int)val); break;
            case CivilDb.UDPBoolean b: pc.SetUDPValue(b, (bool)val); break;
            case CivilDb.UDPEnumeration e: pc.SetUDPValue(e, (string)val); break;
            case CivilDb.UDPString s: pc.SetUDPValue(s, (string)val); break;
        }
    }

    private static List<CivilDb.Parcel> Parcels(EditContext c, IEnumerable<string> keys, string what)
    {
        var all = new List<CivilDb.Parcel>();
        foreach (ObjectId sid in c.Civ.GetSiteIds())
        {
            var site = (CivilDb.Site)c.Tr.GetObject(sid, OpenMode.ForRead);
            foreach (ObjectId pid in site.GetParcelIds()) all.Add((CivilDb.Parcel)c.Tr.GetObject(pid, OpenMode.ForRead));
        }
        var result = new List<CivilDb.Parcel>();
        foreach (var key in keys)
        {
            var hits = all.Where(pc => SameName(pc.Name, key) || SameName(H(pc.ObjectId), key)).ToList();
            if (hits.Count != 1)
                throw new ArgumentException(hits.Count == 0 ? $"{what}: no parcel '{key}' (list_parcels)." : $"{what}: {hits.Count} parcels are named '{key}'; use the handle.");
            hits[0].UpgradeOpen();
            result.Add(hits[0]);
        }
        return result;
    }

    private static CivilDb.CogoPoint Point(EditContext c, uint number, string what)
    {
        foreach (ObjectId id in c.Civ.CogoPoints)
        {
            var pt = (CivilDb.CogoPoint)c.Tr.GetObject(id, OpenMode.ForRead);
            if (pt.PointNumber == number) { pt.UpgradeOpen(); return pt; }
        }
        throw new ArgumentException($"{what}: no COGO point number {number}.");
    }

    /// <summary>Write a user-defined property value on COGO points or parcels: one value for all, or one per item.</summary>
    private static object UdpSet(EditContext c, JsonElement p)
    {
        string kind = Choice(p, "kind", "point", "point", "parcel");
        var udp = FindUdp(c, kind, StrParam(p, "classification"), NeedStr(p, "property"));
        var items = new List<(string key, JsonElement value)>();
        if (Has(p, "values"))
        {
            // {"values": {"101": 12.5, "102": 13.0}} or for parcels {"values": {"LOT 1": "A"}}
            var map = p.GetProperty("values");
            if (map.ValueKind != JsonValueKind.Object) throw new ArgumentException("udp_set: values is an object {point number or parcel name: value}.");
            foreach (var kv in map.EnumerateObject()) items.Add((kv.Name, kv.Value));
        }
        else
        {
            if (!p.TryGetProperty("value", out var v)) throw new ArgumentException("udp_set: give value (with points / parcels) or values.");
            string listKey = kind == "parcel" ? "parcels" : "points";
            foreach (var e in ArrParam(p, listKey, 20000))
                items.Add((e.ValueKind == JsonValueKind.Number ? e.GetRawText() : e.GetString() ?? "", v));
        }
        if (items.Count == 0) throw new ArgumentException("udp_set: nothing to set.");
        var rows = new List<object>();
        foreach (var (key, raw) in items)
        {
            object val = Value(udp, raw, $"udp_set {key}");
            if (kind == "parcel")
            {
                var pc = Parcels(c, new[] { key }, "udp_set")[0];
                SetOn(pc, udp, val);
                rows.Add(new { parcel = pc.Name, value = val });
            }
            else
            {
                if (!uint.TryParse(key, out uint n)) throw new ArgumentException($"udp_set: '{key}' is not a point number.");
                var pt = Point(c, n, "udp_set");
                SetOn(pt, udp, val);
                rows.Add(new { point = n, value = val });
            }
        }
        return new { kind, property = udp.Name, classification = Safe(() => udp.ClassificationName), count = rows.Count, set = rows };
    }

    private static object ParcelSet(EditContext c, JsonElement p)
    {
        var pc = Parcels(c, new[] { NeedStr(p, "parcel") }, "parcel_set")[0];
        var changed = new List<string>();
        if (Has(p, "number")) { pc.Number = WholeParam(p, "number", 1, 1, int.MaxValue); changed.Add("number"); }
        if (StrParam(p, "address") is string addr) { pc.Address = addr; changed.Add("address"); }
        if (Has(p, "tax_id")) { pc.TaxId = WholeParam(p, "tax_id", 0, 0, int.MaxValue); changed.Add("tax_id"); }
        if (StrParam(p, "style") is string style) { pc.StyleId = Styles.Id(Styles.Collection(c.Civ, "parcel"), style, "parcel style"); changed.Add("style"); }
        if (changed.Count == 0) throw new ArgumentException("parcel_set: give number, address, tax_id and/or style.");
        return new { parcel = pc.Name, handle = H(pc.ObjectId), changed, number = pc.Number, address = pc.Address, taxId = pc.TaxId,
                     style = Styles.NameOf(c.Tr, pc.StyleId) };
    }

    private static object PointGroupAction(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "group");
        CivilDb.PointGroup? g = null;
        foreach (ObjectId id in c.Civ.PointGroups)
        {
            var pg = (CivilDb.PointGroup)c.Tr.GetObject(id, OpenMode.ForRead);
            if (SameName(pg.Name, name)) g = pg;
        }
        if (g == null) throw new ArgumentException($"point_group_action: no point group '{name}' (list_point_groups).");
        g.UpgradeOpen();
        string action = Choice(p, "action", "", "lock", "unlock", "update", "delete_points");
        int before = (int)g.PointsCount;
        switch (action)
        {
            case "lock": g.LockPoints(); break;
            case "unlock": g.UnlockPoints(); break;
            case "update": g.Update(); break;
            case "delete_points":
                if (!(BoolParam(p, "confirm") ?? false))
                    throw new ArgumentException("point_group_action: delete_points erases the group's COGO points from the drawing; add confirm: true.");
                g.DeletePoints();
                break;
        }
        return new { group = g.Name, action, pointsBefore = before, pointsAfter = (int)g.PointsCount };
    }
}
