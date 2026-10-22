using WfWasm.CanvasHost;

namespace WfWasm.Host.Server;

/// <summary>Locally prepared inputs; never bundles or downloads application DLLs into the host.</summary>
public static class DesktopBenchmarkInputs
{
    public static string Root
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("WFWASM_BENCHMARK_APPS_ROOT");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "WfWasm.sln")))
                    return Path.Combine(directory.FullName, ".artifacts", "benchmark-apps");
            throw new InvalidOperationException("Set WFWASM_BENCHMARK_APPS_ROOT to the prepared benchmark directory.");
        }
    }

    public static AppTransform.AppSpec CreateSpec(DesktopBenchmarks.Benchmark benchmark, string? root = null)
    {
        if (benchmark.RequiresManualInput)
            throw new InvalidOperationException($"{benchmark.Label} has no verified managed input or startup metadata.");
        return new(Path.Combine(root ?? Root, benchmark.Slug), [benchmark.EntryFile], benchmark.Assembly)
        {
            // BizHawk's portable distribution keeps dependencies in dll/. This is
            // a lookup root, never an instruction to send every file to the client.
            AssemblySearchFolders = [Path.Combine(root ?? Root, benchmark.Slug, "dll")],
        };
    }

    public static bool IsPrepared(DesktopBenchmarks.Benchmark benchmark, string? root = null)
    {
        if (benchmark.RequiresManualInput) return false;
        string folder = Path.Combine(root ?? Root, benchmark.Slug);
        if (!File.Exists(Path.Combine(folder, benchmark.EntryFile))) return false;
        string receipt = Path.Combine(folder, "preparation.json");
        // Manually supplied inputs are allowed; failed/interrupted scripted builds
        // must not advertise an older file left in the output folder as prepared.
        if (!File.Exists(receipt)) return true;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(receipt));
            return json.RootElement.TryGetProperty("prepared", out var prepared)
                && prepared.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (System.Text.Json.JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
