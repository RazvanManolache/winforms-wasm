// Headless System.IO.IsolatedStorage for the browser. Desktop apps
// (Telerik spellchecker dictionaries, user settings, plugin caches)
// isolate per-assembly files on roaming disk; no such disk exists on
// WASM, so each store is an in-memory file map keyed by the calling
// assembly's identity. FileMode semantics are honest over the stored
// bytes (CreateNew throws on duplicates, Open throws when missing,
// Append seeks to the end, Truncate clears), so extract-then-read
// flows work exactly like first-run desktop. Contents live for the
// session; a localStorage-backed persistent tier can replace the
// static dictionaries later without changing this surface.
// API shape mirrors the real assembly for the members the catalog
// touches (GetUserStoreForAssembly/Domain, FileExists,
// CreateDirectory, Dispose, IsolatedStorageFileStream); members not
// listed there do not exist here on purpose.

using System.Collections.Concurrent;
using System.Reflection;

namespace System.IO.IsolatedStorage;

public sealed class IsolatedStorageFile : IDisposable
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte[]>> s_stores = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> s_dirs = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte[]> _files;
    private bool _disposed;

    private IsolatedStorageFile(ConcurrentDictionary<string, byte[]> files) => _files = files;

    public static IsolatedStorageFile GetUserStoreForAssembly()
    {
        string key = "user|" + (Assembly.GetCallingAssembly()?.FullName ?? "default");
        return new IsolatedStorageFile(s_stores.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)));
    }

    public static IsolatedStorageFile GetUserStoreForDomain()
    {
        string key = "domain|" + AppDomain.CurrentDomain.FriendlyName;
        return new IsolatedStorageFile(s_stores.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)));
    }

    // Legacy evidence-based entry points (control-bars window creation
    // calls GetStore(scope, Type, Type) at boot; missing overload threw
    // MissingMethodException and blanked the form). Desktop isolates by
    // assembly evidence + scope; headless keeps the same separation with
    // a session-scoped in-memory map keyed by scope, evidence types and
    // calling assembly. No disk tier exists on WASM; contents live for
    // the session like the GetUserStore* family above.
    public static IsolatedStorageFile GetStore(IsolatedStorageScope scope, Type? domainEvidenceType)
        => GetStore(scope, domainEvidenceType, null);

    public static IsolatedStorageFile GetStore(IsolatedStorageScope scope, Type? domainEvidenceType, Type? assemblyEvidenceType)
    {
        string key = "store|" + scope + "|"
            + (domainEvidenceType?.FullName ?? "null") + "|"
            + (assemblyEvidenceType?.FullName ?? "null") + "|"
            + (Assembly.GetCallingAssembly()?.FullName ?? "default");
        return new IsolatedStorageFile(s_stores.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)));
    }

    internal static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    internal ConcurrentDictionary<string, byte[]> Files
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _files;
        }
    }

    public bool FileExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Files.ContainsKey(Normalize(path));
    }

    public void CreateDirectory(string dir)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ObjectDisposedException.ThrowIf(_disposed, this);
        s_dirs[Normalize(dir).TrimEnd('/') + "/"] = 0;
    }

    public void Dispose() => _disposed = true;

    public bool DirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string norm = Normalize(path).TrimEnd('/') + "/";
        return s_dirs.ContainsKey(norm) || Files.Keys.Any(k => k.StartsWith(norm, StringComparison.OrdinalIgnoreCase));
    }

    public string[] GetFileNames() => GetFileNames("*");

    public string[] GetFileNames(string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);
        var rx = SearchRegex(Normalize(searchPattern));
        return Files.Keys
            .Where(k => k.IndexOf('/') < 0 && rx.IsMatch(k))
            .Select(k => k)
            .ToArray();
    }

    public string[] GetDirectoryNames() => GetDirectoryNames("*");

    public string[] GetDirectoryNames(string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);
        var rx = SearchRegex(Normalize(searchPattern).TrimEnd('/'));
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in s_dirs.Keys)
        {
            string name = dir.TrimEnd('/').TrimStart('/');
            if (name.IndexOf('/') < 0 && rx.IsMatch(name))
                names.Add(name);
        }
        foreach (string key in Files.Keys)
        {
            int slash = key.IndexOf('/');
            if (slash > 0)
            {
                string top = key.Substring(0, slash);
                if (rx.IsMatch(top))
                    names.Add(top);
            }
        }
        return names.ToArray();
    }

    public void DeleteFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Files.TryRemove(Normalize(path), out _))
            throw new IsolatedStorageException($"File not found: {path}");
    }

    public void DeleteDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string norm = Normalize(path).TrimEnd('/') + "/";
        s_dirs.TryRemove(norm, out _);
        foreach (string k in Files.Keys.Where(k => k.StartsWith(norm, StringComparison.OrdinalIgnoreCase)).ToArray())
            Files.TryRemove(k, out _);
    }

    private static System.Text.RegularExpressions.Regex SearchRegex(string pattern) => new System.Text.RegularExpressions.Regex(
        "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

public sealed class IsolatedStorageFileStream : Stream
{
    private readonly IsolatedStorageFile _store;
    private readonly string _path;
    private readonly MemoryStream _data;
    private readonly bool _writable;
    private bool _disposed;

    public IsolatedStorageFileStream(string path, FileMode mode, IsolatedStorageFile isf)
        : this(path, mode, FileAccess.ReadWrite, isf)
    {
    }

    // Legacy openers (control-bars layout persistence opens its state
    // file with explicit access+share). FileShare is accepted and
    // ignored: the session store is single-threaded headless, so there
    // is no concurrent opener to share with or lock out.
    public IsolatedStorageFileStream(string path, FileMode mode, FileAccess access, IsolatedStorageFile isf)
        : this(path, mode, access, FileShare.None, isf)
    {
    }

    public IsolatedStorageFileStream(string path, FileMode mode, FileAccess access, FileShare share, IsolatedStorageFile isf)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(isf);
        _store = isf;
        _path = IsolatedStorageFile.Normalize(path);
        ConcurrentDictionary<string, byte[]> files = isf.Files;
        bool exists = files.TryGetValue(_path, out byte[]? current);
        bool writable = access != FileAccess.Read;
        switch (mode)
        {
            case FileMode.CreateNew:
                if (exists)
                    throw new IOException($"File already exists: {path}");
                _data = new MemoryStream();
                _writable = writable;
                break;
            case FileMode.Create:
                _data = new MemoryStream();
                _writable = writable;
                break;
            case FileMode.Open:
                if (!exists)
                    throw new FileNotFoundException($"File not found: {path}");
                _data = CopyOf(current!);
                _writable = writable;
                break;
            case FileMode.OpenOrCreate:
                _data = exists ? CopyOf(current!) : new MemoryStream();
                _writable = writable;
                break;
            case FileMode.Truncate:
                if (!exists)
                    throw new FileNotFoundException($"File not found: {path}");
                _data = new MemoryStream();
                _writable = writable;
                break;
            case FileMode.Append:
                _data = exists ? CopyOf(current!) : new MemoryStream();
                _data.Seek(0, SeekOrigin.End);
                _writable = writable;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static MemoryStream CopyOf(byte[] bytes)
    {
        var ms = new MemoryStream(bytes.Length);
        ms.Write(bytes, 0, bytes.Length);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => !_disposed && _writable;
    public override long Length => _data.Length;

    public override long Position
    {
        get => _data.Position;
        set => _data.Position = value;
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writable)
            _store.Files[_path] = _data.ToArray();
    }

    public override int Read(byte[] buffer, int offset, int count) => _data.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => _data.Seek(offset, origin);

    public override void SetLength(long value) => _data.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_writable)
            throw new NotSupportedException("Stream is read-only.");
        _data.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            if (_writable)
                _store.Files[_path] = _data.ToArray();
            _data.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
