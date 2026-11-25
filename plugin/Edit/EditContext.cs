using AcColors = Autodesk.AutoCAD.Colors;

namespace C3dMcpBridge;

/// <summary>What an edit operation works with: the locked drawing and the single transaction of its batch.</summary>
internal sealed class EditContext
{
    public required Document Doc { get; init; }
    public required Transaction Tr { get; init; }
    public Database Db => Doc.Database;
    public CivilDoc Civ => CivilDoc.GetCivilDocument(Db);
    public List<string> Warnings { get; } = new();
    public int OpIndex { get; set; }

    /// <summary>Work that must run after the transaction is committed (saving, plotting). Skipped on dry runs.</summary>
    public List<Action> AfterCommit { get; } = new();

    /// <summary>
    /// Work that must run once the batch's transaction is gone, whatever the outcome (committed, rolled back on an error,
    /// or a dry run). Runs inside the document lock. Used to release objects Civil 3D leaves open (section_volume_tables).
    /// </summary>
    public List<Action> AfterEnd { get; } = new();

    /// <summary>
    /// Work that undoes changes the transaction cannot roll back itself (Civil 3D profile PVIs). Runs newest first,
    /// inside the transaction, just before it is aborted (a failed batch or a dry run). Never runs after a commit.
    /// </summary>
    public List<Action> BeforeAbort { get; } = new();

    private const int MaxWarnings = 200;
    private int _suppressed;

    /// <summary>Record a warning. After MaxWarnings, further ones are only counted (one summary line, kept up to date).</summary>
    public void Warn(string message)
    {
        if (Warnings.Count < MaxWarnings) { Warnings.Add($"operation {OpIndex}: {message}"); return; }
        string summary = $"... {++_suppressed} more warning(s) not listed.";
        if (_suppressed == 1) Warnings.Add(summary); else Warnings[^1] = summary;
    }

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

    /// <summary>
    /// Plan labels of model space of the given label class (and its subclasses), by the part they label. Used so that a
    /// repeated label operation does not stack a second label on a part that already has one.
    /// </summary>
    public Dictionary<ObjectId, List<ObjectId>> LabelsByPart<TLabel>() where TLabel : CivilDb.Label
    {
        var cls = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(TLabel));
        var map = new Dictionary<ObjectId, List<ObjectId>>();
        foreach (ObjectId id in ModelSpace(Db, Tr, OpenMode.ForRead))
        {
            if (id.IsErased || !id.ObjectClass.IsDerivedFrom(cls)) continue;
            if (Tr.GetObject(id, OpenMode.ForRead) is not CivilDb.Label label) continue;
            var part = label.FeatureId;
            if (part.IsNull) continue;
            if (!map.TryGetValue(part, out var list)) map[part] = list = new List<ObjectId>();
            list.Add(id);
        }
        return map;
    }

    /// <summary>Add a new entity to model space (or the given space), on 'layer' when given.</summary>
    public ObjectId Append(Entity e, string? layer = null, BlockTableRecord? space = null)
    {
        e.SetDatabaseDefaults(Db);
        if (!string.IsNullOrEmpty(layer)) e.LayerId = LayerId(Db, Tr, layer);
        var id = (space ?? ModelSpace(Db, Tr, OpenMode.ForWrite)).AppendEntity(e);
        Tr.AddNewlyCreatedDBObject(e, true);
        return id;
    }

    /// <summary>Add a new entity to model space, or to the paper space of the operation's 'layout', applying its
    /// 'layer' and 'color' when given.</summary>
    public ObjectId Append(Entity e, JsonElement p)
    {
        var id = Append(e, StrParam(p, "layer"), Space(p));
        if (Has(p, "color")) e.Color = Aci(p);
        return id;
    }

    /// <summary>A named record of a symbol table (text styles, dimension styles, linetypes, views...).</summary>
    public ObjectId Symbol(ObjectId tableId, string name, string what)
    {
        var table = (SymbolTable)Tr.GetObject(tableId, OpenMode.ForRead);
        return table.Has(name) ? table[name] : throw new ArgumentException($"{what} '{name}' not found. Use list_cad_styles.");
    }

    /// <summary>A multileader style by name.</summary>
    public ObjectId MLeaderStyleId(string name)
    {
        var dict = (DBDictionary)Tr.GetObject(Db.MLeaderStyleDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry e in dict) if (SameName(e.Key, name)) return e.Value;
        throw new ArgumentException($"Multileader style '{name}' not found. Use list_cad_styles.");
    }

    /// <summary>The block a new entity goes into: model space, or the paper space of the 'layout' parameter.</summary>
    public BlockTableRecord Space(JsonElement p)
    {
        string? layout = StrParam(p, "layout");
        if (layout == null || SameName(layout, "Model")) return ModelSpace(Db, Tr, OpenMode.ForWrite);
        return (BlockTableRecord)Tr.GetObject(FindLayout(Db, Tr, layout).BlockTableRecordId, OpenMode.ForWrite);
    }

    /// <summary>
    /// The entities named by the 'handles' parameter, opened for write. A locked layer or an xref object fails the
    /// whole batch with a clear message. plainOnlyFor: name of an operation that must not touch Civil 3D objects.
    /// </summary>
    public List<Entity> OpenEntities(JsonElement p, string? plainOnlyFor = null)
    {
        var list = new List<Entity>();
        var seen = new HashSet<ObjectId>();
        foreach (var h in StrList(p, "handles", 5000))
        {
            var id = IdFromHandle(Db, h);
            if (!seen.Add(id)) continue;
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
