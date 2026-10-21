namespace System.Windows.Forms;

// Rewritten-module entry for ComponentResourceManager/ResourceManager
// .GetObject calls (the rewriter retargets those call sites here instead
// of Drawing.ResourceHelper). Designer ".ImageStream" entries hydrate
// into real ImageListStreamers (the fork Deserialize decodes the MSFt
// body into the managed store); everything else flows through to the
// Drawing helper unchanged.
public static class WasmResourceShim
{
    public static object? SafeGetObject(System.Resources.ResourceManager? rm, string? name)
    {
        if (TryHydrateImageStream(rm, name, out ImageListStreamer? streamer))
            return streamer;
        return WfWasm.Drawing.ResourceHelper.SafeGetObject(rm, name);
    }

    public static object? SafeGetObject(System.Resources.ResourceManager? rm, string? name, System.Globalization.CultureInfo? culture)
    {
        if (TryHydrateImageStream(rm, name, out ImageListStreamer? streamer))
            return streamer;
        return WfWasm.Drawing.ResourceHelper.SafeGetObject(rm, name, culture);
    }

    // Rewritten-module entries for ResourceManager/ComponentResourceManager
    // .GetString calls (the rewriter retargets those call sites here). BCL
    // GetString funnels into GetObject/GetResourceSet, which instantiates
    // the legacy ResourceReader for v1-header streams; vendor .resources
    // that name DeserializingResourceReader then throw NSE out of BCL with
    // no managed catch on the path, and the WASM unwinder cannot cross the
    // reflection-invoke frames (native OOB trap, session dead). The shim
    // resolves through the throw-free SafeGetObject instead and answers
    // null for anything unresolvable (desktop returns null for a missing
    // string entry; a wholly missing set surfaces as null here rather than
    // MissingManifestResourceException — documented headless deviation).
    public static string? SafeGetString(System.Resources.ResourceManager? rm, string? name)
    {
        try
        {
            return WasmResourceShim.SafeGetObject(rm, name) as string;
        }
        catch
        {
            return null;
        }
    }

    public static string? SafeGetString(System.Resources.ResourceManager? rm, string? name, System.Globalization.CultureInfo? culture)
    {
        return SafeGetString(rm, name);
    }

    // Same treatment for .GetStream: byte[] entries surface as a read-only
    // MemoryStream (headless stand-in for BCL's UnmanagedMemoryStream),
    // Stream entries pass through, everything else answers null.
    public static System.IO.Stream? SafeGetStream(System.Resources.ResourceManager? rm, string? name)
    {
        try
        {
            object? value = WasmResourceShim.SafeGetObject(rm, name);
            if (value is System.IO.Stream stream)
                return stream;
            if (value is byte[] bytes)
                return new System.IO.MemoryStream(bytes, writable: false);
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static System.IO.Stream? SafeGetStream(System.Resources.ResourceManager? rm, string? name, System.Globalization.CultureInfo? culture)
    {
        return SafeGetStream(rm, name);
    }

    // Rewritten-module entry for ResourceManager/ComponentResourceManager
    // .GetResourceSet (3-arg). Syncfusion's SR wrappers call it directly
    // (SetResources/GetLocalizedResourceManager); BCL opens the stream
    // with the legacy reader and the v1-header/DeserializingResourceReader
    // vendor streams throw NSE with no managed catch (same silent OOB
    // trap). The shim answers a derived set whose virtuals resolve
    // through the throw-free SafeGetObject, so readers get desktop
    // values and the session never dies. createIfNotExists: false
    // answers null exactly like BCL (missing-set detection keeps
    // working); enumeration over the derived set is empty (its table is
    // never filled) rather than fatal.
    public static System.Resources.ResourceSet? SafeGetResourceSet(
        System.Resources.ResourceManager? rm,
        System.Globalization.CultureInfo? culture,
        bool createIfNotExists,
        bool tryParents)
    {
        try
        {
            if (rm is null || !createIfNotExists)
                return null;
            return new SafeResourceSet(rm);
        }
        catch
        {
            return null;
        }
    }

    private sealed class SafeResourceSet : System.Resources.ResourceSet
    {
        private readonly System.Resources.ResourceManager _rm;

        public SafeResourceSet(System.Resources.ResourceManager rm)
        {
            _rm = rm;
        }

        public override object? GetObject(string name)
        {
            try
            {
                return WasmResourceShim.SafeGetObject(_rm, name);
            }
            catch
            {
                return null;
            }
        }

        public override object? GetObject(string name, bool ignoreCase)
        {
            try
            {
                object? exact = WasmResourceShim.SafeGetObject(_rm, name);
                if (exact is not null || !ignoreCase)
                    return exact;
                foreach (string key in WfWasm.Drawing.ResourceHelper.EnumerateKeys(_rm))
                {
                    if (string.Equals(key, name, System.StringComparison.OrdinalIgnoreCase))
                        return WasmResourceShim.SafeGetObject(_rm, key);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public override string? GetString(string name)
        {
            try
            {
                return WasmResourceShim.SafeGetObject(_rm, name) as string;
            }
            catch
            {
                return null;
            }
        }

        public override string? GetString(string name, bool ignoreCase)
        {
            try
            {
                return GetObject(name, ignoreCase) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool TryHydrateImageStream(System.Resources.ResourceManager? rm, string? name, out ImageListStreamer? streamer)
    {
        streamer = null;
        if (rm is null || name is null || !name.EndsWith(".ImageStream", StringComparison.Ordinal))
            return false;
        try
        {
            if (!WfWasm.Drawing.ResourceHelper.TryGetImageListStreamerBytes(rm, name, out byte[]? msft)
                || msft is null)
                return false;
            var candidate = new ImageListStreamer(msft);
            if (candidate.GetNativeImageList() is not { } native || native.HIMAGELIST.IsNull)
                return false;
            streamer = candidate;
            int slots = WasmImageListStore.GetCount(native.HIMAGELIST.Value);
            System.Console.WriteLine($"[ImageStream] hydrated {name} slots={slots}");
            return true;
        }
        catch (Exception ex)
        {
            try { System.Console.WriteLine($"[ImageStream] hydrate FAILED {name}: {ex.GetType().Name} {ex.Message?.Split('\n')[0]}"); } catch { }
            return false;
        }
    }
}
