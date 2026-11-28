using Autodesk.AutoCAD.PlottingServices;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace C3dMcpBridge;

/// <summary>
/// The drawing as a file: layouts, external references, saving / exporting, plotting to PDF. Saving and plotting
/// cannot happen inside the batch transaction, so they are queued and run right after the commit.
/// </summary>
internal static class DocumentOps
{
    private const string PdfPlotter = "DWG To PDF.pc3";

    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["layout_create"] = LayoutCreate;
        ops["xref_attach"] = XrefAttach;
        ops["drawing_save"] = DrawingSave;
        ops["plot_pdf"] = PlotPdf;
    }

    // ---------------------------------------------------------------- file paths

    /// <summary>The 'path' parameter as a full path with one of the given extensions.</summary>
    private static string FullPath(JsonElement p, string op, bool write, params string[] extensions)
    {
        string path = NeedStr(p, "path");
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException($"{op}: path must be a full path such as C:\\Projects\\file{extensions[0]}.");
        if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{op}: the file must end in {string.Join(" or ", extensions)}.");
        return write ? FilePolicy.Write(path, op) : FilePolicy.Read(path, op);
    }

    /// <summary>A file about to be written: its folder must exist, and an existing file needs overwrite: true.</summary>
    private static void CheckWritable(JsonElement p, string op, string target)
    {
        if (!Directory.Exists(Path.GetDirectoryName(target))) throw new ArgumentException($"{op}: folder not found: {Path.GetDirectoryName(target)}");
        if (File.Exists(target) && !(BoolParam(p, "overwrite") ?? false))
            throw new ArgumentException($"{op}: {target} already exists (pass overwrite: true to replace it).");
    }

    // ---------------------------------------------------------------- layouts and xrefs

    private static object LayoutCreate(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "name");
        var lm = LayoutManager.Current;
        if (lm.LayoutExists(name)) throw new ArgumentException($"Layout '{name}' already exists.");
        CheckSymbolName(name, "layout");
        return new { layout = name, handle = H(SheetOps.CreateLayout(c, name).ObjectId) };
    }

    private static object XrefAttach(EditContext c, JsonElement p)
    {
        string path = FullPath(p, "xref_attach", false, ".dwg");
        if (!File.Exists(path)) throw new ArgumentException($"xref_attach: file not found: {path}");
        string name = StrParam(p, "name") ?? Path.GetFileNameWithoutExtension(path);
        var bt = (BlockTable)c.Tr.GetObject(c.Db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(name)) throw new ArgumentException($"A block or xref named '{name}' already exists.");
        var xrefId = c.Db.AttachXref(path, name);
        if (xrefId.IsNull) throw new InvalidOperationException("Civil 3D could not attach the file.");
        var br = new BlockReference(OptPt(p, "at", "xref_attach") ?? Point3d.Origin, xrefId);
        return new { xref = name, path, handle = H(c.Append(br, p)) };
    }

    // ---------------------------------------------------------------- save / export

    /// <summary>Verified live: Database.SaveAs to a new path renames the OPEN drawing (it is "Save As"), so a true
    /// copy is written from a Wblock clone instead.</summary>
    private static object DrawingSave(EditContext c, JsonElement p)
    {
        string format = Choice(p, "format", "dwg", "dwg", "dxf");
        bool copy = BoolParam(p, "copy") ?? false;
        string target = Has(p, "path") ? FullPath(p, "drawing_save", true, "." + format) : c.Doc.Name;
        bool inPlace = SameName(target, c.Doc.Name);

        string mode;
        if (inPlace)
        {
            if (format != "dwg" || copy) throw new ArgumentException("drawing_save: give a different path for a copy or a DXF export.");
            if (!c.Doc.IsNamedDrawing) throw new ArgumentException("drawing_save: this drawing has never been saved; give a path.");
            mode = "save";
        }
        else
        {
            CheckWritable(p, "drawing_save", target);
            mode = format == "dxf" ? "dxf export" : copy ? "copy" : "save as";
        }

        var db = c.Db;
        c.AfterCommit.Add(() =>
        {
            switch (mode)
            {
                case "dxf export": db.DxfOut(target, 16, DwgVersion.Current); break;
                case "copy":
                    using (var clone = db.Wblock()) clone.SaveAs(target, DwgVersion.Current);
                    break;
                default: db.SaveAs(target, true, DwgVersion.Current, db.SecurityParameters); break;
            }
        });
        return new
        {
            queued = true, mode, target,
            note = mode switch
            {
                "save" => "The drawing is saved in place after the batch is committed.",
                "save as" => "After the batch is committed the OPEN drawing is saved under this new name and continues as that file (like Save As).",
                "copy" => "A copy is written after the batch is committed; the open drawing keeps its own name.",
                _ => "A DXF is written after the batch is committed; the open drawing keeps its own name.",
            },
        };
    }

    // ---------------------------------------------------------------- plot to PDF

    /// <summary>
    /// One layout to one PDF (path, layout?), several layouts to one multi-sheet PDF (path + layouts), or one PDF per
    /// layout (folder + layouts). 'layouts' is a list of names or "all" (every paper-space layout in tab order).
    /// </summary>
    private static object PlotPdf(EditContext c, JsonElement p)
    {
        string? paper = StrParam(p, "paper");
        string? plotStyle = StrParam(p, "plot_style");
        var doc = c.Doc;
        List<string>? layouts = null;
        if (Has(p, "layouts"))
        {
            var v = p.GetProperty("layouts");
            layouts = v.ValueKind == JsonValueKind.String && SameName(v.GetString(), "all")
                ? PaperLayouts(c.Db, c.Tr).Select(l => l.LayoutName).ToList()
                : StrList(p, "layouts", 500).Select(n => FindLayout(c.Db, c.Tr, n).LayoutName).ToList();
            if (layouts.Count == 0) throw new ArgumentException("plot_pdf: no layouts to plot.");
        }

        if (StrParam(p, "folder") is string folder)
        {
            if (layouts == null) throw new ArgumentException("plot_pdf: 'folder' plots one PDF per layout; give layouts (a list or \"all\").");
            if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new ArgumentException($"plot_pdf: folder not found: {folder}");
            string stem = Path.GetFileNameWithoutExtension(doc.Name);
            var files = new List<string>();
            foreach (var l in layouts)
            {
                string file = FilePolicy.Write(Path.Combine(folder, $"{stem} - {FileSafe(l)}.pdf"), "plot_pdf");
                if (File.Exists(file) && !(BoolParam(p, "overwrite") ?? false))
                    throw new ArgumentException($"plot_pdf: {file} already exists (pass overwrite: true to replace it).");
                files.Add(file);
            }
            c.AfterCommit.Add(() => { for (int i = 0; i < layouts.Count; i++) Plot(doc, new List<string?> { layouts[i] }, files[i], paper, plotStyle); });
            return new { queued = true, files, layouts, note = $"One PDF per layout with '{PdfPlotter}', after the batch is committed." };
        }

        string target = FullPath(p, "plot_pdf", true, ".pdf");
        CheckWritable(p, "plot_pdf", target);
        if (layouts == null)
        {
            string? layout = StrParam(p, "layout");
            if (layout != null) layout = FindLayout(c.Db, c.Tr, layout).LayoutName;
            c.AfterCommit.Add(() => Plot(doc, new List<string?> { layout }, target, paper, plotStyle));
            return new
            {
                queued = true, target, layout = layout ?? "(current)",
                note = $"Plotted with '{PdfPlotter}' after the batch is committed. Model space is plotted to its extents, fitted; a layout is plotted as laid out.",
            };
        }
        // AutoCAD's plot engine puts only sheets of the same paper and orientation into one PDF; check before committing.
        var papers = layouts.Select(n => FindLayout(c.Db, c.Tr, n))
            .GroupBy(l => paper ?? $"{l.CanonicalMediaName} / {l.PlotRotation}").ToList();
        if (papers.Count > 1)
            throw new ArgumentException("plot_pdf: one multi-sheet PDF needs every layout on the same paper and orientation. These differ: " +
                string.Join("; ", papers.Select(g => $"{g.Key}: {string.Join(", ", g.Select(l => l.LayoutName))}")) +
                ". Give 'paper' for all of them, set them alike with page_setup_set, or use 'folder' for one PDF per layout.");
        var sheets = layouts.Cast<string?>().ToList();
        c.AfterCommit.Add(() => Plot(doc, sheets, target, paper, plotStyle));
        return new { queued = true, target, sheets = layouts, note = $"One multi-sheet PDF with '{PdfPlotter}', after the batch is committed; each layout uses its own page setup." };
    }

    private static string FileSafe(string name) => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    /// <summary>The canonical name of the first paper size whose canonical or display name contains 'paper'.</summary>
    internal static string FindPaper(PlotSettingsValidator validator, PlotSettings settings, string paper)
    {
        foreach (string? canonical in validator.GetCanonicalMediaNameList(settings))
        {
            if (canonical == null) continue;
            string display = validator.GetLocaleMediaName(settings, canonical) ?? "";
            if (canonical.Contains(paper, StringComparison.OrdinalIgnoreCase) || display.Contains(paper, StringComparison.OrdinalIgnoreCase))
                return canonical;
        }
        throw new ArgumentException($"No paper size of '{settings.PlotConfigurationName}' matches '{paper}' (try e.g. 'A1', 'A3', 'ISO full bleed A1').");
    }

    /// <summary>The layout's own page setup, redirected to the PDF plotter (with the paper and plot style asked for).</summary>
    private static PlotSettings PdfSettings(Layout layout, string? paper, string? plotStyle)
    {
        var settings = new PlotSettings(layout.ModelType);
        settings.CopyFrom(layout);
        var validator = PlotSettingsValidator.Current;
        // Keep the layout's own paper: switching the device with no paper falls back to the device default (Letter).
        string media = layout.CanonicalMediaName;
        var size = layout.PlotPaperSize;
        validator.SetPlotConfigurationName(settings, PdfPlotter, null);
        validator.RefreshLists(settings);
        if (paper != null) validator.SetCanonicalMediaName(settings, FindPaper(validator, settings, paper));
        else if (!layout.ModelType)
        {
            bool known = false;
            foreach (string? m in validator.GetCanonicalMediaNameList(settings))
                if (string.Equals(m, media, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
            if (known) validator.SetCanonicalMediaName(settings, media);
            else if (size.X > 0 && size.Y > 0) validator.SetClosestMediaName(settings, size.X, size.Y, PlotPaperUnit.Millimeters, true);
        }
        if (layout.ModelType)
        {
            validator.SetUseStandardScale(settings, true);
            validator.SetPlotType(settings, AcDb.PlotType.Extents);
            validator.SetStdScaleType(settings, StdScaleType.ScaleToFit);
            validator.SetPlotCentered(settings, true);
        }
        // a paper-space layout keeps its own plot area and scale (1:1 as laid out, or what page_setup_set chose)
        if (plotStyle != null) validator.SetCurrentStyleSheet(settings, plotStyle);
        return settings;
    }

    /// <summary>Plot the layouts (null = the current one) as the pages of one PDF, in the foreground.</summary>
    private static void Plot(Document doc, List<string?> layoutNames, string target, string? paper, string? plotStyle)
    {
        if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
            throw new InvalidOperationException("Another plot is in progress.");
        object oldBackground = CoreApp.GetSystemVariable("BACKGROUNDPLOT");
        CoreApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);   // plot in the foreground so the file exists when we return
        var lm = LayoutManager.Current;
        string original = lm.CurrentLayout;
        try
        {
            // a layout is plotted only while it is current; switch to the first one before the plot starts
            SheetOps.MakeCurrent(layoutNames[0] ?? original);
            using var tr = doc.Database.TransactionManager.StartTransaction();
            using var engine = PlotFactory.CreatePublishEngine();
            engine.BeginPlot(null, null);
            for (int i = 0; i < layoutNames.Count; i++)
            {
                string name = layoutNames[i] ?? original;
                SheetOps.MakeCurrent(name);
                var layout = (Layout)tr.GetObject(lm.GetLayoutId(name), OpenMode.ForRead);
                using var settings = PdfSettings(layout, paper, plotStyle);
                using var info = new PlotInfo { Layout = layout.ObjectId, OverrideSettings = settings };
                using var infoValidator = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                infoValidator.Validate(info);
                if (i == 0) engine.BeginDocument(info, doc.Name, null, 1, true, target);
                using var page = new PlotPageInfo();
                engine.BeginPage(page, info, i == layoutNames.Count - 1, null);
                engine.BeginGenerateGraphics(null);
                engine.EndGenerateGraphics(null);
                engine.EndPage(null);
            }
            engine.EndDocument(null);
            engine.EndPlot(null);
            tr.Commit();
        }
        finally
        {
            try { SheetOps.MakeCurrent(original); } catch { /* keep going */ }
            try { CoreApp.SetSystemVariable("BACKGROUNDPLOT", oldBackground); } catch { /* keep going */ }
        }
    }
}
