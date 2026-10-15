extern alias WfDraw;

using System;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using Xunit;

public sealed class HdcTests
{
    // Headless has no GDI device contexts, but Telerik builds its text
    // measurement Graphics from a screen DC (MeasurementGraphics..ctor).
    // FromHdcInternal answers the same dummy canvas Graphics as its
    // FromHdc/FromHwnd siblings instead of throwing: measurement works,
    // HDC rendering was fatal before either way.
    // Regression: NotSupportedException | FromHdcInternal during
    // RadButtonElement measure on the first Telerik demo form-create.
    [Fact]
    public void FromHdcInternal_Answers_Dummy_Graphics()
    {
        using var g = WfGraphics.FromHdcInternal(new IntPtr(0x1000));
        Assert.NotNull(g);
    }

    [Fact]
    public void FromHdc_Family_Answers_Dummy_Graphics()
    {
        using var a = WfGraphics.FromHdc(new IntPtr(0x1000));
        using var b = WfGraphics.FromHdc(new IntPtr(0x1000), IntPtr.Zero);
        using var c = WfGraphics.FromHwnd(new IntPtr(0x2000));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotNull(c);
    }

    [Fact]
    public void ReleaseHdc_Tolerates_Stub_Handles()
    {
        using var g = WfGraphics.FromHdcInternal(new IntPtr(0x1000));
        var ex = Record.Exception(() =>
        {
            g.ReleaseHdc();
            g.ReleaseHdc(new IntPtr(0x1000));
            g.ReleaseHdcInternal(new IntPtr(0x1000));
        });
        Assert.Null(ex);
    }
}
