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
    private const int MaxRequestChars = 4 * 1024 * 1024;                       // 4 MB per request line (large edit batches)
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);   // a started request line must finish in 30 s
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);   // idle connections are dropped
    private const int MaxConnections = 16;
    private static int _connections;

    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static string _token = "";

    public static int Port => _listener?.LocalEndpoint is IPEndPoint ep ? ep.Port : 0;

    private static string BridgeFile => Path.Combine(BridgeInfo.DataFolder, "bridge.json");

    public static void Start()
    {
        if (_listener != null) return;

        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _listener = new TcpListener(IPAddress.Loopback, 0);   // 127.0.0.1 only, never 0.0.0.0
        _listener.Start();
        _cts = new CancellationTokenSource();

        WriteBridgeFile();

        var ct = _cts.Token;
        _ = Task.Run(() => AcceptLoop(ct));
    }

    /// <summary>Publishes this Civil 3D as the one Claude talks to (also called by MCPSTATUS when several are open).</summary>
    public static void WriteBridgeFile()
    {
        if (_listener == null) return;
        Directory.CreateDirectory(BridgeInfo.DataFolder);
        File.WriteAllText(BridgeFile, JsonSerializer.Serialize(new
        {
            port = Port,
            token = _token,
            pid = Environment.ProcessId,
            startedUtc = DateTime.UtcNow
        }));
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
        try { if (File.Exists(BridgeFile) && BridgeFileIsMine()) File.Delete(BridgeFile); } catch { /* best effort */ }
    }

    public static string Describe() => _listener == null
        ? "Civil 3D MCP bridge: not running."
        : $"Civil 3D MCP bridge v{BridgeInfo.Version} listening on 127.0.0.1:{Port} (" +
          $"UI context: {(MainThread.HasUiContext ? "yes" : "NO")}; editing: {(WriteGate.Granted ? "ALLOWED for this session" : "not allowed yet")}). " +
          $"Connection details: {BridgeFile}";

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
                    if (_end == 0) return line.Length == 0 ? null : line.ToString();   // connection closed
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
            var result = await MainThread.RunAsync(() => action.Run(args), QueueTimeout);
            return JsonSerializer.Serialize(new { id = reqId, ok = true, result }, Json.Out);
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

    private static string Error(string? id, string message) =>
        JsonSerializer.Serialize(new { id, ok = false, error = message });
}
