namespace C3dMcpBridge;

/// <summary>Presentation: label styles, the styles and labels of a network's parts, alignment label sets, profile view bands.</summary>
internal static class StyleOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["label_style_create"] = LabelStyleCreate;
        ops["network_styles"] = NetworkStyles;
        ops["alignment_label_set"] = AlignmentLabelSet;
        ops["profile_view_set"] = ProfileViewSet;
        ops["profile_view_bands"] = ProfileViewBands;
        ops["band_style_set"] = BandStyleSet;
        ops["style_copy"] = StyleCopy;
    }

    /// <summary>Copy a Civil 3D style (any kind list_styles knows) under a new name, so the copy can be changed and the original kept.</summary>
    private static object StyleCopy(EditContext c, JsonElement p)
    {
        string kind = NeedStr(p, "kind"), name = NeedStr(p, "name"), newName = NeedStr(p, "new_name");
        if (SameName(name, newName)) throw new ArgumentException("style_copy: new_name must differ from name.");
        var styles = Styles.Collection(c.Civ, kind);
        var source = (CivilStyles.StyleBase)c.Tr.GetObject(Styles.Id(styles, name, kind + " style"), OpenMode.ForRead);
        string ifExists = Choice(p, "if_exists", "fail", "fail", "skip");
        ObjectId existing = ObjectId.Null;
        try { existing = styles[newName]; } catch { /* free name */ }
        if (!existing.IsNull)
        {
            if (ifExists == "skip") return new { kind, name, newName, copied = false, handle = H(existing), note = "a style with new_name already exists" };
            throw new ArgumentException($"style_copy: a {kind} style '{newName}' already exists (if_exists: 'skip').");
        }
        var id = source.CopyAsSibling(newName);
        if (StrParam(p, "description") is string d) ((CivilStyles.StyleBase)c.Tr.GetObject(id, OpenMode.ForWrite)).Description = d;
        return new { kind, name, newName, copied = true, handle = H(id) };
    }

    /// <summary>The title and size of a band style. What the band writes at each station cannot be changed through the API.</summary>
    private static object BandStyleSet(EditContext c, JsonElement p)
    {
        string kind = NeedStr(p, "kind"), name = NeedStr(p, "name");
        if (Styles.BandTypeOf(kind) == null) throw new ArgumentException("band_style_set: kind must be band_profile_data, band_pipe_network, band_horizontal_geometry or band_vertical_geometry.");
        var band = (CivilStyles.BandStyle)c.Tr.GetObject(Styles.Id(Styles.Collection(c.Civ, kind), name, kind + " style"), OpenMode.ForWrite);
        var changed = new List<string>();
        if (StrParam(p, "title") is string t) { band.Text = t; changed.Add("title"); }
        if (PositiveParam(p, "height") is double h) { band.BandHeight = h / 1000.0; changed.Add("height"); }
        if (PositiveParam(p, "title_width") is double w) { band.TextBoxWidth = w / 1000.0; changed.Add("title width"); }
        if (PositiveParam(p, "title_text_height") is double th) { band.TextHeight = th / 1000.0; changed.Add("title text height"); }
        if (changed.Count == 0) throw new ArgumentException("band_style_set: give title, height, title_width and/or title_text_height (sizes in mm on paper).");
        return new { kind, name, changed };
    }

    // ---------------------------------------------------------------- label styles

    /// <summary>
    /// A label style made of text components. Contents use Civil 3D's property fields, e.g.
    /// "&lt;[Name(CP)]&gt;" - get_style on an existing style shows the exact field syntax to copy.
    /// </summary>
    private static object LabelStyleCreate(EditContext c, JsonElement p)
    {
        string kind = NeedStr(p, "kind"), name = NeedStr(p, "name");
        var styles = Styles.Collection(c.Civ, kind) as CivilStyles.LabelStyleCollection
                     ?? throw new ArgumentException($"label_style_create: '{kind}' is not a label style kind (pipe_label, structure_label, pressure_pipe_label, ...).");
        string ifExists = Choice(p, "if_exists", "fail", "fail", "skip", "replace");
        ObjectId existing = ObjectId.Null;
        try { existing = styles[name]; } catch { /* new name */ }
        if (!existing.IsNull)
        {
            if (ifExists == "skip") return new { kind, name, created = false, note = "already exists" };
            if (ifExists == "fail") throw new ArgumentException($"A {kind} style '{name}' already exists (if_exists: 'skip' or 'replace').");
        }

        var style = (CivilStyles.LabelStyle)c.Tr.GetObject(existing.IsNull ? styles.Add(name) : existing, OpenMode.ForWrite);
        // Start from an empty style: remove whatever text components the new or existing style carries.
        foreach (var type in new[] { CivilStyles.LabelStyleComponentType.Text, CivilStyles.LabelStyleComponentType.DirectionArrow })
        {
            if (!style.IsSupportedComponent(type)) continue;
            foreach (ObjectId cid in style.GetComponents(type))
                if (Styles.ComponentName(c.Tr.GetObject(cid, OpenMode.ForRead)) is string old) style.RemoveComponent(old);
        }

        var made = new List<string>();
        var specs = NeedArr(p, "components", 20);
        for (int i = 0; i < specs.Count; i++)
        {
            var s = specs[i];
            if (SameName(StrParam(s, "type"), "arrow"))
            {
                // A direction arrow: for pipes it points in the direction of flow.
                string aname = StrParam(s, "name") ?? $"Arrow {i + 1}";
                style.AddComponent(aname, CivilStyles.LabelStyleComponentType.DirectionArrow);
                made.Add(aname);
                continue;
            }
            string cname = StrParam(s, "name") ?? $"Text {i + 1}";
            var comp = (CivilStyles.LabelStyleTextComponent)c.Tr.GetObject(
                style.AddComponent(cname, CivilStyles.LabelStyleComponentType.Text), OpenMode.ForWrite);
            comp.Text.Contents.Value = NeedStr(s, "text");
            if (PositiveParam(s, "height") is double mm) comp.Text.Height.Value = mm / 1000.0;   // plotted size: mm on paper
            comp.Text.Attachment.Value = EnumParam(s, "attachment", Autodesk.Civil.LabelTextAttachmentType.MiddleCenter);
            if (DblParam(s, "x_offset") is double dx) comp.Text.XOffset.Value = dx / 1000.0;
            if (DblParam(s, "y_offset") is double dy) comp.Text.YOffset.Value = dy / 1000.0;
            made.Add(cname);
        }
        return new { kind, name, created = existing.IsNull, components = made };
    }

    // ---------------------------------------------------------------- network parts

    private static object NetworkStyles(EditContext c, JsonElement p)
    {
        var net = FindPipeNetwork(c.Civ, c.Tr, NeedStr(p, "network"), OpenMode.ForWrite);
        ObjectId? Style(CivilStyles.StyleCollectionBase coll, string param, string what) =>
            StrParam(p, param) is string n ? Styles.Id(coll, n, what) : null;
        var pipeStyle = Style(c.Civ.Styles.PipeStyles, "pipe_style", "pipe style");
        var structureStyle = Style(c.Civ.Styles.StructureStyles, "structure_style", "structure style");
        var pipeLabel = Style(c.Civ.Styles.LabelStyles.PipeLabelStyles.PlanProfileLabelStyles, "pipe_label_style", "pipe label style");
        var structureLabel = Style(c.Civ.Styles.LabelStyles.StructureLabelStyles.LabelStyles, "structure_label_style", "structure label style");
        ObjectId? pipeLayer = StrParam(p, "pipe_layer") is string pl ? LayerId(c.Db, c.Tr, pl) : null;
        ObjectId? structureLayer = StrParam(p, "structure_layer") is string sl ? LayerId(c.Db, c.Tr, sl) : null;
        ObjectId? labelLayer = StrParam(p, "label_layer") is string ll ? LayerId(c.Db, c.Tr, ll) : null;

        if (pipeLabel is ObjectId pls) net.PipePlanLabelStyleId = pls;
        if (structureLabel is ObjectId sls) net.StructurePlanLabelStyleId = sls;
        if (pipeLayer is ObjectId pid) net.PipePlanLayerId = pid;
        if (structureLayer is ObjectId sid) net.StructurePlanLayerId = sid;

        // A part that already has a plan label gets its label(s) restyled instead of a second label.
        var pipeLabels = pipeLabel != null ? c.LabelsByPart<CivilDb.PipeLabel>() : new();
        var structureLabels = structureLabel != null ? c.LabelsByPart<CivilDb.StructureLabel>() : new();
        int restyled = 0;
        void Restyle(List<ObjectId> existing, ObjectId style)
        {
            foreach (var lid in existing)
            {
                var label = (CivilDb.Label)c.Tr.GetObject(lid, OpenMode.ForWrite);
                label.StyleId = style;
                if (labelLayer is ObjectId lab) label.LayerId = lab;
                restyled++;
            }
        }

        int pipes = 0, structures = 0, labels = 0;
        foreach (ObjectId id in net.GetPipeIds())
        {
            var pipe = (CivilDb.Pipe)c.Tr.GetObject(id, OpenMode.ForWrite);
            if (pipeStyle is ObjectId ps) pipe.StyleId = ps;
            if (pipeLayer is ObjectId lay) pipe.LayerId = lay;
            if (pipeLabel is ObjectId rs && pipeLabels.TryGetValue(id, out var had)) Restyle(had, rs);
            else if (pipeLabel is ObjectId ls)
            {
                var label = (Entity)c.Tr.GetObject(CivilDb.PipeLabel.Create(id, 0.5, ls), OpenMode.ForWrite);
                if (labelLayer is ObjectId lab) label.LayerId = lab;
                labels++;
            }
            pipes++;
        }
        var labelOffset = new Vector3d(DblParam(p, "structure_label_dx") ?? 3.0, DblParam(p, "structure_label_dy") ?? -3.0, 0);
        foreach (ObjectId id in net.GetStructureIds())
        {
            var st = (CivilDb.Structure)c.Tr.GetObject(id, OpenMode.ForWrite);
            if (structureStyle is ObjectId ss) st.StyleId = ss;
            if (structureLayer is ObjectId lay) st.LayerId = lay;
            if (structureLabel is ObjectId rs && structureLabels.TryGetValue(id, out var had)) Restyle(had, rs);
            else if (structureLabel is ObjectId ls)
            {
                var label = (Entity)c.Tr.GetObject(CivilDb.StructureLabel.Create(id, ls, st.Location + labelOffset), OpenMode.ForWrite);
                if (labelLayer is ObjectId lab) label.LayerId = lab;
                labels++;
            }
            structures++;
        }
        return new { network = net.Name, pipes, structures, labelsAdded = labels, labelsRestyled = restyled };
    }

    // ---------------------------------------------------------------- alignments

    private static object AlignmentLabelSet(EditContext c, JsonElement p)
    {
        var names = StrList(p, "alignments", 500);
        var set = Styles.Id(c.Civ.Styles.LabelSetStyles.AlignmentLabelSetStyles, NeedStr(p, "label_set"), "alignment label set");
        ObjectId? style = StrParam(p, "style") is string sn ? Styles.Id(c.Civ.Styles.AlignmentStyles, sn, "alignment style") : null;
        ObjectId? layer = StrParam(p, "layer") is string ln ? LayerId(c.Db, c.Tr, ln) : null;
        foreach (var n in names)
        {
            var a = FindAlignment(c.Civ, c.Tr, n, OpenMode.ForWrite);
            // Replace the labels: remove the current label groups, then import the set.
            foreach (ObjectId lid in a.GetAlignmentLabelGroupIds())
                try { c.Tr.GetObject(lid, OpenMode.ForWrite).Erase(); } catch { /* label that cannot be removed */ }
            a.ImportLabelSet(set);
            if (style is ObjectId s) a.StyleId = s;
            if (layer is ObjectId l) a.LayerId = l;
        }
        return new { alignments = names.Count, labelSet = Styles.NameOf(c.Tr, set) };
    }

    // ---------------------------------------------------------------- profile views

    private static object ProfileViewSet(EditContext c, JsonElement p)
    {
        var pv = FindProfileView(c.Civ, c.Tr, NeedStr(p, "profile_view"));
        pv.UpgradeOpen();
        var changed = new List<string>();
        if (StrParam(p, "style") is string s) { pv.StyleId = Styles.Id(c.Civ.Styles.ProfileViewStyles, s, "profile view style"); changed.Add("style"); }
        if (DblParam(p, "station_start") is double s0 && DblParam(p, "station_end") is double s1)
        {
            pv.StationRangeMode = CivilDb.StationRangeType.UserSpecified;
            pv.StationStart = s0; pv.StationEnd = s1; changed.Add("stations");
        }
        if (DblParam(p, "elevation_min") is double e0 && DblParam(p, "elevation_max") is double e1)
        {
            pv.ElevationRangeMode = CivilDb.ElevationRangeType.UserSpecified;
            pv.ElevationMin = e0; pv.ElevationMax = e1; changed.Add("elevations");
        }
        if (changed.Count == 0) throw new ArgumentException("profile_view_set: give style, station_start + station_end and/or elevation_min + elevation_max.");
        return new { profileView = pv.Name, changed };
    }

    /// <summary>
    /// Replaces the bands of a profile view. Each band uses an existing band style (list_styles kind band_profile_data,
    /// band_pipe_network, ...) and its data: profile1 / profile2 for profile data bands, network for pipe network bands.
    /// </summary>
    /// <summary>Fallback for band sets Civil 3D will not import: copy each band row (style and gap) of the set.</summary>
    private static void CopyBandRows(CivilDb.ProfileView pv, CivilStyles.ProfileViewBandSetStyle set)
    {
        foreach (var (from, location) in new[] { (set.GetBottomBandSetItems(), Autodesk.Civil.BandLocationType.Bottom),
                                                 (set.GetTopBandSetItems(), Autodesk.Civil.BandLocationType.Top) })
        {
            var items = new CivilDb.ProfileViewBandItemCollection(pv.ObjectId, location);
            for (int i = 0; i < from.Count; i++) items.Add(from[i].BandStyleId);
            for (int i = 0; i < from.Count; i++) items[i].Gap = from[i].Gap;
            if (location == Autodesk.Civil.BandLocationType.Bottom) pv.Bands.SetBottomBandItems(items);
            else pv.Bands.SetTopBandItems(items);
        }
    }

    private static object ProfileViewBands(EditContext c, JsonElement p)
    {
        var pv = FindProfileView(c.Civ, c.Tr, NeedStr(p, "profile_view"));
        pv.UpgradeOpen();
        var alignment = (CivilDb.Alignment)c.Tr.GetObject(pv.AlignmentId, OpenMode.ForRead);
        var report = new List<object>();
        if (StrParam(p, "band_set") is string setName)
        {
            // all the bands of a band set, each with its data (profile data bands get the alignment's first profile)
            var setId = AlignmentOps.BandSetFor(c, setName);
            var set = (CivilStyles.ProfileViewBandSetStyle)c.Tr.GetObject(setId, OpenMode.ForRead);
            string method = "import";
            try { pv.Bands.ImportBandSetStyle(setId); }   // Civil 3D's own band-set import (keeps gaps, offsets and settings)
            catch (Exception ex)
            {
                method = "copied rows";
                c.Warn($"profile_view_bands: Civil 3D's band-set import failed ({ex.Message}); the band rows were copied instead.");
                CopyBandRows(pv, set);
            }
            // Data per row after the import, e.g. a different profile pair on the second profile data band.
            var rows = Has(p, "rows") ? ArrParam(p, "rows", 40) : new List<JsonElement>();
            foreach (var loc in new[] { Autodesk.Civil.BandLocationType.Bottom, Autodesk.Civil.BandLocationType.Top })
            {
                string where = loc == Autodesk.Civil.BandLocationType.Bottom ? "bottom" : "top";
                var mine = rows.Where(r => (StrParam(r, "location") ?? "bottom").Equals(where, StringComparison.OrdinalIgnoreCase)).ToList();
                if (mine.Count == 0) continue;
                var items = loc == Autodesk.Civil.BandLocationType.Bottom ? pv.Bands.GetBottomBandItems() : pv.Bands.GetTopBandItems();
                foreach (var r in mine)
                {
                    int idx = (int)NeedDbl(r, "index");
                    if (idx < 0 || idx >= items.Count) throw new ArgumentException($"profile_view_bands: {where} row {idx} does not exist (the band set has {items.Count} {where} bands, numbered from 0).");
                    var item = items[idx];
                    if (StrParam(r, "profile1") is string p1) item.Profile1Id = FindProfile(c.Tr, alignment, p1).ObjectId;
                    if (StrParam(r, "profile2") is string p2) item.Profile2Id = FindProfile(c.Tr, alignment, p2).ObjectId;
                    if (StrParam(r, "network") is string n) item.DataSourceId = FindPipeNetwork(c.Civ, c.Tr, n).ObjectId;
                    if (DblParam(r, "gap") is double gap) item.Gap = gap / 1000.0;
                }
                if (loc == Autodesk.Civil.BandLocationType.Bottom) pv.Bands.SetBottomBandItems(items); else pv.Bands.SetTopBandItems(items);
            }
            var imported = AlignmentOps.FillBandData(c, pv, alignment);
            if (imported.Count == 0) throw new ArgumentException($"profile_view_bands: band set '{set.Name}' has no bands.");
            return new { profileView = pv.Name, bandSet = set.Name, method, bands = imported };
        }
        foreach (var (param, location) in new[] { ("bottom", Autodesk.Civil.BandLocationType.Bottom), ("top", Autodesk.Civil.BandLocationType.Top) })
        {
            if (!Has(p, param)) continue;
            var specs = ArrParam(p, param, 20);
            var items = new CivilDb.ProfileViewBandItemCollection(pv.ObjectId, location);
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                string kind = "band_" + Choice(s, "type", "", "profile_data", "pipe_network", "horizontal_geometry", "vertical_geometry");
                string styleName = NeedStr(s, "style");
                items.Add(Styles.Id(Styles.Collection(c.Civ, kind), styleName, kind.Replace('_', ' ') + " style"));
            }
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                var item = items[i];
                if (StrParam(s, "profile1") is string p1) item.Profile1Id = FindProfile(c.Tr, alignment, p1).ObjectId;
                if (StrParam(s, "profile2") is string p2) item.Profile2Id = FindProfile(c.Tr, alignment, p2).ObjectId;
                if (StrParam(s, "network") is string n) item.DataSourceId = FindPipeNetwork(c.Civ, c.Tr, n).ObjectId;
                if (DblParam(s, "gap") is double gap) item.Gap = gap / 1000.0;   // mm on paper
                if (BoolParam(s, "label_start") is bool ls) item.LabelAtStartStation = ls;
                if (BoolParam(s, "label_end") is bool le) item.LabelAtEndStation = le;
                report.Add(new { location = param, type = item.BandType.ToString(), style = Styles.NameOf(c.Tr, item.BandStyleId) });
            }
            if (location == Autodesk.Civil.BandLocationType.Bottom) pv.Bands.SetBottomBandItems(items);
            else pv.Bands.SetTopBandItems(items);
        }
        if (report.Count == 0) throw new ArgumentException("profile_view_bands: give band_set, or 'bottom' and/or 'top' bands.");
        return new { profileView = pv.Name, bands = report };
    }
}
