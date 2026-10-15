using System.Text.Json;
using WfWasm.Analyzer;

namespace WfWasm.Packager;

/// <summary>
/// Stage 4 (early): plan and stage an app for the wasm target.
/// Flags the legacy binary .resx problem (MSB3822) via metadata before any build.
/// </summary>
public static class Packager
{
    public const string ManifestFileName = "wfwasm-manifest.json";
    private const string ResourceExtensionsAssembly = "System.Resources.Extensions";

    public static PackageManifest CreatePlan(string appAssemblyPath)
    {
        if (!File.Exists(appAssemblyPath))
            throw new FileNotFoundException($"App assembly not found: {appAssemblyPath}", appAssemblyPath);

        ScanResult scan = AssemblyScanner.Scan(appAssemblyPath);
        var warnings = new List<string>();

        int binaryResources = scan.ManifestResources
            .Count(r => r.EndsWith(".resources", StringComparison.OrdinalIgnoreCase));
        bool hasExtensionsRef = scan.AssemblyReferences
            .Any(r => r.StartsWith(ResourceExtensionsAssembly + " ", StringComparison.Ordinal));
        if (binaryResources > 0 && !hasExtensionsRef)
        {
            warnings.Add(
                $"Legacy binary resources ({binaryResources}) without a {ResourceExtensionsAssembly} " +
                "reference: classic .resx icons/images fail the wasm build (MSB3822). " +
                "Convert the resources or add the reference.");
        }

        if (scan.PInvokeModules.Count > 0)
        {
            warnings.Add(
                $"Native P/Invoke modules ({string.Join(", ", scan.PInvokeModules)}) " +
                "have no browser equivalent yet.");
        }

        string fileName = Path.GetFileName(appAssemblyPath);
        return new PackageManifest(fileName, new List<string> { fileName },
            scan.AssemblyReferences, warnings);
    }

    public static void Stage(PackageManifest manifest, string appAssemblyPath, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(outputDirectory);
        File.Copy(appAssemblyPath,
            Path.Combine(outputDirectory, manifest.EntryAssembly), overwrite: true);
        File.WriteAllText(Path.Combine(outputDirectory, ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
