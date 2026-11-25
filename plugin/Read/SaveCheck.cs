namespace C3dMcpBridge;

/// <summary>
/// diagnose_save: finds what would stop the drawing from being saved ("object cannot be saved ... no file was created").
/// Read only: the drawing is not changed. Three checks:
///   1. header: the current layer / text style / dimension style / ... must point at live objects;
///   2. scan: every object is written through a probe DWG filer (DBObject.DwgOut with a CopyFiler). An object whose
///      writing throws or sets a filer error is what breaks the save; an object that still points (hard) at an erased or
///      invalid object is the usual cause. A FileFiler is never used: native objects written to a managed FileFiler
///      crashed Civil 3D 2027 within the first objects of an empty drawing (live test 2026-11-25);
///   3. save_copy: a copy of the drawing is written to %TEMP% (Wblock + SaveAs) and deleted again, the real end-to-end check.
/// </summary>
internal static class SaveCheck
{
    /// <summary>A DWG filer that stores nothing and records every object id that is written pointing at an erased or
    /// invalid object. Reading is never asked of it (DwgOut only writes).</summary>
    private sealed class ProbeFiler : DwgFiler
    {
        private readonly FilerType _type;
        private Autodesk.AutoCAD.Runtime.ErrorStatus _status = Autodesk.AutoCAD.Runtime.ErrorStatus.OK;
        public readonly List<(string kind, ObjectId id)> BadRefs = new();

        public ProbeFiler(FilerType type) { _type = type; }

        public void Reset() { BadRefs.Clear(); _status = Autodesk.AutoCAD.Runtime.ErrorStatus.OK; }

        private void Ref(string kind, ObjectId id)
        {
            if (id.IsNull) return;
            if (!id.IsValid || id.IsErased) BadRefs.Add((kind, id));
        }

        public override Autodesk.AutoCAD.Runtime.ErrorStatus FilerStatus { get => _status; set => _status = value; }
        public override FilerType FilerType => _type;
        public override FullDwgVersion DwgVersion =>
            new(Autodesk.AutoCAD.DatabaseServices.DwgVersion.Current, MaintenanceReleaseVersion.ReleaseCurrent);
        public override MaintenanceReleaseVersion ExtendedMinorVersion => MaintenanceReleaseVersion.ReleaseCurrent;
        public override long Position => 0;
        public override void ResetFilerStatus() => _status = Autodesk.AutoCAD.Runtime.ErrorStatus.OK;
        public override void Seek(long offset, int method) { }

        public override void WriteHardOwnershipId(ObjectId value) => Ref("hard owner", value);
        public override void WriteHardPointerId(ObjectId value) => Ref("hard pointer", value);
        public override void WriteSoftOwnershipId(ObjectId value) => Ref("soft owner", value);
        public override void WriteSoftPointerId(ObjectId value) => Ref("soft pointer", value);
        public override void WriteAddress(IntPtr value) { }
        public override void WriteBinaryChunk(byte[] chunk) { }
        public override void WriteBoolean(bool value) { }
        public override void WriteByte(byte value) { }
        public override void WriteBytes(byte[] value) { }
        public override void WriteDouble(double value) { }
        public override void WriteHandle(Handle handle) { }
        public override void WriteInt16(short value) { }
        public override void WriteInt32(int value) { }
        public override void WriteInt64(long value) { }
        public override void WritePoint2d(Point2d value) { }
        public override void WritePoint3d(Point3d value) { }
        public override void WriteScale3d(Scale3d value) { }
        public override void WriteString(string value) { }
        public override void WriteUInt16(ushort value) { }
        public override void WriteUInt32(uint value) { }
        public override void WriteUInt64(ulong value) { }
        public override void WriteVector2d(Vector2d value) { }
        public override void WriteVector3d(Vector3d value) { }

        private static NotSupportedException NoRead() => new("The save probe only writes.");
        public override IntPtr ReadAddress() => throw NoRead();
        public override byte[] ReadBinaryChunk() => throw NoRead();
        public override bool ReadBoolean() => throw NoRead();
        public override byte ReadByte() => throw NoRead();
        public override void ReadBytes(byte[] value) => throw NoRead();
        public override double ReadDouble() => throw NoRead();
        public override Handle ReadHandle() => throw NoRead();
        public override ObjectId ReadHardOwnershipId() => throw NoRead();
        public override ObjectId ReadHardPointerId() => throw NoRead();
        public override short ReadInt16() => throw NoRead();
        public override int ReadInt32() => throw NoRead();
        public override long ReadInt64() => throw NoRead();
        public override Point2d ReadPoint2d() => throw NoRead();
        public override Point3d ReadPoint3d() => throw NoRead();
        public override Scale3d ReadScale3d() => throw NoRead();
        public override ObjectId ReadSoftOwnershipId() => throw NoRead();
        public override ObjectId ReadSoftPointerId() => throw NoRead();
        public override string ReadString() => throw NoRead();
        public override ushort ReadUInt16() => throw NoRead();
        public override uint ReadUInt32() => throw NoRead();
        public override ulong ReadUInt64() => throw NoRead();
        public override Vector2d ReadVector2d() => throw NoRead();
        public override Vector3d ReadVector3d() => throw NoRead();
    }

