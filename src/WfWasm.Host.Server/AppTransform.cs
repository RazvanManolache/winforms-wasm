using System.Security.Cryptography;
using WfWasm.Rewrite;

namespace WfWasm.Host.Server;

// On-demand app delivery: instead of bundling every rewritten app
// assembly into the client at build time, the browser fetches the
// assemblies it needs per app. This service rewrites one pristine
// assembly with the same inputs as the batch scripts and caches the
// output keyed by content hash, so identical inputs serve identical
// bytes without re-running the rewriter.
public static class AppTransform
{
    // Assemblies: IL-rewritten files, served by relative path. Verbatim:
    // served without a full rewrite (third-party interop gets its
    // P/Invokes stubbed instead; the batch pipeline's stub pass).
    // Handmap/PdnCpu: paint.net-style closure inputs (hand type-map
    // overlay + CPU paint helper, same as rewrite-pdn.ps1).
    // SourceOverride: name -> alternate pristine source (e.g. the
    // prebuilt DirectWriteForwarder fake).
    public sealed record AppSpec(string Folder, string[] Assemblies, string Entry)
    {
        public string[] AssemblySearchFolders { get; init; } = [];
        public string[] Verbatim { get; init; } = [];
        public string[] Pristine { get; init; } = [];
        public string? Handmap { get; init; }
        public string? PdnCpu { get; init; }
        public Dictionary<string, string> SourceOverride { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        // Loose content shipped raw (images, data): manifest name ->
        // absolute source path. Served unconverted at /api/appcontent.
        public Dictionary<string, string> ContentFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Telerik dependencies are looked up by the name requested by the CLR.
    // The shared folder is a search root; it is never enumerated for downloads.
    private static readonly string Root = FindRoot();
    private static string FindRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("WFWASM_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "WfWasm.sln"))) return d.FullName;
        throw new InvalidOperationException("Set WFWASM_ROOT to the repository directory.");
    }
    private static string Repo(string relative) => Path.Combine(Root, relative.Replace('\\', Path.DirectorySeparatorChar));
    private static string Setting(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) ?? Repo(fallback);
    private static readonly string TelerikPool = Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release");
    // net462 system assemblies Telerik touches as field/base types but never
    // calls headless (UIA provider slots on RadForm). Served pristine from
    // the GAC (real IL: Mono rejects both Cecil-processed and pristine
    // metadata-only reference assemblies at Assembly.Load).
    private static string GacMsil(string name) =>
        @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\" + name + @"\v4.0_4.0.0.0__31bf3856ad364e35\" + name + ".dll";
    private static readonly string[] TelerikSystemAssemblies = new[]
    {
        "UIAutomationProvider.dll",
        "UIAutomationTypes.dll",
        "UIAutomationClient.dll",
        "WindowsBase.dll",
    };
    // PhotoAlbum reads background.jpg + Photos/*.jpg loose from disk
    // (Form1_Load). Manifest names use forward slashes; the VFS
    // normalizes every lookup, so backslash callers still hit.
    private static Dictionary<string, string> PhotoAlbumContent()
    {
        string proj = Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\PhotoAlbum");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["background.jpg"] = Path.Combine(proj, "bin", "background.jpg"),
        };
        for (int i = 1; i <= 7; i++)
            map[$"Photos/{i}.jpg"] = Path.Combine(proj, "Photos", i + ".jpg");
        return map;
    }

    // BookShelf falls back to BackUp.xml next to the exe when its data
    // service is unreachable (always, in the browser): serve it as VFS
    // content so the offline load succeeds (File.Exists/FileStream are
    // VFS-redirected, rev36).
    private static Dictionary<string, string> BookShelfContent()
    {
        string bin = Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\BookShelf\bin");
        // Books.xml seeds the app-data cache path (GetApplicationDataFolder
        // + CacheFileName): without it every load replays the dead eBay
        // service call, shows the "unavailable" dialog, and double-fills
        // the Popular/Latest lists (DeserializeData only clears
        // Categories). VFS matches by tail, so this answers the cache
        // lookup exactly like a desktop second run.
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BackUp.xml"] = Path.Combine(bin, "BackUp.xml"),
            ["Books.xml"] = Path.Combine(bin, "BackUp.xml"),
        };
    }

