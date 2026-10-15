namespace WfWasm.Packager;

/// <summary>Staging plan for one app assembly plus analyzer-derived warnings.</summary>
public sealed record PackageManifest(
    string EntryAssembly,
    List<string> StagedFiles,
    List<string> AssemblyReferences,
    List<string> Warnings);
