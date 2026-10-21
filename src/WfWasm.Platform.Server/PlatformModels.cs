using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WfWasm.Platform.Server;

public sealed record PlatformScope(string UserId, string AppId)
{
    internal string StorageId => Hash(UserId + "\0" + AppId);
    internal static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}

// Grants are administrator configuration, never accepted from an application request.
public sealed record FileMount(string Id, string Label, string PhysicalPath, bool IsFile = false, bool Writable = false,
    IReadOnlyDictionary<string, string>? FileList = null);
public sealed record FileMountInfo(string Id, string Label, bool Writable, bool IsFile);
public sealed record FileEntry(string Name, string Path, bool IsDirectory, long Length);
public sealed record DirectoryPage(IReadOnlyList<FileEntry> Entries, int? NextOffset);
public sealed record RegistryEntry(string Name, string Kind, JsonElement Data);
public sealed record RegistrySnapshot(IReadOnlyList<string> Keys, IReadOnlyList<RegistrySnapshotValue> Values);
public sealed record RegistrySnapshotValue(string Key, string Name, string Kind, JsonElement Data);

public sealed class PlatformOptions
{
    public required string DataDirectory { get; init; }
    // Deployment supplies the app allow-list. Nothing is exposed by default.
    public Func<string, bool> IsKnownApp { get; init; } = _ => false;
    public Func<PlatformScope, IEnumerable<FileMount>> Mounts { get; init; } = _ => [];
    // Use a trusted authenticated principal in multi-user hosting. Never use a request header/user query value.
    public Func<HttpContext, string?> AuthenticatedUser { get; init; } = context =>
        context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
    // Empty means loopback hostnames only. Public hosting must explicitly name its origin(s).
    public IReadOnlySet<string> TrustedOrigins { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool RequireAuthenticatedUsers { get; init; }
    public long MaxFileBytes { get; init; } = 16 * 1024 * 1024;
    public long MaxWorkspaceBytes { get; init; } = 128 * 1024 * 1024;
    public int MaxWorkspaceEntries { get; init; } = 4096;
    public int MaxRegistryValues { get; init; } = 4096;
    public int MaxRegistryKeys { get; init; } = 4096;
    public int MaxRegistryValueBytes { get; init; } = 64 * 1024;
    public int MaxRegistryBytes { get; init; } = 4 * 1024 * 1024;
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);
}

internal static class Names
{
    internal static string Id(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid identifier.");
        return value;
    }

    internal static string[] Segments(string path)
    {
        if (path.Length > 1024 || path.StartsWith('/') || path.StartsWith('\\')) throw new ArgumentException("Use a relative virtual path.");
        if (path.Length == 0) return [];
        var parts = path.Replace('\\', '/').Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part.Length > 180 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || c == 127 || ":%<>\"|?*".Contains(c))) throw new ArgumentException("Invalid virtual path.");
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9')
                throw new ArgumentException("Reserved device path.");
        }
        return parts;
    }
}
