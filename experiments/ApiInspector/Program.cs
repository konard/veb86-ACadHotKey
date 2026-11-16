using System.Reflection;

// args[0]: directory with AutoCAD reference DLLs; args[1]: .NET Framework reference assemblies dir;
// args[2..]: full or partial type names to dump.
var acadDir = args[0];
var fxDir = args[1];
var paths = Directory.GetFiles(acadDir, "*.dll", SearchOption.AllDirectories)
    .Concat(Directory.GetFiles(fxDir, "*.dll", SearchOption.AllDirectories)
        .Where(p => !p.Contains("Facades") || true))
    .GroupBy(Path.GetFileName).Select(g => g.First()).ToList();
var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver, "mscorlib");
var acadAsms = Directory.GetFiles(acadDir, "*.dll", SearchOption.AllDirectories)
    .Select(p => { try { return mlc.LoadFromAssemblyPath(p); } catch { return null; } })
    .Where(a => a != null)!;
const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
foreach (var asm in acadAsms)
{
    Type[] types;
    try { types = asm!.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
    foreach (var t in types)
    {
        if (!t.IsPublic && !t.IsNestedPublic) continue;
        if (!args.Skip(2).Any(f => t.FullName == f)) continue;
        Console.WriteLine($"== {t.FullName}  [{asm!.GetName().Name}]");
        foreach (var m in t.GetMembers(F))
        {
            try { Console.WriteLine("   " + m.MemberType + " " + Describe(m)); }
            catch (Exception ex) { Console.WriteLine("   " + m.Name + " <" + ex.GetType().Name + ">"); }
        }
    }
}

static string Describe(MemberInfo m) => m switch
{
    PropertyInfo p => $"{p.PropertyType} {p.Name} {{{(p.CanRead ? "get;" : "")}{(p.CanWrite ? "set;" : "")}}}",
    EventInfo e => $"{e.EventHandlerType} {e.Name}",
    MethodInfo mi when !mi.IsSpecialName => $"{(mi.IsStatic ? "static " : "")}{mi.ReturnType} {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => p.ParameterType + " " + p.Name))})",
    FieldInfo f => $"{f.FieldType} {f.Name}",
    ConstructorInfo c => $".ctor({string.Join(", ", c.GetParameters().Select(p => p.ParameterType + " " + p.Name))})",
    _ => m.Name,
};
