// Headless loose-file registry: desktop apps ship content next to the exe
// (PhotoAlbum's background.jpg + Photos/) that has no browser path. The
// client downloads the manifest's content files and registers their bytes
// here; image loaders consult the registry before touching the disk.
namespace WfWasm.Drawing;

public static class AppFileSystem
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> s_files =
        new(System.StringComparer.OrdinalIgnoreCase);

    // Browser-decoded pixels (JPEG has no managed decoder: the loader
    // decodes via canvas at bootstrap and registers w/h/RGBA here).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DecodedImage> s_decoded =
        new(System.StringComparer.OrdinalIgnoreCase);

    public sealed record DecodedImage(int Width, int Height, byte[] Rgba);

    public static void Register(string name, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(name) || bytes is null) return;
        string n = Normalize(name);
        s_files[n] = bytes;
        MaterializeForFrameworkReaders(n, bytes);
    }

    // Mirror registry for UNREWRITTEN framework readers: System.Data,
    // XmlReader and friends open content by string path on the real
    // FS, bypassing every VFS shim (their assemblies are the
    // runtime's own, never rewritten). Safe relative manifest names
    // are therefore also written to MEMFS root (browser cwd is /,
    // so "XMLSchema.xsd" resolves to the mirror). Decoded images
    // stay registry-only (their readers are ours). Browser-only:
    // headless Register callers (tests, tools) must never touch
    // disk; Clear removes the mirrors (per-app switch hygiene).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_mirrors =
        new(System.StringComparer.OrdinalIgnoreCase);

    private static void MaterializeForFrameworkReaders(string normalized, byte[] bytes)
    {
        if (!OperatingSystem.IsBrowser()) return;
        if (!TryMirrorPath(normalized, out string? full) || full is null) return;
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(full, bytes);
            s_mirrors[full] = 0;
        }
        catch { }
    }

    // Mirror-path policy (pure, pinned by tests): manifest nesting
    // (Photos/1.jpg) is fine; anything that could escape MEMFS root
    // (.. segments, rooted/drive spellings that survived Normalize)
    // or abuse the mirror (empties, overlong names) is rejected.
    internal static bool TryMirrorPath(string normalized, out string? full)
    {
        full = null;
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 260) return false;
        if (normalized.Contains("..", System.StringComparison.Ordinal)) return false;
        foreach (string seg in normalized.Split('/'))
        {
            if (seg.Length == 0 || seg.Length > 128) return false;
            if (seg.Contains(':') || seg == ".") return false;
            foreach (char c in seg)
                if (c < 0x20 || c == 0x7F) return false;
        }

        full = "/" + normalized;
        return true;
    }

    public static void RegisterDecoded(string name, int width, int height, byte[] rgba)
    {
        if (string.IsNullOrWhiteSpace(name) || rgba is null) return;
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4) return;
        s_decoded[Normalize(name)] = new DecodedImage(width, height, rgba);
    }

    public static bool TryGetDecoded(string? path, out DecodedImage? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string n = Normalize(path);
        if (s_decoded.TryGetValue(n, out image)) return true;
        foreach (var kv in s_decoded)
        {
            if (n.EndsWith('/' + kv.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                image = kv.Value;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetBytes(string? path, out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string n = Normalize(path);
        if (s_files.TryGetValue(n, out bytes)) return true;
        // Desktop code passes StartupPath-rooted absolutes
        // (C:/.../Photos/1.jpg): match registered tails.
        foreach (var kv in s_files)
        {
            if (n.EndsWith('/' + kv.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                bytes = kv.Value;
                return true;
            }
        }

        return false;
    }

    // Directory.GetFiles shape (the rewriter redirects BCL calls here):
    // TopDirectoryOnly prefix match with * / ? globbing on the file name.
    // Returns display paths in the caller's directory spelling so a
    // follow-up FromFile hit resolves through the same registry.
    public static string[] GetFiles(string? path) => GetFiles(path, "*", System.IO.SearchOption.TopDirectoryOnly);

    public static string[] GetFiles(string? path, string? searchPattern) =>
        GetFiles(path, searchPattern, System.IO.SearchOption.TopDirectoryOnly);

    public static string[] GetFiles(string? path, string? searchPattern, System.IO.SearchOption searchOption)
    {
        string dir = Normalize(path ?? string.Empty).TrimEnd('/');
        string pattern = string.IsNullOrEmpty(searchPattern) ? "*" : searchPattern;
        string prefix = dir.Length == 0 ? string.Empty : dir + '/';
        var hits = new System.Collections.Generic.List<string>();
        foreach (string key in s_files.Keys)
            Consider(key);
        foreach (string key in s_decoded.Keys)
            Consider(key);
        hits.Sort(System.StringComparer.OrdinalIgnoreCase);
        return hits.ToArray();

        void Consider(string key)
        {
            if (dir.Length > 0 && !key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return;
            string tail = dir.Length == 0 ? key : key[prefix.Length..];
            if (searchOption == System.IO.SearchOption.TopDirectoryOnly && tail.Contains('/'))
                return;
            string name = tail.Contains('/') ? tail[(tail.LastIndexOf('/') + 1)..] : tail;
            if (!Glob(name, pattern))
                return;
            hits.Add((path ?? string.Empty) + name);
        }
    }

    private static bool Glob(string name, string pattern)
    {
        // Classic * / ? matcher over one path segment.
        int ni = 0, pi = 0, star = -1, mark = 0;
        while (ni < name.Length)
        {
            if (pi < pattern.Length && (pattern[pi] == '?'
                || char.ToUpperInvariant(pattern[pi]) == char.ToUpperInvariant(name[ni])))
            {
                ni++;
                pi++;
            }
            else if (pi < pattern.Length && pattern[pi] == '*')
            {
                star = pi++;
                mark = ni;
            }
            else if (star >= 0)
            {
                pi = star + 1;
                ni = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (pi < pattern.Length && pattern[pi] == '*')
            pi++;
        return pi == pattern.Length;
    }

    // File.Exists shape (the rewriter redirects BCL calls here): served
    // content answers true (StartupPath-rooted absolutes match by tail),
    // including browser-decoded pixels (PNG/JPEG register decoded-only and
    // load fine through new Bitmap(path), but never touch s_files: without
    // this they read as missing, e.g. diagram BitmapNode image3.png);
    // anything else falls through to the real disk (MEMFS user cache).
    public static bool Exists(string? path) =>
        TryGetBytes(path, out _) || TryGetDecoded(path, out _) || (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path));

    // FileStream(path, FileMode) shape (rewriter redirect): VFS bytes are
    // spooled to a MEMFS temp so callers get a REAL FileStream (Seek,
    // XmlSerializer and StreamReader work unmodified). Write modes drop
    // the VFS entry first so later reads see MEMFS, not stale bytes.
    // Misses construct the real stream (reads fail, creates write MEMFS).
    public static System.IO.FileStream OpenFileStream(string path, System.IO.FileMode mode)
    {
        if (TryGetBytes(path, out byte[]? bytes) && bytes is not null)
        {
            if (mode == System.IO.FileMode.Create || mode == System.IO.FileMode.CreateNew
                || mode == System.IO.FileMode.Truncate || mode == System.IO.FileMode.Append)
                ForgetMatches(path);
            else
            {
                string temp = TempPathFor(path);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(temp)!);
                System.IO.File.WriteAllBytes(temp, bytes);
                return new System.IO.FileStream(temp, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            }
        }

        return new System.IO.FileStream(path, mode);
    }

    // File.Copy shapes (rewriter redirect): VFS sources materialize at
    // the destination (MEMFS); misses copy for real. Overwrite semantics
    // match the BCL (existing destination without overwrite throws).
    public static void CopyFile(string? source, string? dest) => CopyFile(source, dest, overwrite: false);

    public static void CopyFile(string? source, string? dest, bool overwrite)
    {
        if (TryGetBytes(source, out byte[]? bytes) && bytes is not null && !string.IsNullOrWhiteSpace(dest))
        {
            if (!overwrite && System.IO.File.Exists(dest))
                throw new System.IO.IOException($"Cannot create '{dest}' because it already exists.");
            ForgetMatches(dest);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(
                System.IO.Path.GetFullPath(dest))!);
            System.IO.File.WriteAllBytes(dest, bytes);
            return;
        }

        System.IO.File.Copy(source!, dest!, overwrite);
    }

    // File.GetCreationTime shape (rewriter redirect): served content
    // reports fetch time; misses query for real (1601 default kept).
    public static System.DateTime GetCreationTime(string? path) =>
        TryGetBytes(path, out _) ? System.DateTime.Now : System.IO.File.GetCreationTime(path ?? string.Empty);

    // File.ReadAllText shapes (rewriter redirect): served content
    // decodes from the registry (backslash spellings normalize, so a
    // desktop Concat dir + "\X.json" hits); misses read for real
    // (FileNotFound semantics preserved).
    public static string ReadAllText(string? path) =>
        ReadAllText(path, System.Text.Encoding.UTF8);

    public static string ReadAllText(string? path, System.Text.Encoding? encoding)
    {
        if (TryGetBytes(path, out byte[]? bytes) && bytes is not null)
            return (encoding ?? System.Text.Encoding.UTF8).GetString(bytes);
        return System.IO.File.ReadAllText(path!, encoding ?? System.Text.Encoding.UTF8);
    }

    // Drops every entry a path would resolve to (exact + tail matches):
    // lookups are tail-based, so exact-key removal would leave stale hits.
    private static void ForgetMatches(string path)
    {
        string n = Normalize(path);
        s_files.TryRemove(n, out _);
        foreach (string key in s_files.Keys)
        {
            if (n.EndsWith('/' + key, System.StringComparison.OrdinalIgnoreCase))
                s_files.TryRemove(key, out _);
        }
    }

    private static string TempPathFor(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Normalize(path)));
        return System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "vfs_" + System.Convert.ToHexString(hash)[..16] + ".bin");
    }

    public static void Clear()
    {
        s_files.Clear();
        s_decoded.Clear();
        if (OperatingSystem.IsBrowser())
        {
            foreach (string m in s_mirrors.Keys)
            {
                try { System.IO.File.Delete(m); } catch { }
            }
        }

        s_mirrors.Clear();
    }

    internal static string Normalize(string path)
    {
        string n = path.Replace('\\', '/');
        int colon = n.IndexOf(':');
        if (colon >= 0) n = n[(colon + 1)..];
        return n.TrimStart('/');
    }
}
