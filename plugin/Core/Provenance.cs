namespace C3dMcpBridge;

/// <summary>
/// Import provenance (roadmap #72): the source model, the element ID in it and the import run, stored on a drawing
/// object as XData under the registered application C3DMCP. Lets later checks match drawing objects to model elements by
/// ID instead of by name. XData travels with copies of the object and survives save / reopen.
/// </summary>
internal static class Provenance
{
    public const string App = "C3DMCP";
    private const int MaxText = 250;   // XData string limit is 255 bytes

    internal sealed record Tag(string Source, string Id, string? Run);

    private static void EnsureApp(Transaction tr, Database db)
    {
        var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (table.Has(App)) return;
        table.UpgradeOpen();
        var rec = new RegAppTableRecord { Name = App };
        table.Add(rec);
        tr.AddNewlyCreatedDBObject(rec, true);
    }

    private static string Clip(string text, string what)
    {
        if (text.Length == 0) throw new ArgumentException($"{what} is empty.");
        if (text.Length > MaxText) throw new ArgumentException($"{what} is longer than {MaxText} characters.");
        return text;
    }

    public static void Write(Transaction tr, Database db, DBObject obj, Tag tag)
    {
        EnsureApp(tr, db);
        if (!obj.IsWriteEnabled) obj.UpgradeOpen();
        var values = new List<TypedValue>
        {
            new((int)DxfCode.ExtendedDataRegAppName, App),
            new((int)DxfCode.ExtendedDataAsciiString, "source=" + Clip(tag.Source, "source")),
            new((int)DxfCode.ExtendedDataAsciiString, "id=" + Clip(tag.Id, "source id")),
        };
        if (!string.IsNullOrEmpty(tag.Run)) values.Add(new((int)DxfCode.ExtendedDataAsciiString, "run=" + Clip(tag.Run!, "run")));
        using var rb = new ResultBuffer(values.ToArray());
        obj.XData = rb;   // replaces only this application's XData
    }

    public static Tag? Read(DBObject obj)
    {
        using var rb = obj.GetXDataForApplication(App);
        if (rb == null) return null;
        string? source = null, id = null, run = null;
        foreach (TypedValue tv in rb)
        {
            if (tv.TypeCode != (int)DxfCode.ExtendedDataAsciiString || tv.Value is not string s) continue;
            int eq = s.IndexOf('=');
            if (eq <= 0) continue;
            string k = s.Substring(0, eq), v = s.Substring(eq + 1);
            if (k == "source") source = v; else if (k == "id") id = v; else if (k == "run") run = v;
        }
        return source != null && id != null ? new Tag(source, id, run) : null;
    }

    public static void Clear(DBObject obj)
    {
        if (obj.GetXDataForApplication(App) == null) return;
        if (!obj.IsWriteEnabled) obj.UpgradeOpen();
        using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App));
        obj.XData = rb;   // an application name with no data removes that application's XData
    }

    /// <summary>A tag from a spec: 'source_id' on the item, 'source' and 'run' on the item or the operation.</summary>
    public static Tag? FromSpec(JsonElement item, JsonElement op)
    {
        string? id = StrParam(item, "source_id");
        if (id == null) return null;
        string source = StrParam(item, "source") ?? StrParam(op, "source")
                        ?? throw new ArgumentException("source_id needs 'source' (e.g. 'SewerGEMS: model.xlsx') on the item or the operation.");
        return new Tag(source, id, StrParam(item, "run") ?? StrParam(op, "run"));
    }
}
