namespace C3dMcpBridge;

/// <summary>Style names (read only).</summary>
internal static class StyleRead
{
    public static object ListStyles(JsonElement p)
    {
        string kind = NeedStr(p, "kind");
        return WithRead((db, tr) =>
        {
            var rows = Styles.Collection(Civ, kind).Cast<ObjectId>()
                .Select(id => new { name = Styles.NameOf(tr.GetObject(id, OpenMode.ForRead)), handle = H(id) }).ToList();
            return new
            {
                kind, count = rows.Count, styles = rows,
                note = "An edit operation that gives no style name uses 'Standard' when the drawing has it, otherwise the first style listed.",
            };
        });
    }

    /// <summary>What a style contains: for a label style its components and their text; for a band style its title and size;
    /// for a profile view band set its bands. Used to find out what a template's styles show before using them.</summary>
    public static object GetStyle(JsonElement p)
    {
        string kind = NeedStr(p, "kind"), name = NeedStr(p, "name");
        return WithRead<object>((db, tr) =>
        {
            var id = Styles.Id(Styles.Collection(Civ, kind), name, kind + " style");
            var style = tr.GetObject(id, OpenMode.ForRead);
            switch (style)
            {
                case CivilStyles.LabelStyle label:
                    var parts = new List<object>();
                    foreach (var type in Enum.GetValues<CivilStyles.LabelStyleComponentType>())
                    {
                        if (!label.IsSupportedComponent(type)) continue;
                        foreach (ObjectId cid in label.GetComponents(type))
                        {
                            var comp = tr.GetObject(cid, OpenMode.ForRead);
                            parts.Add(comp is CivilStyles.LabelStyleTextComponent t
                                ? new { type = type.ToString(), name = t.General.Name, contents = Safe(() => t.Text.Contents.Value),
                                        height = Safe(() => t.Text.Height.Value), attachment = Safe(() => t.Text.Attachment.Value.ToString()),
                                        xOffset = Safe(() => t.Text.XOffset.Value),
                                        yOffset = Safe(() => t.Text.YOffset.Value), visible = Safe(() => t.General.Visible.Value) }
                                : new { type = type.ToString(), name = Styles.NameOf(comp) ?? "", contents = (object?)null, height = (object?)null,
                                        attachment = (object?)null, xOffset = (object?)null, yOffset = (object?)null, visible = (object?)null });
                        }
                    }
                    return new { kind, name, styleType = style.GetType().Name, components = parts, children = Safe(() => label.ChildrenCount) };
                case CivilStyles.BandStyle band:
                    return new
                    {
                        kind, name, bandType = band.BandType.ToString(), title = Safe(() => band.Text), height = Safe(() => band.BandHeight),
                        titleBoxWidth = Safe(() => band.TextBoxWidth), textHeight = Safe(() => band.TextHeight),
                        note = "The labels inside a band style (what it writes at each station) cannot be read or edited through the .NET API; check them in Civil 3D's band style dialog.",
                    };
                case CivilStyles.ProfileViewBandSetStyle set:
                    object Items(CivilStyles.ProfileViewBandSetItemCollection items) =>
                        Enumerable.Range(0, items.Count).Select(i => items[i]).Select(b => new
                        {
                            bandType = b.BandType.ToString(), style = Styles.NameOf(tr, b.BandStyleId), gap = b.Gap,
                        }).ToList();
                    return new { kind, name, bottom = Items(set.GetBottomBandSetItems()), top = Items(set.GetTopBandSetItems()) };
                default:
                    return new { kind, name, styleType = style.GetType().Name };
            }
        });
    }
}
