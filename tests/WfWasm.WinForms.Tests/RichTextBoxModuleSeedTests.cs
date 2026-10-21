using System.Reflection;
using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// RichTextBox.Wasm seeds the private cached module handle on browser only
// (no native RichEdit headless; LoadLibrary + version probe throw). These
// pin the reflection target against upstream renames and prove the seeder
// stays inert off-browser (real load path untouched there).
public class RichTextBoxModuleSeedTests
{
    [Fact]
    public void ModuleHandleField_Exists_For_Headless_Seeding()
    {
        FieldInfo? field = typeof(RichTextBox).GetField("s_moduleHandle",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(typeof(IntPtr), field.FieldType);
    }

    [Fact]
    public void OffBrowser_ModuleHandle_Not_Preseeded()
    {
        // The migrated fork under test has stubbed native loads, so even
        // desktop handle creation throws (same Win32Exception as headless).
        // What this pins: off-browser the static seeder stays inert (the
        // field remains zero until the real load path runs).
        FieldInfo? field = typeof(RichTextBox).GetField("s_moduleHandle",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(IntPtr.Zero, (IntPtr)field.GetValue(null)!);
    }
}
