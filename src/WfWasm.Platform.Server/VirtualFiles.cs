namespace WfWasm.Platform.Server;

public sealed class VirtualFiles(PlatformOptions options)
{
    private readonly object _writeLock = new();

    public IReadOnlyList<FileMountInfo> GetMounts(PlatformScope scope) => ResolveMounts(scope)
        .Select(m => new FileMountInfo(m.Id, m.Label, m.Writable, m.IsFile)).ToArray();

    private FileMount[] ResolveMounts(PlatformScope scope)
    {
        string workspace = Path.Combine(options.DataDirectory, "workspaces", scope.StorageId);
        Directory.CreateDirectory(workspace);
        var result = new[] { new FileMount("workspace", "My workspace", workspace, Writable: true) }.Concat(options.Mounts(scope)).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mount in result)
        {
            if (!ids.Add(Names.Id(mount.Id))) throw new InvalidOperationException("Duplicate mount identifier.");
            if (mount.FileList is not null && (mount.Writable || mount.FileList.Count > 4096)) throw new InvalidOperationException("File lists must be bounded and read-only.");
        }
        return result;
    }

    private (FileMount Mount, string Path) Resolve(PlatformScope scope, string mountId, string relative, bool allowMissing = false)
    {
        var mount = ResolveMounts(scope).SingleOrDefault(m => m.Id == mountId) ?? throw new UnauthorizedAccessException("Mount not granted.");
        var parts = Names.Segments(relative);
        if (mount.FileList is not null)
        {
            if (!mount.FileList.TryGetValue(string.Join('/', parts), out var granted)) throw new UnauthorizedAccessException("File not granted.");
            string file = Path.GetFullPath(granted); CheckAncestors(file); return (mount, file);
        }
        string root = Path.GetFullPath(mount.PhysicalPath);
        CheckAncestors(root);
        if (mount.IsFile)
        {
            if (parts.Length != 1 || !string.Equals(parts[0], Path.GetFileName(root), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("File not granted.");
            return (mount, root);
        }
        string current = root;
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            CheckNode(current, allowMissing);
        }
        return (mount, current);
    }

    // Reject junctions/symlinks in the grant root as well as every descendant.
    // Mounts assume administrators do not concurrently swap host directory entries.
    private static void CheckAncestors(string full)
    {
        for (string? p = full; p is not null; p = Path.GetDirectoryName(p)) CheckNode(p, false);
    }

    private static void CheckNode(string path, bool allowMissing)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Linked paths are not exposed.");
        }
        catch (FileNotFoundException) when (allowMissing) { }
        catch (DirectoryNotFoundException) when (allowMissing) { }
    }

    public DirectoryPage List(PlatformScope scope, string mountId, string path = "", int offset = 0)
    {
        if (offset < 0 || offset > 100_000) throw new ArgumentException("Invalid offset.");
        var mount = ResolveMounts(scope).SingleOrDefault(m => m.Id == mountId) ?? throw new UnauthorizedAccessException("Mount not granted.");
        if (mount.FileList is not null)
        {
            var prefix = string.Join('/', Names.Segments(path)); if (prefix.Length > 0) prefix += "/";
            var mappedEntries = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
            foreach (var item in mount.FileList)
            {
                string name = string.Join('/', Names.Segments(item.Key));
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string tail = name[prefix.Length..]; if (tail.Length == 0) continue;
                int slash = tail.IndexOf('/'); string leaf = slash < 0 ? tail : tail[..slash];
                if (slash >= 0) mappedEntries[leaf] = new(leaf, prefix + leaf, true, 0);
                else { var (_, full) = Resolve(scope, mountId, name); mappedEntries.TryAdd(leaf, new(leaf, name, false, new FileInfo(full).Length)); }
            }
            if (path.Length > 0 && mappedEntries.Count == 0) throw new DirectoryNotFoundException();
            var sorted = mappedEntries.Values.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(sorted.Skip(offset).Take(256).ToArray(), offset + 256 < sorted.Length ? offset + 256 : null);
        }
        if (mount.IsFile && path.Length == 0)
        {
            var (_, full) = Resolve(scope, mountId, Path.GetFileName(mount.PhysicalPath));
            return new([new(Path.GetFileName(full), Path.GetFileName(full), false, new FileInfo(full).Length)], null);
        }
        var (_, directory) = Resolve(scope, mountId, path);
        // Bound enumeration before sorting; callers can page through a bounded directory.
        var children = Directory.EnumerateFileSystemEntries(directory).Take(10_001).ToArray();
        if (children.Length > 10_000) throw new IOException("Directory has too many entries.");
        var entries = children.Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .Select(p => new FileEntry(Path.GetFileName(p), string.Join('/', Names.Segments(path).Append(Path.GetFileName(p))),
                Directory.Exists(p), Directory.Exists(p) ? 0 : new FileInfo(p).Length))
            .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(entries.Skip(offset).Take(256).ToArray(), offset + 256 < entries.Length ? offset + 256 : null);
    }

    public byte[] Read(PlatformScope scope, string mountId, string path)
    {
        var (_, full) = Resolve(scope, mountId, path);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > options.MaxFileBytes) throw new IOException("File exceeds the transfer limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Write(PlatformScope scope, string mountId, string path, byte[] data)
    {
        if (data.LongLength > options.MaxFileBytes) throw new IOException("File exceeds the transfer limit.");
        if (path.Length == 0) throw new ArgumentException("File name required.");
        lock (_writeLock)
        {
            var (mount, full) = Resolve(scope, mountId, path, true);
            if (!mount.Writable) throw new UnauthorizedAccessException("Mount is read-only.");
            if (mountId == "workspace")
            {
                var root = Resolve(scope, mountId, "").Path;
                var (size, count) = MeasureTree(root);
                long previous = File.Exists(full) ? new FileInfo(full).Length : 0;
                if (size - previous + data.LongLength > options.MaxWorkspaceBytes) throw new IOException("Workspace quota exceeded.");
                int added = MissingEntries(root, path);
                if (count + added > options.MaxWorkspaceEntries) throw new IOException("Workspace entry quota exceeded.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temp = Path.Combine(Path.GetDirectoryName(full)!, ".wf-" + Guid.NewGuid().ToString("N"));
            try { File.WriteAllBytes(temp, data); File.Move(temp, full, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    private static (long Bytes, int Count) MeasureTree(string root)
    {
        long size = 0; int count = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            CheckNode(entry, false);
            count++;
            if (Directory.Exists(entry)) { var child = MeasureTree(entry); size = checked(size + child.Bytes); count = checked(count + child.Count); }
            else size = checked(size + new FileInfo(entry).Length);
        }
        return (size, count);
    }

    private static int MissingEntries(string root, string path)
    {
        int count = 0; string current = root;
        foreach (string segment in Names.Segments(path)) { current = Path.Combine(current, segment); if (!File.Exists(current) && !Directory.Exists(current)) count++; }
        return count;
    }

    public void CreateDirectory(PlatformScope scope, string mountId, string path)
    {
        lock (_writeLock)
        {
            var (mount, full) = Resolve(scope, mountId, path, true);
            if (mount.IsFile || !mount.Writable) throw new UnauthorizedAccessException("Mount does not allow folders.");
            if (mountId == "workspace")
            {
                string root = Resolve(scope, mountId, "").Path;
                if (MeasureTree(root).Count + MissingEntries(root, path) > options.MaxWorkspaceEntries) throw new IOException("Workspace entry quota exceeded.");
            }
            Directory.CreateDirectory(full);
        }
    }

    public void Delete(PlatformScope scope, string mountId, string path)
    {
        if (path.Length == 0) throw new UnauthorizedAccessException("Cannot delete a mount.");
        lock (_writeLock)
        {
            var (mount, full) = Resolve(scope, mountId, path);
            if (!mount.Writable) throw new UnauthorizedAccessException("Mount is read-only.");
            if (Directory.Exists(full)) Directory.Delete(full, false); else File.Delete(full);
        }
    }
}
