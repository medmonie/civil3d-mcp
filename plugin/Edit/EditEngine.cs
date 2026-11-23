namespace C3dMcpBridge;

/// <summary>
/// The only code path that changes a drawing. One call = one batch of operations = one document lock and ONE
/// transaction: either every operation succeeds and the batch is committed, or nothing is changed. dry_run executes
/// the same code and then rolls the transaction back, so validation can never drift from the real behaviour.
/// </summary>
internal static class EditEngine
{
    private const int MaxOperations = 500;

    private static readonly Dictionary<string, Func<EditContext, JsonElement, object>> Ops = new(StringComparer.OrdinalIgnoreCase);

    static EditEngine()
    {
        // AutoCAD
        ObjectOps.Register(Ops);
        DrawOps.Register(Ops);
        BlockOps.Register(Ops);
        DocumentOps.Register(Ops);
        CadOps.Register(Ops);
        SheetOps.Register(Ops);
        // Civil 3D
        TerrainOps.Register(Ops);
        AlignmentOps.Register(Ops);
        CorridorOps.Register(Ops);
        GravityOps.Register(Ops);
        PressureOps.Register(Ops);
        StyleOps.Register(Ops);
        CivilOps.Register(Ops);
    }

    public static List<string> OperationNames => Ops.Keys.OrderBy(k => k).ToList();

    public static object Run(JsonElement p)
    {
        var ops = NeedArr(p, "operations", MaxOperations);
        bool dryRun = BoolParam(p, "dry_run") ?? false;

        // 1. Check the shape of the request before the user is asked anything.
        var names = OperationNamesOf(ops);
        string summary = string.Join(", ", names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"{g.Count()} x {g.Key}" : g.Key));

        var target = DrawingTarget.Read(p);
        var requestId = DrawingTarget.ReadId(p, "request_id");
        var doc = target.RequireActive();
        if (doc.IsReadOnly) throw new InvalidOperationException("The active drawing is read-only.");

        // 2. The first edit of this drawing needs permission.
        if (WriteGate.Ensure(doc, summary + (dryRun ? "  (dry run)" : "")) is string denied)
            return new { applied = false, dryRun, permission = "not granted", reason = denied };

        target.RequireActive();

        // 3. Run every operation in one transaction; the first failure rolls everything back.
        var results = new List<object>();
        EditContext ctx;
        using (doc.LockDocument())
        {
            target.RequireActive();
            using var tr = doc.Database.TransactionManager.StartTransaction();
            ctx = new EditContext { Doc = doc, Tr = tr };
            for (int i = 0; i < ops.Count; i++)
            {
                ctx.OpIndex = i + 1;
                try
                {
                    results.Add(new { index = i + 1, op = names[i], result = Ops[names[i]](ctx, ops[i]) });
                }
                catch (Exception ex)
                {
                    tr.Abort();
                    string error = ErrorText(ex);
                    Audit.Write("edit_failed", new { requestId, drawing = doc.Name, dryRun, summary, failedAt = i + 1, op = names[i], error });
                    return new
                    {
                        applied = false, dryRun, failedAt = i + 1, op = names[i], error,
                        note = "Nothing was changed: the whole batch was rolled back.",
                        completedBeforeFailure = results, warnings = ctx.Warnings,
                    };
                }
            }
            if (dryRun) tr.Abort();
            else
            {
                tr.TransactionManager.QueueForGraphicsFlush();
                tr.Commit();
            }
        }

        // 4. Saving and plotting cannot run inside the transaction; they follow the commit.
        bool afterCommitSucceeded = dryRun || RunAfterCommit(doc, target, ctx);

        Audit.Write(dryRun ? "edit_dry_run" : "edit_applied", new { requestId, drawing = doc.Name, summary, operations = ops.Count, warnings = ctx.Warnings.Count });
        return new
        {
            applied = !dryRun, dryRun, afterCommitSucceeded, drawing = Path.GetFileName(doc.Name), operations = ops.Count, results, warnings = ctx.Warnings,
            note = dryRun ? "Dry run: everything was executed and then rolled back; the drawing is unchanged."
                          : "Committed as one transaction (one UNDO step in Civil 3D).",
        };
    }

    private static List<string> OperationNamesOf(List<JsonElement> ops)
    {
        var names = new List<string>();
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i].ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"operation {i + 1} must be an object with an 'op' field.");
            string name = NeedStr(ops[i], "op");
            if (!Ops.ContainsKey(name))
                throw new ArgumentException($"operation {i + 1}: unknown op '{name}'. Available: {string.Join(", ", OperationNames)}");
            names.Add(name);
        }
        return names;
    }

    private static bool RunAfterCommit(Document doc, DrawingTarget target, EditContext ctx)
    {
        try { doc.Editor.UpdateScreen(); } catch { /* cosmetic */ }
        if (ctx.AfterCommit.Count == 0) return true;
        bool succeeded = true;
        try
        {
            target.RequireActive();
            using var docLock = doc.LockDocument();
            foreach (var work in ctx.AfterCommit)
            {
                try { target.RequireActive(); work(); }
                catch (Exception ex) { succeeded = false; ctx.Warnings.Add($"after commit (save / plot) FAILED: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            succeeded = false;
            ctx.Warnings.Add($"after commit (save / plot) could not start: {ErrorText(ex)}");
        }
        return succeeded;
    }
}
