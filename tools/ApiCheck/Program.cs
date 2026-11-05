using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// 核对插件 DLL 引用的每个 Emby 类型 / 方法 / 字段，在指定版本的 Emby 服务端程序集里是否都存在、签名一致。
// 通用包按 4.8 接口编译，发布前用它对 4.8 / 4.9 / 4.10 各跑一遍，结果应为「问题 0 个」。
//
// 用法：dotnet run --project tools/ApiCheck -- <插件 DLL> <Emby 的 system 目录>
//       dotnet run --project tools/ApiCheck -- <插件 DLL> <编译用的 Emby 目录> --enums <运行的 Emby 目录>   （核对枚举数值）
//       dotnet run --project tools/ApiCheck -- <插件 DLL> x --refs                                         （列出引用的程序集）
var dll = args[0];
using var fs = File.OpenRead(dll);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var skip = new[] { "System", "Microsoft", "netstandard", "mscorlib", "0Harmony", "TinyPinyin", "ChineseConverter", "StrmAssistantLite" };
bool IsEmby(string asm) => !skip.Any(s => asm == s || asm.StartsWith(s + "."));

if (args.Contains("--refs")) { foreach (var h in md.AssemblyReferences) { var a = md.GetAssemblyReference(h); Console.WriteLine(md.GetString(a.Name) + " " + a.Version); } return; }

var dir = args[1];
var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
var paths = Directory.GetFiles(dir, "*.dll").Concat(Directory.GetFiles(runtimeDir, "*.dll"))
    .GroupBy(Path.GetFileName).Select(g => g.First()).ToList();
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");

string AsmOf(EntityHandle scope) => scope.Kind == HandleKind.AssemblyReference ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) : "";
string TypeRefName(TypeReferenceHandle h, out string asm)
{
    var tr = md.GetTypeReference(h);
    if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        return TypeRefName((TypeReferenceHandle)tr.ResolutionScope, out asm) + "+" + md.GetString(tr.Name);
    asm = AsmOf(tr.ResolutionScope);
    var ns = md.GetString(tr.Namespace);
    return (ns.Length > 0 ? ns + "." : "") + md.GetString(tr.Name);
}
var provider = new Prov(md, TypeRefName);

