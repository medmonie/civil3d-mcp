namespace C3dMcpBridge;

/// <summary>Reading request parameters from JSON. Every accessor validates; nothing here touches Civil 3D.</summary>
internal static class J
{
    // Coordinate sanity limits: anything beyond them is a unit mix-up or a typo, not a real site.
    private const double MaxXY = 1e8, MaxZ = 1e5;

    /// <summary>The value of a parameter that is present and not null.</summary>
    private static bool TryGet(JsonElement p, string name, out JsonElement value)
    {
        value = default;
        return p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    public static bool Has(JsonElement p, string name) => TryGet(p, name, out _);

    /// <summary>A JSON value as text: strings as they are, null as "", anything else as written (150 -> "150").</summary>
    public static string Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => e.GetRawText(),
    };

    // ---------------------------------------------------------------- text

    public static string? StrParam(JsonElement p, string name) =>
        TryGet(p, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static string NeedStr(JsonElement p, string name) =>
        StrParam(p, name) is { Length: > 0 } s ? s : throw new ArgumentException($"Missing text parameter '{name}'.");

    /// <summary>A size given either as a number (150) or as text ("150"); "" when absent.</summary>
    public static string SizeParam(JsonElement p, string name) =>
        TryGet(p, name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String ? Text(v) : "";

    /// <summary>One of a fixed set of words (case-insensitive); returns it in lower case.</summary>
    public static string Choice(JsonElement p, string name, string dflt, params string[] allowed)
    {
        string value = (StrParam(p, name) ?? dflt).ToLowerInvariant();
        return allowed.Contains(value) ? value : throw new ArgumentException($"'{name}' must be one of: {string.Join(", ", allowed)}.");
    }

    /// <summary>An enum member by name (case-insensitive); numbers are not accepted.</summary>
    public static T EnumParam<T>(JsonElement p, string name, T dflt) where T : struct, Enum
    {
        if (StrParam(p, name) is not string s) return dflt;
        return Enum.TryParse<T>(s, true, out var value) && !int.TryParse(s, out _)
            ? value
            : throw new ArgumentException($"'{name}' must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    // ---------------------------------------------------------------- numbers and flags

    public static int IntParam(JsonElement p, string name, int dflt, int min, int max) =>
        TryGet(p, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? Math.Clamp(n, min, max) : dflt;

    private static double Number(JsonElement v, string what)
    {
        if (v.ValueKind != JsonValueKind.Number) throw new ArgumentException($"{what} must be a number.");
        double d = v.GetDouble();
        return double.IsFinite(d) ? d : throw new ArgumentException($"{what} is not a finite number.");
    }

    public static double? DblParam(JsonElement p, string name) =>
        TryGet(p, name, out var v) ? Number(v, $"Parameter '{name}'") : null;

    public static double NeedDbl(JsonElement p, string name) =>
        DblParam(p, name) ?? throw new ArgumentException($"Missing numeric parameter '{name}'.");

    /// <summary>An optional number that must be greater than zero when given.</summary>
    public static double? PositiveParam(JsonElement p, string name) =>
        DblParam(p, name) is double d ? (d > 0 ? d : throw new ArgumentException($"'{name}' must be positive.")) : null;

    public static double NeedPositive(JsonElement p, string name) =>
        PositiveParam(p, name) ?? throw new ArgumentException($"Missing numeric parameter '{name}'.");

    public static bool? BoolParam(JsonElement p, string name)
    {
        if (!TryGet(p, name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"Parameter '{name}' must be true or false."),
        };
    }

    // ---------------------------------------------------------------- lists

    public static List<JsonElement> ArrParam(JsonElement p, string name, int max)
    {
        if (!TryGet(p, name, out var v)) return new List<JsonElement>();
        if (v.ValueKind != JsonValueKind.Array) throw new ArgumentException($"Parameter '{name}' must be a list.");
        if (v.GetArrayLength() > max) throw new ArgumentException($"'{name}' has {v.GetArrayLength()} items; the limit is {max} per call.");
        return v.EnumerateArray().ToList();
    }

    public static List<JsonElement> NeedArr(JsonElement p, string name, int max)
    {
        var a = ArrParam(p, name, max);
        return a.Count > 0 ? a : throw new ArgumentException($"'{name}' must be a non-empty list.");
    }

    public static List<string> StrList(JsonElement p, string name, int max) => NeedArr(p, name, max).Select(Text).ToList();

    /// <summary>An optional list of numbers (empty when absent).</summary>
    public static List<double> NumList(JsonElement p, string name, int max) =>
        ArrParam(p, name, max).Select((e, i) => Number(e, $"'{name}' item {i + 1}")).ToList();

    // ---------------------------------------------------------------- points

    /// <summary>Rejects implausible magnitudes (unit mix-ups, typos).</summary>
    public static Point3d CheckedPoint(double x, double y, double z, string what) =>
        Math.Abs(x) <= MaxXY && Math.Abs(y) <= MaxXY && Math.Abs(z) <= MaxZ
            ? new Point3d(x, y, z)
            : throw new ArgumentException($"{what} has implausible coordinates ({x}, {y}, {z}).");

    /// <summary>[x, y] or [x, y, z].</summary>
    public static Point3d Pt(JsonElement e, string what, double defaultZ = 0.0)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() is < 2 or > 3)
            throw new ArgumentException($"{what} must be [x, y] or [x, y, z].");
        var c = e.EnumerateArray().Select(v => Number(v, what)).ToArray();
        return CheckedPoint(c[0], c[1], c.Length == 3 ? c[2] : defaultZ, what);
    }

    public static Point3d NeedPt(JsonElement p, string name, string what, double defaultZ = 0.0) =>
        TryGet(p, name, out var v) ? Pt(v, what, defaultZ) : throw new ArgumentException($"{what}: missing '{name}'.");

    public static Point3d? OptPt(JsonElement p, string name, string what) =>
        TryGet(p, name, out var v) ? Pt(v, what) : null;

    /// <summary>A list of points taken from a JSON array value.</summary>
    public static List<Point3d> Points(JsonElement array, string what, int min)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new ArgumentException($"{what} must be a list of points.");
        var pts = array.EnumerateArray().Select((e, i) => Pt(e, $"{what} point {i + 1}")).ToList();
        return pts.Count >= min ? pts : throw new ArgumentException($"{what}: at least {min} points are needed.");
    }

    public static List<Point3d> PtList(JsonElement p, string name, string what, int min, int max)
    {
        NeedArr(p, name, max);   // presence and size limit
        return Points(p.GetProperty(name), what, min);
    }

    /// <summary>The displacement given by dx / dy / dz (each defaults to 0).</summary>
    public static Vector3d Displacement(JsonElement p) =>
        new(DblParam(p, "dx") ?? 0, DblParam(p, "dy") ?? 0, DblParam(p, "dz") ?? 0);
}
