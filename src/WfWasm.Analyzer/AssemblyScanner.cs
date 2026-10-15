using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace WfWasm.Analyzer;

/// <summary>Findings from scanning one managed assembly (metadata only, never loaded).</summary>
public sealed record ScanResult(
    string Path,
    List<string> AssemblyReferences,
    List<(string FullName, int Count)> TypeReferences,
    List<string> PInvokeModules,
    List<string> ManifestResources);

/// <summary>Stage 1 of the pipeline: reports which runtime APIs an unmodified app needs.</summary>
public static class AssemblyScanner
{
    public static ScanResult Scan(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        bool hasMetadata;
        try
        {
            hasMetadata = peReader.HasMetadata;
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidDataException($"Not a managed assembly: {path}", ex);
        }

        if (!hasMetadata)
            throw new InvalidDataException($"No managed metadata: {path}");
        var reader = peReader.GetMetadataReader();

        var assemblyReferences = new List<string>();
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            assemblyReferences.Add($"{reader.GetString(reference.Name)} {reference.Version}");
        }

        var typeRefCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        void CountTypeRef(TypeReference typeRef)
        {
            string ns = typeRef.Namespace.IsNil ? "" : reader.GetString(typeRef.Namespace);
            string name = reader.GetString(typeRef.Name);
            string key = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            typeRefCounts[key] = typeRefCounts.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        foreach (var handle in reader.TypeReferences)
            CountTypeRef(reader.GetTypeReference(handle));
        foreach (var handle in reader.MemberReferences)
        {
            var memberRef = reader.GetMemberReference(handle);
            if (memberRef.Parent.Kind == HandleKind.TypeReference)
                CountTypeRef(reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent));
        }

        var typeReferences = typeRefCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        var pinvokeModules = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
                continue;
            var import = method.GetImport();
            pinvokeModules.Add(reader.GetString(reader.GetModuleReference(import.Module).Name));
        }

        var manifestResources = new List<string>();
        foreach (var handle in reader.ManifestResources)
            manifestResources.Add(reader.GetString(reader.GetManifestResource(handle).Name));

        return new ScanResult(path, assemblyReferences, typeReferences,
            new List<string>(pinvokeModules), manifestResources);
    }
}
