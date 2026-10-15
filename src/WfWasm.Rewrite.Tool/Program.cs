// GapQueue: the rewrite work-queue runner. Auto-drafts the type map by
// name, measures the post-remap gap, and prints it grouped by replacement
// type (the work items) plus the unmapped source types with reference
// counts (the hand-map backlog). Exits 1 while any gap remains.
using Mono.Cecil;
using WfWasm.Rewrite;

if (args.Length >= 3 && args[0] == "--stub-pinvokes")
{
    AssemblyRewriter.StubPInvokes(args[1], args[2]);
    Console.WriteLine($"Stubbed pinvokes in {args[1]} -> {args[2]}");
    return 0;
}

if (args.Length >= 3 && args[0] == "--patch-probes")
{
    AssemblyRewriter.PatchRuntimeProbes(args[1], args[2]);
    Console.WriteLine($"Patched runtime probes in {args[1]} -> {args[2]}");
    return 0;
}

// Diagnostic only: --probe <in> <out> <Type::Method>... inserts
// ENTER/EXIT Console.WriteLine breadcrumbs for instant-death triage.
// Never wired into the regen scripts; wiped by the next regen.
if (args.Length >= 3 && args[0] == "--probe")
{
    int probed = AssemblyRewriter.InstrumentProbes(args[1], args[2], args.Skip(3));
    Console.WriteLine($"Probed {probed} methods in {args[1]} -> {args[2]}");
    return 0;
}

string? app = null;
string? forms = null;
string? drawing = null;
string? mapFile = null;
string? outFile = null;
string? pdnCpu = null;
int top = 30;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--app" && i + 1 < args.Length)
        app = args[++i];
    else if (args[i] == "--forms" && i + 1 < args.Length)
        forms = args[++i];
    else if (args[i] == "--drawing" && i + 1 < args.Length)
        drawing = args[++i];
    else if (args[i] == "--map" && i + 1 < args.Length)
        mapFile = args[++i];
    else if (args[i] == "--top" && i + 1 < args.Length)
        top = int.Parse(args[++i]);
    else if (args[i] == "--out" && i + 1 < args.Length)
        outFile = args[++i];
    else if (args[i] == "--pdncp" && i + 1 < args.Length)
        pdnCpu = args[++i];
    else
    {
        Console.Error.WriteLine("usage: gap --app <dll> --forms <WfWasm.WinForms.dll> [--drawing <WfWasm.Drawing.dll>] [--map handmap.txt] [--top N] [--out rewritten.dll] [--pdncp WfWasm.PdnCpu.dll]");
        return 2;
    }
}

if (app is null || forms is null)
{
    Console.Error.WriteLine("usage: gap --app <dll> --forms <WfWasm.WinForms.dll> [--drawing <WfWasm.Drawing.dll>] [--map handmap.txt] [--top N] [--out rewritten.dll] [--pdncp WfWasm.PdnCpu.dll]");
    return 2;
}

var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["System.Windows.Forms"] = forms,
    ["System.Windows.Forms.Primitives"] = forms,
};
if (drawing is not null)
{
    assemblyMap["System.Drawing"] = drawing;
    assemblyMap["System.Drawing.Common"] = drawing;
    assemblyMap["System.Drawing.Primitives"] = drawing;
}

var typeMap = GapAnalyzer.AutoMap(app, assemblyMap);
if (mapFile is not null)
{
    // Hand map: lines "Source.Full.Name=AssemblyName:Target.Full.Name".
    foreach (string line in File.ReadAllLines(mapFile))
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            continue;
        string[] sides = trimmed.Split('=', 2);
        string[] target = sides[1].Split(':', 2);
        typeMap[sides[0]] = (target[0], target[1]);
    }

    Console.WriteLine($"hand-mapped types: {File.ReadAllLines(mapFile).Count(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))}");
}

Console.WriteLine($"app: {app}");
Console.WriteLine($"mapped types: {typeMap.Count}");

// Unmapped source types with MEMBER-reference counts (TypeRef rows are
// deduplicated by Cecil, so rows alone cannot prioritize): the backlog.
using (var target = AssemblyDefinition.ReadAssembly(app))
{
    var scopes = new HashSet<string>(assemblyMap.Keys, StringComparer.Ordinal);
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (MemberReference memberRef in target.MainModule.GetMemberReferences())
    {
        TypeReference declaring = memberRef.DeclaringType;
        while (declaring is TypeSpecification spec)
            declaring = spec.ElementType;
        if (declaring.Scope is not AssemblyNameReference scope || !scopes.Contains(scope.Name))
            continue;
        string full = declaring.FullName.Replace('/', '+');
        if (typeMap.ContainsKey(full))
            continue;
        counts[full] = counts.TryGetValue(full, out int n) ? n + 1 : 1;
    }

    foreach (TypeReference typeRef in target.MainModule.GetTypeReferences())
    {
        if (typeRef.Scope is not AssemblyNameReference scope || !scopes.Contains(scope.Name))
            continue;
        string full = typeRef.FullName.Replace('/', '+');
        if (typeMap.ContainsKey(full) || counts.ContainsKey(full))
            continue;
        counts[full] = 0;
    }

    var unmapped = counts.OrderByDescending(kv => kv.Value).ToList();
    Console.WriteLine($"unmapped source types: {unmapped.Count}");
    foreach ((string full, int n) in unmapped.Take(top))
        Console.WriteLine($"  {n,6}  {full}");
}

List<Gap> gaps = GapAnalyzer.Analyze(app, assemblyMap, typeMap);
Console.WriteLine($"post-remap gaps: {gaps.Count}");
var byType = gaps
    .GroupBy(g => g.DeclaringType)
    .OrderByDescending(g => g.Count())
    .Take(top);
foreach (var group in byType)
{
    string members = string.Join(", ", group.Select(g =>
        string.IsNullOrEmpty(g.Member) ? "<missing type>"
        : g.Kind == "signature" && g.Detail.Length != 0 ? $"{g.Member} (signature {g.Detail})"
        : $"{g.Member} ({g.Kind})"));
    Console.WriteLine($"{group.Key} [{group.Count()}]: {members}");
}

if (outFile is not null)
{
    AssemblyRewriter.Rewrite(app, outFile, assemblyMap, typeMap, pdnCpu);
    Console.WriteLine($"rewrote: {outFile}");
}

return gaps.Count == 0 ? 0 : 1;
