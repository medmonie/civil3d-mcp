namespace C3dMcpBridge;

/// <summary>
/// Sheets: viewports on layouts, named views, layouts copied from a template drawing, page setups. Paper space is in
/// millimetres; model space in drawing units (a 1:500 viewport of a metre drawing shows 0.5 m per paper mm).
/// </summary>
internal static class SheetOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["viewport_create"] = ViewportCreate;
        ops["viewport_set"] = ViewportSet;
        ops["view_create"] = ViewCreate;
        ops["layout_from_template"] = LayoutFromTemplate;
        ops["layout_delete"] = LayoutDelete;
        ops["page_setup_set"] = PageSetupSet;
    }

    // ---------------------------------------------------------------- viewports

    /// <summary>
    /// A layout that has never been shown has no paper-space viewport of its own, and the first viewport added to it
    /// would become that one (showing nothing). Showing the layout once makes Civil 3D create it.
    /// </summary>
    internal static void EnsureActivated(EditContext c, Layout layout)
    {
        static bool HasViewport(EditContext c, Layout layout)
        {
            var space = (BlockTableRecord)c.Tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            foreach (ObjectId id in space)
                if (id.ObjectClass.DxfName == "VIEWPORT" && !id.IsErased) return true;
            return false;
        }
        if (HasViewport(c, layout)) return;
        // show it once with LAYOUTCREATEVIEWPORT off, so Civil 3D adds only the paper-space viewport, not a default one
        string current = LayoutManager.Current.CurrentLayout;
        WithoutDefaultViewport(() =>
        {
            try { MakeCurrent(layout.LayoutName); MakeCurrent(current); } catch { /* fall back to creating it below */ }
        });
        if (HasViewport(c, layout)) return;

        // Create the layout's own paper-space viewport: the first viewport of a layout is paper space itself.
        var paper = layout.PlotPaperSize;
        bool turned = layout.PlotRotation is PlotRotation.Degrees090 or PlotRotation.Degrees270;
        double w = Math.Max(turned ? paper.Y : paper.X, 1), h = Math.Max(turned ? paper.X : paper.Y, 1);
        var space = (BlockTableRecord)c.Tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
        var ps = new Viewport { CenterPoint = new Point3d(w / 2, h / 2, 0), Width = w * 1.05, Height = h * 1.05 };
        ps.SetDatabaseDefaults(c.Db);
        space.AppendEntity(ps);
        c.Tr.AddNewlyCreatedDBObject(ps, true);
        ps.ViewCenter = new Point2d(w / 2, h / 2);
        ps.ViewHeight = h * 1.05;
        c.Warn($"layout '{layout.LayoutName}' had never been shown; its paper-space viewport was created ({w:0} x {h:0} mm).");
    }

    /// <summary>Run work with LAYOUTCREATEVIEWPORT = 0 (no automatic viewport on a new layout), then restore it.</summary>
    internal static T WithoutDefaultViewport<T>(Func<T> work)
    {
        object? old = null;
        try { old = CoreApp.GetSystemVariable("LAYOUTCREATEVIEWPORT"); CoreApp.SetSystemVariable("LAYOUTCREATEVIEWPORT", (short)0); }
        catch { /* not available: Civil 3D's default applies */ }
        try { return work(); }
        finally { if (old != null) try { CoreApp.SetSystemVariable("LAYOUTCREATEVIEWPORT", old); } catch { /* keep going */ } }
    }

    internal static void WithoutDefaultViewport(Action work) => WithoutDefaultViewport(() => { work(); return 0; });

    /// <summary>A new, empty layout (no automatic viewport), initialised so viewports can be added at once.</summary>
    internal static Layout CreateLayout(EditContext c, string name)
    {
        var id = WithoutDefaultViewport(() => LayoutManager.Current.CreateLayout(name));
        var layout = (Layout)c.Tr.GetObject(id, OpenMode.ForWrite);
        EnsureActivated(c, layout);
        return layout;
    }

    /// <summary>Make a layout current (needed to plot it). The CTAB system variable is the fallback when the layout
    /// manager does not switch from the bridge's context.</summary>
    internal static void MakeCurrent(string name)
    {
        var lm = LayoutManager.Current;
        if (SameName(lm.CurrentLayout, name)) return;
        try { lm.CurrentLayout = name; } catch { /* try CTAB */ }
        if (!SameName(lm.CurrentLayout, name)) CoreApp.SetSystemVariable("CTAB", name);
        if (!SameName(lm.CurrentLayout, name) && !SameName(CoreApp.GetSystemVariable("CTAB") as string, name))
            throw new InvalidOperationException($"Layout '{name}' could not be made current.");
    }

    /// <summary>Delete a layout so its name is free at once in the same batch: renamed first, then deleted.</summary>
    internal static void DeleteLayout(string name)
    {
        var lm = LayoutManager.Current;
        string gone = $"~deleted {Guid.NewGuid():N}";
        lm.RenameLayout(name, gone);
        lm.DeleteLayout(gone);
    }

    /// <summary>Model window [xmin, ymin, xmax, ymax] from the parameter.</summary>
    private static (Point2d min, Point2d max)? Window(JsonElement p, string name, string op)
    {
        if (!Has(p, name)) return null;
        var w = NumList(p, name, 4);
        if (w.Count != 4 || w[2] <= w[0] || w[3] <= w[1]) throw new ArgumentException($"{op}: {name} must be [xmin, ymin, xmax, ymax] with max > min.");
        return (new Point2d(w[0], w[1]), new Point2d(w[2], w[3]));
    }

    /// <summary>
    /// Point a viewport at a model area. The model point goes in ViewTarget and ViewCenter stays at the origin, so a
    /// twist turns the view about that point. Scale: 'scale' (1:N), else 'custom_scale', else the window fitted.
    /// </summary>
    private static void Aim(EditContext c, Viewport vp, JsonElement p, string op, bool creating)
    {
        Point2d? centre = null;
        double? fit = null;
        double twist = vp.TwistAngle;

        if (StrParam(p, "view") is string viewName)
        {
            var view = (ViewTableRecord)c.Tr.GetObject(c.Symbol(c.Db.ViewTableId, viewName, "Named view"), OpenMode.ForRead);
            // a view's CenterPoint is in its own display coordinates around its Target
            var v = view.CenterPoint;
            var rot = Matrix3d.Rotation(-view.ViewTwist, Vector3d.ZAxis, Point3d.Origin);
            var world = new Point3d(v.X, v.Y, 0).TransformBy(rot) + view.Target.GetAsVector();
            centre = new Point2d(world.X, world.Y);
            fit = Math.Min(vp.Width / view.Width, vp.Height / view.Height);
            twist = view.ViewTwist;
        }
        if (Window(p, "window", op) is var (min, max))
        {
            centre = new Point2d((min.X + max.X) / 2, (min.Y + max.Y) / 2);
            fit = Math.Min(vp.Width / (max.X - min.X), vp.Height / (max.Y - min.Y));
        }
        if (Has(p, "view_center"))
        {
            var vc = NeedPt(p, "view_center", op);
            centre = new Point2d(vc.X, vc.Y);
        }
        if (DblParam(p, "twist_deg") is double t) twist = Rad(t);

        double? scale = ScaleDenominator(p, "scale") is double n ? UnitMm(c.Db) / n : DblParam(p, "custom_scale") ?? fit;
        if (creating && centre == null) throw new ArgumentException($"{op}: give the model area: window, view_center + scale, or view.");
        if (creating && scale == null) throw new ArgumentException($"{op}: give scale (e.g. \"1:500\") or a window to fit.");
        if (scale is double s && !(s > 0)) throw new ArgumentException($"{op}: the scale must be positive.");

        vp.ViewDirection = Vector3d.ZAxis;
        if (centre is Point2d cp)
        {
            vp.ViewTarget = new Point3d(cp.X, cp.Y, 0);
            vp.ViewCenter = Point2d.Origin;
        }
        vp.TwistAngle = twist;
        if (scale is double sc) vp.CustomScale = sc;
    }

    private static void FreezeThaw(EditContext c, Viewport vp, JsonElement p)
    {
        if (Has(p, "freeze_layers"))
            vp.FreezeLayersInViewport(StrList(p, "freeze_layers", 2000).Select(n => LayerId(c.Db, c.Tr, n)).ToList().GetEnumerator());
        if (Has(p, "thaw_layers"))
            vp.ThawLayersInViewport(StrList(p, "thaw_layers", 2000).Select(n => LayerId(c.Db, c.Tr, n)).ToList().GetEnumerator());
    }

    private static object Describe(EditContext c, Viewport vp) => new
    {
        handle = H(vp.ObjectId), at = new[] { R3(vp.CenterPoint.X), R3(vp.CenterPoint.Y) }, width = R3(vp.Width), height = R3(vp.Height),
        scale = ScaleText(c.Db, vp.CustomScale), customScale = vp.CustomScale,
        viewCenter = new[] { R3(vp.ViewTarget.X + vp.ViewCenter.X), R3(vp.ViewTarget.Y + vp.ViewCenter.Y) },
        twistDeg = Deg(vp.TwistAngle), locked = vp.Locked, on = vp.On,
    };

    private static object ViewportCreate(EditContext c, JsonElement p)
    {
        const string op = "viewport_create";
        NeedStr(p, "layout");
        if (SameName(StrParam(p, "layout"), "Model")) throw new ArgumentException($"{op}: viewports go on a paper-space layout, not Model.");
        var at = NeedPt(p, "at", op);
        var layout = FindLayout(c.Db, c.Tr, NeedStr(p, "layout"));
        EnsureActivated(c, layout);
        MakeCurrent(layout.LayoutName);     // a viewport can be switched on only while its layout is current
        var vp = new Viewport
        {
            CenterPoint = new Point3d(at.X, at.Y, 0),
            Width = NeedPositive(p, "width"),
            Height = NeedPositive(p, "height"),
        };
        var id = c.Append(vp, p);
        Aim(c, vp, p, op, creating: true);
        vp.On = true;
        if (BoolParam(p, "lock") is bool locked) vp.Locked = locked;
        FreezeThaw(c, vp, p);
        return Describe(c, vp);
    }

    private static object ViewportSet(EditContext c, JsonElement p)
    {
        const string op = "viewport_set";
        var vp = c.OpenByHandle<Viewport>(p, "a viewport");
        bool wasLocked = vp.Locked;
        vp.Locked = false;    // a locked viewport keeps its view; unlock to change it, then restore
        if (OptPt(p, "at", op) is Point3d at) vp.CenterPoint = new Point3d(at.X, at.Y, 0);
        if (PositiveParam(p, "width") is double w) vp.Width = w;
        if (PositiveParam(p, "height") is double h) vp.Height = h;
        Aim(c, vp, p, op, creating: false);
        vp.Locked = BoolParam(p, "lock") ?? wasLocked;
        FreezeThaw(c, vp, p);
        return Describe(c, vp);
    }

    // ---------------------------------------------------------------- named views

    private static object ViewCreate(EditContext c, JsonElement p)
    {
        const string op = "view_create";
        string name = NeedStr(p, "name");
        CheckSymbolName(name, "view");
        var (min, max) = Window(p, "window", op) ?? throw new ArgumentException($"{op}: give window [xmin, ymin, xmax, ymax] (model space).");
        var table = (ViewTable)c.Tr.GetObject(c.Db.ViewTableId, OpenMode.ForRead);
        ViewTableRecord view;
        bool created = !table.Has(name);
        if (!created)
        {
            if (Choice(p, "if_exists", "error", "error", "replace") == "error") throw new ArgumentException($"{op}: view '{name}' already exists (if_exists: replace).");
            view = (ViewTableRecord)c.Tr.GetObject(table[name], OpenMode.ForWrite);
        }
        else
        {
            view = new ViewTableRecord { Name = name };
            table.UpgradeOpen();
            table.Add(view);
            c.Tr.AddNewlyCreatedDBObject(view, true);
        }
        double twist = Rad(DblParam(p, "twist_deg") ?? 0);
        // target = window centre, view centre = origin: the twist turns the view about the window centre
        view.ViewDirection = Vector3d.ZAxis;
        view.Target = new Point3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, 0);
        view.CenterPoint = Point2d.Origin;
        view.ViewTwist = twist;
        view.Width = max.X - min.X;
        view.Height = max.Y - min.Y;
        view.IsPaperspaceView = false;
        return new { view = name, created, center = new[] { R3(view.Target.X), R3(view.Target.Y) }, width = R3(view.Width), height = R3(view.Height), twistDeg = Deg(twist) };
    }

    // ---------------------------------------------------------------- layouts

    private static object LayoutDelete(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var layout = FindLayout(c.Db, c.Tr, name);
        if (layout.ModelType) throw new ArgumentException("layout_delete: the Model tab cannot be deleted.");
        if (PaperLayouts(c.Db, c.Tr).Count <= 1) throw new ArgumentException("layout_delete: a drawing keeps at least one layout.");
        DeleteLayout(layout.LayoutName);
        return new { deleted = layout.LayoutName };
    }

    /// <summary>
    /// A new layout with the page setup and everything drawn on a layout of a template (.dwt / .dwg): title block,
    /// viewports, notes. Blocks, styles and layers it needs come along; ones the drawing already has are kept.
    /// </summary>
    private static object LayoutFromTemplate(EditContext c, JsonElement p)
    {
        const string op = "layout_from_template";
        string path = Path.GetFullPath(NeedStr(p, "path"));
        string? source = StrParam(p, "layout");
        var lm = LayoutManager.Current;

        using var side = BlockOps.ReadSide(path, op);
        using var st = side.TransactionManager.StartTransaction();
        var layouts = PaperLayouts(side, st);
        if (layouts.Count == 0) throw new ArgumentException($"{op}: {Path.GetFileName(path)} has no paper-space layout.");
        var src = source == null ? layouts[0]
            : layouts.FirstOrDefault(l => SameName(l.LayoutName, source))
              ?? throw new ArgumentException($"{op}: layout '{source}' is not in {Path.GetFileName(path)}. It has: {string.Join(", ", layouts.Select(l => l.LayoutName))}.");
        string name = StrParam(p, "name") ?? src.LayoutName;
        CheckSymbolName(name, "layout");
        if (lm.LayoutExists(name))
        {
            if (Choice(p, "if_exists", "error", "error", "replace") == "error")
                throw new ArgumentException($"{op}: layout '{name}' already exists (if_exists: replace, or give another name).");
            if (PaperLayouts(c.Db, c.Tr).Count <= 1) throw new ArgumentException($"{op}: '{name}' is the only layout and cannot be replaced; give another name.");
            DeleteLayout(name);
        }

        var layout = CreateLayout(c, name);
        using (var settings = new PlotSettings(false))
        {
            settings.CopyFrom(src);               // page setup only (device, paper, scale, plot style)
            layout.CopyFrom(settings);
        }

        // everything on the template layout except its own paper-space viewport (the first viewport)
        var ids = new ObjectIdCollection();
        var srcSpace = (BlockTableRecord)st.GetObject(src.BlockTableRecordId, OpenMode.ForRead);
        bool skippedPaperViewport = false;
        foreach (ObjectId id in srcSpace)
        {
            if (!skippedPaperViewport && id.ObjectClass.DxfName == "VIEWPORT") { skippedPaperViewport = true; continue; }
            ids.Add(id);
        }
        st.Commit();

        int viewports = 0;
        if (ids.Count > 0)
        {
            var map = new IdMapping();
            side.WblockCloneObjects(ids, layout.BlockTableRecordId, map, DuplicateRecordCloning.Ignore, false);
            foreach (IdPair pair in map)
            {
                if (!pair.IsPrimary || !pair.IsCloned) continue;
                if (c.Tr.GetObject(pair.Value, OpenMode.ForRead) is Viewport vp)
                {
                    vp.UpgradeOpen();
                    vp.On = true;
                    viewports++;
                }
            }
        }
        return new { layout = name, from = $"{Path.GetFileName(path)} / {src.LayoutName}", objects = ids.Count, viewports,
                     plotDevice = layout.PlotConfigurationName, paper = layout.CanonicalMediaName };
    }

    // ---------------------------------------------------------------- page setup

    private static object PageSetupSet(EditContext c, JsonElement p)
    {
        const string op = "page_setup_set";
        var layout = FindLayout(c.Db, c.Tr, NeedStr(p, "layout"), OpenMode.ForWrite);
        var v = PlotSettingsValidator.Current;

        if (StrParam(p, "page_setup") is string named)
        {
            var dict = (DBDictionary)c.Tr.GetObject(c.Db.PlotSettingsDictionaryId, OpenMode.ForRead);
            var entry = Entries(dict).FirstOrDefault(e => SameName(e.Key, named));
            if (!entry.Value.IsValid) throw new ArgumentException($"{op}: page setup '{named}' not found. Use list_cad_styles kind page_setup.");
            layout.CopyFrom(c.Tr.GetObject(entry.Value, OpenMode.ForRead));
        }
        if (StrParam(p, "device") is string device)
        {
            try { v.SetPlotConfigurationName(layout, device, null); }
            catch (Exception ex) { throw new ArgumentException($"{op}: plot device '{device}' is not available ({ex.Message}). E.g. 'DWG To PDF.pc3'."); }
        }
        v.RefreshLists(layout);
        if (StrParam(p, "paper") is string paper) v.SetCanonicalMediaName(layout, DocumentOps.FindPaper(v, layout, paper));
        if (StrParam(p, "plot_style") is string ctb) v.SetCurrentStyleSheet(layout, ctb);
        if (Has(p, "rotation"))
            v.SetPlotRotation(layout, Text(p.GetProperty("rotation")).Trim() switch
            {
                "0" => PlotRotation.Degrees000, "90" => PlotRotation.Degrees090, "180" => PlotRotation.Degrees180, "270" => PlotRotation.Degrees270,
                _ => throw new ArgumentException($"{op}: rotation must be 0, 90, 180 or 270."),
            });
        if (Has(p, "scale"))
        {
            string s = Text(p.GetProperty("scale")).Trim();
            v.SetUseStandardScale(layout, true);
            if (SameName(s, "fit")) v.SetStdScaleType(layout, StdScaleType.ScaleToFit);
            else if (s is "1:1" or "1") v.SetStdScaleType(layout, StdScaleType.StdScale1To1);
            else
            {
                double n = ScaleDenominator(p, "scale")!.Value;
                v.SetUseStandardScale(layout, false);
                v.SetCustomPrintScale(layout, new CustomScale(1, n));
            }
        }
        if (BoolParam(p, "center") is bool centred) v.SetPlotCentered(layout, centred);
        return new
        {
            layout = layout.LayoutName, device = layout.PlotConfigurationName, paper = layout.CanonicalMediaName,
            plotStyle = layout.CurrentStyleSheet, rotation = layout.PlotRotation.ToString(),
        };
    }
}
