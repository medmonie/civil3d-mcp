#if NETFRAMEWORK
using System.Reflection;
using System.Runtime.CompilerServices;

namespace C3dMcpBridge;

/// <summary>
/// Civil 3D 2024 (.NET Framework) only. System.Text.Json and its dependencies ship next to C3dMcpBridge.dll, but
/// AutoCAD's own folder holds an older System.Runtime.CompilerServices.Unsafe and other plugins may load other versions
/// of System.Memory etc. When the normal lookup fails, hand out the copy from our bundle folder if it is new enough.
/// Registered by a module initializer, i.e. before any type of this DLL (and so System.Text.Json) is touched.
/// </summary>
internal static class AssemblyResolver
{
    private static readonly string Folder = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location) ?? "";

    [ModuleInitializer]
    internal static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

    private static Assembly? Resolve(object? sender, ResolveEventArgs e)
    {
        try
        {
            var wanted = new AssemblyName(e.Name);
            if (string.IsNullOrEmpty(wanted.Name)) return null;
            var file = Path.Combine(Folder, wanted.Name + ".dll");
            if (!File.Exists(file)) return null;
            var have = AssemblyName.GetAssemblyName(file);
            if (wanted.Version != null && have.Version != null && have.Version < wanted.Version) return null;
            // Reuse it if this exact copy is already loaded (a second LoadFrom would return it anyway, but be explicit).
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (!a.IsDynamic && string.Equals(a.Location, file, StringComparison.OrdinalIgnoreCase)) return a;
            return Assembly.LoadFrom(file);
        }
        catch { return null; }
    }
}
#endif
