using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// usage: apitool <dir> <TypeNameRegex> [memberRegex]   -> prints public members with full signatures (incl. inherited within the dir)
class P : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g.Split('`')[0] + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var t = r.GetTypeDefinition(h); return r.GetString(t.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var t = r.GetTypeReference(h); return r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);

    static void Main(string[] a)
    {
        var rxT = new System.Text.RegularExpressions.Regex(a[1]);
        var rxM = a.Length > 2 ? new System.Text.RegularExpressions.Regex(a[2]) : null;
        var prov = new P();
        foreach (var f in Directory.GetFiles(a[0], "*.dll", SearchOption.AllDirectories))
        {
            using var pe = new PEReader(File.OpenRead(f));
            if (!pe.HasMetadata) continue;
            var r = pe.GetMetadataReader();
            foreach (var th in r.TypeDefinitions)
            {
                var t = r.GetTypeDefinition(th);
                string full = r.GetString(t.Namespace) + "." + r.GetString(t.Name);
                if (!rxT.IsMatch(full)) continue;
                if ((t.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public && (t.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.NestedPublic) continue;
                string bt = "";
                if (!t.BaseType.IsNil) bt = t.BaseType.Kind == HandleKind.TypeDefinition ? r.GetString(r.GetTypeDefinition((TypeDefinitionHandle)t.BaseType).Name) : t.BaseType.Kind == HandleKind.TypeReference ? r.GetString(r.GetTypeReference((TypeReferenceHandle)t.BaseType).Name) : "?";
                Console.WriteLine($"## {full} : {bt}   [{Path.GetFileName(f)}]");
                if (bt == "Enum") { foreach (var fh in t.GetFields()) { var fd = r.GetFieldDefinition(fh); if ((fd.Attributes & FieldAttributes.Static) != 0) Console.Write(r.GetString(fd.Name) + " "); } Console.WriteLine(); continue; }
                var lines = new List<string>();
                foreach (var mh in t.GetMethods())
                {
                    var m = r.GetMethodDefinition(mh);
                    if ((m.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
                    string n = r.GetString(m.Name);
                    if (rxM != null && !rxM.IsMatch(n)) continue;
                    var sig = m.DecodeSignature(prov, null);
                    var names = m.GetParameters().Select(ph => r.GetParameter(ph)).Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber).Select(p => r.GetString(p.Name)).ToList();
                    var ps = sig.ParameterTypes.Select((pt, i) => pt + " " + (i < names.Count ? names[i] : "p" + i));
                    lines.Add($"  {((m.Attributes & MethodAttributes.Static) != 0 ? "static " : "")}{sig.ReturnType} {n}({string.Join(", ", ps)})");
                }
                foreach (var fh in t.GetFields())
                {
                    var fd = r.GetFieldDefinition(fh);
                    if ((fd.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public) continue;
                    string n = r.GetString(fd.Name);
                    if (rxM != null && !rxM.IsMatch(n)) continue;
                    lines.Add($"  field {((fd.Attributes & FieldAttributes.Static) != 0 ? "static " : "")}{fd.DecodeSignature(prov, null)} {n}");
                }
                foreach (var l in lines.Distinct().OrderBy(x => x)) Console.WriteLine(l);
            }
        }
    }
}
