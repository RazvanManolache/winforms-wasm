extern alias Fork;
extern alias WasmDraw;

using ForkForms = Fork::System.Windows.Forms;
using Hdc = Fork::Windows.Win32.Graphics.Gdi.HDC;
using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfBrushes = WasmDraw::System.Drawing.Brushes;
using WfColor = WasmDraw::System.Drawing.Color;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfRectangle = WasmDraw::System.Drawing.Rectangle;

// The browser CPU raster has no HDCs (Graphics.GetHdc() is
// IntPtr.Zero). Control.PaintTransparentBackground builds an
// HDC-backed PaintEventArgs for the parent repaint, which threw
// ANE 'dc' out of DrawingEventArgs..ctor for every transparent-
// BackColor control. The DrawingEventArgs.Wasm.cs overlay hands
// out a targetless Graphics instead: the parent repaint becomes
// a safe no-op (the host's retained layers already show the true
// beneath-pixels), so transparent Telerik labels paint their
// theme content over the wood instead of a flat black Clear.
// Regression: black BookShelf header labels + Self-paint failed
// RadLabel/RadButton the moment the host stopped swapping in an
// opaque BackColor.
public sealed class TransparentPaintTests
{
    [Fact]
    public void Null_Hdc_Hands_Out_Graphics_Without_Throw()
    {
        var clip = new WfRectangle(0, 0, 10, 10);
        using var args = new ForkForms.PaintEventArgs(default(Hdc), clip);

        Assert.NotNull(args.Graphics);
        Assert.Equal(clip, args.ClipRectangle);
    }

    [Fact]
    public void Null_Hdc_Paint_Operations_Are_Safe_No_Ops()
    {
        var clip = new WfRectangle(0, 0, 10, 10);
        using var args = new ForkForms.PaintEventArgs(default(Hdc), clip);
        WfGraphics g = args.Graphics;

        var ex = Record.Exception(() =>
        {
            g.FillRectangle(WfBrushes.Red, clip);
            g.SetClip(clip);
            g.TranslateTransform(2, 3);
            g.Save();
            g.Restore(g.Save());
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Transparent_Parented_PaintBackground_Preserves_Bitmap()
    {
        using var panel = new ForkForms.Panel();
        using var label = new ForkForms.Label { BackColor = WfColor.Transparent };
        panel.Controls.Add(label);

        using var bmp = new WfBitmap(16, 16);
        using var g = WfGraphics.FromImage(bmp);
        var rect = new WfRectangle(0, 0, 16, 16);
        using var e = new ForkForms.PaintEventArgs(g, rect);

        // Was: ANE 'dc' from the parent-repaint HDC event args.
        var ex = Record.Exception(() => label.PaintBackground(e, rect));
        Assert.Null(ex);

        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(0, bmp.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void Graphics_Ctor_Passes_External_Graphics_Through()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        var clip = new WfRectangle(0, 0, 4, 4);
        using var args = new ForkForms.PaintEventArgs(g, clip);

        Assert.Same(g, args.Graphics);
    }
}
