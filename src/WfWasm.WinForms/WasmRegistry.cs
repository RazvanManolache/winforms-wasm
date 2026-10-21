using Microsoft.Win32;

namespace System.Windows.Forms;

// Headless registry: the browser has no Windows registry and the runtime's
// RegistryKey implementation NREs (hive getters return null). PDN reads
// theme/accent keys (DWM colorization) and file-association keys. The
// rewriter retargets Registry/RegistryKey calls here with missing-key
// semantics: opens return null, reads return defaults, writes are no-ops.
// Instance callvirt becomes static Call, so null receivers are fine.
// Method names mirror the originals exactly (including get_* property
// getters) because the rewriter builds hook references per call site.
// Regression: NRE in UIHelpers.GetWindowColorizationInfo <- PdnToolBar
// paint on every PDN frame (Registry.CurrentUser is null).
public static class WasmRegistry
{
    public static RegistryKey? OpenSubKey(RegistryKey? key, string name) => null;
    public static RegistryKey? OpenSubKey(RegistryKey? key, string name, bool writable) => null;
    public static RegistryKey? OpenSubKey(RegistryKey? key, string name, RegistryKeyPermissionCheck permissionCheck) => null;
    public static RegistryKey? OpenSubKey(RegistryKey? key, string name, RegistryKeyPermissionCheck permissionCheck, System.Security.AccessControl.RegistryRights rights) => null;
    public static object? GetValue(RegistryKey? key, string? name) => null;
    public static object? GetValue(RegistryKey? key, string? name, object? defaultValue) => defaultValue;
    public static object? GetValue(RegistryKey? key, string? name, object? defaultValue, RegistryValueOptions options) => defaultValue;
    public static object? GetValue(string keyName, string? valueName, object? defaultValue) => defaultValue;
    public static void SetValue(RegistryKey? key, string? name, object value) { }
    public static void SetValue(RegistryKey? key, string? name, object value, RegistryValueKind kind) { }
    public static void SetValue(string keyName, string? valueName, object value) { }
    public static void SetValue(string keyName, string? valueName, object value, RegistryValueKind kind) { }
    public static RegistryKey? CreateSubKey(RegistryKey? key, string subkey) => null;
    public static RegistryKey? CreateSubKey(RegistryKey? key, string subkey, RegistryKeyPermissionCheck permissionCheck, RegistryOptions options) => null;
    public static RegistryKey? CreateSubKey(RegistryKey? key, string subkey, bool writable, RegistryOptions options) => null;
    public static RegistryKey? CreateSubKey(RegistryKey? key, string subkey, RegistryKeyPermissionCheck permissionCheck, System.Security.AccessControl.RegistrySecurity security) => null;
    public static void Close(RegistryKey? key) { }
    public static void Dispose(RegistryKey? key) { }
    public static void Flush(RegistryKey? key) { }
    public static string[] GetSubKeyNames(RegistryKey? key) => [];
    public static string[] GetValueNames(RegistryKey? key) => [];
    public static void DeleteValue(RegistryKey? key, string name) { }
    public static void DeleteValue(RegistryKey? key, string name, bool throwOnMissingValue) { }
    public static void DeleteSubKey(RegistryKey? key, string subkey) { }
    public static void DeleteSubKey(RegistryKey? key, string subkey, bool throwOnMissingSubKey) { }
    public static void DeleteSubKeyTree(RegistryKey? key, string subkey) { }
    public static RegistryValueKind GetValueKind(RegistryKey? key, string? name) => RegistryValueKind.Unknown;
    public static string get_Name(RegistryKey? key) => string.Empty;
    public static int get_ValueCount(RegistryKey? key) => 0;
    public static int get_SubKeyCount(RegistryKey? key) => 0;
    public static Microsoft.Win32.SafeHandles.SafeRegistryHandle? get_Handle(RegistryKey? key) => null;
    public static RegistryView get_View(RegistryKey? key) => RegistryView.Default;
    public static RegistryKey? OpenBaseKey(RegistryHive hive, RegistryView view) => null;
    public static RegistryKey? OpenRemoteBaseKey(RegistryHive hive, string machineName, RegistryView view) => null;
    public static RegistryKey? OpenRemoteBaseKey(RegistryHive hive, string machineName) => null;
}
