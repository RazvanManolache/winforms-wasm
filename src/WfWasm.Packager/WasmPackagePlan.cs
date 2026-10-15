namespace WfWasm.Packager;

/// <summary>
/// Planned output of stage 4: bundle unmodified app assemblies plus the
/// shim runtime for a wasm target. Must handle legacy binary .resx
/// (see OSS_SURVEY.md: MSB3822/MSB3823).
/// </summary>
public sealed record WasmPackagePlan(
    string AppAssembly,
    string OutputDirectory,
    string TargetFramework = "net9.0-browserwasm");
