namespace C3dMcpBridge;

/// <summary>
/// More labels for sheets (roadmap #74): alignment station and geometry point label sets, profile line (grade) labels,
/// spanning pipe labels in plan and profile, profile view depth labels, crossing pressure pipe labels in a profile view.
/// Without a style name the drawing's 'Standard' (or first) style of that kind is used.
/// </summary>
internal static class LabelPackOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["alignment_labels_add"] = AlignmentLabels;
        ops["profile_grade_labels_add"] = ProfileGradeLabels;
        ops["spanning_pipe_label_add"] = SpanningPipeLabel;
        ops["profile_view_depth_label_add"] = DepthLabel;
        ops["crossing_pressure_labels_add"] = CrossingPressureLabels;
    }

    private static CivilStyles.LabelStylesRoot L(EditContext c) => c.Civ.Styles.LabelStyles;

    private static object AlignmentLabels(EditContext c, JsonElement p)
    {
        var a = FindAlignment(c.Civ, c.Tr, NeedStr(p, "alignment"));
        var made = new List<object>();
        if (PositiveParam(p, "major_interval") is double major)
        {
            var style = Styles.Id(L(c).AlignmentLabelStyles.MajorStationLabelStyles, StrParam(p, "major_style"), "major station label style");
            var gid = CivilDb.AlignmentStationLabelGroup.CreateMajor(style, a.ObjectId, major);
            made.Add(new { kind = "major stations", interval = major, style = Styles.NameOf(c.Tr, style), handle = H(gid) });
            if (PositiveParam(p, "minor_interval") is double minor)
            {
                if (minor >= major) throw new ArgumentException("alignment_labels_add: minor_interval must be smaller than major_interval.");
                var ms = Styles.Id(L(c).AlignmentLabelStyles.MinorStationLabelStyles, StrParam(p, "minor_style"), "minor station label style");
                var mid = CivilDb.AlignmentMinorStationLabelGroup.Create(ms, gid, minor);
                made.Add(new { kind = "minor stations", interval = minor, style = Styles.NameOf(c.Tr, ms), handle = H(mid) });
            }
        }
        else if (Has(p, "minor_interval")) throw new ArgumentException("alignment_labels_add: minor_interval needs major_interval.");
        if (BoolParam(p, "geometry_points") ?? false)
        {
            var gs = Styles.Id(L(c).AlignmentLabelStyles.GeometryPointLabelStyles, StrParam(p, "geometry_style"), "geometry point label style");
            var gid = CivilDb.AlignmentGeometryPointLabelGroup.Create(gs, a.ObjectId);
            made.Add(new { kind = "geometry points", style = Styles.NameOf(c.Tr, gs), handle = H(gid) });
        }
        if (made.Count == 0) throw new ArgumentException("alignment_labels_add: give major_interval (and minor_interval) and / or geometry_points: true.");
        return new { alignment = a.Name, labelGroups = made };
    }

    private static object ProfileGradeLabels(EditContext c, JsonElement p)
    {
        var pv = FindProfileView(c.Civ, c.Tr, NeedStr(p, "profile_view"));
        var a = (CivilDb.Alignment)c.Tr.GetObject(pv.AlignmentId, OpenMode.ForRead);
        var pr = FindProfile(c.Tr, a, NeedStr(p, "profile"));
        var style = Styles.Id(L(c).ProfileLabelStyles.LineLabelStyles, StrParam(p, "style"), "profile line label style");
        var gid = CivilDb.ProfileLineLabelGroup.Create(pv.ObjectId, pr.ObjectId, style);
        return new { profileView = pv.Name, profile = pr.Name, style = Styles.NameOf(c.Tr, style), handle = H(gid) };
    }

    private static object SpanningPipeLabel(EditContext c, JsonElement p)
    {
        string? network = StrParam(p, "network");
        var ids = new List<ObjectId>();
        foreach (var e in ArrParam(p, "pipes", 500))
            ids.Add(NetworkEditOps.PartId(c.Tr, c.Db, c.Civ, e.GetString() ?? "", network, true, "spanning_pipe_label_add pipes"));
        if (ids.Count < 2) throw new ArgumentException("spanning_pipe_label_add: give at least two pipes of one run.");
        var anchor = Has(p, "anchor") ? NetworkEditOps.PartId(c.Tr, c.Db, c.Civ, NeedStr(p, "anchor"), network, true, "anchor") : ids[ids.Count / 2];
        if (!ids.Contains(anchor)) throw new ArgumentException("spanning_pipe_label_add: anchor must be one of the pipes.");
        double ratio = DblParam(p, "ratio") ?? 0.5;
        if (ratio < 0 || ratio > 1) throw new ArgumentException("spanning_pipe_label_add: ratio is 0-1.");
        var style = Styles.Id(L(c).PipeLabelStyles.PlanProfileLabelStyles, StrParam(p, "style"), "pipe label style");
        if (StrParam(p, "profile_view") is string pvName)
        {
            var pv = FindProfileView(c.Civ, c.Tr, pvName);
            var parts = new ObjectIdCollection();
            ObjectId anchorPart = ObjectId.Null;
            foreach (var id in ids)
            {
                var part = (CivilDb.Part)c.Tr.GetObject(id, OpenMode.ForRead);
                if (part.ProfileViewPartId.IsNull) throw new ArgumentException($"spanning_pipe_label_add: '{part.Name}' is not shown in a profile view (profile_view_add_parts).");
                parts.Add(part.ProfileViewPartId);
                if (id == anchor) anchorPart = part.ProfileViewPartId;
            }
            var lid = CivilDb.SpanningPipeProfileLabel.Create(parts, anchorPart, pv.ObjectId, ratio, style);
            return new { where = "profile view", profileView = pv.Name, pipes = ids.Count, handle = H(lid), style = Styles.NameOf(c.Tr, style) };
        }
        var col = new ObjectIdCollection(ids.ToArray());
        var planId = CivilDb.SpanningPipeLabel.Create(col, anchor, ratio, style);
        return new { where = "plan", pipes = ids.Count, handle = H(planId), style = Styles.NameOf(c.Tr, style) };
    }

    private static object DepthLabel(EditContext c, JsonElement p)
    {
        var pv = FindProfileView(c.Civ, c.Tr, NeedStr(p, "profile_view"));
        var a = NeedPt(p, "from", "profile_view_depth_label_add");
        var b = NeedPt(p, "to", "profile_view_depth_label_add");
        var style = Styles.Id(L(c).ProfileViewLabelStyles.DepthLabelStyles, StrParam(p, "style"), "profile view depth label style");
        var id = CivilDb.ProfileViewDepthLabel.Create(pv.ObjectId, style, new Point2d(a.X, a.Y), new Point2d(b.X, b.Y));
        return new { profileView = pv.Name, handle = H(id), style = Styles.NameOf(c.Tr, style),
                     note = "from / to are drawing points inside the profile view (profile_sheet_window / list_profile_views give its position)." };
    }

    private static object CrossingPressureLabels(EditContext c, JsonElement p)
    {
        var pv = FindProfileView(c.Civ, c.Tr, NeedStr(p, "profile_view"));
        var net = FindPressureNetwork(c.Civ, c.Tr, NeedStr(p, "pressure_network"));
        double ratio = DblParam(p, "ratio") ?? 0.5;
        var style = Styles.Id(Styles.PressurePipeLabels(c.Civ), StrParam(p, "style"), "pressure pipe label style");
        int made = 0, skipped = 0;
        foreach (ObjectId id in net.GetPipeIds())
        {
            var part = (CivilDb.PressurePart)c.Tr.GetObject(id, OpenMode.ForRead);
            ObjectId pvPart = ObjectId.Null;
            try { pvPart = part.ProfileViewPartId; } catch { }
            if (pvPart.IsNull) { skipped++; continue; }
            try { made += CivilDb.CrossingPressurePipeProfileLabel.Create(pvPart, pv.ObjectId, ratio, style).Count; }
            catch { skipped++; }
        }
        if (made == 0) throw new ArgumentException($"crossing_pressure_labels_add: no pipe of '{net.Name}' is shown crossing '{pv.Name}' (profile_view_add_parts first; the network must cross the view's alignment).");
        return new { profileView = pv.Name, pressureNetwork = net.Name, labels = made, skipped };
    }
}
