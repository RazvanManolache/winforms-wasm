namespace System.Windows.Forms;

// Browser emulation for System.Diagnostics.FileVersionInfo.
//
// The browser-wasm framework ships FileVersionInfo as a THROWING STUB:
// every getter, GetVersionInfo, even the internal ctor unconditionally
// throws PlatformNotSupportedException (verified by decompiling
// Microsoft.NETCore.App.Runtime.Mono.browser-wasm 9.0.21's
// System.Diagnostics.FileVersionInfo.dll: assembly carries
// NotSupported=True + UnsupportedOSPlatform("browser"), zero fields).
// There are no backing fields, so filling an uninitialized instance via
// reflection cannot work; the first property read still throws and (as
// seen in Cyotek AboutDialog.OnLoad) aborts whole OnLoad chains, leaving
// e.g. the Demonstrations tab unbuilt and a blank panel.
//
// Strategy: the rewriter redirects GetVersionInfo(string) here AND every
// instance getter/ToString to the Read*/Describe helpers below. The
// returned object is an uninitialized FileVersionInfo used only as a
// ConditionalWeakTable key carrying the resolved assembly metadata;
// readers answer from that record (never from the stub instance). The
// class is sealed with non-virtual getters, so no subclass could help.
// All reflection is guarded: worst case the app sees blank labels,
// never an exception.
public static class WasmFileVersion
{
    private sealed class Record
    {
        public string FileName = "";
        public string SimpleName = "";
        public string Original = "";
        public int Major;
        public int Minor;
        public int Build;
        public int Revision;
        public string Product = "";
        public string Company = "";
        public string Description = "";
        public string Copyright = "";
        public string Informational = "";
        public string FileVersion = "";
        public string Language = "";
        public string Comments = "";
        public string Trademarks = "";
        public string PrivateBuild = "";
        public string SpecialBuild = "";
        public bool IsDebug;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        System.Diagnostics.FileVersionInfo, Record> _records = new();

    public static System.Diagnostics.FileVersionInfo GetVersionInfo(string? fileName)
    {
        Record rec = Resolve(fileName);
        object box;
        try
        {
            box = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(System.Diagnostics.FileVersionInfo));
        }
        catch
        {
            // Should never happen; callers expect non-null.
            throw new InvalidOperationException("WasmFileVersion: cannot materialize FileVersionInfo.");
        }
        var info = (System.Diagnostics.FileVersionInfo)box;
        try { _records.Add(info, rec); } catch { }
        return info;
    }

    private static Record? Lookup(object? fvi)
    {
        try
        {
            if (fvi is System.Diagnostics.FileVersionInfo info && _records.TryGetValue(info, out Record? rec))
                return rec;
        }
        catch { }
        return null;
    }

    private static string Str(object? fvi, Func<Record, string> pick)
    {
        try
        {
            Record? r = Lookup(fvi);
            if (r is not null)
                return pick(r) ?? "";
        }
        catch { }
        return "";
    }

    private static int Num(object? fvi, Func<Record, int> pick)
    {
        try
        {
            Record? r = Lookup(fvi);
            if (r is not null)
                return pick(r);
        }
        catch { }
        return 0;
    }

    private static bool Flag(object? fvi, Func<Record, bool> pick)
    {
        try
        {
            Record? r = Lookup(fvi);
            if (r is not null)
                return pick(r);
        }
        catch { }
        return false;
    }

    public static string ReadComments(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Comments);
    public static string ReadCompanyName(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Company);
    public static string ReadFileDescription(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Description);
    public static string ReadFileVersion(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.FileVersion);
    public static string ReadInternalName(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Original);
    public static string ReadLegalCopyright(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Copyright);
    public static string ReadLegalTrademarks(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Trademarks);
    public static string ReadOriginalFilename(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Original);
    public static string ReadPrivateBuild(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.PrivateBuild);
    public static string ReadProductName(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Product);
    public static string ReadProductVersion(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Informational);
    public static string ReadSpecialBuild(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.SpecialBuild);
    public static string ReadLanguage(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.Language);
    public static string ReadFileName(System.Diagnostics.FileVersionInfo fvi) => Str(fvi, r => r.FileName);

