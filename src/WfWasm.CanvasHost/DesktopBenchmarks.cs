using System.Text.Json;
using System.Text.Json.Serialization;
using WfWasm.Shim;

namespace WfWasm.CanvasHost;

/// <summary>Optional real-application targets. Registration is not a compatibility claim.</summary>
public static class DesktopBenchmarks
{
    public sealed record Benchmark(AppScene Scene, string Slug, string Label, string Family,
        string SourceFolder, string SourceUrl, string Revision, string Project, string Assembly,
        string EntryFile, string FormType, string FormAssembly, string Build, string[] BuildArguments,
        string Purpose, string[] Components, string License, string Notes, string? DownloadUrl, string? Sha256)
    {
        public bool RequiresManualInput => Build == "manual";
    }

    public static IReadOnlyList<Benchmark> Entries { get; } = Read();

    private static IReadOnlyList<Benchmark> Read()
    {
        using var stream = typeof(DesktopBenchmarks).Assembly.GetManifestResourceStream(
            "WfWasm.CanvasHost.DesktopBenchmarks.json")
            ?? throw new InvalidOperationException("Desktop benchmark catalog resource missing.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return Array.AsReadOnly(JsonSerializer.Deserialize<Benchmark[]>(stream, options)
            ?? throw new InvalidOperationException("Invalid desktop benchmark catalog."));
    }

    public static Benchmark? Find(AppScene scene) => Entries.FirstOrDefault(d => d.Scene == scene);
}
