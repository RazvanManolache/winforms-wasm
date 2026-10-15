// Safe resource accessor that catches BinaryFormatter removal exceptions in .NET 9+
namespace WfWasm.Drawing;

using System.Drawing;

public static class ResourceHelper
{
    // Decoded resource images, per manager: ResourceManager caches
    // successful GetObject calls but never failures, and every Bitmap/
    // Icon entry fails on .NET 9+ (BinaryFormatter removed), so without
    // this each lookup would re-throw PNSE and re-parse NRBF. Misses
    // store the sentinel (the bytes are static: a miss never heals),
    // which also skips the doomed GetObject re-throw; placeholders
    // stay uncached (fresh instance per call, as today).
    private static readonly object s_imageMiss = new();
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
            if (ReferenceEquals(v, s_imageMiss))
            {
                knownMiss = true;
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
            d[name] = image ?? s_imageMiss;
        }
    }

    public static object? SafeGetObject(System.Resources.ResourceManager? rm, string? name)
    {
        if (rm == null || name == null) return null;
        if (TryGetCachedImage(rm, name, out object? cached, out bool knownMiss))
            return cached;
        if (!knownMiss)
        {
            try
            {
                var obj = rm.GetObject(name);
                if (obj != null)
                {
                    // Pass BCL successes through verbatim (byte[] SVG
                    // payloads, streams, strings, images): the value is
                    // exactly what desktop returns for this entry.
                    // (Null-for-proven-non-image here starved Telerik
                    // RadSvgImage.FromByteArray and killed BugTracker
                    // construction; misses still answer below.)
                    return obj;
                }
            }
            catch { }
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

    // ResourceManager hides which assembly it reads (.NET Core dropped
    // MainAssembly entirely), so the probe resolves the resource
    // holder by scanning loaded assemblies for the manifest stream.
    // Base names are namespace-qualified: the first hit is the
    // manager's own assembly. Cached per manager; any failure
    // degrades to false, i.e. today's placeholder for every path.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        System.Resources.ResourceManager, System.Reflection.Assembly> s_probeAsm = new();

    private static bool IsProvenNonImageEntry(System.Resources.ResourceManager rm, string name)
    {
        if (!TryResourceCandidates(rm, out System.Collections.Generic.List<string>? candidates)
            || candidates is null)
            return false;
        if (!TryResolveResourceAssembly(rm, candidates, out System.Reflection.Assembly? asm) || asm is null)
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
        if (!TryResolveResourceAssembly(rm, candidates, out System.Reflection.Assembly? asm) || asm is null)
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
            catch { }
        }
        return false;
    }

    private static bool TryResourceCandidates(System.Resources.ResourceManager rm,
        out System.Collections.Generic.List<string>? candidates)
    {
        candidates = null;
        string? baseName;
        try { baseName = rm.BaseName; }
        catch { return false; }
        if (string.IsNullOrEmpty(baseName)) return false;
        candidates = new System.Collections.Generic.List<string>(3);
        for (System.Globalization.CultureInfo? c = System.Globalization.CultureInfo.CurrentUICulture;
            c != null && c != System.Globalization.CultureInfo.InvariantCulture; c = c.Parent)
            candidates.Add(baseName + "." + c.Name + ".resources");
        candidates.Add(baseName + ".resources");
        return true;
    }

    private static bool TryResolveResourceAssembly(System.Resources.ResourceManager rm,
        System.Collections.Generic.List<string> candidates, out System.Reflection.Assembly? asm)
    {
        asm = null;
        if (s_probeAsm.TryGetValue(rm, out System.Reflection.Assembly? cached))
        {
            asm = cached;
            return true;
        }
        try
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                foreach (var cand in candidates)
                {
                    bool hit = false;
                    try { using var t = a.GetManifestResourceStream(cand); hit = t != null; }
                    catch { }
                    if (hit) { asm = a; break; }
                }
                if (asm != null) break;
            }
        }
        catch { return false; }
        if (asm == null) return false;
        try { s_probeAsm.Add(rm, asm); } catch { }
        return true;
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
                using var reader = new System.Resources.ResourceReader(s);
                reader.GetResourceData(name, out typeName, out _);
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

        try
        {
            rm.ApplyResources(value, objectName, culture);
        }
        catch { }

        try
        {
            var rs = rm.GetResourceSet(culture ?? System.Globalization.CultureInfo.InvariantCulture, true, true);
            if (rs == null) return;

            string prefix = objectName + ".";
            var props = System.ComponentModel.TypeDescriptor.GetProperties(value);

            // Explicit IDictionaryEnumerator: foreach resolves Current
            // (key AND value) eagerly, so one BinaryFormatter image entry
            // throws out of MoveNext/Current and aborts every remaining
            // apply. Per-member access keeps failures per entry.
            var enumerator = rs.GetEnumerator();
            while (true)
            {
                bool moved;
                try
                {
                    moved = enumerator.MoveNext();
                }
                catch
                {
                    break;
                }
                if (!moved)
                    break;
                string? key;
                try
                {
                    key = enumerator.Key as string;
                }
                catch
                {
                    continue;
                }
                if (key is null || !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    string propName = key.Substring(prefix.Length);
                    var pd = props[propName];
                    if (pd == null || pd.IsReadOnly)
                        continue;

                    object? val;
                    try
                    {
                        val = enumerator.Value;
                    }
                    catch
                    {
                        // Image entries throw here (BinaryFormatter
                        // removed); recover through the NRBF path per key.
                        val = SafeGetObject(rm, key);
                    }
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

                    object? converted = ConvertToDrawingType(val, pd.PropertyType);
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
