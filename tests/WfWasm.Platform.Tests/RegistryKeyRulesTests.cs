using System.Text.Json;
using WfWasm.Platform.Server;

namespace WfWasm.Platform.Tests;

// Key-canonicalization and value-validation edges beyond the existing
// storage suite: hive aliases, separator normalization, and boundary
// names that must fail closed.
public sealed class RegistryKeyRulesTests : IDisposable
{
    private readonly string _root = Path.Combine("E:/tmp/wf-platform-tests", Guid.NewGuid().ToString("N"));
    private static readonly PlatformScope Alice = new("alice", "app");
    private static RegistryEntry Value(string name, string kind, object data) =>
        new(name, kind, JsonSerializer.SerializeToElement(data));
    private PlatformOptions Options() => new() { DataDirectory = _root };

    [Theory]
    [InlineData("HKEY_CURRENT_USER\\Software", "HKCU\\SOFTWARE")]
    [InlineData("HKEY_LOCAL_MACHINE\\A", "HKLM\\A")]
    [InlineData("HKEY_CLASSES_ROOT\\x", "HKCR\\X")]
    [InlineData("HKEY_USERS\\u", "HKU\\U")]
    [InlineData("HKEY_CURRENT_CONFIG\\c", "HKCC\\C")]
    [InlineData("hkcu/software/demo", "HKCU\\SOFTWARE\\DEMO")]
    [InlineData("HKCU/Software/Demo", "HKCU\\SOFTWARE\\DEMO")]
    [InlineData("HKCU\\Software\\", "HKCU\\SOFTWARE")]
    public void NormalizeKey_Canonicalizes_Hive_And_Case(string input, string expected)
    {
        Assert.Equal(expected, VirtualRegistry.NormalizeKey(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HKCU\\.\\A")]
    [InlineData("HKCU\\A\\.")]
    [InlineData("HKCU\\A\\.\\.\\B")]
    [InlineData("HKCU\\A/B\\C//D")]
    [InlineData("HKCU\\A\u007fB")]
    public void NormalizeKey_Rejects_Bad_Shapes(string key)
    {
        Assert.Throws<ArgumentException>(() => VirtualRegistry.NormalizeKey(key));
    }

    [Fact]
    public void NormalizeKey_Too_Long_Rejects()
    {
        Assert.Throws<ArgumentException>(() => VirtualRegistry.NormalizeKey("HKCU\\" + new string('A', 1024)));
    }

    [Fact]
    public void Registry_DWord_Above_Uint32_Rejects()
    {
        var r = new VirtualRegistry(Options());
        Assert.Throws<ArgumentException>(() => r.Set(Alice, "HKCU\\Demo", Value("v", "DWord", 4294967296L)));
        Assert.Empty(r.Snapshot(Alice).Values);
    }

    [Fact]
    public void Registry_DWord_Signed_Negative_And_MaxUint32_Accepted()
    {
        var r = new VirtualRegistry(Options());
        r.Set(Alice, "HKCU\\Demo", Value("neg", "DWord", -1));
        r.Set(Alice, "HKCU\\Demo", Value("max", "DWord", uint.MaxValue));
        Assert.Equal(2, r.Snapshot(Alice).Values.Count);
    }

    [Fact]
    public void Registry_Value_Name_Too_Long_Rejects()
    {
        var r = new VirtualRegistry(Options());
        Assert.Throws<ArgumentException>(() =>
            r.Set(Alice, "HKCU\\Demo", Value(new string('n', 257), "String", "x")));
    }

    [Fact]
    public void Registry_Value_Display_Name_Preserves_Case()
    {
        var r = new VirtualRegistry(Options());
        r.Set(Alice, "HKCU\\Demo", Value("MixedCase", "String", "v"));
        // Storage key folds (case-insensitive lookup); display name keeps original case.
        Assert.Equal("MixedCase", Assert.Single(r.Snapshot(Alice).Values).Name);
        Assert.True(r.DeleteValue(Alice, "HKCU\\Demo", "mixedcase"));
    }

    [Fact]
    public void Registry_CreateKey_Is_Idempotent()
    {
        var r = new VirtualRegistry(Options());
        r.CreateKey(Alice, "HKCU\\Idem");
        r.CreateKey(Alice, "hkcu\\idem");
        // Snapshot always lists the 5 root hives; the created key appears exactly once.
        Assert.Single(r.Snapshot(Alice).Keys.Where(k => k == "HKCU\\IDEM"));
    }

    [Fact]
    public void PlatformScope_Record_Equality()
    {
        Assert.Equal(new PlatformScope("alice", "app"), new PlatformScope("alice", "app"));
        Assert.NotEqual(new PlatformScope("alice", "app"), new PlatformScope("bob", "app"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
