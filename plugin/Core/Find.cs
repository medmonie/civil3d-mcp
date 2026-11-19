using System.Collections;

namespace C3dMcpBridge;

/// <summary>
/// Finding Civil 3D objects by name (case-insensitive). Every finder opens for read and upgrades to write only the
/// object it returns. "Find..." throws a message that names the list action to use; "TryFind..." returns null.
/// </summary>
internal static class Find
{
    /// <summary>The object named 'name' among 'ids', or null.</summary>
    public static T? TryFind<T>(Transaction tr, IEnumerable ids, string name, OpenMode mode = OpenMode.ForRead) where T : CivilDb.Entity
    {
        foreach (ObjectId id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not T e || !SameName(e.Name, name)) continue;
            if (mode == OpenMode.ForWrite) e.UpgradeOpen();
            return e;
        }
        return null;
    }

    private static T Need<T>(T? found, string what, string name, string listAction) where T : class =>
        found ?? throw new ArgumentException($"{what} '{name}' not found. Use {listAction} for names.");

    /// <summary>Throws when an object of that name already exists among 'ids' (used before creating one).</summary>
    public static void RequireNewName<T>(Transaction tr, IEnumerable ids, string name, string what, string hint = "") where T : CivilDb.Entity
    {
        if (TryFind<T>(tr, ids, name) != null)
            throw new ArgumentException($"{what} named '{name}' already exists{hint}.");
    }

    // ---------------------------------------------------------------- id collections

    /// <summary>Siteless alignments plus the alignments owned by each site.</summary>
    public static List<ObjectId> AllAlignmentIds(CivilDoc civ, Transaction tr)
    {
        var ids = civ.GetSitelessAlignmentIds().Cast<ObjectId>().ToList();
        foreach (ObjectId siteId in civ.GetSiteIds())
            ids.AddRange(((CivilDb.Site)tr.GetObject(siteId, OpenMode.ForRead)).GetAlignmentIds().Cast<ObjectId>());
        return ids;
    }

    /// <summary>Every alignment, opened for read; 'only' restricts it to one name.</summary>
    public static IEnumerable<CivilDb.Alignment> Alignments(CivilDoc civ, Transaction tr, string? only = null) =>
        AllAlignmentIds(civ, tr).Select(id => (CivilDb.Alignment)tr.GetObject(id, OpenMode.ForRead))
                                .Where(a => only == null || SameName(a.Name, only));

    public static IEnumerable<CivilDb.SampleLineGroup> SampleLineGroups(Transaction tr, CivilDb.Alignment a) =>
        a.GetSampleLineGroupIds().Cast<ObjectId>().Select(id => (CivilDb.SampleLineGroup)tr.GetObject(id, OpenMode.ForRead));

    public static List<ObjectId> PressureNetworkIds(CivilDoc civ) =>
        Autodesk.Civil.ApplicationServices.CivilDocumentPressurePipesExtension.GetPressurePipeNetworkIds(civ).Cast<ObjectId>().ToList();

    // ---------------------------------------------------------------- by name

    public static CivilDb.Alignment FindAlignment(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        Need(TryFind<CivilDb.Alignment>(tr, AllAlignmentIds(civ, tr), name, mode), "Alignment", name, "list_alignments");

    public static CivilDb.Profile FindProfile(Transaction tr, CivilDb.Alignment a, string name, OpenMode mode = OpenMode.ForRead) =>
        TryFind<CivilDb.Profile>(tr, a.GetProfileIds(), name, mode)
        ?? throw new ArgumentException($"Profile '{name}' not found on alignment '{a.Name}'. Use list_profiles for names.");

    public static CivilDb.ProfileView FindProfileView(CivilDoc civ, Transaction tr, string name)
    {
        foreach (var a in Alignments(civ, tr))
            if (TryFind<CivilDb.ProfileView>(tr, a.GetProfileViewIds(), name) is { } pv) return pv;
        throw new ArgumentException($"Profile view '{name}' not found. Use list_profile_views for names.");
    }

    public static CivilDb.SampleLineGroup? TryFindSampleLineGroup(Transaction tr, CivilDb.Alignment a, string name, OpenMode mode = OpenMode.ForRead) =>
        TryFind<CivilDb.SampleLineGroup>(tr, a.GetSampleLineGroupIds(), name, mode);

    public static CivilDb.Surface FindSurface(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        Need(TryFind<CivilDb.Surface>(tr, civ.GetSurfaceIds(), name, mode), "Surface", name, "list_surfaces");

    /// <summary>A TIN surface opened for write (points, breaklines and boundaries only exist on TIN surfaces).</summary>
    public static CivilDb.TinSurface FindTinForWrite(CivilDoc civ, Transaction tr, string name) =>
        FindSurface(civ, tr, name, OpenMode.ForWrite) as CivilDb.TinSurface
        ?? throw new ArgumentException($"Surface '{name}' is not a TIN surface.");

    public static CivilDb.Site? TryFindSite(CivilDoc civ, Transaction tr, string name) =>
        TryFind<CivilDb.Site>(tr, civ.GetSiteIds(), name);

    public static CivilDb.Network FindPipeNetwork(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        Need(TryFind<CivilDb.Network>(tr, civ.GetPipeNetworkIds(), name, mode), "Gravity pipe network", name, "list_pipe_networks");

    public static CivilDb.PressurePipeNetwork? TryFindPressureNetwork(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        TryFind<CivilDb.PressurePipeNetwork>(tr, PressureNetworkIds(civ), name, mode);

    public static CivilDb.PressurePipeNetwork FindPressureNetwork(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        Need(TryFindPressureNetwork(civ, tr, name, mode), "Pressure network", name, "list_pressure_networks");

    public static CivilDb.Corridor FindCorridor(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        Need(TryFind<CivilDb.Corridor>(tr, civ.CorridorCollection, name, mode), "Corridor", name, "list_corridors");

    public static CivilDb.Assembly FindAssembly(CivilDoc civ, Transaction tr, string name, OpenMode mode = OpenMode.ForRead) =>
        Need(TryFind<CivilDb.Assembly>(tr, civ.AssemblyCollection, name, mode), "Assembly", name, "list_assemblies");
}
