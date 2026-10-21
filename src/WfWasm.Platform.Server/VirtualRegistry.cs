using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WfWasm.Platform.Server;

// No Windows Registry APIs are called. Hive names are namespaces inside SQLite.
public sealed class VirtualRegistry
{
    private readonly PlatformOptions _options;
    private readonly string _connection;
    private readonly object _gate = new();
    public VirtualRegistry(PlatformOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.DataDirectory);
        _connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(options.DataDirectory, "registry.sqlite"), Pooling = false }.ToString();
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS registry_keys(scope TEXT NOT NULL, key TEXT NOT NULL, PRIMARY KEY(scope,key)); CREATE TABLE IF NOT EXISTS registry_values(scope TEXT NOT NULL,key TEXT NOT NULL,name TEXT NOT NULL,display_name TEXT NOT NULL,kind TEXT NOT NULL,data TEXT NOT NULL,PRIMARY KEY(scope,key,name));");
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connection); c.Open(); return c; }
    private static int Execute(SqliteConnection db, string sql, params (string, object)[] values)
    {
        using var cmd = Command(db, sql, values); return cmd.ExecuteNonQuery();
    }
    private static SqliteCommand Command(SqliteConnection db, string sql, params (string, object)[] values)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql;
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1024 || key.Any(c => c < 32 || c == 127)) throw new ArgumentException("Invalid registry key.");
        var parts = key.Replace('/', '\\').TrimEnd('\\').Split('\\');
        parts[0] = parts[0].ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => "HKCU", "HKEY_LOCAL_MACHINE" => "HKLM", "HKEY_CLASSES_ROOT" => "HKCR",
            "HKEY_USERS" => "HKU", "HKEY_CURRENT_CONFIG" => "HKCC", var s => s
        };
        if (parts[0] is not ("HKCU" or "HKLM" or "HKCR" or "HKU" or "HKCC") || parts.Any(p => p.Length == 0 || p is "." or ".."))
            throw new ArgumentException("Unknown hive or invalid registry key.");
        return string.Join('\\', parts).ToUpperInvariant();
    }
    private static string ValueName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > 256 || name.Any(c => c < 32 || c == 127)) throw new ArgumentException("Invalid registry value name.");
        return name.ToUpperInvariant();
    }

    private void Validate(RegistryEntry entry)
    {
        ValueName(entry.Name);
        bool valid = entry.Kind switch
        {
            "String" or "ExpandString" => entry.Data.ValueKind == JsonValueKind.String,
            "DWord" => entry.Data.ValueKind == JsonValueKind.Number && entry.Data.TryGetInt64(out long n) && n >= int.MinValue && n <= uint.MaxValue,
            // Decimal strings preserve all signed 64 bits across JavaScript JSON.
            "QWord" => entry.Data.ValueKind == JsonValueKind.String && long.TryParse(entry.Data.GetString(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _),
            "MultiString" => entry.Data.ValueKind == JsonValueKind.Array && entry.Data.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),
            "Binary" or "None" => entry.Data.ValueKind == JsonValueKind.String && IsBase64(entry.Data.GetString()!),
            _ => false
        };
        if (!valid || Encoding.UTF8.GetByteCount(entry.Data.GetRawText()) > _options.MaxRegistryValueBytes) throw new ArgumentException("Invalid or oversized typed registry value.");
    }
    private static bool IsBase64(string s) { try { Convert.FromBase64String(s); return true; } catch (FormatException) { return false; } }

    private void CreateKeys(SqliteConnection db, string scope, string key)
    {
        var parts = key.Split('\\');
        for (int i = 1; i <= parts.Length; i++) Execute(db, "INSERT OR IGNORE INTO registry_keys VALUES($s,$k)", ("$s", scope), ("$k", string.Join('\\', parts.Take(i))));
    }
    private void Quota(SqliteConnection db, string scope)
    {
        using var cmd = Command(db, "SELECT (SELECT COUNT(*) FROM registry_keys WHERE scope=$s),(SELECT COUNT(*) FROM registry_values WHERE scope=$s),(SELECT COALESCE(SUM(length(CAST(data AS BLOB))),0) FROM registry_values WHERE scope=$s)", ("$s", scope));
        using var row = cmd.ExecuteReader(); row.Read();
        if (row.GetInt64(0) > _options.MaxRegistryKeys || row.GetInt64(1) > _options.MaxRegistryValues || row.GetInt64(2) > _options.MaxRegistryBytes)
            throw new IOException("Registry quota exceeded.");
    }
    public void CreateKey(PlatformScope scope, string key)
    {
        key = NormalizeKey(key);
        lock (_gate) { using var db = Open(); using var tx = db.BeginTransaction(); CreateKeys(db, scope.StorageId, key); Quota(db, scope.StorageId); tx.Commit(); }
    }
    public void Set(PlatformScope scope, string key, RegistryEntry entry)
    {
        Validate(entry); key = NormalizeKey(key);
        lock (_gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction(); CreateKeys(db, scope.StorageId, key);
            Execute(db, "INSERT INTO registry_values VALUES($s,$k,$n,$d,$t,$v) ON CONFLICT(scope,key,name) DO UPDATE SET display_name=$d,kind=$t,data=$v",
                ("$s", scope.StorageId), ("$k", key), ("$n", ValueName(entry.Name)), ("$d", entry.Name), ("$t", entry.Kind), ("$v", entry.Data.GetRawText()));
            Quota(db, scope.StorageId); tx.Commit();
        }
    }
    public RegistrySnapshot Snapshot(PlatformScope scope)
    {
        lock (_gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            var keys = new List<string>(); var values = new List<RegistrySnapshotValue>();
            using (var cmd = Command(db, "SELECT key FROM registry_keys WHERE scope=$s ORDER BY key", ("$s", scope.StorageId)))
            using (var r = cmd.ExecuteReader()) while (r.Read()) keys.Add(r.GetString(0));
            using (var cmd = Command(db, "SELECT key,display_name,kind,data FROM registry_values WHERE scope=$s ORDER BY key,name", ("$s", scope.StorageId)))
            using (var r = cmd.ExecuteReader()) while (r.Read()) values.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), JsonDocument.Parse(r.GetString(3)).RootElement.Clone()));
            tx.Commit(); return new(keys.Concat(new[] { "HKCU", "HKLM", "HKCR", "HKU", "HKCC" }).Distinct().Order().ToArray(), values);
        }
    }
    public bool DeleteValue(PlatformScope scope, string key, string name)
    {
        lock (_gate) { using var db = Open(); return Execute(db, "DELETE FROM registry_values WHERE scope=$s AND key=$k AND name=$n", ("$s", scope.StorageId), ("$k", NormalizeKey(key)), ("$n", ValueName(name))) > 0; }
    }
    public bool DeleteKey(PlatformScope scope, string key, bool recursive = false)
    {
        key = NormalizeKey(key);
        if (!key.Contains('\\')) throw new UnauthorizedAccessException("Cannot delete a hive.");
        lock (_gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            // substr avoids LIKE wildcard interpretation of valid '%'/'_' key names.
            string clause = "scope=$s AND (key=$k OR substr(key,1,length($p))=$p)";
            using var count = Command(db, "SELECT COUNT(*) FROM registry_keys WHERE scope=$s AND substr(key,1,length($p))=$p", ("$s", scope.StorageId), ("$p", key + "\\"));
            if (!recursive && (long)count.ExecuteScalar()! > 0) throw new IOException("Registry key has subkeys.");
            Execute(db, "DELETE FROM registry_values WHERE " + clause, ("$s", scope.StorageId), ("$k", key), ("$p", key + "\\"));
            bool found = Execute(db, "DELETE FROM registry_keys WHERE " + clause, ("$s", scope.StorageId), ("$k", key), ("$p", key + "\\")) > 0;
            tx.Commit(); return found;
        }
    }
}
