using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace C3dMcpBridge;

/// <summary>
/// Localhost-only, newline-delimited JSON protocol.
///   request : {"id":"..","token":"..","method":"list_layers","params":{...}}
///   response: {"id":"..","ok":true,"result":{...}}  or  {"id":"..","ok":false,"error":".."}
/// The port is chosen by the OS and, together with a random per-session token, written to
/// %LOCALAPPDATA%\C3dMcpBridge\bridge.json. The MCP server reads that file on every call, so no
/// configuration is needed and other local users/processes without access to the file cannot call in.
/// </summary>
internal static class BridgeServer
{
    // How long a request may WAIT for Civil 3D's main thread (busy with a command, dialog or regen). Once a handler
    // has started it is never abandoned, so this is not a limit on how long an edit or the permission dialog may take.
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PingWait = TimeSpan.FromSeconds(2);
    private const int MaxRequestChars = 4 * 1024 * 1024;                       // 4 MB per request line (large edit batches)
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);   // a started request line must finish in 30 s
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);    // idle connections cannot hold a slot indefinitely
    private const int MaxConnections = 16;
    private static int _connections;

    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static string _token = "";

    public static int Port => _listener?.LocalEndpoint is IPEndPoint ep ? ep.Port : 0;
    public static Guid SessionId { get; private set; }

    private static string BridgeFile => Path.Combine(BridgeInfo.DataFolder, "bridge.json");

    public static void Start()
    {
        if (_listener != null) return;

        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        SessionId = Guid.NewGuid();
        _listener = new TcpListener(IPAddress.Loopback, 0);   // 127.0.0.1 only, never 0.0.0.0
        _listener.Start();
        _cts = new CancellationTokenSource();

        // Every instance announces itself in bridge-<pid>.json. Only one is the MCP target (bridge.json): a second
        // Civil 3D started while another is running does NOT take over silently; MCPSTATUS in it switches explicitly.
        WriteInstanceFile();
        var owner = TargetInstance();
        if (owner == null || owner.Value.pid == Environment.ProcessId) WriteBridgeFile();

        var ct = _cts.Token;
        _ = Task.Run(() => AcceptLoop(ct));
    }

    /// <summary>False when another running Civil 3D instance is the MCP target.</summary>
    public static bool IsTarget => BridgeFileIsMine();
    private static readonly DateTime StartedUtc = DateTime.UtcNow;
    private static string InstanceFile(int pid) => Path.Combine(BridgeInfo.DataFolder, $"bridge-{pid}.json");

    private static string ConnectionJson() => JsonSerializer.Serialize(new
    {
        port = Port,
        token = _token,
        session_id = SessionId,
        pid = Environment.ProcessId,
        startedUtc = StartedUtc
    });

    /// <summary>Publishes this Civil 3D instance as the target for MCP clients.</summary>
    public static void WriteBridgeFile()
    {
        if (_listener == null) return;
        WriteAtomically(BridgeFile, ConnectionJson());
    }

    private static void WriteInstanceFile() => WriteAtomically(InstanceFile(Environment.ProcessId), ConnectionJson());

    /// <summary>The MCP server reads bridge.json on every call: never let it see a half-written file.</summary>
    private static void WriteAtomically(string file, string text)
    {
        Directory.CreateDirectory(BridgeInfo.DataFolder);
        string temp = file + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, text);
        if (File.Exists(file)) File.Replace(temp, file, null);
        else File.Move(temp, file);
    }

    private static bool Alive(int pid)
    {
        try { using var pr = System.Diagnostics.Process.GetProcessById(pid); return !pr.HasExited; }
        catch { return false; }
    }

    private static (int pid, string session)? ReadInfo(string file)
    {
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(file));
            var r = d.RootElement;
            return (r.GetProperty("pid").GetInt32(), r.TryGetProperty("session_id", out var s) ? s.ToString() : "");
        }
        catch { return null; }
    }

    /// <summary>The instance named in bridge.json, if that process is still running.</summary>
    private static (int pid, string session)? TargetInstance()
    {
        if (!File.Exists(BridgeFile)) return null;
        var info = ReadInfo(BridgeFile);
        return info != null && Alive(info.Value.pid) ? info : null;
    }

    /// <summary>Other running Civil 3D instances with the bridge loaded (stale files of closed ones are removed).</summary>
    public static List<object> OtherInstances()
    {
        var list = new List<object>();
        try
        {
            var target = TargetInstance();
            foreach (var f in Directory.GetFiles(BridgeInfo.DataFolder, "bridge-*.json"))
            {
                var info = ReadInfo(f);
                if (info == null || !Alive(info.Value.pid)) { try { File.Delete(f); } catch { } continue; }
                if (info.Value.pid == Environment.ProcessId) continue;
                list.Add(new { pid = info.Value.pid, session_id = info.Value.session, isTarget = target?.pid == info.Value.pid });
            }
        }
        catch { /* best effort */ }
        return list;
    }

    private static bool BridgeFileIsMine()
    {
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(BridgeFile));
            return d.RootElement.TryGetProperty("pid", out var p) && p.GetInt32() == Environment.ProcessId;
        }
        catch { return false; }
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); } catch { /* shutting down */ }
        try { _listener?.Stop(); } catch { /* shutting down */ }
        _listener = null;
        try { File.Delete(InstanceFile(Environment.ProcessId)); } catch { /* best effort */ }
        try
        {
            if (File.Exists(BridgeFile) && BridgeFileIsMine())
            {
                File.Delete(BridgeFile);
                // hand the target to another running instance, if there is one
                foreach (var f in Directory.GetFiles(BridgeInfo.DataFolder, "bridge-*.json"))
                    if (ReadInfo(f) is { } info && Alive(info.pid)) { WriteAtomically(BridgeFile, File.ReadAllText(f)); break; }
            }
        }
        catch { /* best effort */ }
    }

    public static string Describe() => _listener == null
        ? "Civil 3D MCP bridge: not running."
        : $"Civil 3D MCP bridge v{BridgeInfo.Version} listening on 127.0.0.1:{Port} (" +
          $"UI context: {(MainThread.HasUiContext ? "yes" : "NO")}; editing: {(WriteGate.Granted ? "allowed for the active drawing" : "not allowed yet")}). " +
          (IsTarget ? $"This instance is the MCP target. Connection details: {BridgeFile}"
                    : $"Another Civil 3D instance (pid {TargetInstance()?.pid}) is the MCP target; run MCPSTATUS here to make this one the target.");

    private static async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch { break; }   // cancelled or listener stopped
            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                Interlocked.Decrement(ref _connections);
                client.Dispose();
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await HandleClient(client, ct); }
                finally { Interlocked.Decrement(ref _connections); }
            });
        }
    }

    private static async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var utf8 = new UTF8Encoding(false);
                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };
                var lines = new LineReader(new StreamReader(stream, utf8));

                string? line;
                while (!ct.IsCancellationRequested && (line = await lines.ReadLineAsync(ct)) != null)
                {
                    if (line.Length == 0) continue;
                    await writer.WriteLineAsync(await ProcessAsync(line));
                }
            }
            catch { /* client went away, sent too much or too slowly; nothing to report */ }
        }
    }

    /// <summary>
    /// Reads request lines in blocks (not character by character), refusing a line over MaxRequestChars, a line that
    /// takes longer than ReadTimeout to arrive, and a connection idle for longer than IdleTimeout.
    /// </summary>
    private sealed class LineReader(StreamReader reader)
    {
        private readonly char[] _block = new char[16 * 1024];
        private int _start, _end;   // unread part of _block

        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            var line = new StringBuilder();
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timer.CancelAfter(IdleTimeout);
            bool started = false;
            while (true)
            {
                if (_start == _end)
                {
                    _start = 0;
                    _end = await reader.ReadAsync(_block.AsMemory(), timer.Token);
                    if (_end == 0) return null;   // Only dispatch complete newline-delimited requests.
                }
                if (!started) { started = true; timer.CancelAfter(ReadTimeout); }   // replaces the idle timer

                int newline = Array.IndexOf(_block, '\n', _start, _end - _start);
                int stop = newline < 0 ? _end : newline;
                line.Append(_block, _start, stop - _start);
                _start = newline < 0 ? _end : newline + 1;
                if (line.Length > MaxRequestChars) throw new InvalidDataException("request too large");
                if (newline >= 0) return line.ToString().TrimEnd('\r');
            }
        }
    }

    private static async Task<string> ProcessAsync(string line)
    {
        string? reqId = null;
        string method;
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(reqId, "invalid JSON request");
            if (root.TryGetProperty("id", out var idEl)) reqId = idEl.ToString();

            string supplied = root.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(_token)))
                return Error(reqId, "unauthorized");

            method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? "" : "";
            // Clone: the element must outlive 'doc' while the handler is queued on the main thread.
            args = root.TryGetProperty("params", out var p) ? p.Clone() : default;
        }
        catch (JsonException)
        {
            return Error(reqId, "invalid JSON request");
        }

        if (!Registry.TryGet(method, out var action))
            return Error(reqId, $"unknown method '{method}'. Call list_actions for the available ones.");

        try
        {
            if (method == "get_edit_status")
                return Success(reqId, action.Run(args));  // Request status remains available while Civil 3D is busy.
            if (action.Kind == "write")
            {
                var target = DrawingTarget.Read(args);
                var requestId = DrawingTarget.ReadId(args, "request_id");
                var result = await EditRequests.RunAsync(requestId, target, args, () =>
                    MainThread.RunAsync(() => action.Run(args), QueueTimeout));
                return Success(reqId, result);
            }

            if (method == "ping" && !Has(args, "target"))
            {
                // A connection check must answer even while Civil 3D's main thread is held (the MCP permission
                // dialog, a command, a long regen): after a short wait, answer with what is known off that thread.
                try { return Success(reqId, await MainThread.RunAsync(() => action.Run(args), PingWait)); }
                catch (TimeoutException) { return Success(reqId, DrawingRead.PingBusy()); }
            }

            var readTarget = Has(args, "target") ? DrawingTarget.Read(args) : (DrawingTarget?)null;
            var read = await MainThread.RunAsync(() =>
            {
                var doc = readTarget?.RequireActive() ?? AcApp.DocumentManager.MdiActiveDocument;
                var target = DrawingTarget.Describe(doc);
                return (Result: action.Run(args), Target: target);
            }, QueueTimeout);
            return Success(reqId, read.Result, read.Target);
        }
        catch (TimeoutException ex)
        {
            return Error(reqId, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(reqId, ErrorText(ex));
        }
    }

    private static string Success(string? id, object result, object? target = null) =>
        JsonSerializer.Serialize(new { id, ok = true, result, target }, Json.Out);

    private static string Error(string? id, string message) =>
        JsonSerializer.Serialize(new { id, ok = false, error = message });
}
