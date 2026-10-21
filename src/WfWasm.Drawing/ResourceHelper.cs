// Safe resource accessor that catches BinaryFormatter removal exceptions in .NET 9+
namespace WfWasm.Drawing;

using System.Drawing;

public static class ResourceHelper
{
    // Decoded resource images, per manager: ResourceManager caches
    // successful GetObject calls but never failures, and every Bitmap/
    // Icon entry fails on .NET 9+ (BinaryFormatter removed), so without
    // this each lookup would re-throw PNSE and re-parse NRBF. Misses
    // store a miss versioned by the loaded-assembly generation: bytes
    // are static but assemblies load lazily, so a miss heals when the
    // set grows (FilteringIcon probes can run before GridView
    // arrives). Versioned misses still skip the doomed GetObject
    // re-throw; placeholders stay uncached (fresh per call).
    private static int s_asmGen;

    private sealed record CachedMiss(int Generation);

    static ResourceHelper()
    {
        System.AppDomain.CurrentDomain.AssemblyLoad += (_, _) => NoteAssemblyLoaded();
    }

    internal static void NoteAssemblyLoaded() =>
        System.Threading.Interlocked.Increment(ref s_asmGen);

    // Browser BaseDirectory root: the runtime answers null/empty
    // (assemblies load from bytes), so Combine(null, ...) dies. Lives
    // HERE, not in the fork shim, because the small-app migration
    // self-rewrites the fork: a fork-resident AppContext call would be
    // retargeted at the shim itself (infinite recursion -> stack
    // overflow in every migrated-fork consumer). Drawing is never
    // self-rewritten, so this call stays real. Same delegation shape
    // as WasmResourceShim -> ResourceHelper.SafeGetObject.
    public static string AppBaseDirectory()
    {
        string? real = System.AppContext.BaseDirectory;
        return string.IsNullOrEmpty(real) ? "/app" : real;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        System.Resources.ResourceManager, System.Collections.Generic.Dictionary<string, object?>> s_imgCache = new();

    private static bool TryGetCachedImage(System.Resources.ResourceManager rm, string name,
        out object? image, out bool knownMiss)
    {
        image = null;
        knownMiss = false;
        if (!s_imgCache.TryGetValue(rm, out System.Collections.Generic.Dictionary<string, object?>? d))
            return false;
        lock (d)
        {
            if (!d.TryGetValue(name, out object? v))
                return false;
            if (v is CachedMiss miss)
            {
                if (miss.Generation == System.Threading.Volatile.Read(ref s_asmGen))
                {
                    knownMiss = true;
                    return false;
                }
                d.Remove(name);
                return false;
            }
            image = v;
            return true;
        }
    }

    private static void CacheImage(System.Resources.ResourceManager rm, string name, object? image)
    {
        var d = s_imgCache.GetValue(rm,
            _ => new System.Collections.Generic.Dictionary<string, object?>(System.StringComparer.Ordinal));
        lock (d)
        {
            d[name] = image ?? new CachedMiss(System.Threading.Volatile.Read(ref s_asmGen));
        }
    }

    // Entries whose value the .resources V2 table stores inline
    // (ResourceTypeCode primitives, strings, byte arrays, streams)
    // deserialize without BinaryFormatter; every other resolved entry
    // is a user-type payload that can only throw PNSE out of GetObject
    // on .NET 9+. Closed by the V2 spec; anything unrecognized keeps
    // the legacy GetObject probe below.
    // ResourceReader.GetResourceData reports inline V2 entries by
    // wire code (verified live: "ResourceTypeCode.String" etc.),
    // never by CLR name; user-type payloads carry the stored
    // assembly-qualified name instead. All wire-code entries
    // materialize without BinaryFormatter, so they keep the
    // direct GetObject.
    private static bool IsInlineEntryType(string typeName) =>
        typeName.StartsWith("ResourceTypeCode.", System.StringComparison.Ordinal);

    private static bool IsImageEntryType(string typeName) =>
        typeName.Contains("Bitmap", System.StringComparison.Ordinal)
        || typeName.Contains("Image,", System.StringComparison.Ordinal)
        || typeName.Contains("Icon", System.StringComparison.Ordinal);

    // Repeat lookups of proven inline entries skip the manifest probe.
    // True when the stream header carries an explicit non-legacy reader
    // name (modern streams name DeserializingResourceReader). v1 legacy
    // streams carry no name field, so a candidate string only counts
    // when it actually looks like a reader type name; anything else is
    // legacy content and answers false. Never throws; leaves position.
    private static bool TryPeekNamedReader(System.IO.Stream s, out string readerName)
    {
        readerName = "";
        try
        {
            if (!s.CanSeek)
                return false;
            long pos = 0;
            try { pos = s.Position; }
            catch { return false; }
            try
            {
                s.Position = 0;
                using var br = new System.IO.BinaryReader(
                    s, System.Text.Encoding.UTF8, leaveOpen: true);
                if (br.ReadInt32() != unchecked((int)0xBEEFCACE))
                    return false;
                int version = br.ReadInt32();
                if (version != 1)
                {
                    try { readerName = br.ReadString(); }
                    catch { readerName = ""; }
                    return true;
                }
                string candidate = "";
                try { candidate = br.ReadString(); }
                catch { return false; }
                // Containment (not equality): stray table bytes never
                // spell the 14-char token at a valid 7-bit boundary,
                // and a miss degrades to a lookup miss, never a throw.
                if (candidate.Contains("ResourceReader", System.StringComparison.Ordinal))
                {
                    readerName = candidate;
                    return !IsLegacyReaderName(readerName);
                }
                return false;
            }
            finally
            {
                try { s.Position = pos; }
                catch { }
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLegacyReaderName(string readerName) =>
        readerName.StartsWith("System.Resources.ResourceReader,",
            System.StringComparison.Ordinal);

    // Version-tolerant reader resolution: vendor streams stamp the
    // reference-assembly version (4.0.0.0) while the runtime ships
    // whatever it ships (9.0.0.0 on desktop, framework subset on
    // WASM), so strict Type.GetType misses a perfectly good reader
    // and every downstream enumeration degrades to zero keys (managed
    // calendar daycontainer stuck at its 200x100 default, hit=null).
    private static Type? ResolveReaderType(string readerName)
    {
        try
        {
            Type? t = Type.GetType(readerName, throwOnError: false);
            if (t is not null)
                return t;
            int comma = readerName.IndexOf(',');
            if (comma <= 0)
                return null;
            string bare = readerName.Substring(0, comma).Trim();
            string asmPart = readerName.Substring(comma + 1).Trim();
            string simpleName = asmPart.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(simpleName))
            {
                try
                {
                    var loaded = System.Reflection.Assembly.Load(
                        new System.Reflection.AssemblyName(simpleName));
                    t = loaded?.GetType(bare, throwOnError: false, ignoreCase: false);
                    if (t is not null)
                        return t;
                }
                catch { }
            }
            try
            {
                t = Type.GetType(bare, throwOnError: false);
                if (t is not null)
                    return t;
            }
            catch { }
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (a.IsDynamic)
                        continue;
                    t = a.GetType(bare, throwOnError: false, ignoreCase: false);
                    if (t is not null)
                        return t;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    public static System.Resources.IResourceReader? OpenResourceReader(System.IO.Stream s)
    {
        string namedReader = "";
        bool hasNamed = false;
        try { hasNamed = TryPeekNamedReader(s, out namedReader); }
        catch { }
        if (hasNamed && !string.IsNullOrEmpty(namedReader))
        {
            // NOTE: no RuntimeResourceSet attempt here on purpose: it has
            // no public Stream ctor (MissingMethodException), BCL builds
            // it through internals. Frankenstein blobs answer null from
            // this method (named ctor rejects v1, legacy NSEs) and
            // enumerate through the BCL-GetResourceSet fallback in
            // EnumeratePrefixedKeys instead, which is proven on the
            // vendor calendar blob (134 keys).
            Type? t = ResolveReaderType(namedReader);
            if (t is not null
                && typeof(System.Resources.IResourceReader).IsAssignableFrom(t))
            {
                try
                {
                    object? r = Activator.CreateInstance(t, s);
                    if (r is System.Resources.IResourceReader named)
                        return named;
                }
                catch { }
            }
            // Named but unresolvable/unconstructible: fall through to
            // the legacy attempt below (clean v1 content under a
            // renamed header still reads; frankenstein content throws
            // NSE there, caught into null). Returning null here is
            // what zeroed every enumeration on runtimes whose
            // framework version differs from the stamped one.
        }
        try
        {
            return new System.Resources.ResourceReader(s);
        }
        catch
        {
            return null;
        }
    }

    public static System.Collections.Generic.IEnumerable<string> EnumerateKeys(
        System.Resources.ResourceManager rm)
    {
        try
        {
            return EnumeratePrefixedKeys(rm, "", null);
        }
        catch
        {
            return System.Linq.Enumerable.Empty<string>();
        }
    }

    private static void Trace(string line)
    {
        /* resource tracing removed (was ~1.2k lines/run); re-enable temporarily for resource triage */ System.GC.KeepAlive(line);
    }

    private static bool TryGetNamedEntry(
        System.Reflection.Assembly asm,
        System.Collections.Generic.List<string> candidates,
        string name,
        out object? value)
    {
        value = null;
        foreach (string cand in candidates)
        {
            System.IO.Stream? s = null;
            try { s = asm.GetManifestResourceStream(cand); }
            catch { }
            if (s is null)
                continue;
            try
            {
                using (s)
                {
                    System.Resources.IResourceReader? reader = OpenResourceReader(s);
                    Trace("TEMP named-probe cand=" + cand + " reader=" + (reader is null ? "null" : reader.GetType().FullName));
                    if (reader is null || reader is System.Resources.ResourceReader)
                        continue;
                    using (reader)
                    {
                        var en = reader.GetEnumerator();
                        while (en.MoveNext())
                        {
                            if (!string.Equals(en.Key as string, name, System.StringComparison.Ordinal))
                                continue;
                            Trace("TEMP named-enum-hit cand=" + cand + " key=" + name);
                            value = en.Value;
                            return true;
                        }
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private sealed record KnownDirect;

    // Pass BCL successes through verbatim (byte[] SVG payloads,
    // streams, strings, images): the value is exactly what desktop
    // returns for this entry. (Null-for-proven-non-image here starved
    // Telerik RadSvgImage.FromByteArray and killed BugTracker
    // construction; misses still answer below.)
    private static object? DirectGetObject(System.Resources.ResourceManager rm, string name)
    {
        Trace("TEMP bcl-enter key=" + name);
        try
        {
            object? v = rm.GetObject(name);
            Trace("TEMP bcl-exit key=" + name + " value=" + (v is null ? "null" : v.GetType().FullName));
            return v;
        }
        catch (Exception ex)
        {
            Trace("TEMP bcl-throw key=" + name + " ex=" + ex.GetType().Name);
            return null;
        }
    }

    public static object? SafeGetObject(System.Resources.ResourceManager? rm, string? name)
    {
        Trace("TEMP get key=" + name + " base=" + (rm?.BaseName ?? "?"));
        if (rm == null || name == null) return null;
        if (TryGetCachedImage(rm, name, out object? cached, out bool knownMiss))
            return (cached is KnownDirect) ? DirectGetObject(rm, name) : cached;
        if (!knownMiss)
        {
            // Throw-free entry probe first: GetResourceData reads the raw
            // entry type without any deserializer, so BinaryFormatter
            // image entries go straight to the NRBF decoder and never pay
            // for the doomed GetObject below. Each caught throw there is
            // still a first-chance report (the per-boot PNSE noise across
            // all Telerik demos), so avoiding the throw IS the fix.
            if (TryResourceCandidates(rm, out System.Collections.Generic.List<string>? namedCandidates)
                && namedCandidates is not null
                && TryResolveResourceAssembly(rm, namedCandidates, name, out System.Reflection.Assembly? namedAsm)
                && namedAsm is not null
                && TryGetNamedEntry(namedAsm, namedCandidates, name, out object? namedValue)
                && namedValue is not null)
            {
                Trace("TEMP named-hit key=" + name);
                return namedValue;
            }
            if (TryResolveImageEntry(rm, name, out string? typeName, out byte[]? payload)
                && typeName is not null && payload is not null)
            {
                if (IsImageEntryType(typeName))
                {
                    if (TryDecodePayload(typeName, payload, out object? decoded))
                    {
                        CacheImage(rm, name, decoded);
                        return decoded;
                    }
                    CacheImage(rm, name, null);
                }
                else if (IsInlineEntryType(typeName))
                {
                    CacheImage(rm, name, new KnownDirect());
                    object? inline = DirectGetObject(rm, name);
                    if (inline != null)
                        return inline;
                    CacheImage(rm, name, null);
                }
                else
                {
                    // Resolved non-image user type (ImageListStreamer
                    // blobs, cursors, ...): GetObject could only throw
                    // PNSE for these, so record the miss directly.
                    CacheImage(rm, name, null);
                }
            }
            else
            {
                object? direct = DirectGetObject(rm, name);
                if (direct != null)
                    return direct;
                // BinaryFormatter-serialized Bitmap/Icon entries always land
                // here on .NET 9+: recover the image file bytes from the raw
                // user-type payload (no deserializer involved).
                if (TryDecodeResourceImage(rm, name, out object? decoded))
                {
                    CacheImage(rm, name, decoded);
                    return decoded;
                }
                CacheImage(rm, name, null);
            }
        }

        // Fallbacks for games/apps with stripped BinaryFormatter resources:
        if (string.Equals(name, "X", StringComparison.OrdinalIgnoreCase))
        {
            var bmp = new Bitmap(64, 64);
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Red, 6f);
            g.DrawLine(pen, 12, 12, 52, 52);
            g.DrawLine(pen, 12, 52, 52, 12);
            return bmp;
        }

        if (string.Equals(name, "O", StringComparison.OrdinalIgnoreCase))
        {
            var bmp = new Bitmap(64, 64);
            using var g = Graphics.FromImage(bmp);
            using var pen = new Pen(Color.Blue, 6f);
            g.DrawEllipse(pen, 10, 10, 44, 44);
            return bmp;
        }

        if (string.Equals(name, "$this.Icon", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "icons8_list", StringComparison.OrdinalIgnoreCase))
        {
            return new Icon();
        }

        // A failed lookup for a proven non-image entry (ImageListStreamer
        // blobs, cursors, ...) answers null like a missing resource: the
        // bitmap placeholder below can never satisfy the caller's cast
        // and only converts the resource miss into an InvalidCast.
        // Entries whose type is unknown or image-compatible keep the
        // placeholder (every path that works today is unchanged).
        if (IsProvenNonImageEntry(rm, name))
            return null;

        return new Bitmap(32, 32);
    }

    // Resolves a designer ".ImageStream" entry into its raw "MSFt" body
    // for the fork's imagelist hydration (the fork's ImageListStreamer
    // decodes it; Drawing cannot reference fork types). Anything
    // unexpected answers false and the caller degrades to an empty list.
    public static bool TryGetImageListStreamerBytes(System.Resources.ResourceManager? rm, string? name,
        out byte[]? msft)
    {
        msft = null;
        if (rm is null || string.IsNullOrEmpty(name))
            return false;
        try
        {
            if (!TryResolveImageEntry(rm, name, out string? typeName, out byte[]? payload)
                || payload is null)
                return false;
            if (typeName is null || !typeName.Contains("ImageListStreamer", StringComparison.Ordinal))
                return false;
            if (!NrbfImage.TryExtractImageListBytes(payload, out byte[] found))
                return false;
            msft = found;
            return true;
        }
        catch
        {
            msft = null;
            return false;
        }
    }

    // ResourceManager hides which assembly it reads (.NET Core dropped
    // MainAssembly entirely), so the probe must recover the resource
    // holder. Resolution is ENTRY-aware: an assembly only wins when its
    // candidate stream actually contains the requested entry (several
    // loaded assemblies routinely share a base name -- Telerik repeats
    // one per demo -- so the old first-stream-hit scan opened the wrong
    // assembly and every entry missed into the 32px placeholder).
    // App-derived managers name their own assembly first. Cached per
    // manager; any failure degrades to false, i.e. today's placeholder.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        System.Resources.ResourceManager, System.Reflection.Assembly> s_probeAsm = new();

    private static bool IsProvenNonImageEntry(System.Resources.ResourceManager rm, string name)
    {
        if (!TryResourceCandidates(rm, out System.Collections.Generic.List<string>? candidates)
            || candidates is null)
            return false;
        if (!TryResolveResourceAssembly(rm, candidates, name, out System.Reflection.Assembly? asm) || asm is null)
            return false;
        foreach (var cand in candidates)
        {
            System.IO.Stream? s = null;
            try { s = asm.GetManifestResourceStream(cand); }
            catch { }
            if (ProbeStream(s, name, out bool nonImage))
                return nonImage;
        }
        return false;
    }

    // Recovers a BinaryFormatter-serialized Bitmap/Icon entry without
    // a deserializer: GetResourceData returns the raw user-type bytes
    // (no BinaryFormatter involved), NrbfImage lifts the embedded
    // image file out of the record stream, and the Bitmap/Icon ctors
    // decode it. Anything unexpected answers false (caller falls back
    // to today's placeholder), so entries that decode keep working
    // and entries that cannot decode render exactly as today.
    private static bool TryDecodeResourceImage(System.Resources.ResourceManager rm, string name, out object? image)
    {
        image = null;
        if (!TryResolveImageEntry(rm, name, out string? typeName, out byte[]? payload)
            || typeName is null || payload is null)
            return false;
        return TryDecodePayload(typeName, payload, out image);
    }

    private static bool TryDecodePayload(string typeName, byte[] payload, out object? image)
    {
        image = null;
        try
        {
            if (typeName.Contains("Icon", System.StringComparison.Ordinal)
                && NrbfImage.TryExtractImageBytes(payload, out byte[] icoBytes))
            {
                using var ms = new System.IO.MemoryStream(icoBytes, false);
                image = new Icon(ms);
                return true;
            }
            if ((typeName.Contains("Bitmap", System.StringComparison.Ordinal)
                    || typeName.Contains("Image,", System.StringComparison.Ordinal))
                && NrbfImage.TryExtractImageBytes(payload, out byte[] imgBytes))
            {
                using var ms = new System.IO.MemoryStream(imgBytes, false);
                image = new Bitmap(ms);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryResolveImageEntry(System.Resources.ResourceManager rm, string name,
        out string? typeName, out byte[]? payload)
    {
        typeName = null;
        payload = null;
        if (!TryResourceCandidates(rm, out System.Collections.Generic.List<string>? candidates)
            || candidates is null)
            return false;
        if (!TryResolveResourceAssembly(rm, candidates, name, out System.Reflection.Assembly? asm) || asm is null)
            return false;
        foreach (string cand in candidates)
        {
            System.IO.Stream? s = null;
            try { s = asm.GetManifestResourceStream(cand); }
            catch { }
            if (s is null)
                continue;
            try
            {
                using (s)
                {
                    if (TryPeekNamedReader(s, out string _))
                        continue;
                    using (var reader = new System.Resources.ResourceReader(s))
                    {
                        reader.GetResourceData(name, out string? tn, out byte[]? data);
                        if (!string.IsNullOrEmpty(tn) && data is not null)
                        {
                            typeName = tn;
                            payload = data;
                            return true;
                        }
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private static bool TryResourceCandidates(System.Resources.ResourceManager rm,
        out System.Collections.Generic.List<string>? candidates) =>
        TryResourceCandidates(rm, null, out candidates);

    private static bool TryResourceCandidates(System.Resources.ResourceManager rm,
        System.Globalization.CultureInfo? culture,
        out System.Collections.Generic.List<string>? candidates)
    {
        candidates = null;
        string? baseName;
        try { baseName = rm.BaseName; }
        catch { return false; }
        if (string.IsNullOrEmpty(baseName)) return false;
        candidates = new System.Collections.Generic.List<string>(3);
        for (System.Globalization.CultureInfo? c = culture ?? System.Globalization.CultureInfo.CurrentUICulture;
            c != null && c != System.Globalization.CultureInfo.InvariantCulture; c = c.Parent)
            candidates.Add(baseName + "." + c.Name + ".resources");
        candidates.Add(baseName + ".resources");
        return true;
    }

    // Throw-free key enumeration across the culture chain (most specific
    // first): opens each candidate manifest stream in its owning assembly
    // and reads entry NAMES only (ResourceReader Key access never
    // materializes values). Mirrors the RuntimeResourceSet merge order
    // (specific wins) via the seen set. Any stream failure degrades to
    // fewer keys, never to an exception.
    private static System.Collections.Generic.IEnumerable<string> EnumeratePrefixedKeys(
        System.Resources.ResourceManager rm, string prefix, System.Globalization.CultureInfo? culture)
    {
        var keys = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            if (TryResourceCandidates(rm, culture, out var candidates) && candidates is not null)
            {
            foreach (string cand in candidates)
            {
                if (!TryResolveCandidateAssembly(rm, cand, out var asm) || asm is null)
                    continue;
                System.IO.Stream? s = null;
                try { s = asm.GetManifestResourceStream(cand); }
                catch { }
                if (s is null)
                    continue;
                try
                {
                    using (s)
                    using (var reader = OpenResourceReader(s))
                    {
                        if (reader is null)
                            continue;
                        var en = reader.GetEnumerator();
                        while (en.MoveNext())
                        {
                            string? name = en.Key as string;
                            if (name is null || !name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (seen.Add(name))
                                keys.Add(name);
                        }
                    }
                }
                catch { }
            }
            }
            if (keys.Count == 0)
            {
                // Raw-stream enumeration needs a resolvable reader; the
                // BCL ResourceManager resolves the header-named reader
                // version-tolerantly (frankenstein v1-header/vendor-reader
                // vendor streams enumerate fine through it), so fall back
                // to its set when raw yields nothing. Key-only walk with
                // per-entry guards: one bad value must not abort the
                // remaining applies (same shape as SafeApplyResources'
                // historical BCL-enumeration path).
                try
                {
                    var rs = rm.GetResourceSet(
                        culture ?? System.Globalization.CultureInfo.InvariantCulture,
                        true, true);
                    if (rs is not null)
                    {
                        var en = rs.GetEnumerator();
                        while (true)
                        {
                            bool moved;
                            try { moved = en.MoveNext(); }
                            catch { break; }
                            if (!moved)
                                break;
                            string? key;
                            try { key = en.Key as string; }
                            catch { continue; }
                            if (key is null || !key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (seen.Add(key))
                                keys.Add(key);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        return keys;
    }

    private static bool TryResolveCandidateAssembly(System.Resources.ResourceManager rm,
        string candidate, out System.Reflection.Assembly? asm)
    {
        asm = null;
        try
        {
            System.Reflection.Assembly own = rm.GetType().Assembly;
            if (!IsInfrastructureAssembly(own) && HasStream(own, candidate))
            {
                asm = own;
                return true;
            }
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                if (HasStream(a, candidate)) { asm = a; return true; }
            }
        }
        catch { return false; }
        return asm is not null;
    }

    private static bool HasStream(System.Reflection.Assembly asm, string candidate)
    {
        try
        {
            using var s = asm.GetManifestResourceStream(candidate);
            return s is not null;
        }
        catch { return false; }
    }

    private static bool TryResolveResourceAssembly(System.Resources.ResourceManager rm,
        System.Collections.Generic.List<string> candidates, string name,
        out System.Reflection.Assembly? asm)
    {
        asm = null;
        if (s_probeAsm.TryGetValue(rm, out System.Reflection.Assembly? cached))
        {
            asm = cached;
            return true;
        }
        try
        {
            // App-derived managers (Properties.Resources subclasses) live
            // in the owning assembly; plain ResourceManager reports the
            // CoreLib and ComponentResourceManager the fork, so
            // infrastructure assemblies are skipped and the scan decides.
            System.Reflection.Assembly own = rm.GetType().Assembly;
            if (!IsInfrastructureAssembly(own) && HasEntry(own, candidates, name))
            {
                asm = own;
            }
            else
            {
                foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.IsDynamic) continue;
                    if (HasEntry(a, candidates, name)) { asm = a; break; }
                }
            }
        }
        catch { return false; }
        Trace("TEMP resolve base=" + rm.BaseName + " asm=" + (asm is null ? "null" : asm.GetName().Name));
        if (asm == null) return false;
        try { s_probeAsm.Add(rm, asm); } catch { }
        return true;
    }

    private static bool IsInfrastructureAssembly(System.Reflection.Assembly asm)
    {
        string simple = asm.GetName().Name ?? string.Empty;
        return simple.StartsWith("System.", System.StringComparison.Ordinal)
            || simple.StartsWith("Microsoft.", System.StringComparison.Ordinal)
            || simple is "mscorlib" or "netstandard" or "WfWasm.WinForms" or "WfWasm.Drawing";
    }

    private static bool HasEntry(System.Reflection.Assembly asm,
        System.Collections.Generic.List<string> candidates, string name)
    {
        foreach (var cand in candidates)
        {
            try
            {
                using var s = asm.GetManifestResourceStream(cand);
                if (s is null) continue;
                using var reader = OpenResourceReader(s);
                if (reader is null) continue;
                if (reader is System.Resources.ResourceReader legacy)
                {
                    legacy.GetResourceData(name, out string? tn, out _);
                    if (!string.IsNullOrEmpty(tn)) return true;
                }
                else
                {
                    var en = reader.GetEnumerator();
                    while (en.MoveNext())
                    {
                        if (string.Equals(en.Key as string, name, System.StringComparison.Ordinal)) return true;
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private static bool ProbeStream(System.IO.Stream? s, string name, out bool nonImage)
    {
        nonImage = false;
        if (s is null) return false;
        using (s)
        {
            string? typeName;
            try
            {
                using var reader = OpenResourceReader(s);
                if (reader is not System.Resources.ResourceReader legacy) return false;
                legacy.GetResourceData(name, out typeName, out _);
            }
            catch { return false; }
            if (string.IsNullOrEmpty(typeName)) return false;
            nonImage = IsNonImageTypeName(typeName);
            return true;
        }
    }

    internal static bool IsNonImageTypeName(string typeName) =>
        !typeName.Contains("Bitmap", StringComparison.Ordinal)
        && !typeName.Contains("Image,", StringComparison.Ordinal)
        && !typeName.Contains("Icon", StringComparison.Ordinal)
        && !typeName.Contains("String", StringComparison.Ordinal);

    public static object? SafeGetObject(System.Resources.ResourceManager? rm, string? name, System.Globalization.CultureInfo? culture)
    {
        return SafeGetObject(rm, name);
    }

    public static void SafeApplyResources(System.ComponentModel.ComponentResourceManager? rm, object? value, string? objectName)
    {
        SafeApplyResources(rm, value, objectName, null);
    }

    public static void SafeApplyResources(System.ComponentModel.ComponentResourceManager? rm, object? value, string? objectName, System.Globalization.CultureInfo? culture)
    {
        if (rm == null || value == null || string.IsNullOrEmpty(objectName))
            return;

        // The BCL ApplyResources eagerly deserializes every user-type
        // entry on a background thread (one PNSE first-chance per image,
        // caught but still reported), so values apply exclusively through
        // the throw-free walk below. Zero enumerated keys means an exotic
        // non-stream ResourceSet: keep the BCL behavior there rather than
        // dropping applies.
        try
        {
            string prefix = objectName + ".";
            var props = System.ComponentModel.TypeDescriptor.GetProperties(value);

            // Throw-free key walk: ResourceReader names enumerate without
            // materializing values, so keys come from the raw streams and
            // each value resolves per key through SafeGetObject (NRBF
            // decode / inline passthrough, no deserializer involved).
            var keys = new System.Collections.Generic.List<string>(
                EnumeratePrefixedKeys(rm, prefix, culture));

            if (keys.Count == 0)
            {
                try { rm.ApplyResources(value, objectName, culture); }
                catch { }
                return;
            }
            foreach (string key in keys)
            {
                try
                {
                    string propName = key.Substring(prefix.Length);
                    // Identity/site plumbing is never resourced: setting
                    // them renames or rebases live controls.
                    if (propName == "Name" || propName == "Site")
                        continue;
                    var pd = props[propName];
                    if (pd == null || pd.IsReadOnly)
                        continue;

                    object? val = SafeGetObject(rm, key);
                    if (val == null)
                        continue;

                    if (pd.PropertyType.IsInstanceOfType(val))
                    {
                        try
                        {
                            pd.SetValue(value, val);
                        }
                        catch { }
                        continue;
                    }

                    object? converted = ConvertToDrawingType(val, pd.PropertyType)
                        ?? ConvertWithTypeConverter(val, pd.PropertyType);
                    if (converted != null)
                    {
                        try
                        {
                            pd.SetValue(value, converted);
                        }
                        catch { }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch { }
    }

    // BCL ApplyResources conversions for everything the fork shapes do not
    // cover (enums, primitives, Image-from-bytes): the same TypeConverter
    // machinery the BCL apply uses, per property, guarded. Null when the
    // converter is missing, unwilling, or throws.
    private static object? ConvertWithTypeConverter(object val, Type targetType)
    {
        try
        {
            var converter = System.ComponentModel.TypeDescriptor.GetConverter(targetType);
            if (converter is null || !converter.CanConvertFrom(val.GetType()))
                return null;
            return converter.ConvertFrom(val);
        }
        catch { return null; }
    }

    private static object? ConvertToDrawingType(object val, Type targetType)
    {
        var valType = val.GetType();

        if (targetType == typeof(Point))
        {
            if (val is string str)
            {
                var parts = str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int px) && int.TryParse(parts[1].Trim(), out int py))
                    return new Point(px, py);
            }
            var xProp = valType.GetProperty("X")?.GetValue(val);
            var yProp = valType.GetProperty("Y")?.GetValue(val);
            if (xProp is int x && yProp is int y)
                return new Point(x, y);
        }
        else if (targetType == typeof(PointF))
        {
            if (val is string str)
            {
                var parts = str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && float.TryParse(parts[0].Trim(), out float px) && float.TryParse(parts[1].Trim(), out float py))
                    return new PointF(px, py);
            }
            var xProp = valType.GetProperty("X")?.GetValue(val);
            var yProp = valType.GetProperty("Y")?.GetValue(val);
            if (xProp != null && yProp != null)
                return new PointF(Convert.ToSingle(xProp), Convert.ToSingle(yProp));
        }
        else if (targetType == typeof(Size))
        {
            if (val is string str)
            {
                var parts = str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int pw) && int.TryParse(parts[1].Trim(), out int ph))
                    return new Size(pw, ph);
            }
            var wProp = valType.GetProperty("Width")?.GetValue(val);
            var hProp = valType.GetProperty("Height")?.GetValue(val);
            if (wProp is int w && hProp is int h)
                return new Size(w, h);
        }
        else if (targetType == typeof(SizeF))
        {
            if (val is string str)
            {
                var parts = str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && float.TryParse(parts[0].Trim(), out float pw) && float.TryParse(parts[1].Trim(), out float ph))
                    return new SizeF(pw, ph);
            }
            var wProp = valType.GetProperty("Width")?.GetValue(val);
            var hProp = valType.GetProperty("Height")?.GetValue(val);
            if (wProp != null && hProp != null)
                return new SizeF(Convert.ToSingle(wProp), Convert.ToSingle(hProp));
        }
        else if (targetType == typeof(Rectangle))
        {
            var xProp = valType.GetProperty("X")?.GetValue(val);
            var yProp = valType.GetProperty("Y")?.GetValue(val);
            var wProp = valType.GetProperty("Width")?.GetValue(val);
            var hProp = valType.GetProperty("Height")?.GetValue(val);
            if (xProp is int x && yProp is int y && wProp is int w && hProp is int h)
                return new Rectangle(x, y, w, h);
        }
        else if (targetType == typeof(Color))
        {
            if (val is string str)
                return new ColorConverter().ConvertFrom(null, null, str);
            var aProp = valType.GetProperty("A")?.GetValue(val);
            var rProp = valType.GetProperty("R")?.GetValue(val);
            var gProp = valType.GetProperty("G")?.GetValue(val);
            var bProp = valType.GetProperty("B")?.GetValue(val);
            if (aProp != null && rProp != null && gProp != null && bProp != null)
                return Color.FromArgb(Convert.ToByte(aProp), Convert.ToByte(rProp), Convert.ToByte(gProp), Convert.ToByte(bProp));
        }
        else if (targetType == typeof(Font))
        {
            if (val is string str)
                return FontConverter.FromString(str, null);
            var pName = valType.GetProperty("Name")?.GetValue(val) as string;
            var pSize = valType.GetProperty("Size")?.GetValue(val);
            if (pName != null && pSize != null)
                return new Font(pName, Convert.ToSingle(pSize));
        }

        return null;
    }
}
