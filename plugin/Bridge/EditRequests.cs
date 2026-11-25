using System.Security.Cryptography;
using System.Text;

namespace C3dMcpBridge;

/// <summary>Keeps edit identities for the bridge session and caches recent full replies.</summary>
internal static class EditRequests
{
    private const int MaxRequests = 8192;
    private const long MaxReplyBytes = 32 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, Entry> Requests = new();
    private static readonly Queue<(Entry Entry, int Bytes)> Replies = new();
    private static long _replyBytes;
    private static int _active;   // edits (and dry runs) between arrival and reply; guarded by Gate

    public const string BusyMessage = "Another MCP edit is still running in Civil 3D (or waiting for the permission dialog). " +
                                      "Nothing was changed; send this edit again with the same request_id when it finishes.";

    /// <summary>True while an MCP edit is running or waiting for the permission dialog.</summary>
    public static bool EditRunning { get { lock (Gate) return _active > 0; } }

    /// <summary>
    /// Refuse a second edit at once while one is running. It cannot run anyway: the first holds Civil 3D's main thread
    /// (its permission dialog does not pump the queued work), so the second would only wait for the 30 s queue timeout.
    /// Caller holds Gate.
    /// </summary>
    private static void Enter()
    {
        if (_active > 0) throw new InvalidOperationException(BusyMessage);
        _active++;
    }

    private static void Leave() { lock (Gate) _active--; }

    private sealed class Entry(DrawingTarget target, string fingerprint, string? workflow)
    {
        public DrawingTarget Target { get; } = target;
        public string? Workflow { get; } = workflow;
        public string Fingerprint { get; } = fingerprint;
        public string State { get; set; } = "pending";
        public bool Retryable { get; set; }
        public bool? AfterCommitSucceeded { get; set; }
        public bool? Applied { get; set; }
        public string? Error { get; set; }
        public JsonElement? Result { get; set; }
    }

    public static async Task<object> RunAsync(Guid id, DrawingTarget target, JsonElement args, Func<Task<object>> run)
    {
        // A dry run changes nothing, so it needs no idempotency record. Recording it would make the real edit with the
        // same request_id fail the fingerprint check ("already identifies a different edit").
        if (BoolParam(args, "dry_run") == true)
        {
            lock (Gate) Enter();
            try { return await run(); }
            finally { Leave(); }
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(args.GetRawText())));
        Entry entry;
        lock (Gate)
        {
            if (Requests.TryGetValue(id, out var existing))
            {
                if (existing.Fingerprint != fingerprint)
                    throw new ArgumentException("This request_id already identifies a different edit. Use its original parameters or submit a new edit with a new ID.");
                if (existing.State != "error")
                    return existing.Result is JsonElement result ? result : Describe(id, existing);
                if (!existing.Retryable) return Describe(id, existing);
                Enter();
                existing.State = "pending";
                existing.Error = null;
                existing.Retryable = false;
                entry = existing;
            }
            else
            {
                if (Requests.Count >= MaxRequests)
                    throw new InvalidOperationException("The edit history for this Civil 3D session is full. Restart Civil 3D and read the drawing before starting new edits.");
                Enter();
                entry = new Entry(target, fingerprint, StrParam(args, "workflow"));
                Requests.Add(id, entry);
            }
        }

        try
        {
            object reply;
            try
            {
                reply = await run();
            }
            catch (Exception ex)
            {
                lock (Gate)
                {
                    entry.State = "error";
                    entry.Retryable = ex is CadQueueTimeoutException;
                    entry.Error = ErrorText(ex);
                }
                throw;
            }

            try
            {
                var result = JsonSerializer.SerializeToElement(reply, Json.Out);
                bool? applied = Flag(result, "applied");
                int size = Encoding.UTF8.GetByteCount(result.GetRawText());
                lock (Gate)
                {
                    entry.State = "completed";
                    entry.Applied = applied;
                    entry.AfterCommitSucceeded = Flag(result, "afterCommitSucceeded");
                    if (size <= MaxReplyBytes)
                    {
                        entry.Result = result;
                        Replies.Enqueue((entry, size));
                        _replyBytes += size;
                        while (_replyBytes > MaxReplyBytes)
                        {
                            var expired = Replies.Dequeue();
                            expired.Entry.Result = null;
                            _replyBytes -= expired.Bytes;
                        }
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                // The edit ran but its reply could not be recorded: never run it again under this ID.
                lock (Gate)
                {
                    entry.State = "completed";
                    entry.Error = "The edit finished but its reply could not be recorded: " + ErrorText(ex);
                }
                throw;
            }
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>A true/false property of a reply; null when absent or not a boolean (never throws).</summary>
    private static bool? Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    public static object Status(JsonElement p)
    {
        var target = DrawingTarget.Read(p);
        var id = DrawingTarget.ReadId(p, "request_id");
        lock (Gate)
        {
            if (!Requests.TryGetValue(id, out var entry)) return new { request_id = id, state = "unknown" };
            if (entry.Target != target) throw new ArgumentException("This request_id belongs to a different drawing.");
            return Describe(id, entry);
        }
    }

    private static object Describe(Guid id, Entry entry) => new
    {
        request_id = id,
        state = entry.State,
        retryable = entry.Retryable,
        workflow = entry.Workflow,
        applied = entry.Applied,
        afterCommitSucceeded = entry.AfterCommitSucceeded,
        result = entry.Result,
        error = entry.Error,
        resultExpired = entry.State == "completed" && entry.Result == null,
    };
}
