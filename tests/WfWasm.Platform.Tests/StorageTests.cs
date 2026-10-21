using System.Text.Json;
using WfWasm.Platform.Server;

namespace WfWasm.Platform.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.Combine(Path.GetTempPath(), "wf-platform-tests"), Guid.NewGuid().ToString("N"));
    private static readonly PlatformScope Alice = new("alice", "app");
    private static readonly PlatformScope Bob = new("bob", "app");
    private static RegistryEntry Value(string name, string kind, object data) => new(name, kind, JsonSerializer.SerializeToElement(data));
    private PlatformOptions Options() => new() { DataDirectory = _root };

    [Fact] public void Registry_Persists_And_Isolates_User_And_App()
    {
        var registry = new VirtualRegistry(Options()); registry.Set(Alice, "HKCU/Software/Demo", Value("theme", "String", "dark"));
        var reopened = new VirtualRegistry(Options());
        Assert.Equal("dark", Assert.Single(reopened.Snapshot(Alice).Values).Data.GetString());
        Assert.Empty(reopened.Snapshot(Bob).Values); Assert.Empty(reopened.Snapshot(new("alice", "other")).Values);
    }
    [Fact] public void Registry_Case_Insensitive_Default_Value_And_Empty_Keys()
    {
        var r = new VirtualRegistry(Options()); r.CreateKey(Alice, "hkey_current_user\\Software\\Empty");
        r.Set(Alice, "hkcu\\software\\demo", Value("", "DWord", 42));
        r.Set(Alice, "HKCU\\SOFTWARE\\DEMO", Value("", "DWord", 43));
        Assert.Contains("HKCU\\SOFTWARE\\EMPTY", r.Snapshot(Alice).Keys);
        Assert.Equal(43, Assert.Single(r.Snapshot(Alice).Values).Data.GetInt32());
    }
    [Theory]
    [InlineData("String", "hello")][InlineData("ExpandString", "%APPDATA%\\demo")][InlineData("Binary", "AQID")]
    [InlineData("None", "")][InlineData("QWord", "9223372036854775807")]
    public void Registry_String_Wire_Types_Roundtrip(string kind, string data)
    {
        var r = new VirtualRegistry(Options()); r.Set(Alice, "HKLM\\Demo", Value("v", kind, data));
        Assert.Equal(data, Assert.Single(r.Snapshot(Alice).Values).Data.GetString());
    }
    [Fact] public void Registry_MultiString_And_DWord_Preserve_Type()
    {
        var r = new VirtualRegistry(Options()); r.Set(Alice, "HKCU\\Demo", Value("multi", "MultiString", new[] { "a", "b" }));
        r.Set(Alice, "HKCU\\Demo", Value("n", "DWord", uint.MaxValue));
        Assert.Equal(2, r.Snapshot(Alice).Values.Single(v => v.Name == "multi").Data.GetArrayLength());
        Assert.Equal(uint.MaxValue, r.Snapshot(Alice).Values.Single(v => v.Name == "n").Data.GetUInt32());
    }
    [Theory][InlineData("DWord", "text")][InlineData("Binary", "not base64!")][InlineData("QWord", "9223372036854775808")][InlineData("Unknown", "x")]
    public void Registry_Rejects_Invalid_Types(string kind, string data)
    {
        var r = new VirtualRegistry(Options()); Assert.Throws<ArgumentException>(() => r.Set(Alice, "HKCU\\Demo", Value("v", kind, data))); Assert.Empty(r.Snapshot(Alice).Values);
    }
    [Fact] public void Registry_Delete_Does_Not_Treat_Wildcards_As_Paths()
    {
        var r = new VirtualRegistry(Options()); r.Set(Alice, "HKCU\\A_", Value("n", "String", "a"));
        r.Set(Alice, "HKCU\\AB", Value("n", "String", "b")); r.CreateKey(Alice, "HKCU\\A_\\Child");
        Assert.Throws<IOException>(() => r.DeleteKey(Alice, "HKCU\\A_"));
        Assert.True(r.DeleteKey(Alice, "HKCU\\A_", true)); Assert.Equal("b", Assert.Single(r.Snapshot(Alice).Values).Data.GetString());
        Assert.Throws<UnauthorizedAccessException>(() => r.DeleteKey(Alice, "HKCU", true));
    }
    [Fact] public void Registry_Quota_Rolls_Back_The_Whole_Operation()
    {
        var r = new VirtualRegistry(new() { DataDirectory = _root, MaxRegistryValues = 1 });
        r.Set(Alice, "HKCU\\Demo", Value("first", "String", "ok"));
        Assert.Throws<IOException>(() => r.Set(Alice, "HKCU\\New", Value("second", "String", "no")));
        Assert.Single(r.Snapshot(Alice).Values); Assert.DoesNotContain("HKCU\\NEW", r.Snapshot(Alice).Keys);
    }
    [Theory][InlineData("\\\\host\\HKCU")][InlineData("HKCU\\..\\a")][InlineData("HKCU\\a\\\\b")][InlineData("HKBOGUS\\a")]
    public void Registry_Rejects_Remote_And_Invalid_Keys(string key) => Assert.Throws<ArgumentException>(() => VirtualRegistry.NormalizeKey(key));

    [Fact] public void Workspace_Persists_And_Isolates()
    {
        var fs = new VirtualFiles(Options()); fs.Write(Alice, "workspace", "docs/a.txt", [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, new VirtualFiles(Options()).Read(Alice, "workspace", "docs/a.txt"));
        Assert.ThrowsAny<IOException>(() => fs.Read(Bob, "workspace", "docs/a.txt"));
        Assert.Empty(fs.List(Bob, "workspace").Entries);
    }
    [Theory][InlineData("../outside")][InlineData("C:\\Windows\\win.ini")][InlineData("/etc/passwd")][InlineData("%2e%2e/a")]
    [InlineData("a//b")][InlineData("a:stream")][InlineData("CON.txt")][InlineData("dir./a")][InlineData("a\\..\\b")]
    public void Files_Reject_Traversal_And_Device_Paths(string path)
    {
        var fs = new VirtualFiles(Options()); Assert.Throws<ArgumentException>(() => fs.Read(Alice, "workspace", path));
    }
    [Fact] public void Explicit_File_Grant_Cannot_Read_Adjacent_Files_Or_Write()
    {
        Directory.CreateDirectory(_root); string granted = Path.Combine(_root, "allowed.txt"); File.WriteAllText(granted, "yes"); File.WriteAllText(Path.Combine(_root, "other.txt"), "no");
        var fs = new VirtualFiles(new() { DataDirectory = _root, Mounts = scope => scope.UserId == "alice" ? [new("appfiles", "App files", granted, IsFile: true)] : [] });
        Assert.Single(fs.List(Alice, "appfiles").Entries);
        Assert.Equal("yes", System.Text.Encoding.UTF8.GetString(fs.Read(Alice, "appfiles", "allowed.txt")));
        Assert.Throws<UnauthorizedAccessException>(() => fs.Read(Alice, "appfiles", "other.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.Read(Bob, "appfiles", "allowed.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.Write(Alice, "appfiles", "allowed.txt", [0]));
        Assert.DoesNotContain(granted, JsonSerializer.Serialize(fs.GetMounts(Alice)));
    }
    [Fact] public void Files_Quota_And_Readonly_And_Delete()
    {
        var fs = new VirtualFiles(new() { DataDirectory = _root, MaxFileBytes = 4, MaxWorkspaceBytes = 6 });
        fs.Write(Alice, "workspace", "a", [1, 2, 3, 4]);
        Assert.Throws<IOException>(() => fs.Write(Alice, "workspace", "b", [1, 2, 3]));
        Assert.Throws<IOException>(() => fs.Write(Alice, "workspace", "large", [1, 2, 3, 4, 5]));
        fs.Write(Alice, "workspace", "a", [9]); fs.CreateDirectory(Alice, "workspace", "empty"); fs.Delete(Alice, "workspace", "empty");
        Assert.Throws<UnauthorizedAccessException>(() => fs.Delete(Alice, "workspace", "")); fs.Delete(Alice, "workspace", "a"); Assert.Empty(fs.List(Alice, "workspace").Entries);
    }
    [Fact] public void File_List_Exposes_Only_Explicit_Files_With_Virtual_Subfolders()
    {
        Directory.CreateDirectory(_root); var allowed = Path.Combine(_root, "real-name.txt"); File.WriteAllText(allowed, "allowed");
        File.WriteAllText(Path.Combine(_root, "private.txt"), "private");
        var fs = new VirtualFiles(new() { DataDirectory = _root, Mounts = _ => [new("app", "App files", "", FileList: new Dictionary<string, string> { ["Documents/Readme.txt"] = allowed })] });
        Assert.True(Assert.Single(fs.List(Alice, "app").Entries).IsDirectory);
        Assert.Equal("Readme.txt", Assert.Single(fs.List(Alice, "app", "Documents").Entries).Name);
        Assert.Equal("allowed", System.Text.Encoding.UTF8.GetString(fs.Read(Alice, "app", "Documents/Readme.txt")));
        Assert.Throws<UnauthorizedAccessException>(() => fs.Read(Alice, "app", "private.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.Write(Alice, "app", "Documents/Readme.txt", [0]));
    }
    [Fact] public void Workspace_Zero_Byte_Files_And_Folders_Still_Consume_Entry_Quota()
    {
        var fs = new VirtualFiles(new() { DataDirectory = _root, MaxWorkspaceEntries = 2 });
        fs.Write(Alice, "workspace", "a/b", []);
        Assert.Throws<IOException>(() => fs.CreateDirectory(Alice, "workspace", "c"));
        Assert.Throws<IOException>(() => fs.Write(Alice, "workspace", "c", []));
        Assert.Single(fs.List(Alice, "workspace").Entries);
    }
    [Fact] public void Linked_Descendants_And_Linked_Grant_Roots_Are_Denied()
    {
        string outside = Path.Combine(_root, "outside"), grant = Path.Combine(_root, "grant"), link = Path.Combine(grant, "linked");
        Directory.CreateDirectory(outside); Directory.CreateDirectory(grant); File.WriteAllText(Path.Combine(outside, "secret.txt"), "private");
        if (OperatingSystem.IsWindows())
        {
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/J"); info.ArgumentList.Add(Path.GetFullPath(link)); info.ArgumentList.Add(Path.GetFullPath(outside));
            using var proc = System.Diagnostics.Process.Start(info)!; proc.WaitForExit(); Assert.True(proc.ExitCode == 0, proc.StandardError.ReadToEnd());
        }
        else Directory.CreateSymbolicLink(link, outside);
        var fs = new VirtualFiles(new() { DataDirectory = _root, Mounts = _ => [new("grant", "Grant", grant), new("rootlink", "Linked root", link)] });
        Assert.Empty(fs.List(Alice, "grant").Entries);
        Assert.Throws<UnauthorizedAccessException>(() => fs.Read(Alice, "grant", "linked/secret.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.List(Alice, "rootlink"));
        Assert.Equal("private", File.ReadAllText(Path.Combine(outside, "secret.txt")));
        Directory.Delete(link); // Remove the test-owned junction before recursive fixture cleanup.
    }
    [Fact] public void Directory_Listing_Is_Paged()
    {
        var fs = new VirtualFiles(Options()); for (int i = 0; i < 260; i++) fs.Write(Alice, "workspace", $"f{i:D3}", []);
        var a = fs.List(Alice, "workspace"); Assert.Equal(256, a.Entries.Count); Assert.Equal(256, a.NextOffset);
        var b = fs.List(Alice, "workspace", offset: 256); Assert.Equal(4, b.Entries.Count); Assert.Null(b.NextOffset);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