    public static object DiagnoseSave(JsonElement p)
    {
        int limit = IntParam(p, "limit", 100, 1, 2000);
        int maxObjects = IntParam(p, "max_objects", 2_000_000, 1, 20_000_000);
        bool scan = BoolParam(p, "scan") ?? true;
        bool saveCopy = BoolParam(p, "save_copy") ?? true;
        bool softRefs = BoolParam(p, "soft_refs") ?? false;
        // Only the copy filer: a managed FileFiler handed to DwgOut crashes Civil 3D (native objects expect AutoCAD's own).
        if (StrParam(p, "filer") is { Length: > 0 } fl && !string.Equals(fl, "copy", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("diagnose_save: filer must be 'copy' (a probe file filer crashes Civil 3D). " +
                                        "save_copy writes a real copy to %TEMP% as the end-to-end save check.");
        var filerType = FilerType.CopyFiler;
        var doc = ActiveDoc();
        var db = doc.Database;

        var header = Header(db);
        bool scanOk = true, copyOk = true;
        object? scanResult = scan ? Scan(db, filerType, limit, maxObjects, softRefs, out scanOk) : null;
        object? copy = saveCopy ? SaveCopy(db, out copyOk) : null;
        bool headerOk = header.All(h => h.ok);

        return new
        {
            drawing = Path.GetFileName(doc.Name),
            savable = headerOk && scanOk && copyOk,
            header, headerOk,
            scan = scanResult,
            saveCopy = copy,
            activeTransactions = Safe(() => db.TransactionManager.NumberOfActiveTransactions),
            settings = new
            {
                named = doc.IsNamedDrawing,
                modified = Safe(() => CoreApp.GetSystemVariable("DBMOD")),
                autosaveMinutes = Safe(() => CoreApp.GetSystemVariable("SAVETIME")),
                autosaveFolder = Safe(() => CoreApp.GetSystemVariable("SAVEFILEPATH")),
            },
            note = "Read only. scan.failures: objects whose writing fails (what blocks a save). scan.danglingHard: objects that " +
                   "still point (hard) at an erased / invalid object. saveCopy writes a copy to %TEMP% and deletes it.",
        };
    }

    private sealed record HeaderRef(string variable, string? handle, string status, object? name, bool ok);

    private static List<HeaderRef> Header(Database db) => WithReadDb(db, tr =>
    {
        HeaderRef Check(string variable, Func<ObjectId> get)
        {
            ObjectId id;
            try { id = get(); }
            catch (Exception ex) { return new HeaderRef(variable, null, "unreadable: " + ex.Message, null, false); }
            if (id.IsNull) return new HeaderRef(variable, null, "null", null, variable is "CMATERIAL" or "CDETAILVIEWSTYLE" or "CSECTIONVIEWSTYLE");
            if (!id.IsValid) return new HeaderRef(variable, H(id), "invalid", null, false);
            if (id.IsErased) return new HeaderRef(variable, H(id), "erased", Safe(() => DxfOrName(id)), false);
            return new HeaderRef(variable, H(id), "ok", Safe(() => (tr.GetObject(id, OpenMode.ForRead) as SymbolTableRecord)?.Name ?? DxfOrName(id)), true);
        }
        var rows = new List<HeaderRef>
        {
            Check("CLAYER", () => db.Clayer), Check("TEXTSTYLE", () => db.Textstyle), Check("DIMSTYLE", () => db.Dimstyle),
            Check("CELTYPE", () => db.Celtype), Check("CMATERIAL", () => db.Cmaterial), Check("CMLEADERSTYLE", () => db.MLeaderstyle),
            Check("CTABLESTYLE", () => db.Tablestyle), Check("CDETAILVIEWSTYLE", () => db.DetailViewStyle),
            Check("CSECTIONVIEWSTYLE", () => db.SectionViewStyle),
        };
        try
        {
            var scale = db.Cannoscale;
            bool known = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES").HasContext(scale.Name);
            rows.Add(new HeaderRef("CANNOSCALE", null, known ? "ok" : "not in the annotation scale list", scale.Name, known));
        }
        catch (Exception ex) { rows.Add(new HeaderRef("CANNOSCALE", null, "unreadable: " + ex.Message, null, false)); }
        return rows;
    });

    private static T WithReadDb<T>(Database db, Func<Transaction, T> body)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var result = body(tr);
        tr.Commit();
        return result;
    }

