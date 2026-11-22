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

    private sealed class Entry(DrawingTarget target, string fingerprint)
    {
        public DrawingTarget Target { get; } = target;
        public string Fingerprint { get; } = fingerprint;
        public string State { get; set; } = "pending";
        public bool? Applied { get; set; }
        public string? Error { get; set; }
        public JsonElement? Result { get; set; }
    }

    public static async Task<object> RunAsync(Guid id, DrawingTarget target, JsonElement args, Func<Task<object>> run)
    {
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
                // An edit that ended in an exception changed nothing: failures inside the batch are returned as
                // results, and exceptions come from checks before the transaction or from a queue timeout before
                // the work started. So the same request may run again, e.g. after Civil 3D was busy.
                existing.State = "pending";
                existing.Error = null;
                entry = existing;
            }
            else
            {
                if (Requests.Count >= MaxRequests)
                    throw new InvalidOperationException("This bridge session reached its edit request limit. Restart Civil 3D to start a new session.");
                entry = new Entry(target, fingerprint);
                Requests.Add(id, entry);
            }
        }

        object reply;
        try
        {
            reply = await run();
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                entry.State = "error";   // nothing was applied, so this request_id may be sent again
                entry.Error = ErrorText(ex);
            }
            throw;
        }

        try
        {
            var result = JsonSerializer.SerializeToElement(reply, Json.Out);
            bool? applied = result.TryGetProperty("applied", out var value) ? value.GetBoolean() : null;
            int size = Encoding.UTF8.GetByteCount(result.GetRawText());
            lock (Gate)
            {
                entry.State = "completed";
                entry.Applied = applied;
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
        applied = entry.Applied,
        result = entry.Result,
        error = entry.Error,
        resultExpired = entry.State == "completed" && entry.Result == null,
    };
}
