extern alias Fork;

using ForkForms = Fork::System.Windows.Forms;

// Regression: size assignments on a Maximized form are diverted to
// restore-bounds storage headless (no window manager ever applies them),
// so the design size sticks and maximized-start has no effect (DockPanel
// stayed 579x409). The host placement sequence must drop to Normal,
// assign bounds, then re-assert Maximized.
namespace WfWasm.WinForms.Tests;

public sealed class MaximizedPlacementTests
{
    [Fact]
    public void MaximizedForm_Accepts_Bounds_Via_Normal_Dance()
    {
        using var f = new ForkForms.Form();
        var panel = new ForkForms.Panel { Dock = ForkForms.DockStyle.Fill };
        f.Controls.Add(panel);
        try { f.Visible = true; } catch { }
        f.WindowState = ForkForms.FormWindowState.Normal;
        f.Width = 866;
        f.Height = 674;
        f.WindowState = ForkForms.FormWindowState.Maximized;
        try { f.PerformLayout(); } catch { }
        Assert.Equal(866, f.Width);
        Assert.Equal(674, f.Height);
        Assert.Equal(ForkForms.FormWindowState.Maximized, f.WindowState);
    }

    [Fact]
    public void MaximizedForm_Direct_Size_Sticks_Only_When_Honored()
    {
        // Documents the underlying diversion: a direct set while Maximized
        // does NOT change current bounds (goes to restore storage). The
        // host must use the Normal dance above, never a direct set.
        using var f = new ForkForms.Form();
        try { f.Visible = true; } catch { }
        f.WindowState = ForkForms.FormWindowState.Maximized;
        int before = f.Width;
        f.Width = before + 100;
        Assert.Equal(before, f.Width);
    }
}
