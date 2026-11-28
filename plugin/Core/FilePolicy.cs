namespace C3dMcpBridge;

/// <summary>
/// Which files an MCP client may name. Every read and write of a client-chosen path goes through here.
///
/// Always on:
///   - the path must be fully qualified; device paths (\\?\, \\.\) and alternate data streams are refused;
///   - nothing is read from or written to the bridge's own folder (bridge.json holds the session token, audit.log
///     is the record of what the client did);
///   - nothing is written into Windows, Program Files, a Startup folder or the Autodesk plug-in folder, and never a
///     file Windows or AutoCAD would run (.exe, .bat, .lsp, .scr ...).
///
/// Optional, for offices that want a project boundary: %LOCALAPPDATA%\C3dMcpBridge\policy.json
///     { "allowedRoots": ["D:\\Projects", "\\\\server\\share\\jobs"] }
/// With that file present every path must lie under one of the roots. A file that cannot be read as that JSON
/// refuses all file access (fail closed) rather than silently allowing everything. The MCP server applies the same
/// file to the workbooks it reads (server/filepolicy.py).
///
/// Not covered: a junction or symbolic link inside an allowed root that points outside it. Paths are compared as
/// written after normalisation, not resolved to their final target.
/// </summary>
internal static class FilePolicy
{
    private static readonly HashSet<string> NeverWritten = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".hta", ".msi", ".msp", ".reg", ".lnk", ".pif", ".cpl", ".jar",
        ".lsp", ".fas", ".vlx", ".mnl", ".arx", ".crx", ".dbx", ".dvb", ".cuix", ".cui",
    };

    private static string PolicyFile => Path.Combine(BridgeInfo.DataFolder, "policy.json");

    private static readonly object Gate = new();
    private static DateTime _loadedStampUtc;
    private static List<string>? _roots;      // null = no policy file: any location
    private static string? _policyError;

    /// <summary>A file the client asks to read: the normalised full path, or an exception saying why not.</summary>
    public static string Read(string path, string op) => Check(path, op, write: false);

    /// <summary>A file the client asks to write: the normalised full path, or an exception saying why not.</summary>
    public static string Write(string path, string op) => Check(path, op, write: true);

    /// <summary>What get_edit_permission reports, so a client can see the boundary before it asks for a file.</summary>
    public static object Describe()
    {
        var roots = Roots(out string? error);
        return new
        {
            policyFile = PolicyFile,
            allowedRoots = roots,
            restricted = roots != null,
            error,
            note = roots == null && error == null
                ? "No policy.json: any folder the Windows user can reach, except system, start-up, plug-in and bridge folders for writing."
                : null,
        };
    }

    private static string Check(string path, string op, bool write)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"{op}: give a full path such as C:\\Projects\\file.");
        if (IsDevicePath(path)) throw new ArgumentException($"{op}: device paths (\\\\?\\ or \\\\.\\) are not accepted.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) { throw new ArgumentException($"{op}: '{path}' is not a valid path ({ex.Message})."); }
        // .NET Framework maps reserved names (CON, NUL, COM1 ...) to \\.\ here.
        if (IsDevicePath(full)) throw new ArgumentException($"{op}: '{path}' names a device, not a file.");
        if (full.IndexOf(':', 2) >= 0) throw new ArgumentException($"{op}: alternate data streams are not accepted.");

        if (Under(full, BridgeInfo.DataFolder))
            throw new InvalidOperationException($"{op}: the bridge's own folder is not available to MCP clients.");

        var roots = Roots(out string? error);
        if (error != null) throw new InvalidOperationException($"{op}: file access is refused because {error}");
        if (roots != null && !roots.Any(r => Under(full, r)))
            throw new InvalidOperationException(
                $"{op}: {full} is outside the folders this PC allows for MCP ({string.Join("; ", roots)}). " +
                $"The user can change them in {PolicyFile}.");

        if (write)
        {
            if (NeverWritten.Contains(Path.GetExtension(full)))
                throw new ArgumentException($"{op}: files of type '{Path.GetExtension(full)}' are never written.");
            foreach (var folder in ProtectedForWriting())
                if (Under(full, folder))
                    throw new InvalidOperationException($"{op}: nothing is written under {folder}.");
        }
        return full;
    }

    private static bool IsDevicePath(string path) =>
        path.Length >= 4 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/')
        && (path[2] == '?' || path[2] == '.') && (path[3] == '\\' || path[3] == '/');

    /// <summary>True when 'path' is the folder itself or anything inside it (case-insensitive, whole segments).</summary>
    private static bool Under(string path, string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        string root;
        try { root = Path.GetFullPath(folder).TrimEnd('\\', '/'); }
        catch { return false; }
        if (root.Length == 0) return false;
        if (path.Length == root.Length) return string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
        return path.Length > root.Length
               && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && (path[root.Length] == '\\' || path[root.Length] == '/');
    }

    private static IEnumerable<string> ProtectedForWriting()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (roaming.Length > 0) yield return Path.Combine(roaming, "Autodesk", "ApplicationPlugins");
        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (common.Length > 0) yield return Path.Combine(common, "Autodesk", "ApplicationPlugins");
    }

    /// <summary>The allowed roots (null when there is no policy file); 'error' is set when the file cannot be used.</summary>
    private static List<string>? Roots(out string? error)
    {
        lock (Gate)
        {
            DateTime stamp;
            try { stamp = File.Exists(PolicyFile) ? File.GetLastWriteTimeUtc(PolicyFile) : DateTime.MinValue; }
            catch (Exception ex) { error = $"{PolicyFile} cannot be read ({ex.Message})."; return null; }

            if (stamp != _loadedStampUtc)
            {
                _loadedStampUtc = stamp;
                _roots = null;
                _policyError = null;
                if (stamp != DateTime.MinValue)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(PolicyFile));
                        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                            !doc.RootElement.TryGetProperty("allowedRoots", out var list) || list.ValueKind != JsonValueKind.Array)
                            throw new InvalidDataException("it needs {\"allowedRoots\": [\"C:\\\\Projects\", ...]}");
                        var roots = new List<string>();
                        foreach (var item in list.EnumerateArray())
                        {
                            string? root = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root!) || IsDevicePath(root!))
                                throw new InvalidDataException("every root must be a full folder path");
                            roots.Add(Path.GetFullPath(root!).TrimEnd('\\', '/'));
                        }
                        if (roots.Count == 0) throw new InvalidDataException("allowedRoots is empty");
                        _roots = roots;
                    }
                    catch (Exception ex)
                    {
                        _policyError = $"{PolicyFile} is not usable: {ex.Message}. Fix or delete the file.";
                    }
                }
            }
            error = _policyError;
            return _roots;
        }
    }
}
