using System.Reflection;

namespace C3dMcpBridge;

/// <summary>Developer aid: reflect over a loaded Autodesk type. Read only; nothing is invoked.</summary>
internal static class ApiRead
{
    public static object DescribeApi(JsonElement p)
    {
        string name = NeedStr(p, "type_name");
        string? contains = StrParam(p, "contains");
        int limit = IntParam(p, "limit", 300, 1, 2000);
        if (!name.StartsWith("Autodesk.", StringComparison.Ordinal))
            throw new ArgumentException("Only types in the Autodesk.* namespaces can be described.");

        Type? t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(name, false, false); } catch { /* unloadable assembly */ }
            if (t != null) break;
        }
        if (t == null) throw new ArgumentException($"Type '{name}' not found in the loaded assemblies.");

        static string Tn(Type x) => x.IsGenericType
            ? x.Name.Split('`')[0] + "<" + string.Join(",", x.GetGenericArguments().Select(Tn)) + ">"
            : x.Name;
        static string Sig(MethodBase m) => string.Join(", ", m.GetParameters().Select(a =>
            $"{(a.IsOut ? "out " : a.ParameterType.IsByRef ? "ref " : "")}{Tn(a.ParameterType.IsByRef ? a.ParameterType.GetElementType()! : a.ParameterType)} {a.Name}"));
        bool Keep(string s) => contains == null || s.Contains(contains, StringComparison.OrdinalIgnoreCase);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var methods = t.GetMethods(flags)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
            .Select(m => $"{(m.IsStatic ? "static " : "")}{Tn(m.ReturnType)} {m.Name}({Sig(m)})")
            .Where(Keep).Distinct().OrderBy(x => x).ToList();
        var props = t.GetProperties(flags)
            .Select(pr => $"{Tn(pr.PropertyType)} {pr.Name} {{ {(pr.CanRead ? "get; " : "")}{(pr.CanWrite ? "set; " : "")}}}")
            .Where(Keep).Distinct().OrderBy(x => x).ToList();

        return new
        {
            type = t.FullName, baseType = t.BaseType?.FullName, isEnum = t.IsEnum,
            enumValues = t.IsEnum ? Enum.GetNames(t) : null,
            constructors = t.GetConstructors().Select(c => $"{Tn(t)}({Sig(c)})").ToList(),
            properties = props.Take(limit).ToList(),
            methods = methods.Take(limit).ToList(),
            truncated = props.Count > limit || methods.Count > limit,
        };
    }
}
