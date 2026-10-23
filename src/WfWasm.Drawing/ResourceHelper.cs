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
            if (!IsLegacyReaderName(namedReader))
                return null; // legacy construction provably throws NSE on a
                              // non-legacy stamp (name validation), so skip it
                              // and its first-chance; the raw walk below
                              // recovers entries without any reader.
            // Named-but-legacy yet unresolvable/unconstructible: fall
            // through to the legacy attempt below (clean v1 content
            // under a renamed header still reads; frankenstein content
            // throws NSE there, caught into null). Returning null here
            // is what zeroed every enumeration on runtimes whose
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

    // Permanent attribution markers for resource hydration (same standing
    // as the fork's [ImageStream] lines): which image entries recover and
    // which miss, with the owning BaseName/key/type. First 8 + every
    // 128th; cache hits stay silent, so a key logs once per generation.
    private static int s_resMarkCount;

    private static void NoteResMark(string msg)
    {
        int n = System.Threading.Interlocked.Increment(ref s_resMarkCount);
        if (n <= 8 || n % 128 == 0)
            System.Console.WriteLine(msg);
    }

    private static string SafeBaseName(System.Resources.ResourceManager? rm)
    {
        try { return rm?.BaseName ?? "?"; }
        catch { return "?"; }
    }

    private static string ResSize(object? o) => o switch
    {
        Bitmap bmp => bmp.Width + "x" + bmp.Height,
        Icon ic => ic.Width + "x" + ic.Height,
        _ => "?",
    };

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
                        NoteResMark($"[RESIMG] ok base={SafeBaseName(rm)} key={name} {ResSize(decoded)}");
                        return decoded;
                    }
                    CacheImage(rm, name, null);
                    NoteResMark($"[RESMISS] base={SafeBaseName(rm)} key={name} type={typeName}");
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
                    // BinaryFormatter-serialized drawing value types
                    // (Point/Size designer bounds): strict NRBF member
                    // decode, no deserializer. A hit caches and returns
                    // the real value; anything else keeps today's miss
                    // (generic GetObject could only throw PNSE here).
                    if (TryDecodeValueType(typeName, payload, out object? decoded)
                        && decoded is not null)
                    {
                        CacheImage(rm, name, decoded);
                        return decoded;
                    }
                    // Resolved non-image user type (ImageListStreamer
                    // blobs, cursors, ...): GetObject could only throw
                    // PNSE for these, so record the miss directly.
                    CacheImage(rm, name, null);
                    NoteResMark($"[RESMISS] base={SafeBaseName(rm)} key={name} type={typeName} non-image");
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
        {
            NoteResMark($"[RESMISS] base={SafeBaseName(rm)} key={name} type=non-image");
            return null;
        }

        NoteResMark($"[RESMISS] base={SafeBaseName(rm)} key={name} type=?");
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
                && (NrbfImage.TryExtractImageBytes(payload, out byte[] icoBytes)
                    || NrbfImage.TryExtractPrefixedImageBytes(payload, out icoBytes)))
            {
                using var ms = new System.IO.MemoryStream(icoBytes, false);
                image = new Icon(ms);
                return true;
            }
            if ((typeName.Contains("Bitmap", System.StringComparison.Ordinal)
                    || typeName.Contains("Image,", System.StringComparison.Ordinal))
                && (NrbfImage.TryExtractImageBytes(payload, out byte[] imgBytes)
                    || NrbfImage.TryExtractPrefixedImageBytes(payload, out imgBytes)))
            {
                using var ms = new System.IO.MemoryStream(imgBytes, false);
                image = new Bitmap(ms);
                return true;
            }
        }
        catch { }
        return false;
    }

    // Designer drawing value types (Point/Size/Rectangle bounds) without
    // a deserializer, two strict shapes: (1) the SDK converter-string
    // form observed live (0x03 marker + length-prefixed "x, y" text with
    // the assembly-qualified user type name — what modern resgen writes
    // instead of BinaryFormatter); (2) the legacy full BinaryFormatter
    // serialization for a two/four-Int32 struct (netfx-built blobs).
    // Anything unexpected answers false and the caller keeps today's
    // miss. Never throws; every read is bounds-checked.
    private static bool TryDecodeValueType(string typeName, byte[] payload, out object? value)
    {
        value = null;
        try
        {
            int want = 0; // 1 = Point, 2 = Size, 3 = Rectangle
            if (typeName.StartsWith("System.Drawing.Point,", System.StringComparison.Ordinal))
                want = 1;
            else if (typeName.StartsWith("System.Drawing.Size,", System.StringComparison.Ordinal))
                want = 2;
            else if (typeName.StartsWith("System.Drawing.Rectangle,", System.StringComparison.Ordinal))
                want = 3;
            if (want == 0)
                return false;
            if (TryDecodeConverterString(want, payload, out value) && value is not null)
                return true;
            if (TryDecodeNrbfStruct(want, payload, out value) && value is not null)
                return true;
            value = null;
            return false;
        }
        catch { value = null; return false; }
    }

    // Shape 2: strict NRBF member decode of the full serialization for a
    // two/four-Int32 struct (header, System.Drawing library, class record
    // with Primitive/Int32 members, the values, MessageEnd at exact end).
    // Member names ride along unchecked (positional ints are unambiguous
    // once the class matches); anything unexpected answers false.
    private static bool TryDecodeNrbfStruct(int want, byte[] payload, out object? value)
    {
        value = null;
        try
        {
            string wantClass = want == 1 ? "System.Drawing.Point"
                : want == 2 ? "System.Drawing.Size" : "System.Drawing.Rectangle";
            int wantCount = want == 3 ? 4 : 2;
            int pos = 0;
            if (!NrbfExpectByte(payload, ref pos, 0x00)) return false;
            if (!NrbfExpectInt(payload, ref pos, 1)) return false;
            if (!NrbfExpectInt(payload, ref pos, -1)) return false;
            if (!NrbfExpectInt(payload, ref pos, 1)) return false;
            if (!NrbfExpectInt(payload, ref pos, 0)) return false;
            if (!NrbfExpectByte(payload, ref pos, 0x0C)) return false;
            if (!NrbfSkipInt(payload, ref pos)) return false;
            if (!NrbfSkipLengthPrefixedString(payload, ref pos)) return false;
            if (!NrbfExpectByte(payload, ref pos, 0x05)) return false;
            if (!NrbfSkipInt(payload, ref pos)) return false;
            if (!NrbfReadLengthPrefixedString(payload, ref pos, out string? className)
                || className is null)
                return false;
            if (!string.Equals(className, wantClass, System.StringComparison.Ordinal))
                return false;
            if (!NrbfExpectInt(payload, ref pos, wantCount)) return false;
            for (int i = 0; i < wantCount; i++)
                if (!NrbfSkipLengthPrefixedString(payload, ref pos)) return false;
            for (int i = 0; i < wantCount; i++)
                if (!NrbfExpectByte(payload, ref pos, 0x00)) return false;
            for (int i = 0; i < wantCount; i++)
                if (!NrbfExpectByte(payload, ref pos, 0x08)) return false;
            if (!NrbfSkipInt(payload, ref pos)) return false;
            int[] nums = new int[wantCount];
            for (int i = 0; i < wantCount; i++)
                if (!NrbfReadInt(payload, ref pos, out nums[i])) return false;
            if (!NrbfExpectByte(payload, ref pos, 0x0B)) return false;
            if (pos != payload.Length) return false;
            value = want == 1 ? new Point(nums[0], nums[1])
                : want == 2 ? (object)new Size(nums[0], nums[1])
                : new Rectangle(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }
        catch { return false; }
    }

    // Shape 1: 0x03 + length-prefixed invariant "x, y" / "w, h" /
    // "x, y, w, h" text (desktop runs it through the TypeConverter;
    // manual invariant parse here, no converter machinery involved).
    private static bool TryDecodeConverterString(int want, byte[] payload, out object? value)
    {
        value = null;
        try
        {
            int pos = 0;
            if (!NrbfExpectByte(payload, ref pos, 0x03)) return false;
            if (!NrbfReadLengthPrefixedString(payload, ref pos, out string? text) || text is null)
                return false;
            if (pos != payload.Length) return false;
            string[] parts = text.Split(',');
            int need = want == 3 ? 4 : 2;
            if (parts.Length != need) return false;
            int[] nums = new int[need];
            for (int i = 0; i < need; i++)
            {
                if (!int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out nums[i]))
                    return false;
            }
            value = want == 1 ? new Point(nums[0], nums[1])
                : want == 2 ? (object)new Size(nums[0], nums[1])
                : new Rectangle(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }
        catch { value = null; return false; }
    }

    private static bool NrbfExpectByte(byte[] buf, ref int pos, byte want)
    {
        if (pos < 0 || pos >= buf.Length || buf[pos] != want)
            return false;
        pos++;
        return true;
    }

    private static bool NrbfReadInt(byte[] buf, ref int pos, out int v)
    {
        v = 0;
        if (pos < 0 || pos + 4 > buf.Length)
            return false;
        v = buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24);
        pos += 4;
        return true;
    }

    private static bool NrbfExpectInt(byte[] buf, ref int pos, int want) =>
        NrbfReadInt(buf, ref pos, out int v) && v == want;

    private static bool NrbfSkipInt(byte[] buf, ref int pos)
    {
        if (pos < 0 || pos + 4 > buf.Length)
            return false;
        pos += 4;
        return true;
    }

    private static bool NrbfReadLengthPrefixedString(byte[] buf, ref int pos, out string? s)
    {
        s = null;
        int len = 0, shift = 0;
        for (int i = 0; i < 5; i++)
        {
            if (pos < 0 || pos >= buf.Length)
                return false;
            byte b = buf[pos++];
            len |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
            if (i == 4)
                return false;
        }
        if (len < 0 || pos + len > buf.Length)
            return false;
        try { s = System.Text.Encoding.UTF8.GetString(buf, pos, len); }
        catch { return false; }
        pos += len;
        return true;
    }

    private static bool NrbfSkipLengthPrefixedString(byte[] buf, ref int pos) =>
        NrbfReadLengthPrefixedString(buf, ref pos, out _);

    // Raw .resources entry read without any ResourceReader: the legacy
    // reader throws NSE on modern (DeserializingResourceReader-stamped)
    // vendor streams, and the named reader is sealed with no raw-data API
    // and cannot deserialize user-type payloads without BinaryFormatter
    // (removed on .NET 9+). This walks the data section directly — magic,
    // version, skip block holding the reader/set names, counts, type
    // table, 8-alignment, hashes, name positions, data offset, names,
    // data (verified byte-for-byte against real ResourceWriter output) —
    // and verifies the found name, so any misalignment degrades to a miss
    // instead of corrupt data. Never throws and never trips first-chance:
    // every read is bounds-checked against the stream length. Payload
    // bounds for unprefixed user-type blobs come from the next entry's
    // data offset (or the stream end), matching what the BCL reader hands
    // the NRBF decoder.
    private static bool TryReadRawEntry(System.IO.Stream s, string name, out string? typeName, out byte[]? payload)
    {
        typeName = null;
        payload = null;
        try
        {
            if (s is null || string.IsNullOrEmpty(name) || !s.CanSeek)
                return false;
            long len = s.Length;
            if (len < 16 || len > int.MaxValue)
                return false;
            s.Position = 0;
            using var br = new System.IO.BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
            if (!RawReadInt(br, len, out int magic) || magic != unchecked((int)0xBEEFCACE))
                return false;
            if (!RawReadInt(br, len, out _)) // version (1 on modern writer output, tolerated either way)
                return false;
            if (!RawReadInt(br, len, out int skip) || skip < 0)
                return false;
            // skip counts the name-block bytes starting AFTER itself
            // (magic+version+skip = 12), verified against real writer
            // output: names end exactly at 12+skip.
            long cursor = 12L + skip;
            if (cursor < 0 || cursor + 12 > len)
                return false;
            s.Position = cursor;
            if (!RawReadInt(br, len, out _)) // header field preceding the counts (2 on real streams)
                return false;
            if (!RawReadInt(br, len, out int numResources) || numResources < 0 || numResources > 100000)
                return false;
            if (!RawReadInt(br, len, out int numTypes) || numTypes < 0 || numTypes > 10000)
                return false;
            string[] types = new string[numTypes];
            for (int i = 0; i < numTypes; i++)
            {
                if (!RawReadString(br, len, out string? tn) || tn is null)
                    return false;
                types[i] = tn;
            }
            long aligned = s.Position + ((8 - (s.Position % 8)) % 8);
            if (aligned > len)
                return false;
            s.Position = aligned;
            // Hash array: linear name scan below instead, just skip it.
            long hashes = (long)numResources * 4;
            if (s.Position + hashes > len)
                return false;
            s.Position += hashes;
            int[] positions = new int[numResources];
            for (int i = 0; i < numResources; i++)
                if (!RawReadInt(br, len, out positions[i]))
                    return false;
            if (!RawReadInt(br, len, out int dataStart) || dataStart < 0 || dataStart > len)
                return false;
            long namesStart = s.Position;
            var rels = new System.Collections.Generic.List<int>(numResources);
            bool found = false;
            int foundRel = 0;
            for (int i = 0; i < numResources; i++)
            {
                if (positions[i] < 0)
                    continue;
                long np = namesStart + positions[i];
                if (np < 0 || np >= len)
                    continue;
                s.Position = np;
                if (!RawReadUtf16Name(br, len, out string? entry) || entry is null)
                    continue;
                if (!RawReadInt(br, len, out int dataRel) || dataRel < 0 || (long)dataStart + dataRel > len)
                    continue;
                rels.Add(dataRel);
                if (!found && string.Equals(entry, name, System.StringComparison.Ordinal))
                {
                    found = true;
                    foundRel = dataRel;
                }
            }
            if (!found)
                return false;
            int bound = (int)(len - dataStart);
            foreach (int r in rels)
                if (r > foundRel && r < bound)
                    bound = r;
            long rec = (long)dataStart + foundRel;
            if (rec < 0 || rec >= len)
                return false;
            s.Position = rec;
            if (!RawReadByte(br, len, out int code))
                return false;
            return RawReadValue(br, len, s, dataStart, bound - foundRel, code, types, out typeName, out payload);
        }
        catch
        {
            typeName = null;
            payload = null;
            return false;
        }
    }

    private static bool RawReadByte(System.IO.BinaryReader br, long len, out int value)
    {
        value = 0;
        if (br.BaseStream.Position + 1 > len)
            return false;
        value = br.ReadByte();
        return true;
    }

    private static bool RawReadInt(System.IO.BinaryReader br, long len, out int value)
    {
        value = 0;
        if (br.BaseStream.Position + 4 > len)
            return false;
        value = br.ReadInt32();
        return true;
    }

    private static bool RawReadBytes(System.IO.BinaryReader br, long len, int count, out byte[] value)
    {
        value = System.Array.Empty<byte>();
        if (count < 0 || br.BaseStream.Position + count > len)
            return false;
        value = br.ReadBytes(count);
        return value.Length == count;
    }

    // 7-bit length prefix + UTF-8 bytes (BinaryWriter string encoding).
    private static bool RawReadString(System.IO.BinaryReader br, long len, out string? value)
    {
        value = null;
        int n = 0;
        int shift = 0;
        for (int i = 0; i < 5; i++)
        {
            if (!RawReadByte(br, len, out int b))
                return false;
            n |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                if (n < 0 || br.BaseStream.Position + n > len)
                    return false;
                value = System.Text.Encoding.UTF8.GetString(br.ReadBytes(n), 0, n);
                return true;
            }
            shift += 7;
        }
        return false;
    }

    // Resource name encoding: 7-bit BYTE length + UTF-16 bytes.
    private static bool RawReadUtf16Name(System.IO.BinaryReader br, long len, out string? value)
    {
        value = null;
        int n = 0;
        int shift = 0;
        for (int i = 0; i < 5; i++)
        {
            if (!RawReadByte(br, len, out int b))
                return false;
            n |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                if (n < 0 || (n % 2) != 0 || br.BaseStream.Position + n > len)
                    return false;
                byte[] bytes = br.ReadBytes(n);
                if (bytes.Length != n)
                    return false;
                value = System.Text.Encoding.Unicode.GetString(bytes, 0, n);
                return true;
            }
            shift += 7;
        }
        return false;
    }

    private static bool RawReadValue(System.IO.BinaryReader br, long len, System.IO.Stream s, int dataStart, int available, int code, string[] types, out string? typeName, out byte[]? payload)
    {
        typeName = null;
        payload = null;
        // Fixed-size primitives: ResourceTypeCode order and sizes.
        if (code >= 2 && code <= 16)
        {
            int[] sizes = new int[] { 1, 2, 1, 1, 2, 2, 4, 4, 8, 8, 4, 8, 16, 8, 8 };
            string[] names = new string[] { "Boolean", "Char", "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal", "DateTime", "TimeSpan" };
            int size = sizes[code - 2];
            if (!RawReadBytes(br, len, size, out byte[] raw))
                return false;
            typeName = "ResourceTypeCode." + names[code - 2];
            payload = raw;
            return true;
        }
        switch (code)
        {
            case 0:
                typeName = "ResourceTypeCode.Null";
                payload = System.Array.Empty<byte>();
                return true;
            case 1:
                if (!RawReadString(br, len, out string? text) || text is null)
                    return false;
                typeName = "ResourceTypeCode.String";
                payload = System.Text.Encoding.UTF8.GetBytes(text);
                return true;
            case 32:
            case 33:
                if (!RawReadInt(br, len, out int count) || count < 0 || br.BaseStream.Position + count > len)
                    return false;
                if (!RawReadBytes(br, len, count, out byte[] bytes))
                    return false;
                typeName = code == 32 ? "ResourceTypeCode.ByteArray" : "ResourceTypeCode.Stream";
                payload = bytes;
                return true;
            default:
                if (code < 64 || code - 64 >= types.Length)
                    return false;
                typeName = types[code - 64];
                // available is already relative (bound - foundRel); exactly
                // one code byte was consumed above. (An earlier revision
                // re-added dataStart here, truncating every record whose
                // data offset is nonzero — invisible on offset-0 fixtures.)
                long rest = (long)available - 1;
                if (rest < 0 || s.Position + rest > len)
                    return false;
                if (!RawReadBytes(br, len, (int)rest, out byte[] blob))
                    return false;
                payload = blob;
                return true;
        }
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
                    // Legacy reader first: identical bytes to before on
                    // legacy streams (the whole suite pins this).
                    using (var reader = OpenResourceReader(s))
                    {
                        if (reader is System.Resources.ResourceReader raw)
                        {
                            raw.GetResourceData(name, out string? tn, out byte[]? data);
                            if (!string.IsNullOrEmpty(tn) && data is not null)
                            {
                                typeName = tn;
                                payload = data;
                                return true;
                            }
                        }
                    }
                    // Streams no reader can open (DeserializingResourceReader
                    // is sealed with no raw-data API and cannot deserialize
                    // user types without BinaryFormatter): validated raw
                    // walk of the data section, no deserializer involved.
                    if (TryReadRawEntry(s, name, out string? wtn, out byte[]? wdata)
                        && !string.IsNullOrEmpty(wtn) && wdata is not null)
                    {
                        typeName = wtn;
                        payload = wdata;
                        return true;
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
                if (TryReadRawEntry(s, name, out _, out _)) return true;
                try { s.Position = 0; }
                catch { continue; }
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
            // Reader-free probe first: works on streams no reader can
            // open (modern stamps), with identical answers on legacy
            // streams (the suite pins both).
            if (TryReadRawEntry(s, name, out string? wtn, out _) && !string.IsNullOrEmpty(wtn))
                typeName = wtn;
            else try
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
