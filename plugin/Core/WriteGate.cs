using System.Diagnostics;

namespace C3dMcpBridge;

/// <summary>
/// Edit permission for each open drawing. Main thread only.
/// </summary>
internal static class WriteGate
{
    private static readonly TimeSpan DeclineCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LateAnswer = TimeSpan.FromMinutes(9);   // the caller gives up at ~10 minutes

    private sealed record Permission(DateTime GrantedUtc, string GrantedBy);
    private static readonly object PermissionKey = new();
    private static readonly object DeclinedKey = new();

    private static Permission? ActivePermission => AcApp.DocumentManager.MdiActiveDocument?.UserData[PermissionKey] as Permission;
    public static bool Granted => ActivePermission != null;

    public static object Status()
    {
        var permission = ActivePermission;
        return new
        {
            editingAllowed = permission != null,
            grantedUtc = permission?.GrantedUtc,
            grantedBy = permission?.GrantedBy,
            scope = "active drawing; ends when the drawing closes or MCPREVOKE is run",
            howToGrant = "answer Yes to the first edit, or run MCPALLOW in the intended drawing",
            auditLog = Audit.FilePath,
            files = FilePolicy.Describe(),
        };
    }

    public static void Grant(Document doc, string by)
    {
        doc.UserData[PermissionKey] = new Permission(DateTime.UtcNow, by);
        doc.UserData.Remove(DeclinedKey);
        Audit.Write("permission_granted", new { by, drawing = doc.Name });
    }

    public static void Revoke(string by)
    {
        foreach (Document doc in AcApp.DocumentManager)
        {
            if (doc.UserData.ContainsKey(PermissionKey)) Audit.Write("permission_revoked", new { by, drawing = doc.Name });
            doc.UserData.Remove(PermissionKey);
        }
    }

    /// <summary>Returns null when editing may proceed, otherwise the reason it may not.</summary>
    public static string? Ensure(Document doc, string firstRequestSummary)
    {
        if (doc.UserData[PermissionKey] is Permission) return null;
        if (doc.UserData[DeclinedKey] is DateTime declined && DateTime.UtcNow - declined < DeclineCooldown)
            return "Editing was declined in Civil 3D a moment ago. Ask the user before trying again (they can also run MCPALLOW).";

        var sw = Stopwatch.StartNew();
        bool yes = Ui.AskYesNo(
            "Allow MCP edits in Civil 3D?",
            "An AI assistant (MCP client such as Claude or ChatGPT) is asking to edit this Civil 3D drawing.\n\n" +
            "First request (drawing " + Path.GetFileName(doc.Name) + "):\n" + firstRequestSummary + "\n\n" +
            "Yes  = allow edits in this drawing until it closes or you run MCPREVOKE.\n" +
            "No   = nothing is changed.\n\n" +
            "Every edit is one undo step and is written to the audit log. Run MCPREVOKE at any time to stop edits.");
        if (!yes)
        {
            doc.UserData[DeclinedKey] = DateTime.UtcNow;
            Audit.Write("permission_declined", new { drawing = doc.Name });
            return "The user declined editing in Civil 3D. Nothing was changed.";
        }
        Grant(doc, "dialog");
        return sw.Elapsed > LateAnswer
            ? "Editing is now allowed, but this request expired without applying. Send it again if you still want the edit."
            : null;
    }
}