    private static object Describe(Transaction tr, ObjectId id, DBObject? obj) => new
    {
        handle = H(id),
        type = Safe(() => DxfOrName(id)),
        cls = obj?.GetType().FullName,
        civilName = obj is CivilDb.Entity ce ? Safe(() => ce.Name) : obj is SymbolTableRecord str ? str.Name : null,
        owner = obj == null || obj.OwnerId.IsNull ? null : new { handle = H(obj.OwnerId), type = Safe(() => DxfOrName(obj.OwnerId)) },
        layer = obj is Entity e ? Safe(() => e.Layer) : null,
    };

    private static object Scan(Database db, FilerType filerType, int limit, int maxObjects, bool softRefs, out bool clean)
    {
        var failures = new List<object>();
        var dangling = new List<object>();
        var softDangling = new List<object>();
        var failTypes = new Dictionary<string, int>();
        var danglingTypes = new Dictionary<string, int>();
        int failCount = 0, hardCount = 0, softCount = 0, checkedCount = 0, unopenable = 0;
        long seed = db.Handseed.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
        using (var filer = new ProbeFiler(filerType))
        {
            for (long h = 1; h < seed && checkedCount < maxObjects; h++)
            {
                if (!db.TryGetObjectId(new Handle(h), out ObjectId id) || id.IsNull || id.IsErased) continue;
                checkedCount++;
                DBObject obj;
                try { obj = tr.GetObject(id, OpenMode.ForRead, false); }
                catch (Exception ex)
                {
                    unopenable++;
                    if (failures.Count < limit) failures.Add(new { stage = "open", error = ErrorText(ex), @object = Describe(tr, id, null) });
                    continue;
                }
                filer.Reset();
                string? error = null;
                try
                {
                    obj.DwgOut(filer);
                    if (filer.FilerStatus != Autodesk.AutoCAD.Runtime.ErrorStatus.OK) error = "filer status " + filer.FilerStatus;
                }
                catch (Exception ex) { error = ErrorText(ex); }
                string type = DxfOrName(id);
                if (error != null)
                {
                    failCount++;
                    failTypes[type] = failTypes.TryGetValue(type, out var n) ? n + 1 : 1;
                    if (failures.Count < limit) failures.Add(new { stage = "write", error, @object = Describe(tr, id, obj) });
                }
                foreach (var (kind, target) in filer.BadRefs)
                {
                    bool hard = kind.StartsWith("hard", StringComparison.Ordinal);
                    var row = new
                    {
                        reference = kind, @object = Describe(tr, id, obj),
                        target = new { handle = H(target), type = Safe(() => DxfOrName(target)), erased = Safe(() => target.IsErased), valid = target.IsValid },
                    };
                    if (hard)
                    {
                        hardCount++;
                        danglingTypes[type] = danglingTypes.TryGetValue(type, out var n) ? n + 1 : 1;
                        if (dangling.Count < limit) dangling.Add(row);
                    }
                    else
                    {
                        softCount++;
                        if (softRefs && softDangling.Count < limit) softDangling.Add(row);
                    }
                }
            }
            tr.Commit();
        }
        clean = failCount == 0 && unopenable == 0 && hardCount == 0;
        return new
        {
            clean, filer = filerType.ToString(), objectsChecked = checkedCount, handseed = seed,
            stoppedAtMaxObjects = checkedCount >= maxObjects, seconds = Math.Round(sw.Elapsed.TotalSeconds, 1),
            failureCount = failCount, unopenable, failuresByType = failTypes, failures,
            danglingHardCount = hardCount, danglingHardByType = danglingTypes, danglingHard = dangling,
            danglingSoftCount = softCount, danglingSoft = softRefs ? softDangling : null,
        };
    }

    private static object SaveCopy(Database db, out bool ok)
    {
        ok = false;
        string file = Path.Combine(Path.GetTempPath(), $"c3dmcp-savecheck-{Guid.NewGuid():N}.dwg");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using (var copy = db.Wblock()) copy.SaveAs(file, DwgVersion.Current);
            long bytes = File.Exists(file) ? new FileInfo(file).Length : 0;
            ok = bytes > 0;
            return new { ok = bytes > 0, bytes, seconds = Math.Round(sw.Elapsed.TotalSeconds, 1), error = bytes > 0 ? null : "no file was written" };
        }
        catch (Exception ex)
        {
            return new { ok = false, bytes = 0L, seconds = Math.Round(sw.Elapsed.TotalSeconds, 1), error = ErrorText(ex) };
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* temp file: Windows cleans it up */ }
        }
    }
}
