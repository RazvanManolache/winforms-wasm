extern alias Fork;

using WasmEnvironment = Fork::System.Windows.Forms.WasmEnvironment;

namespace WfWasm.WinForms.Tests;

// Virtual special-folder layout: stock WASM answers "" for every folder
// and Framework-era builders die on it (Telerik PDF FontsManager..cctor
// GetParents the System folder). Every answer is non-empty and two
// levels deep so GetParent never nulls, and the Fonts/System pair is
// coherent under both the direct query and the GetParent + "Fonts"
// discovery spelling.
public sealed class WasmEnvironmentTests
{
    [Fact]
    public void VirtualPaths_Are_NonEmpty_And_Two_Levels_Deep()
    {
        foreach (Environment.SpecialFolder folder in Enum.GetValues<Environment.SpecialFolder>())
        {
            string path = WasmEnvironment.GetVirtualPath(folder);
            Assert.False(string.IsNullOrEmpty(path), folder.ToString());
            string[] segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(segs.Length >= 2, $"{folder} -> {path}");
        }
    }

    [Fact]
    public void Fonts_And_System_Agree_On_Fonts_Directory()
    {
        string system = WasmEnvironment.GetVirtualPath(Environment.SpecialFolder.System);
        string? parent = Path.GetDirectoryName(system);
        Assert.NotNull(parent);
        Assert.Equal(
            Path.Combine(parent, "Fonts").Replace('\\', '/'),
            WasmEnvironment.GetVirtualPath(Environment.SpecialFolder.Fonts));
    }

    [Fact]
    public void GetFolderPath_Headless_Passes_Through_Without_Throw()
    {
        // Headless the shim is transparent (desktop truth); it must not
        // throw for any folder/option pair.
        foreach (Environment.SpecialFolder folder in Enum.GetValues<Environment.SpecialFolder>())
        {
            string a = WasmEnvironment.GetFolderPath(folder);
            Assert.NotNull(a);
        }

        // Create must not throw for the app-data pair (already present,
        // so no profile side effects from this probe).
        Assert.NotNull(WasmEnvironment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create));
        Assert.NotNull(WasmEnvironment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create));
    }
}