    // BugTracker fills its DataSet from XMLSchema.xsd + source.xml
    // next to the exe (FillDataSet, ctor): serve both as VFS content
    // (System.Data is unrewritten framework code, so the MEMFS
    // mirror answers its internal reads).
    private static Dictionary<string, string> BugTrackerContent()
    {
        string bin = Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\BugTracker\bin");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["XMLSchema.xsd"] = Path.Combine(bin, "XMLSchema.xsd"),
            ["source.xml"] = Path.Combine(bin, "source.xml"),
        };
    }

    private static AppSpec TelerikAppSpec(string exeFolder, string exeName, string entry)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string sysDll in TelerikSystemAssemblies)
            overrides[sysDll] = GacMsil(Path.GetFileNameWithoutExtension(sysDll));
        return new AppSpec(exeFolder, [exeName], entry)
        {
            AssemblySearchFolders = [TelerikPool],
            SourceOverride = overrides,
            Pristine = TelerikSystemAssemblies,
        };
    }
    public static IReadOnlyDictionary<string, string> AppFolders =>
        Apps.ToDictionary(kv => kv.Key, kv => kv.Value.Folder, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetSpec(string app, out AppSpec? spec) =>
        Apps.TryGetValue(app, out spec);

    private static string AssemblyStem(string file) =>
        file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? file[..^4] : file;

    public static bool TryResolveAssembly(string app, string name, out string? file)
    {
        file = null;
        if (!Apps.TryGetValue(app, out var spec) || string.IsNullOrWhiteSpace(name) || name.Length > 200
            || name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || name.Any(char.IsControl))
            return false;
        string stem = AssemblyStem(name);
        // Overrides are routing/transform policy, not a download list.
        foreach (string known in spec.Assemblies.Concat(spec.SourceOverride.Keys))
            if (AssemblyStem(Path.GetFileName(known)).Equals(stem, StringComparison.OrdinalIgnoreCase)
                && TryAssemblyPath(spec, known, out _))
            {
                file = known;
                return true;
            }
        foreach (string extension in new[] { ".dll", ".exe" })
            if (TryAssemblyPath(spec, stem + extension, out _))
            {
                file = stem + extension;
                return true;
            }
        return false;
    }

    private static bool TryAssemblyPath(AppSpec spec, string name, out string? path)
    {
        path = null;
        if (spec.SourceOverride.TryGetValue(name, out string? overridden))
        {
            if (!File.Exists(overridden)) return false;
            path = overridden;
            return true;
        }
        if (spec.Assemblies.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            string known = Path.Combine(spec.Folder, name);
            if (!File.Exists(known)) return false;
            path = known;
            return true;
        }
        // Dynamic lookups can only name a single DLL/EXE in configured roots.
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || !(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            return false;
        foreach (string folder in new[] { spec.Folder }.Concat(spec.AssemblySearchFolders))
        {
            string candidate = Path.Combine(folder, name);
            if (!File.Exists(candidate)) continue;
            path = candidate;
            return true;
        }
        return false;
    }

    // Browsers retry slow transforms, so the same output can be
    // requested concurrently: serialize per output file.
    // The memory cache is authoritative: disk persistence is
    // best-effort (some environments deny file creation for
    // freshly-written DLL bytes while reads/writes of other files
    // in the same directory succeed).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> MemCache = new();
    private static readonly Dictionary<string, object> TransformLocks = new(StringComparer.Ordinal);

    private static object LockFor(string key)
    {
        lock (TransformLocks)
        {
            if (!TransformLocks.TryGetValue(key, out object? gate))
                TransformLocks[key] = gate = new object();
            return gate;
        }
    }

    private static readonly Dictionary<string, AppSpec> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calculator"] = new(
            Setting("WFWASM_CALCULATOR_DIR", @"external-apps\calculator"),
            new[] { "Calculator.dll" },
            "Calculator"),
        ["todo"] = new(
            Repo(@"targets\simple-csharp-todolist\SimpleDailyToDoList\SimpleDailyToDoList\bin\Debug"),
            new[] { "SimpleDailyToDoList.exe" },
            "SimpleDailyToDoList"),
        ["tictactoe"] = new(
            Repo(@"targets\tic-tac-toe\bin\Debug"),
            new[] { "Tic Tac Toe.exe" },
            "Tic Tac Toe"),
        ["calendar"] = new(
            Repo(@"targets\functional-calendar\bin\Debug\net8.0-windows"),
            new[] { "grp2Calendar.dll" },
            "grp2Calendar"),
        ["paintnet"] = new(
            Repo(@"targets\paint.net"),
            new[]
            {
                "PaintDotNet.Base.dll",
                "PaintDotNet.Collections.dll",
                "PaintDotNet.ComponentModel.dll",
                "PaintDotNet.Core.dll",
                "PaintDotNet.Data.dll",
                "paintdotnet.dll",
                "PaintDotNet.Effects.Core.dll",
                "PaintDotNet.Effects.dll",
                "PaintDotNet.Effects.Gpu.dll",
                "PaintDotNet.Effects.Legacy.dll",
                "PaintDotNet.Framework.dll",
                "PaintDotNet.Fundamentals.dll",
                "PaintDotNet.ObjectModel.dll",
                "PaintDotNet.Plugins.Compatibility.dll",
                "PaintDotNet.Primitives.dll",
                "PaintDotNet.PropertySystem.dll",
                "PaintDotNet.Resources.dll",
                "PaintDotNet.Runtime.dll",
                "PaintDotNet.SystemLayer.dll",
                "PaintDotNet.Systrace.dll",
                "PaintDotNet.UI.dll",
                "PaintDotNet.Windows.Core.dll",
                "PaintDotNet.Windows.dll",
                "PaintDotNet.Windows.Framework.dll",
                "CommunityToolkit.HighPerformance.dll",
                "ComputeSharp.Core.dll",
                "ComputeSharp.D2D1.dll",
                "K4os.Compression.LZ4.dll",
                "Newtonsoft.Json.dll",
                "PhotoSauce.MagicScaler.dll",
                "PointerToolkit.dll",
                "PointerToolkit.TerraFX.Interop.Windows.dll",
                "SauceControl.Blake2Fast.dll",
                "TerraFX.Interop.Windows.dll",
                "WindowsBase.dll",
                "System.Xaml.dll",
                "PresentationCore.dll",
                "PresentationFramework.dll",
                "System.Windows.Forms.Legacy.dll",
                "System.Windows.Extensions.dll",
                "System.IO.Packaging.dll",
                "System.Reflection.MetadataLoadContext.dll",
                "DirectWriteForwarder.dll",
                "Bundled/AvifFileType/AvifFileType.dll",
                "Bundled/DDSFileTypePlus/DdsFileTypePlus.dll",
                "Bundled/JpegXLFileType/JpegXLFileType.dll",
                "Bundled/WebPFileType/WebPFileType.dll",
            },
            "paintdotnet")
        {
            Verbatim = new[]
            {
                "CommunityToolkit.HighPerformance.dll",
                "ComputeSharp.Core.dll",
                "ComputeSharp.D2D1.dll",
                "K4os.Compression.LZ4.dll",
                "Newtonsoft.Json.dll",
                "PhotoSauce.MagicScaler.dll",
                "PointerToolkit.dll",
                "PointerToolkit.TerraFX.Interop.Windows.dll",
                "SauceControl.Blake2Fast.dll",
                "TerraFX.Interop.Windows.dll",
                "WindowsBase.dll",
                "System.Xaml.dll",
                "PresentationCore.dll",
                "PresentationFramework.dll",
                "System.Windows.Forms.Legacy.dll",
                "System.Windows.Extensions.dll",
                "System.IO.Packaging.dll",
                "System.Reflection.MetadataLoadContext.dll",
                "Bundled/AvifFileType/AvifFileType.dll",
                "Bundled/DDSFileTypePlus/DdsFileTypePlus.dll",
                "Bundled/JpegXLFileType/JpegXLFileType.dll",
                "Bundled/WebPFileType/WebPFileType.dll",
            },
            Handmap = Repo(@"src\WfWasm.Rewrite.Tool\handmap.paint.net.txt"),
            PdnCpu = Repo(@"src\WfWasm.PdnCpu\bin\Debug\net9.0\WfWasm.PdnCpu.dll"),
            SourceOverride = new(StringComparer.OrdinalIgnoreCase)
            {
                ["DirectWriteForwarder.dll"] = Repo(@"external-apps\paintnet-support\DirectWriteForwarder.dll"),
            },
        },
        ["telerik-bookshelf"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\BookShelf\bin"),
            "BookShelf.exe",
            "BookShelf") with
        {
            ContentFiles = BookShelfContent(),
        },
        ["telerik-bugtracker"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\BugTracker\bin"),
            "BugTracker.exe",
            "BugTracker") with
        {
            ContentFiles = BugTrackerContent(),
        },
        ["telerik-demoappshub"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\DemoAppsHub\bin"),
            "DemoAppsHub.exe",
            "DemoAppsHub"),
        ["telerik-diagram"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\Diagram\DiagramFirstLook\bin"),
            "DiagramFirstLook.exe",
            "DiagramFirstLook"),
        ["telerik-erp"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\ERP\ERP.Client\bin"),
            "ERP.Client.exe",
            "ERP.Client"),
        ["telerik-exportwithdpl"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\ExportWithDpl\bin"),
            "ExportWithDpl.exe",
            "ExportWithDpl"),
        ["telerik-fileexplorer"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\FileExplorer\bin"),
            "FileExplorer.exe",
            "FileExplorer"),
        ["telerik-hotelapp"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\HotelApp\HotelAppCS\bin"),
            "HotelApp.exe",
            "HotelApp"),
        ["telerik-medicalapp"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\MedicalApp\MedicalAppCS\bin"),
            "MedicalAppCS.exe",
            "MedicalAppCS"),
        ["telerik-richtexteditor"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\RichTextEditor\bin"),
            "RichTextEditor.exe",
            "RichTextEditor"),
        ["telerik-rssreader"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\RssReader\bin"),
            "RssReader.exe",
            "RssReader"),
        ["telerik-spellcheckasyoutype"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\SpellCheckAsYouType\bin"),
            "SpellCheckAsYouType.exe",
            "SpellCheckAsYouType"),
        ["telerik-toastnotificationmanagerexamples"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\ToastNotificationManagerExamples\bin"),
            "ToastNotificationManagerExamples.exe",
            "ToastNotificationManagerExamples"),
        ["telerik-photo"] = TelerikAppSpec(
            Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\PhotoAlbum\bin"),
            "PhotoAlbum.exe",
            "PhotoAlbum") with
        {
            ContentFiles = PhotoAlbumContent(),
        },
    };


    // Regenerable scratch belongs in E:/tmp (and %TEMP% is where
    // on-access scanners race file creation, observed as flaky
    // Access-denied on freshly-written outputs).
    public static string CacheRoot => Setting("WFWASM_CACHE", @".artifacts\transform-cache");

    private static string ForkDll =>
        Repo(@".artifacts\runtime\WfWasm.WinForms.dll");

    private static string DrawingDll =>
        Repo(@"src\WfWasm.Drawing\bin\Debug\net9.0\WfWasm.Drawing.dll");

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    // Bump when the transform PROCESS changes without its inputs
    // changing (a new code path must not reuse bytes cached by the
    // old one, e.g. pristine fallbacks cached before sibling
    // hardlinks fixed third-party stubbing).
    private const int TransformRevision = 37;

    // Cache key covers every transform input: pristine bytes, the
    // fork and drawing assemblies a rewrite remaps onto, the handmap
    // and CPU helper of paint.net-style closures, and the rewriter.
    public static string DependencyHash(string app)
    {
        if (!Apps.TryGetValue(app, out var spec))
            throw new ArgumentException($"unknown app '{app}'", nameof(app));
        byte[] fork = File.ReadAllBytes(ForkDll);
        using var sha = SHA256.Create();
        sha.TransformBlock(fork, 0, fork.Length, null, 0);
        var drawing = File.ReadAllBytes(DrawingDll);
        sha.TransformBlock(drawing, 0, drawing.Length, null, 0);
        if (spec.Handmap is not null && File.Exists(spec.Handmap))
        {
            var map = File.ReadAllBytes(spec.Handmap);
            sha.TransformBlock(map, 0, map.Length, null, 0);
        }

        if (spec.PdnCpu is not null && File.Exists(spec.PdnCpu))
        {
            var cpu = File.ReadAllBytes(spec.PdnCpu);
            sha.TransformBlock(cpu, 0, cpu.Length, null, 0);
        }

        var stamp = typeof(AssemblyRewriter).Assembly.ManifestModule.ModuleVersionId.ToString();
        var stampBytes = System.Text.Encoding.UTF8.GetBytes(stamp + "|rev" + TransformRevision);
        sha.TransformFinalBlock(stampBytes, 0, stampBytes.Length);
        return Convert.ToHexString(sha.Hash!);
    }

    private static string CacheKey(byte[] pristine, string dependencyHash)
    {
        // A digest of the common inputs is shared by every assembly in one
        // manifest request. Re-read/hash all inputs each request; no mtime-only
        // shortcut can hide a restored file with a preserved timestamp.
        using var sha = SHA256.Create();
        sha.TransformBlock(pristine, 0, pristine.Length, null, 0);
        byte[] dependencies = System.Text.Encoding.UTF8.GetBytes(dependencyHash);
        sha.TransformFinalBlock(dependencies, 0, dependencies.Length);
        return Convert.ToHexString(sha.Hash!);
    }

    // Returns transformed bytes, rewriting once per distinct inputs.
    public static byte[] TransformOrCached(string app, string name, string? dependencyHash = null)
    {
        if (!Apps.TryGetValue(app, out var spec))
            throw new ArgumentException($"unknown app '{app}'", nameof(app));
        if (!TryAssemblyPath(spec, name, out string? pristinePath) || pristinePath is null)
            throw new FileNotFoundException($"assembly not found in configured app roots: {app}/{name}");
        byte[] pristine = File.ReadAllBytes(pristinePath);

        string inputHash = CacheKey(pristine, dependencyHash ?? DependencyHash(app));
        string key = app + "/" + name + "/" + inputHash;
        if (MemCache.TryGetValue(key, out byte[]? mem))
            return mem;

        string dir = Path.Combine(CacheRoot, app);
        Directory.CreateDirectory(dir);
        string flat = name.Replace('/', '_').Replace('\\', '_');
        string cached = Path.Combine(dir, flat + "." + inputHash + ".bin");
        string sidecar = cached + ".sha";
        lock (LockFor(app + "/" + name))
        {
            if (MemCache.TryGetValue(key, out mem))
                return mem;

            try
            {
                if (File.Exists(cached) && File.Exists(sidecar))
                {
                    string want = File.ReadAllText(sidecar);
                    byte[] disk = File.ReadAllBytes(cached);
                    if (string.Equals(want, Sha256Hex(disk), StringComparison.OrdinalIgnoreCase))
                    {
                        MemCache[key] = disk;
                        return disk;
                    }

                    try { File.Delete(cached); } catch { }
                    try { File.Delete(sidecar); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppTransform] Disk cache unreadable for {app}/{name}, re-transforming: {ex.GetType().Name}");
            }

            if (spec.Pristine.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                // Byte-identical serving for assemblies the rewriter must
                // not touch (Mono rejects Cecil-processed metadata-only
                // reference assemblies; the pristine bytes load fine).
                MemCache[key] = pristine;
                try
                {
                    File.WriteAllBytes(cached, pristine);
                    File.WriteAllText(sidecar, Sha256Hex(pristine));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AppTransform] Disk cache write failed for {app}/{name} (serving from memory): {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
                return pristine;
            }

            var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = ForkDll,
                ["System.Windows.Forms.Primitives"] = ForkDll,
                ["System.Drawing"] = DrawingDll,
                ["System.Drawing.Common"] = DrawingDll,
                ["System.Drawing.Primitives"] = DrawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(pristinePath, assemblyMap);
            if (spec.Handmap is not null && File.Exists(spec.Handmap))
            {
                // Hand map lines "Source.Full.Name=Assembly:Target.Full.Name"
                // (same format as the batch tool's --map).
                foreach (string line in File.ReadAllLines(spec.Handmap))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                        continue;
                    string[] sides = trimmed.Split('=', 2);
                    if (sides.Length != 2)
                        continue;
                    string[] target = sides[1].Split(':', 2);
                    if (target.Length != 2)
                        continue;
                    typeMap[sides[0]] = (target[0], target[1]);
                }
            }

            byte[] bytes;
            if (spec.Verbatim.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                // Third-party interop ships without a full rewrite; only
                // its P/Invokes are stubbed (the batch pipeline's stub
                // pass). Stubbing needs files, so this goes through a
                // transient copy; on any file failure the pristine bytes
                // still serve (native calls then fail at call time, not
                // load time).
                bytes = StubbedCopy(app, name, pristinePath, pristine);
            }
            else
            {
                // RAM-only transform: the bytes never rest in a file, so
                // content-sensitive file filters cannot deny re-opening
                // them (observed with grp2Calendar.dll). The disk write
                // below is only the cross-restart cache.
                bytes = AssemblyRewriter.RewriteToBytes(pristinePath, assemblyMap, typeMap, spec.PdnCpu);
            }

            MemCache[key] = bytes;
            try
            {
                File.WriteAllBytes(cached, bytes);
                File.WriteAllText(sidecar, Sha256Hex(bytes));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppTransform] Disk cache write failed for {app}/{name} (serving from memory): {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }

            return bytes;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string target, IntPtr security);

    // Batch-equivalent stub pass for third-party assemblies: the file
    // goes to a transient dir with hardlinks to its pristine siblings
    // (the stub resolver searches the file's own directory, mirroring
    // the batch's in-place pass over the full closure; hardlinks read
    // the pristine tree without writing to it). Anything file-related
    // failing here falls back to the pristine bytes.
    private static byte[] StubbedCopy(string app, string name, string pristinePath, byte[] pristine)
    {
        string tmpDir = Path.Combine(CacheRoot, "tmp", "stub-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmpDir);
            try
            {
                string? sibDir = Path.GetDirectoryName(Path.GetFullPath(pristinePath));
                string ownName = Path.GetFileName(pristinePath);
                if (sibDir is not null && Directory.Exists(sibDir))
                {
                    foreach (string sib in Directory.GetFiles(sibDir, "*.dll"))
                    {
                        // Never link the file itself: the transient copy
                        // is overwritten in place, which through a link
                        // would corrupt the pristine tree.
                        if (string.Equals(Path.GetFileName(sib), ownName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { CreateHardLink(Path.Combine(tmpDir, Path.GetFileName(sib)), sib, IntPtr.Zero); } catch { }
                    }
                }

                string tmp = Path.Combine(tmpDir, Path.GetFileName(pristinePath));
                File.WriteAllBytes(tmp, pristine);
                // Independent passes, mirroring the batch (which runs
                // patch-probes even when stub-pinvokes fails, e.g.
                // PresentationFramework's unresolvable PlaySoundFlags
                // constant aborts the stub write but not the patch).
                try
                {
                    AssemblyRewriter.StubPInvokes(tmp, tmp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AppTransform] Stub pass failed for {app}/{name}, keeping other passes: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }

                try
                {
                    AssemblyRewriter.PatchRuntimeProbes(tmp, tmp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AppTransform] Probe patch failed for {app}/{name}, serving pristine: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    return pristine;
                }

                return File.ReadAllBytes(tmp);
            }
            finally
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppTransform] Transient stub copy failed for {app}/{name}, serving pristine: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return pristine;
        }
    }

    public static string ContentHash(string app, string name, string? dependencyHash = null) =>
        Sha256Hex(TransformOrCached(app, name, dependencyHash));
}