    public static int ReadFileBuildPart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Build);
    public static int ReadFileMajorPart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Major);
    public static int ReadFileMinorPart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Minor);
    public static int ReadFilePrivatePart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Revision);
    public static int ReadProductBuildPart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Build);
    public static int ReadProductMajorPart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Major);
    public static int ReadProductMinorPart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Minor);
    public static int ReadProductPrivatePart(System.Diagnostics.FileVersionInfo fvi) => Num(fvi, r => r.Revision);

    public static bool ReadIsDebug(System.Diagnostics.FileVersionInfo fvi) => Flag(fvi, r => r.IsDebug);
    public static bool ReadIsPatched(System.Diagnostics.FileVersionInfo fvi) => Flag(fvi, r => false);
    public static bool ReadIsPreRelease(System.Diagnostics.FileVersionInfo fvi) => Flag(fvi, r => false);
    public static bool ReadIsPrivateBuild(System.Diagnostics.FileVersionInfo fvi) => Flag(fvi, r => false);
    public static bool ReadIsSpecialBuild(System.Diagnostics.FileVersionInfo fvi) => Flag(fvi, r => false);

    // Mirrors the desktop multi-line ToString dump (labels + values).
    public static string Describe(System.Diagnostics.FileVersionInfo fvi)
    {
        Record? r = Lookup(fvi);
        if (r is null)
            return "";
        string nl = System.Environment.NewLine;
        return "File:             " + r.FileName + nl
            + "InternalName:     " + r.Original + nl
            + "OriginalFilename: " + r.Original + nl
            + "FileVersion:      " + r.FileVersion + nl
            + "FileDescription:  " + r.Description + nl
            + "Product:          " + r.Product + nl
            + "ProductVersion:   " + r.Informational + nl
            + "Debug:            " + (r.IsDebug ? "True" : "False") + nl
            + "Patched:          False" + nl
            + "PreRelease:       False" + nl
            + "PrivateBuild:     False" + nl
            + "SpecialBuild:     False" + nl
            + "Language:         " + r.Language;
    }

    private static Record Resolve(string? fileName)
    {
        var rec = new Record { FileName = fileName ?? "" };
        try
        {
            System.Reflection.Assembly? asm = FindAssembly(fileName);
            if (asm is null)
            {
                // No metadata resolves: still echo a plausible original name.
                rec.Original = BaseName(fileName);
                return rec;
            }
            try
            {
                string? loc = asm.Location;
                if (!string.IsNullOrEmpty(loc))
                    rec.Original = System.IO.Path.GetFileName(loc);
            }
            catch { }
            if (string.IsNullOrEmpty(rec.Original))
                rec.Original = BaseName(fileName);
            if (string.IsNullOrEmpty(rec.Original))
                rec.Original = rec.SimpleName;
            System.Reflection.AssemblyName an = asm.GetName();
            rec.SimpleName = an.Name ?? "";
            System.Version? version = an.Version;
            rec.Product = GetAttr<System.Reflection.AssemblyProductAttribute>(asm, a => a.Product);
            rec.Copyright = GetAttr<System.Reflection.AssemblyCopyrightAttribute>(asm, a => a.Copyright);
            rec.Company = GetAttr<System.Reflection.AssemblyCompanyAttribute>(asm, a => a.Company);
            rec.Description = GetAttr<System.Reflection.AssemblyDescriptionAttribute>(asm, a => a.Description);
            rec.Informational = GetAttr<System.Reflection.AssemblyInformationalVersionAttribute>(asm, a => a.InformationalVersion);
            rec.FileVersion = GetAttr<System.Reflection.AssemblyFileVersionAttribute>(asm, a => a.Version);
            rec.Comments = GetAttr<System.Reflection.AssemblyMetadataAttribute>(asm, a => a.Key == "Comments" ? a.Value : null);
            if (string.IsNullOrEmpty(rec.FileVersion) && version is not null)
                rec.FileVersion = version.ToString();
            if (string.IsNullOrEmpty(rec.Informational))
                rec.Informational = rec.FileVersion;
            if (string.IsNullOrEmpty(rec.Product))
                rec.Product = rec.SimpleName;
            if (string.IsNullOrEmpty(rec.Description))
                rec.Description = rec.SimpleName;
            if (version is not null)
            {
                rec.Major = version.Major;
                rec.Minor = version.Minor;
                rec.Build = Math.Max(0, version.Build);
                rec.Revision = Math.Max(0, version.Revision);
            }
            try { rec.IsDebug = asm.IsDefined(typeof(System.Diagnostics.DebuggableAttribute), false); } catch { }
            try
            {
                // The native version block carries a language; desktop renders
                // neutral-culture assemblies as "Language Neutral".
                string cult = an.CultureName ?? "";
                if (string.IsNullOrEmpty(cult) || cult.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                    rec.Language = "Language Neutral";
                else
                {
                    try { rec.Language = new System.Globalization.CultureInfo(cult).DisplayName; } catch { }
                    if (string.IsNullOrEmpty(rec.Language))
                        rec.Language = cult;
                }
            }
            catch { }
        }
        catch { }
        return rec;
    }

    private static System.Reflection.Assembly? FindAssembly(string? fileName)
    {
        try
        {
            System.Reflection.Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            if (!string.IsNullOrEmpty(fileName))
            {
                foreach (System.Reflection.Assembly a in loaded)
                {
                    try
                    {
                        string? loc = a.Location;
                        if (!string.IsNullOrEmpty(loc) &&
                            (string.Equals(loc, fileName, StringComparison.OrdinalIgnoreCase) ||
                             loc.EndsWith(System.IO.Path.DirectorySeparatorChar + fileName, StringComparison.OrdinalIgnoreCase) ||
                             loc.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase)))
                            return a;
                    }
                    catch { }
                    try
                    {
                        string? sn = a.GetName().Name;
                        string want = fileName;
                        int slash = Math.Max(want.LastIndexOf('/'), want.LastIndexOf('\\'));
                        if (slash >= 0)
                            want = want.Substring(slash + 1);
                        if (want.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || want.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            want = want.Substring(0, want.Length - 4);
                        if (!string.IsNullOrEmpty(sn) && string.Equals(sn, want, StringComparison.OrdinalIgnoreCase))
                            return a;
                    }
                    catch { }
                }
            }
            try
            {
                System.Reflection.Assembly? entry = System.Reflection.Assembly.GetEntryAssembly();
                if (entry is not null)
                    return entry;
            }
            catch { }
            try
            {
                return System.Reflection.Assembly.GetCallingAssembly();
            }
            catch { }
        }
        catch { }
        return null;
    }

    private static string BaseName(string? fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName))
                return "";
            string want = fileName;
            int slash = Math.Max(want.LastIndexOf('/'), want.LastIndexOf('\\'));
            if (slash >= 0)
                want = want.Substring(slash + 1);
            return want;
        }
        catch { }
        return "";
    }

    private static string GetAttr<T>(System.Reflection.Assembly asm, Func<T, string?> pick) where T : Attribute
    {
        try
        {
            object[] arr = asm.GetCustomAttributes(typeof(T), false);
            if (arr.Length > 0 && arr[0] is T t)
                return pick(t) ?? "";
        }
        catch { }
        return "";
    }
}
