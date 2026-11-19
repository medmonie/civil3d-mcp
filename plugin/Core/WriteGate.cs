using System.Diagnostics;

namespace C3dMcpBridge;

/// <summary>
/// The single permission for editing. Reading never needs it. The first edit request of a Civil 3D session shows ONE
/// dialog; after "Yes" every later edit in this session runs without asking again, in any drawing, until Civil 3D is
/// closed or the user runs MCPREVOKE. MCPALLOW grants it up front from the command line. Main thread only.
/// </summary>
internal static class WriteGate
{
    private static readonly TimeSpan DeclineCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LateAnswer = TimeSpan.FromMinutes(9);   // the caller gives up at ~10 minutes

    private static bool _granted;
    private static DateTime _grantedUtc;
    private static string _grantedBy = "";
    private static DateTime _declinedUtc = DateTime.MinValue;

    public static bool Granted => _granted;

    public static object Status() => new
    {
        editingAllowed = _granted,
        grantedUtc = _granted ? _grantedUtc : (DateTime?)null,
        grantedBy = _granted ? _grantedBy : null,
        scope = "this Civil 3D session, all drawings; ends when Civil 3D closes or MCPREVOKE is run",
        howToGrant = "answer Yes to the one-time dialog on the first edit, or run MCPALLOW in Civil 3D",
        auditLog = Audit.FilePath,
    };

    public static void Grant(string by)
    {
        _granted = true;
        _grantedUtc = DateTime.UtcNow;
        _grantedBy = by;
        Audit.Write("permission_granted", new { by });
    }

    public static void Revoke(string by)
    {
        if (_granted) Audit.Write("permission_revoked", new { by });
        _granted = false;
    }

    /// <summary>Returns null when editing may proceed, otherwise the reason it may not.</summary>
    public static string? Ensure(Document doc, string firstRequestSummary)
    {
        if (_granted) return null;
        if (DateTime.UtcNow - _declinedUtc < DeclineCooldown)
            return "Editing was declined in Civil 3D a moment ago. Ask the user before trying again (they can also run MCPALLOW).";

        var sw = Stopwatch.StartNew();
        bool yes = Ui.AskYesNo(
            "Claude - allow editing in Civil 3D?",
            "Claude is asking to EDIT drawings in this Civil 3D session.\n\n" +
            "First request (drawing " + Path.GetFileName(doc.Name) + "):\n" + firstRequestSummary + "\n\n" +
            "Yes  = allow this and all later edits until Civil 3D is closed. You will not be asked again.\n" +
            "No   = nothing is changed.\n\n" +
            "Every edit is one undo step and is written to the audit log. Run MCPREVOKE at any time to stop edits.");
        if (!yes)
        {
            _declinedUtc = DateTime.UtcNow;
            Audit.Write("permission_declined", new { drawing = doc.Name });
            return "The user declined editing in Civil 3D. Nothing was changed.";
        }
        Grant("dialog");
        return sw.Elapsed > LateAnswer
            ? "Editing is now allowed, but the answer came too late for this request. Send the same edit again."
            : null;
    }
}
