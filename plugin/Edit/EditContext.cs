using AcColors = Autodesk.AutoCAD.Colors;

namespace C3dMcpBridge;

/// <summary>What an edit operation works with: the locked drawing and the single transaction of its batch.</summary>
internal sealed class EditContext
{
    public required Document Doc { get; init; }
    public required Transaction Tr { get; init; }
    public Database Db => Doc.Database;
    public CivilDoc Civ => CivilApp.ActiveDocument;
    public List<string> Warnings { get; } = new();
    public int OpIndex { get; set; }

    /// <summary>Work that must run after the transaction is committed (saving, plotting). Skipped on dry runs.</summary>
    public List<Action> AfterCommit { get; } = new();

    public void Warn(string message) => Warnings.Add($"operation {OpIndex}: {message}");

    // ---------------------------------------------------------------- helpers shared by the operations

    /// <summary>The 'color' parameter as an AutoCAD colour index (1-255).</summary>
    public static AcColors.Color Aci(JsonElement p)
    {
        int n = IntParam(p, "color", -1, -1, 256);
        if (n < 1 || n > 255) throw new ArgumentException("color must be an AutoCAD colour index from 1 to 255.");
        return AcColors.Color.FromColorIndex(AcColors.ColorMethod.ByAci, (short)n);
    }

    /// <summary>The layer named by the 'layer' parameter; the current layer when it is absent.</summary>
    public ObjectId Layer(JsonElement p) => LayerId(Db, Tr, StrParam(p, "layer"));

    /// <summary>Add a new entity to model space, on 'layer' when given.</summary>
    public ObjectId Append(Entity e, string? layer = null)
    {
        e.SetDatabaseDefaults(Db);
        if (!string.IsNullOrEmpty(layer)) e.LayerId = LayerId(Db, Tr, layer);
        var id = ModelSpace(Db, Tr, OpenMode.ForWrite).AppendEntity(e);
        Tr.AddNewlyCreatedDBObject(e, true);
        return id;
    }

    /// <summary>Add a new entity to model space, applying the operation's 'layer' and 'color' when given.</summary>
    public ObjectId Append(Entity e, JsonElement p)
    {
        var id = Append(e, StrParam(p, "layer"));
        if (Has(p, "color")) e.Color = Aci(p);
        return id;
    }

    /// <summary>
    /// The entities named by the 'handles' parameter, opened for write. A locked layer or an xref object fails the
    /// whole batch with a clear message. plainOnlyFor: name of an operation that must not touch Civil 3D objects.
    /// </summary>
    public List<Entity> OpenEntities(JsonElement p, string? plainOnlyFor = null)
    {
        var list = new List<Entity>();
        foreach (var h in StrList(p, "handles", 5000))
        {
            var id = IdFromHandle(Db, h);
            Entity e;
            try
            {
                e = Tr.GetObject(id, OpenMode.ForWrite) as Entity ?? throw new ArgumentException($"Object {h} is not a drawing entity.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new InvalidOperationException($"Object {h} ({DxfOrName(id)}) cannot be modified: {ex.Message}");
            }
            if (plainOnlyFor != null && e is CivilDb.Entity)
                throw new ArgumentException($"{plainOnlyFor} works on plain AutoCAD entities only; {h} is a Civil 3D object ({DxfOrName(id)}).");
            list.Add(e);
        }
        return list;
    }

    /// <summary>Open one object by the 'handle' parameter for write, as the expected type.</summary>
    public T OpenByHandle<T>(JsonElement p, string expected) where T : DBObject
    {
        string handle = NeedStr(p, "handle");
        return Tr.GetObject(IdFromHandle(Db, handle), OpenMode.ForWrite) as T
               ?? throw new ArgumentException($"Object {handle} is not {expected}.");
    }
}
