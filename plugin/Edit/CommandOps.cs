namespace C3dMcpBridge;

/// <summary>
/// command_run (roadmap #76, first step): a fixed list of AutoCAD / Civil 3D commands that have no .NET API, sent to the
/// command line after the edit batch has committed. Only commands written here can run, each with fixed arguments and no
/// dialog; nothing typed by the client reaches the command line. A command runs after the reply, so the receipt says
/// 'queued' and the client checks the effect with a read (e.g. get_drawing_info). It cannot be rolled back with the
/// batch; UNDO in Civil 3D undoes it. Every run is written to the audit log.
/// </summary>
internal static class CommandOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["command_run"] = CommandRun;
    }

    internal sealed record Cmd(string Script, string What);

    /// <summary>The allowlist. Scripts end with a space (Enter); "_." uses the English, built-in command.</summary>
    internal static readonly Dictionary<string, Cmd> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audit"] = new("_.AUDIT _N ", "AUDIT, report only (no fixes)"),
        ["audit_fix"] = new("_.AUDIT _Y ", "AUDIT and fix the errors it finds"),
        ["regenall"] = new("_.REGENALL ", "regenerate every viewport"),
        ["purge_regapps"] = new("_.-PURGE _R * _N ", "purge unused registered applications (often thousands in consultant drawings)"),
        ["purge_zero_length"] = new("_.-PURGE _Z ", "purge zero-length geometry"),
        ["purge_empty_text"] = new("_.-PURGE _E ", "purge empty text objects"),
        ["zoom_extents"] = new("_.ZOOM _E ", "zoom to the drawing extents"),
    };

    private static object CommandRun(EditContext c, JsonElement p)
    {
        string name = NeedStr(p, "command");
        if (!Allowed.TryGetValue(name, out var cmd))
            throw new ArgumentException($"command_run: '{name}' is not allowed. Allowed: {string.Join(", ", Allowed.Keys)}.");
        var doc = c.Doc;
        c.AfterCommit.Add(() =>
        {
            Audit.Write("command_run", new { command = name, script = cmd.Script.Trim(), drawing = doc.Name });
            doc.SendStringToExecute(cmd.Script, true, false, true);
        });
        return new { command = name, what = cmd.What, queued = true,
                     note = "Runs on Civil 3D's command line after this batch commits (not part of the rollback); check its effect with a read." };
    }
}
