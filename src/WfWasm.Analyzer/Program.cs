using WfWasm.Analyzer;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: WfWasm.Analyzer <assembly.dll> [...]");
    return 2;
}

int failures = 0;
foreach (string path in args)
{
    try
    {
        Print(AssemblyScanner.Scan(path));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {path}: {ex.Message}");
        failures++;
    }
}

return failures == 0 ? 0 : 1;

static void Print(ScanResult result)
{
    Console.WriteLine($"== {result.Path}");
    Console.WriteLine("-- assembly-references --");
    foreach (string reference in result.AssemblyReferences)
        Console.WriteLine($"  {reference}");

    int shimRefs = result.TypeReferences
        .Where(t => t.FullName.StartsWith("System.Windows.Forms", StringComparison.Ordinal)
            || t.FullName.StartsWith("System.Drawing", StringComparison.Ordinal))
        .Sum(t => t.Count);
    Console.WriteLine($"-- type-references (Forms/Drawing: {shimRefs}) top 20 --");
    foreach (var (fullName, count) in result.TypeReferences.Take(20))
        Console.WriteLine($"  {count,6}  {fullName}");

    Console.WriteLine("-- pinvoke-modules --");
    if (result.PInvokeModules.Count == 0)
        Console.WriteLine("  (none)");
    foreach (string module in result.PInvokeModules)
        Console.WriteLine($"  {module}");

    Console.WriteLine("-- manifest-resources --");
    if (result.ManifestResources.Count == 0)
        Console.WriteLine("  (none)");
    foreach (string resource in result.ManifestResources)
        Console.WriteLine($"  {resource}");
}
