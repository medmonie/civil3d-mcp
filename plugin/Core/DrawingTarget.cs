namespace C3dMcpBridge;

/// <summary>Identifies a specific open drawing in a specific bridge session.</summary>
internal readonly record struct DrawingTarget(Guid SessionId, Guid DrawingId)
{
    private static readonly object UserDataKey = new();

    public static Guid Id(Document doc)
    {
        // UserData lasts for this open document and follows it through Save As.
        var data = doc.UserData;
        if (data[UserDataKey] is Guid id) return id;
        id = Guid.NewGuid();
        data[UserDataKey] = id;
        return id;
    }

    public static object? Describe(Document? doc) => doc == null ? null : new
    {
        session_id = BridgeServer.SessionId,
        drawing_id = Id(doc),
    };

    public static Guid ReadId(JsonElement p, string name) =>
        Guid.TryParse(StrParam(p, name), out var id) && id != Guid.Empty
            ? id : throw new ArgumentException($"'{name}' must be a non-empty UUID.");

    public static DrawingTarget Read(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("target", out var target) ||
            target.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A drawing target is required. Read get_drawing_info and reuse its target.");
        var result = new DrawingTarget(ReadId(target, "session_id"), ReadId(target, "drawing_id"));
        if (result.SessionId != BridgeServer.SessionId)
            throw new InvalidOperationException("The Civil 3D bridge session changed. Reopen the intended drawing and read it again.");
        return result;
    }

    public Document RequireActive()
    {
        var doc = ActiveDoc();
        if (Id(doc) != DrawingId)
            throw new InvalidOperationException("The active drawing is different from the request target. Activate the intended drawing and read it again.");
        return doc;
    }
}
