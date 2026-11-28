using System.Reflection;

namespace C3dMcpBridge;

/// <summary>
/// property_set (roadmap #71): one operation for single Civil 3D settings that have no operation of their own. Only the
/// properties listed in Allowed can be set; each is read back and reported before / after. Name, style and layer stay
/// with object_set.
/// </summary>
internal static class PropertyOps
{
    public static void Register(Dictionary<string, Func<EditContext, JsonElement, object>> ops)
    {
        ops["property_set"] = PropertySet;
    }

    /// <summary>Settable properties per Civil 3D type (the type or a base type of the object). Keys are snake_case.</summary>
    internal static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Alignment"] = new[] { "reference_point_station", "station_index_increment", "use_design_speed" },
        ["Profile"] = new[] { "update_mode" },
        ["ProfileView"] = new[] { "elevation_range_mode", "elevation_min", "elevation_max", "station_range_mode", "station_start",
                                  "station_end", "split_profile_view", "split_height" },
        ["Pipe"] = new[] { "flow_direction_method", "hold_on_resize_type", "return_period", "flow_rate", "entry_loss", "exit_loss",
                           "junction_loss", "average_loss" },
        ["Structure"] = new[] { "control_sump_by", "sump_depth", "rim_to_sump_height", "automatic_rim_surface_adjustment",
                                "surface_adjustment_value", "structure_type", "tailwater_condition", "tailwater_elevation", "known_capacity" },
        ["Part"] = new[] { "known_flow" },
        ["Corridor"] = new[] { "maximum_triangle_side_length", "rebuild_automatic", "region_lock_mode" },
    };

    internal static string Pascal(string snake) =>
        string.Concat(snake.Split('_').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

    private static IEnumerable<string> AllowedFor(Type t)
    {
        for (var x = t; x != null && x != typeof(object); x = x.BaseType)
            if (Allowed.TryGetValue(x.Name, out var names)) foreach (var n in names) yield return n;
    }

    private static object Convert(Type target, JsonElement v, string what)
    {
        if (target == typeof(double) && v.ValueKind == JsonValueKind.Number)
        {
            double d = v.GetDouble();
            if (!double.IsFinite(d)) throw new ArgumentException($"{what}: not a finite number.");
            return d;
        }
        if (target == typeof(int) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
        if (target == typeof(bool) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        if (target.IsEnum && v.ValueKind == JsonValueKind.String)
        {
            string want = v.GetString()!.Replace("_", "");
            var name = Enum.GetNames(target).FirstOrDefault(n => string.Equals(n, want, StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new ArgumentException($"{what}: '{v.GetString()}' is not one of {string.Join(", ", Enum.GetNames(target))}.");
            return Enum.Parse(target, name);
        }
        throw new ArgumentException($"{what}: takes {(target.IsEnum ? "one of " + string.Join(", ", Enum.GetNames(target)) : target.Name)}, not {v.ValueKind} {v}.");
    }

    private static object? Show(object? value) => value switch
    {
        double d => Math.Round(d, 6),
        Enum e => e.ToString(),
        _ => value,
    };

    private static object PropertySet(EditContext c, JsonElement p)
    {
        var obj = c.OpenByHandle<DBObject>(p, "an object");
        var type = obj.GetType();
        var allowed = AllowedFor(type).ToList();
        if (!p.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("property_set: give properties as {name: value, ...}.");
        if (allowed.Count == 0)
            throw new ArgumentException($"property_set: a {type.Name} has no settable properties here. Allowed types: {string.Join(", ", Allowed.Keys)}.");
        var rows = new List<object>();
        foreach (var kv in props.EnumerateObject())
        {
            string key = kv.Name;
            if (!allowed.Contains(key))
                throw new ArgumentException($"property_set: '{key}' cannot be set on a {type.Name}. Allowed: {string.Join(", ", allowed)}.");
            var pi = type.GetProperty(Pascal(key), BindingFlags.Public | BindingFlags.Instance)
                     ?? throw new ArgumentException($"property_set: this Civil 3D release has no {type.Name}.{Pascal(key)}.");
            if (!pi.CanWrite) throw new ArgumentException($"property_set: {type.Name}.{pi.Name} is read only in this release.");
            object? before = null;
            try { before = pi.GetValue(obj); } catch { /* some getters throw until set */ }
            var value = Convert(pi.PropertyType, kv.Value, $"property_set {key}");
            try { pi.SetValue(obj, value); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new ArgumentException($"property_set {key}: Civil 3D refused {Show(value)} ({ex.InnerException.Message}).");
            }
            object? after = null;
            try { after = pi.GetValue(obj); } catch { }
            rows.Add(new { property = key, before = Show(before), after = Show(after), requested = Show(value) });
        }
        if (rows.Count == 0) throw new ArgumentException("property_set: properties is empty.");
        return new { handle = H(obj.ObjectId), type = type.Name, name = Safe(() => (obj as CivilDb.Entity)?.Name), changed = rows };
    }

    /// <summary>For help: the allowlist with each property's type in this release.</summary>
    internal static object Describe() => Allowed.ToDictionary(kv => kv.Key, kv => kv.Value);
}