Type? Resolve(string asm, string full)
{
    try { var t = mlc.LoadFromAssemblyName(asm).GetType(full, false); if (t != null) return t; } catch { }
    foreach (var a in mlc.GetAssemblies()) { var t = a.GetType(full, false); if (t != null) return t; }
    foreach (var p in Directory.GetFiles(dir, "*.dll")) { try { var t = mlc.LoadFromAssemblyPath(p).GetType(full, false); if (t != null) return t; } catch { } }
    return null;
}
string Canon(Type t)
{
    if (t.IsGenericParameter) return (t.DeclaringMethod != null ? "!!" : "!") + t.GenericParameterPosition;
    if (t.IsByRef) return Canon(t.GetElementType()!) + "&";
    if (t.IsPointer) return Canon(t.GetElementType()!) + "*";
    if (t.IsArray) return Canon(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
    if (t.IsGenericType && !t.IsGenericTypeDefinition) return Canon(t.GetGenericTypeDefinition()) + "<" + string.Join(",", t.GetGenericArguments().Select(Canon)) + ">";
    return (t.FullName ?? t.Name);
}

if (args.Contains("--enums"))
{
    // 用法：apicheck <DLL> <编译用的 Emby 目录> --enums <运行的 Emby 目录>
    var otherDir = args[Array.IndexOf(args, "--enums") + 1];
    var otherPaths = Directory.GetFiles(otherDir, "*.dll").Concat(Directory.GetFiles(runtimeDir, "*.dll")).GroupBy(Path.GetFileName).Select(g => g.First()).ToList();
    using var mlc2 = new MetadataLoadContext(new PathAssemblyResolver(otherPaths), "System.Private.CoreLib");
    int diffs = 0, enums = 0;
    var done = new HashSet<string>();
    foreach (var h in md.TypeReferences)
    {
        var name = TypeRefName(h, out var asm);
        if (!IsEmby(asm) || !done.Add(name)) continue;
        var a = Resolve(asm, name);
        if (a == null || !a.IsEnum) continue;
        Type? b = null;
        foreach (var p in Directory.GetFiles(otherDir, "*.dll")) { try { b = mlc2.LoadFromAssemblyPath(p).GetType(name, false); if (b != null) break; } catch { } }
        if (b == null) { Console.WriteLine("枚举缺失 " + name); diffs++; continue; }
        enums++;
        var mapA = a.GetFields(BindingFlags.Public | BindingFlags.Static).ToDictionary(f => f.Name, f => Convert.ToInt64(f.GetRawConstantValue()));
        var mapB = b.GetFields(BindingFlags.Public | BindingFlags.Static).ToDictionary(f => f.Name, f => Convert.ToInt64(f.GetRawConstantValue()));
        var bad = mapA.Where(kv => !mapB.TryGetValue(kv.Key, out var v) || v != kv.Value).ToList();
        if (bad.Count > 0) { diffs++; Console.WriteLine($"枚举数值不同 {name}: {bad.Count}/{mapA.Count} 个，例如 " + string.Join(", ", bad.Take(5).Select(kv => kv.Key + "=" + kv.Value + "→" + (mapB.TryGetValue(kv.Key, out var v) ? v.ToString() : "无")))); }
    }
    Console.WriteLine($"核对 {enums} 个枚举，不一致 {diffs} 个");
    return;
}

int problems = 0, checkedCount = 0;
var seenTypes = new HashSet<string>();
foreach (var h in md.TypeReferences)
{
    var name = TypeRefName(h, out var asm);
    if (!IsEmby(asm) || !seenTypes.Add(name)) continue;
    if (Resolve(asm, name) == null) { problems++; Console.WriteLine($"缺类型  {asm}: {name}"); }
}
var seen = new HashSet<string>();
foreach (var h in md.MemberReferences)
{
    var mr = md.GetMemberReference(h);
    string parentName; string asm;
    if (mr.Parent.Kind == HandleKind.TypeReference) parentName = TypeRefName((TypeReferenceHandle)mr.Parent, out asm);
    else if (mr.Parent.Kind == HandleKind.TypeSpecification)
    {
        var ts = md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
        var s = ts.DecodeSignature(provider, null);
        if (s.StartsWith("<>")) continue; // 插件自己的匿名类型（泛型参数里有 Emby 类型）
        parentName = s.Contains('<') ? s.Substring(0, s.IndexOf('<')) : s; asm = provider.LastAsm;
    }
    else continue;
    if (!IsEmby(asm) || parentName.StartsWith("<>")) continue; // 编译器生成的匿名类型属于插件自己
    var name = md.GetString(mr.Name);
    var type = Resolve(asm, parentName);
    if (type == null) { if (seen.Add("T" + parentName)) { problems++; Console.WriteLine($"缺类型  {asm}: {parentName}（成员 {name}）"); } continue; }
    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    IEnumerable<Type> chain() { for (var t = type; t != null; t = t.BaseType) yield return t; foreach (var i in type.GetInterfaces()) yield return i; }

    if (mr.GetKind() == MemberReferenceKind.Field)
    {
        var ft = mr.DecodeFieldSignature(provider, null);
        var key = parentName + "::" + name;
        if (!seen.Add(key)) continue; checkedCount++;
        var f = chain().SelectMany(t => t.GetFields(all)).FirstOrDefault(x => x.Name == name);
        if (f == null || Canon(f.FieldType) != ft) { problems++; Console.WriteLine($"字段不符 {parentName}.{name} : 需要 {ft}，实际 {(f == null ? "无" : Canon(f.FieldType))}"); }
        continue;
    }
    var sig = mr.DecodeMethodSignature(provider, null);
    var want = string.Join(",", sig.ParameterTypes);
    var key2 = parentName + "::" + name + "(" + want + ")" + sig.ReturnType + sig.GenericParameterCount;
    if (!seen.Add(key2)) continue; checkedCount++;
    IEnumerable<MethodBase> cands = name == ".ctor" ? type.GetConstructors(all) : chain().SelectMany(t => t.GetMethods(all)).Where(m => m.Name == name);
    var ok = cands.Any(m =>
    {
        var ps = m.GetParameters();
        if (string.Join(",", ps.Select(p => Canon(p.ParameterType))) != want) return false;
        if (m is MethodInfo mi) { if (Canon(mi.ReturnType) != sig.ReturnType) return false; if ((mi.IsGenericMethodDefinition ? mi.GetGenericArguments().Length : 0) != sig.GenericParameterCount) return false; }
        return true;
    });
    if (!ok)
    {
        problems++;
        var near = cands.Select(m => name + "(" + string.Join(",", m.GetParameters().Select(p => Canon(p.ParameterType))) + ")" + (m is MethodInfo mi ? ":" + Canon(mi.ReturnType) : "")).Take(4);
        Console.WriteLine($"方法不符 {parentName}::{name}({want}):{sig.ReturnType}\n          现有：{(near.Any() ? string.Join(" | ", near) : "无同名")}");
    }
}
Console.WriteLine($"核对 {checkedCount} 个成员引用、{seenTypes.Count} 个类型，问题 {problems} 个");
Environment.ExitCode = problems == 0 ? 0 : 1;

class Prov : ISignatureTypeProvider<string, object?>
{
    readonly MetadataReader md; readonly TypeRefFn trn;
    public delegate string TypeRefFn(TypeReferenceHandle h, out string asm);
    public string LastAsm = "";
    public Prov(MetadataReader m, TypeRefFn f) { md = m; trn = f; }
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c switch {
        PrimitiveTypeCode.Boolean => "System.Boolean", PrimitiveTypeCode.Byte => "System.Byte", PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char", PrimitiveTypeCode.Int16 => "System.Int16", PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32", PrimitiveTypeCode.UInt32 => "System.UInt32", PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64", PrimitiveTypeCode.Single => "System.Single", PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.IntPtr => "System.IntPtr", PrimitiveTypeCode.UIntPtr => "System.UIntPtr", PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.String => "System.String", PrimitiveTypeCode.Void => "System.Void", PrimitiveTypeCode.TypedReference => "System.TypedReference", _ => c.ToString() };
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    { var td = r.GetTypeDefinition(h); var ns = r.GetString(td.Namespace); LastAsm = "StrmAssistantLite"; return (ns.Length > 0 ? ns + "." : "") + r.GetString(td.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var n = trn(h, out var a); LastAsm = a; return n; }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
