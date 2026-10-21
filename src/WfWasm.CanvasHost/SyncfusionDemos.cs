using System.Text.Json;
using System.Text.Json.Serialization;
using WfWasm.Shim;

namespace WfWasm.CanvasHost;

// Shared by the native build script, server and runtime. Native binaries stay outside the source tree.
public static class SyncfusionDemos
{
    public const string RepositoryUrl = "https://github.com/syncfusion/winforms-demos";
    // Components are direct references to real vendor Control/Component classes in the
    // native sample. They describe coverage opportunities, not browser support claims.
    public sealed record Demo(AppScene Scene, string Slug, string Label, string Project, string Assembly,
        string FormType, string Family, string Purpose, string[] Components);
    public static IReadOnlyList<Demo> Entries { get; } = Read();
    private static IReadOnlyList<Demo> Read()
    {
        using var stream = typeof(SyncfusionDemos).Assembly.GetManifestResourceStream("WfWasm.CanvasHost.SyncfusionDemos.json")
            ?? throw new InvalidOperationException("Syncfusion demo catalog resource missing.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return Array.AsReadOnly(JsonSerializer.Deserialize<Demo[]>(stream, options) ?? throw new InvalidOperationException("Invalid Syncfusion demo catalog."));
    }
    public static Demo? Find(AppScene scene) => Entries.FirstOrDefault(d => d.Scene == scene);
}
