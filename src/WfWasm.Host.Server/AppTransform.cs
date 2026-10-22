using System.Security.Cryptography;
using System.Reflection;
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

    // Syncfusion grids implement UI Automation providers: Grid.Windows
    // references UIAutomationProvider v9 at load, and without it the Mono
    // interpreter FailFasts in mint_type (transform.c:347) right after the
    // form constructs (grid-cells/grouping/pivot/skin-manager). Served
    // pristine from the v9 desktop pack like the Telerik UIA set: types
    // resolve, and nothing headless ever invokes the native backend.
    private static string UiaDesktopFile(string name)
    {
        try
        {
            string pack = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App";
            if (Directory.Exists(pack))
            {
                string? best = null;
                foreach (string dir in Directory.EnumerateDirectories(pack, "9.*"))
                    if (best is null || string.Compare(Path.GetFileName(dir), Path.GetFileName(best), StringComparison.OrdinalIgnoreCase) > 0)
                        best = dir;
                if (best is not null)
                {
                    string candidate = Path.Combine(best, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch { }
        return @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\" + name[..^4] + @"\v4.0_4.0.0.0__31bf3856ad364e35\" + name;
    }
    private static readonly string[] SyncfusionSystemAssemblies = new[]
    {
        "UIAutomationProvider.dll",
        "UIAutomationTypes.dll",
        "UIAutomationClient.dll",
    };
    private static readonly Dictionary<string, string> UiaOverrides = BuildUiaOverrides();
    private static Dictionary<string, string> BuildUiaOverrides()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string sysDll in SyncfusionSystemAssemblies)
            map[sysDll] = UiaDesktopFile(sysDll);
        return map;
    }
    static AppTransform()
    {
        string root = Environment.GetEnvironmentVariable("WF_SYNCFUSION_APPS_ROOT") ?? Repo(@"external-apps\syncfusion");
        foreach (var demo in WfWasm.CanvasHost.SyncfusionDemos.Entries)
        {
            string folder = Path.Combine(root, demo.Slug);
            Apps.Add(demo.Slug, new AppSpec(folder, [demo.Assembly + ".dll"], demo.Assembly)
            {
                ContentFiles = SyncfusionContent(folder),
                SourceOverride = new Dictionary<string, string>(UiaOverrides, StringComparer.OrdinalIgnoreCase),
                Pristine = SyncfusionSystemAssemblies,
            });
        }
        // Files outside the build-output folder are invisible to
        // SyncfusionContent: add them per slug, skipping sources absent
        // from this checkout (a missing file must not 500 the manifest).
        if (Apps.TryGetValue("syncfusion-diagram", out var diagram) && diagram is not null)
            foreach (var kv in DiagramContent())
                if (File.Exists(kv.Value))
                    diagram.ContentFiles[kv.Key] = kv.Value;
    }

    // NodesDemo.Form1_Load builds a BitmapNode from image3.png via a
    // dev-layout relative path (Form1.cs:129); the file ships upstream
    // under common/images, not in the build output. Served as VFS content
    // (lookups match by tail): without it BitmapNode..ctor throws
    // FileNotFoundException inside OnLoad, which crosses the native Show
    // boundary and OOB-traps the session with no managed frame.
    private static Dictionary<string, string> DiagramContent() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image3.png"] = Repo(@"external-apps\syncfusion-sources\common\images\Diagram\OrgChart Layout\image3.png"),
        };

    private static Dictionary<string, string> SyncfusionContent(string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return files;
        // Only copied sample content. DLLs resolve by CLR request; configs/keys are never loose content.
        var extensions = new HashSet<string>([".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".csv", ".xml", ".xsd", ".shp", ".shx", ".dbf"], StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!extensions.Contains(Path.GetExtension(file)) || Path.GetFileName(file).Contains("license", StringComparison.OrdinalIgnoreCase)) continue;
            files[Path.GetRelativePath(folder, file).Replace('\\', '/')] = file;
        }
        return files;
    }
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
    private static readonly string TelerikPool =
        Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release");
    // net462 system assemblies Telerik touches as field/base types but never
    // calls headless (UIA provider slots on RadForm). Served pristine from
    // the GAC (real IL: Mono rejects both Cecil-processed and pristine
    // metadata-only reference assemblies at Assembly.Load).
        // Most GAC system assemblies carry the framework key; the ECMA-key
    // System.* assemblies (Composition) carry b77a5c561934e089 instead.
    private static string GacPublicKey(string name) =>
        name.Equals("System.ComponentModel.Composition", StringComparison.OrdinalIgnoreCase)
            ? "b77a5c561934e089"
            : "31bf3856ad364e35";
    private static string GacMsil(string name) =>
        @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\" + name + @"\v4.0_4.0.0.0__" + GacPublicKey(name) + @"\" + name + ".dll";
    private static readonly string[] TelerikSystemAssemblies = new[]
    {
        "UIAutomationProvider.dll",
        "UIAutomationTypes.dll",
        "UIAutomationClient.dll",
        "WindowsBase.dll",
        // MEF1: Telerik RichTextEditor/SpellCheck compose parts at startup
        // and die with FileNotFoundException without it. Pure managed, so
        // the pristine GAC copy loads under Mono-WASM.
        "System.ComponentModel.Composition.dll",
    };
    // WinRT contract stubs built from src/WfWasm.WinRtStubs.* (referenced
    // by this project so the DLLs sit beside the server). Served to every
    // app: desktop libraries (Telerik toast, UWP Toolkit) reference
    // Windows.* types that only exist inside the Windows Runtime.
    private static readonly Dictionary<string, string> WinRtStubs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows.Foundation.UniversalApiContract"] = "Windows.Foundation.UniversalApiContract.dll",
        ["Windows.Foundation.FoundationContract"] = "Windows.Foundation.FoundationContract.dll",
    };
    // EventRegistrationToken rides in mscorlib on desktop .NET Framework
    // but is missing from the browser CoreLib; the rewriter repoints its
    // typeref at the FoundationContract stub, which defines it.
    private const string EventRegistrationTokenFullName =
        "System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken";
    private const string WindowsRuntimeMarshalFullName =
        "System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal";
    // PhotoAlbum reads background.jpg + Photos/*.jpg loose from disk
    // (Form1_Load). Manifest names use forward slashes; the VFS
    // normalizes every lookup, so backslash callers still hit.
    // ERP.Repository.InitializeData fills its caches from Data/*.json via
    // Path.Combine(BaseDirectory, "..", "Data") (dev-layout relative).
    // The files live in SOURCES (never bin) and the browser BaseDirectory
    // ("/") breaks the relative walk, so the panes load empty. Serve all
    // 14 as VFS content: lookups match by tail, so "/Data/X.json" hits.
    private static Dictionary<string, string> ErpContent()
    {
        string data = Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\ERP\ERP.Client\Data");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in new[]
        {
            "Bill.json", "Customer.json", "IndividualCustomer.json", "Location.json",
            "Order.json", "Product.json", "ProductInventory.json", "PurchaseOrder.json",
            "ShipMethod.json", "StateProvince.json", "StoreCustomer.json", "UnitMeasure.json",
            "Vendor.json", "WorkOrder.json",
        })
            map[$"Data/{file}"] = Path.Combine(data, file);
        return map;
    }

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

    // Desktop parity: modern runtimes default to Segoe UI 9pt, .NET
    // Framework to Microsoft Sans Serif 8.25pt. The entry assembly's
    // TargetFrameworkAttribute decides; anything unreadable is modern
    // (the majority of the catalog). The browser applies it with
    // Application.SetDefaultFont before the first window exists.
    public static (string Name, float Size) DefaultFontFor(string app)
    {
        try
        {
            if (TryGetSpec(app, out var spec) && spec is not null
                && TryResolveAssembly(app, spec.Entry, out string? entryFile) && entryFile is not null
                && TryAssemblyPath(spec, entryFile, out string? path) && path is not null)
            {
                using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
                var tfm = assembly.CustomAttributes.FirstOrDefault(a =>
                    a.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute");
                string frameworks = tfm?.ConstructorArguments.FirstOrDefault().Value as string ?? "";
                if (frameworks.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase))
                    return ("Microsoft Sans Serif", 8.25f);
            }
        }
        catch
        {
        }

        return ("Segoe UI", 9f);
    }

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
        // WinRT contract stubs live beside the server (project reference),
        // not in any app folder; every app may resolve them.
        if (WinRtStubs.TryGetValue(AssemblyStem(name), out string? stubFile))
        {
            string stubPath = Path.Combine(AppContext.BaseDirectory, stubFile);
            if (!File.Exists(stubPath)) return false;
            path = stubPath;
            return true;
        }
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
                // Hand-built managed fake (mixed-mode pristine aborts WASM
                // interp): no WinForms/Drawing/pinvoke surface, so stub-only.
                "DirectWriteForwarder.dll",
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
            "ERP.Client") with
        {
            ContentFiles = ErpContent(),
        },
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
    private const int TransformRevision = 127; // rev127: PersistedObject<T>.Flush retained as ret (PushNewMemento flushes at push: with disk persist stubbed that eviction loses the only copy, so our replicated Levels memento is born unreadable and every effect-undo IOEs; memory stays bounded by the stack via the rev126 pin); // rev126: SoftReference<T> pinned strongly headless (rev125's async-race theory was wrong: PersistToDisk is a stubbed ret by the older settings-persist rule, so NOTHING ever reaches disk; the soft target demotes at gen-2 GC and every undo IOEs at get_Object's session-file read); // rev125: synchronous history persist (async save task never completes headless: every undo IOEd at get_Object); // rev124: linearize sRGB token black/white points at the LUT boundary (v1 fed them as linear: white=100 mapped 85 to 132, correct is 220); // rev123: defuse abandoned kernel event handles at construction (finalize-crash of the browser PAL at first gen-2 GC: the ~4-stroke-move tab death); // rev122: history row named from IEffect.EffectInfo via the interface map + null row icon (a resolved ImageResource blanks the whole row headless: pixel-less device image; text-only rows paint); // rev121: resume passes real full-surface render scans (nulls NREd the queued memento builder + copy phase, absorbed as applied=True with no undo push); // rev120: permanent [FXAPPLY] resume-result log (rev119 probe: dialog settles, zero canvas/history delta, silent catch â€” result now observable); // rev119: TEMP-DIAG fully reverted (gray-force/census/trace gone; Levels live path back to bona-fide identity no-op pending real dialog input routing); // rev118: TEMP-DIAG force moved into the tile path (owner Token is null at Resume time: "must be configurable"; it is set by tile time); // rev117: TEMP-DIAG force-failure inner logging (the gray-force TIE hides its cause); // rev116: TEMP-DIAG forced constant-gray owner token (dialog input routing dead, no non-default token producible by hand; proves the LUT path live; REVERT IMMEDIATELY); // rev115: Token lookup walks most-derived first (GpuEffect<TToken> shadows EffectBase.Token with new: flat lookup died AmbiguousMatch, tiles fell through); // rev114: read EffectBase.Token with non-public flags (protected: public-only lookup missed, every tile fell through to the null-context base); // rev113: TEMP-DIAG tile dispatch reasons (FXTILE no:* + stash lines; the fallthrough NREs in base OnCopyPixels with no tile line, so which gate misses is unknown); // rev112: CPU Levels tiles (EnsureBitmapSourceCpuTolerant skips the device span on null factory; EnsureGpuTileCpuRender routes OnCopyPixels through CpuPaint.TryRenderGpuTileCpu: linear-light LUT from the token over the stashed surface); // rev111: EnsureEffectDriverInline dooms the Thread construction too (leftover newobj Thread/ThreadStart unbalanced Start's stack at ret: InvalidProgram, observed as Levels DoEffect Shown-handler death with the caller blamed); // rev110: ResumeEffectApply walks the menu hierarchy for private base DoEffect (derived AdjustmentsMenu hides it: "needs EffectMenuBase.DoEffect" after OK); // rev109: CloneEffectToken scans loaded PaintDotNet assemblies for CloneableExtensions (LevelsGpuEffectToken lives in Effects.Gpu, the helper does not: resume died with NSE after OK); // rev108: NoteEffectMenu also notes the hosted inner form (the noted dialog is an EffectConfigFormWrapper but FormClosed fires on the inner LevelsGpuEffectForm: CWT missed by instance); // rev107: TEMP-DIAG FXRESUME note-side trace with instance ids (FormClosed adapter runs but the CWT lookup misses: was the note skipped or a different instance); // rev106: TEMP-DIAG FXRESUME trace in ResumeEffectApply (Levels OK opens/closes, hook fires, but no history entry: which resume step says no); // rev105: ResumeEffectApplyAdap is public (generic EffectConfigForm<T,E> binds it cross-class: private died with MethodAccessException on GPU dialog show); // rev104: EnsureEffectDialogResume hooks the generic EffectConfigForm<T,E>.ShowDialog too (GPU dialogs override it with a direct Form.ShowDialog call: Levels opened/closed with no FormClosed resume and no apply); // rev103: EnsureEffectApplyResume anchors its insert (re-reading seq[at+1] per InsertBefore emitted the note sequence reversed: call on empty stack = InvalidProgram in RunEffectImpl when opening Adjustments/Levels); // rev102: CpuCommandList answers IObjectRef (AddCpuObjectRef: SetInput->CreateRef(IObjectRef) ODEd every Colors crop input although nothing was disposed); // rev101: TEMP-DIAG SetInput refcount trace; // rev100: TEMP-DIAG block-site census (NoteBlock); // rev99: census list adds Slim/RW locks; // rev98: TEMP-DIAG wait-handle census (NoteWaitHandle) to find the per-stroke abandoned event; revert after diagnosis; // rev97: fx gates re-enabled (driver inline/menu-note/dialog-resume for effect OK-apply) + update checks neutered (abandoned boot events finalize-crash the browser PAL at first gen-2 GC, killing strokes ~4 moves in); // rev96: BISECT guards off (effect inline/menu-note/dialog-resume disabled) to isolate the stroke crash; // rev95: effect OK-apply resume (EffectDriver.Start runs the worker inline, tile Enqueue inline + Joins pop, RunEffectImpl notes the menu, FormClosed adapter reruns the product DoEffect with a fresh snapshot: ShowDialog-None-immediate + neutered threads made every Levels OK a silent no-op); // rev94: DrawTextLayout raster-first (TextLayoutRaster reads the layout font by name through the browser text engine, CpuPaint.BlitRaster tints/blits; null raster keeps the 5x7 DrawText fallback: panel headers/menus/palette text instead of black blocks); // rev93: CpuDeviceEffect.SetInput retains the grant in a side table and stores the ORIGINAL in _inputs (v4: v1 stored the grant and broke GetInput casts with InvalidCast, and PDN disposing GetInput results underflowed the effect's ref into ObjectDisposed; originals keep rev91 identity, grants pair +1/-1 privately) // rev92: CpuDeviceEffect.SetInput retains its input via ObjectRefExtensions.CreateRef and releases the overwritten input (desktop D2D AddRefs through COM; without it CreateImpl's tile loop freed the shared bitmap while the processed effect still referenced it: zero-handle upload, blank wheel with only the nub painted) // rev91: GetCachedOrCreateResource matches concrete color-profile carriers (ColorProfile/SimpleColorProfile), not just the IColorProfileProxy interface name: ColorProfiles.Srgb/ScRgb singletons NSEd (blank Colors wheel, paint never reached DrawImage) // rev90: real ReinterpretedBitmapSource wrapper restored (identity shortcut removed: it answered the caller-owned bitmap and CreateImpl disposed it as an owned tile clip, pool-returning live single-ref sources to 0x0 + freeing wrappers the pipeline still read/NRE); WIC imaging clips/color-transforms now answer COUNTED refs via CreateRef (callers own+dispose the returned clip: CreateImpl tile loop, usings, RefHandles) // rev89: GetProxySourceImage fixpoint unwraps BitmapImage.image/proxy/InnerRef shells to the raw CpuDeviceBitmap (color-wheel DrawImage(ColorManagementEffect) blanked with zero errors; effect->image->proxy->PrecisionEffect->bitmap now blits) // rev88: DrawImage ColorManagementEffect falls through to the proxy-source blit (the identity no-op blanked primary blits: color wheel painted nothing with zero errors; GetProxySourceImage already allowlists it) // rev87: BitmapImage.CreateImpl reinterpret branch keeps ownsRef:false custody (identity shortcut answers the caller-owned bitmap: ownsRef:true dispose pool-returned live sources so tile locks read empty; wheel-sized uploads NSEd while small ones survived on spare refs) // rev86: CpuDrawingContext TryGetResourceCache<T> rebuilds its IObjectRef generic constraint via ImportReference (sharing the contract's foreign TypeReference re-scoped it to System.Numerics.Vectors at write time: live TypeLoad 010002E7 killed paint brush strokes + GPU-effect dialogs) // rev85: CpuDrawingContext device union (IDeviceContext/ICompatibleDeviceContext/IResourceCacheHolder slots, RefTrackedObject base, FromBitmap/BeginDraw CPU bodies with copy-in/write-back, CreateCompatible/EndDraw/Dispose(bool) cores) // rev84: paintdotnet ChunkedReadStream prefetch continuations take the ExecuteSynchronously overload (headless single thread has no pump: queued chunk reads never ran, so TryGetNextChunkWhileLocked .Result blocked forever; chained reads always have a complete antecedent, so every prefetch now runs inline) // rev83: paintdotnet ChunkedReadStream.Dispose prefetch drain drops its blocking Task.Wait (headless prefetches never complete: every history-undo died with PNSE ObjWait; pop swaps the void call in place, same stack effect) // rev82: DrawText bridge reports CALCRECT extents through the out pair (tree labels are measured with CALCRECT and painted into the measured rect; stub-zero extents squeezed every label to slivers) // rev81: app-private gdi32 DrawText P/Invokes retarget to fork WasmGdiText.DrawText (generic stub zero dropped all TreeViewAdv node text; vendor RECT& decomposes through the signature's own struct, field casing varies) // rev80: GetScrollBarInfo stub materializes SCROLLBARINFO.rgstate (native marshaller parity: virgin structs NREd vendor ScrollersFrame.UpdateScrollerSettings during TreeViewAdv parenting) // rev79: rev78 Draw trampoline fixed to reuse the module's own type references (hand-built struct refs default IsValueType=false and misbound at runtime: MissingMethod on desktop, TypeLoad in wasm) // rev78: diagram RichText paint headless (ForceDiagramBitmapText defaults m_bUseBitmap in RichTextNode ctors; RichTextBoxDrawing.Draw(Graphics, Rectangle) trampolines to managed WasmRtfDraw; native EM_FORMATRANGE/metafile shells unreached) // rev77: RichTextBox.StreamIn/StreamOut(Stream) bodies replaced with trampolines to managed WasmRtf (GetFunctionPointerForDelegate EDITSTREAM thunks OOB-trapped the wasm interp with no managed frame: diagram RichTextNode boot) // rev75: kernel32 LoadLibraryEx/GetModuleHandle retarget to WasmNativeLibrary null-handle stubs (MsftEdit absent headless: RichTextBox.CreateParams OOB-trapped the session with no managed frame; null keeps the Win32Exception path) // rev74: guard in-try returns rewritten to Leave done (raw Ret inside a guard try is unverifiable: desktop InvalidProgramException, wasm unwind-frame trap; killed syncfusion-calendar/diagram ctors) // rev73: ctor-guard also wraps parameterless instance .ctors (base-ctor/field-init/post-Init throws died identically outside InitializeComponent) // rev72: // rev72: InitializeComponent ctor-guard (uncaught designer throws crossed the reflection-invoke LMF and OOB-trapped the session with zero managed frames: stash + host fallback) // rev71: // rev71: GetResourceSet retarget to fork-shim SafeGetResourceSet (Syncfusion SR SetResources/GetLocalizedResourceManager called BCL direct: same NSE death) // rev70: // rev70: ResourceManager/ComponentResourceManager.GetString+GetStream retarget to fork-shim SafeGetString/SafeGetStream (raw BCL reads of v1-header/DeserializingResourceReader vendor .resources threw NSE with no managed catch: silent OOB trap killed calendar+diagram ctors) + Drawing ResourceHelper opens .resources with the header-named reader (verbatim values from modern streams) // rev69: // rev69: fork RichTextBox.UpdateOleCallback keeps only its DragAcceptFiles(false) tail (COM OLE callback absent headless: GetComPointer PNS killed every RichTextBox handle creation, worker instance OOB-trapped the session) // rev68: Syncfusion.Shared.Base SfForm.CheckIsWindows11 -> false (registry build number missing headless: int.Parse(null) killed every Syncfusion demo form) // rev67: PaintDotNet.Framework PdnCheckBox.GetPreferredSize -> CPU measure (null DWrite text layout threw inside layout/show and aborted dialog presentation) // rev66: PaintDotNet.SystemLayer ShellUtil.GetVirtualPath -> /vfs layout (SHGetFolderPath answers "" headless: Save-As died on empty path, app-data/cache/crash dirs vanished) // rev65: PaintDotNet.Data FileType.GetLastSaveConfigToken -> CreateDefaultSaveConfigToken (persisted per-filetype tokens need the user settings store unavailable headless: Save-As died in set_FileType before the dialog) // rev64: paintdotnet WicHelpers.GetLoad/SaveExtensions -> desktop extension table for Microsoft built-ins (empty WIC headless nulled every built-in DefaultSaveExtension: Save-As died before any dialog) // rev63: // rev63: paintdotnet DiagnosticsGenerator.GetFormattedText -> WfWasm.Drawing.PdnDiagnostics browser-safe fallback (headless inventory probes fault + wrap the original save error in AggregateException: Save-As failure invisibly fatal) // rev62: // rev62: TextBlock.DrawGdi -> DrawGdiPlus managed retarget (GDI DrawTransparentText DIB pipeline has no headless backend: no span glyph ever painted) // rev61: app-private GDI text extents (GetTextExtentPoint32/ExPoint + NativeTextRenderer SelectObject) retarget to fork WasmGdiText (zeroed extents measured every RTE span 0x0: typed glyphs invisible + perpetual relayout) // rev60: InvokeLayoutCallback's BeginInvoke retargets to fork WasmLayoutQueue (handle-less BeginInvoke throws; rev59 guard alone wedged form creation) // rev59: RadControl.InvokeLayoutCallback IsHandleCreated guard neutralized (TPF layout queue starvation: RTE glyph-paint staleness) // rev58: WindowsBase verbatim-plus micro-pass neutralizes Assembly.ReflectionOnly guard (paint MONO cascade: NIE escaping resolve handler kills runtime) // rev57: app resolution closure (spec.Folder + AssemblySearchFolders) threads into RewriteToBytes extraDirs (DiagramFirstLook RadDiagram 500: pool UI.dll invisible to Cecil) // rev56: Telerik ThemeResolutionService.AllowAnimations -> false (Thread.Start neutering deadlocks the popup AnimationEngine handshake: grid right-click wedge) // rev55: System.IO.IsolatedStorage.* retarget to headless stub (spellchecker dictionary PNSE) // rev54: System.Media.* retarget to headless System.Windows.Extensions stub (hub RadMessageBox SystemSounds TypeLoad) // rev53: Environment.GetFolderPath -> fork WasmEnvironment virtual /vfs layout (RTE FontsManager cctor empty-path death) // rev52: coherent FrameworkResolver (real-binds-local, facades-unify-to-fx; pool-facade forwarder cycle SO at write) + dead-ref trim last with reachability (F-bounded visited set) // rev51: assemblyref trim keeps used scopes (DWF self-ref CoreLib corruption) + DWF Verbatim (stub-only fake) // rev50: namespaceless nested typeref remap (Interop+/WMExtensions SWF 500) + fnptr/exotic-sig gap identity (Analyze NRE) // rev49: GetDpiForMonitor stub reports S_OK + 96/96 outs, GetDpiForWindow/System answer 96 (Telerik RadForm boot COMException) // rev48: CpuDrawingContext SystemColor-case stray Ldloc removed (stack-merge InvalidProgram) + FileSystem.CreateTempFile managed redirect (stub null-handle cctor death) // rev47: module cctor preserves same-module ModuleInitializer calls (hook wiring) // rev46: GetObject redirect retargets to fork WasmResourceShim (imagelist hydration) // rev45: generic GetTextMetrics stub fills coherent TEXTMETRIC (NaN-layout fix) // rev44: Telerik RTE Uniscribe.ContainsComplexScript -> false (no usp10 headless) // rev43: PDN Direct2DControl.OnHandleCreatedInternal neutralized (device impl bypassed) // rev42: GetWindowThreadProcessId stub returns UI thread 1 (thread-affinity coherence) // rev41: System.Data.OleDb.* -> headless stub assembly // rev40: UWP Toolkit headless-init stubs // rev39: WindowsRuntimeMarshal -> WinRT stub remap // rev38: EventRegistrationToken -> WinRT stub remap

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

            // WinRT stubs ship byte-identical like Pristine: they carry no
            // WinForms/PInvoke surface for the rewriter, and their contract
            // identity (name/version) must survive untouched.
            if (spec.Pristine.Contains(name, StringComparer.OrdinalIgnoreCase)
                || WinRtStubs.ContainsKey(AssemblyStem(name)))
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
            // WinRT interop token missing from the browser CoreLib (see map).
            typeMap[EventRegistrationTokenFullName] = ("Windows.Foundation.FoundationContract", EventRegistrationTokenFullName);
            typeMap[WindowsRuntimeMarshalFullName] = ("Windows.Foundation.FoundationContract", WindowsRuntimeMarshalFullName);

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
                bytes = AssemblyRewriter.RewriteToBytes(pristinePath, assemblyMap, typeMap, spec.PdnCpu, null,
                    // Rewrites introduce references to host compatibility assemblies (for example
                    // WfWasm.IsolatedStorage). Cecil must resolve those while importing signatures.
                    // This is transform-time lookup only; it adds no browser downloads.
                    new[] { spec.Folder, AppContext.BaseDirectory }.Concat(spec.AssemblySearchFolders));
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

                // Verbatim-plus (rev58): WPF's AssemblyHelper.OnLoaded reads
                // Assembly.ReflectionOnly, whose Mono-WASM builder override
                // throws NIE; escaping an AssemblyResolve handler that kills
                // the runtime (paint MONO cascade). Neutralize the guard to
                // false (exact .NET Core behavior: no reflection-only
                // context exists). Fail-open: the resolve hook (Fix 1)
                // converts any residual death into a catchable miss.
                if (string.Equals(name, "WindowsBase.dll", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int sites = AssemblyRewriter.NeutralizeReflectionOnlyGuards(tmp, tmp);
                        Console.WriteLine($"[AppTransform] ReflectionOnly guards neutralized for {app}/{name}: {sites} site(s).");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AppTransform] ReflectionOnly neutralize failed for {app}/{name}, keeping stubbed bytes: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    }
                }

                // Rev59 layout handle guard runs inside RewriteToBytes (Telerik.WinControls is not Verbatim).
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

    // Manifest prefetch closure: every managed assembly the demand hook
    // could serve, so the client warms them in parallel instead of
    // faulting them one round trip at a time. Native images can never
    // bind (BadImageFormat) and missing files are skipped; the demand
    // hook stays the fallback for everything else (optional assemblies,
    // version drift). Integrity comes from the response header at fetch.
    public static bool TryPrefetchPath(string app, string name, out string? path)
    {
        path = null;
        if (!Apps.TryGetValue(app, out var spec)) return false;
        if (!TryAssemblyPath(spec, name, out path) || path is null) return false;
        if (!File.Exists(path)) return false;
        try { _ = AssemblyName.GetAssemblyName(path); }
        catch (BadImageFormatException) { return false; }
        catch (FileNotFoundException) { return false; }
        return true;
    }

    public static string ContentHash(string app, string name, string? dependencyHash = null) =>
        Sha256Hex(TransformOrCached(app, name, dependencyHash));
}
