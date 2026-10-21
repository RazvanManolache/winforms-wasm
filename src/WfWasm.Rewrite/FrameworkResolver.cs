using Mono.Cecil;

namespace WfWasm.Rewrite;

// Transform-time assembly resolution with facade coherence. App folders
// ship stale NuGet-contract facades (netstandard/System.Runtime 4.x)
// whose forwarders disagree with the shared framework's, forming cycles
// (netstandard -> System.Xml v0 -> ReaderWriter -> netstandard) that
// stack-overflow Cecil's unguarded ExportedType chase at write time
// (AdomdClient killed the server in 8s; the same bytes rewrite in
// 194ms away from the pool facades). Selection mirrors runtime
// binding: real assemblies bind locally (app parity), facades unify
// to the shared framework (the host rolls framework assemblies
// forward). Facade = module-only types + forwarders. Loaded assemblies
// are cached (identity coherence + no re-read churn) and disposed with
// the resolver.
public sealed class FrameworkResolver : IAssemblyResolver
{
    private readonly string? _inputDir;
    private readonly string[] _extraDirs;
    private readonly string[] _frameworkDirs;
    private readonly Dictionary<string, AssemblyDefinition?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AssemblyDefinition> _loaded = new();
    private bool _disposed;

    public FrameworkResolver(string? inputDir, IEnumerable<string>? extraDirs = null, string[]? frameworkDirs = null)
    {
        _inputDir = inputDir;
        _extraDirs = extraDirs?.OfType<string>().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        _frameworkDirs = frameworkDirs ?? SharedFrameworkDirs();
    }

    // The running runtime + the same-version desktop pack: a coherent
    // set by construction, with no hardcoded versions (the 9.0.20 paths
    // baked into the old call sites no longer exist on disk).
    public static string[] SharedFrameworkDirs()
    {
        var dirs = new List<string>();
        try
        {
            string core = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (Directory.Exists(core))
                dirs.Add(core);
            string trimmed = core.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string version = Path.GetFileName(trimmed);
            string? app = Path.GetDirectoryName(trimmed);
            string? shared = app is null ? null : Path.GetDirectoryName(app);
            if (shared is not null)
            {
                string desktop = Path.Combine(shared, "Microsoft.WindowsDesktop.App", version);
                if (Directory.Exists(desktop))
                    dirs.Add(desktop);
            }
        }
        catch { }
        return dirs.ToArray();
    }

    public AssemblyDefinition Resolve(AssemblyNameReference name) =>
        Resolve(name, new ReaderParameters());

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cache.TryGetValue(name.Name, out AssemblyDefinition? cached))
            return cached!;
        AssemblyDefinition? best = SelectBest(name.Name);
        if (best is null)
            throw new AssemblyResolutionException(name);
        _cache[name.Name] = best;
        return best;
    }

    private AssemblyDefinition? SelectBest(string name)
    {
        // Real assemblies first (directory order: input, extras, fx,
        // GAC), then facades (fx first for set coherence). Every
        // candidate loads with this resolver so all hops stay coherent.
        var facadeFallbacks = new List<AssemblyDefinition>();
        AssemblyDefinition? fxFacade = null;
        foreach (string? dir in SearchDirsInOrder())
        {
            if (dir is null || !Directory.Exists(dir))
                continue;
            foreach (string ext in new[] { ".dll", ".exe" })
            {
                string candidate = Path.Combine(dir, name + ext);
                if (!File.Exists(candidate))
                    continue;
                AssemblyDefinition? loaded = TryLoad(candidate);
                if (loaded is null)
                    continue;
                if (!IsFacade(loaded))
                    return loaded;
                facadeFallbacks.Add(loaded);
                if (fxFacade is null && _frameworkDirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    fxFacade = loaded;
            }
        }
        foreach (string gacFile in GacCandidates(name))
        {
            AssemblyDefinition? loaded = TryLoad(gacFile);
            if (loaded is null)
                continue;
            if (!IsFacade(loaded))
                return loaded;
            facadeFallbacks.Add(loaded);
        }
        return fxFacade ?? facadeFallbacks.FirstOrDefault();
    }

    private IEnumerable<string?> SearchDirsInOrder()
    {
        yield return _inputDir;
        foreach (string dir in _extraDirs)
            yield return dir;
        foreach (string dir in _frameworkDirs)
            yield return dir;
    }

    private static bool IsFacade(AssemblyDefinition assembly) =>
        assembly.MainModule.Types.Count <= 1 && assembly.MainModule.ExportedTypes.Count > 0;

    private AssemblyDefinition? TryLoad(string path)
    {
        try
        {
            var loaded = AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { AssemblyResolver = this });
            _loaded.Add(loaded);
            return loaded;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GacCandidates(string name)
    {
        // Any version subdir: retrieval, not binding policy.
        string? windir = null;
        try { windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows); }
        catch { }
        if (string.IsNullOrEmpty(windir))
            yield break;
        foreach (string gac in new[] { "GAC_MSIL", "GAC_32", "GAC_64" })
        {
            string root = Path.Combine(windir, "Microsoft.NET", "assembly", gac, name);
            if (!Directory.Exists(root))
                continue;
            string[] found;
            try { found = Directory.GetFiles(root, name + ".dll", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (string file in found.Order(StringComparer.Ordinal))
                yield return file;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (AssemblyDefinition assembly in _loaded)
        {
            try { assembly.Dispose(); }
            catch { }
        }
        _loaded.Clear();
        _cache.Clear();
    }
}
