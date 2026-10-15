using WfWasm.CanvasHost;
using WfWasm.Renderer;

public sealed class BitmapLayerTests
{
    [Fact]
    public void LayerIdFor_IsStablePerKey()
    {
        object a = new();
        object b = new();
        Assert.Equal(WinFormsAppHost.LayerIdFor(a), WinFormsAppHost.LayerIdFor(a));
        Assert.NotEqual(WinFormsAppHost.LayerIdFor(a), WinFormsAppHost.LayerIdFor(b));
    }

    [Fact]
    public void RecordBitmap_LayerBackend_EmitsDefPlusBlit()
    {
        var recorder = new RecordingRenderer();
        object key = new();
        byte[] rgba = new byte[] { 1, 2, 3, 4 };

        WinFormsAppHost.RecordBitmap(key, recorder, rgba, 1, 1, 5, 6, 7, 8);

        Assert.Equal(2, recorder.Commands.Count);
        var def = Assert.IsType<DefBitmapCommand>(recorder.Commands[0]);
        Assert.Equal(WinFormsAppHost.LayerIdFor(key), def.LayerId);
        Assert.Same(rgba, def.Rgba);
        var blit = Assert.IsType<BlitBitmapCommand>(recorder.Commands[1]);
        Assert.Equal(def.LayerId, blit.LayerId);
        Assert.Equal(5, blit.X);
    }

    [Fact]
    public void HashBytes_Detects_Single_Byte_Change()
    {
        byte[] a = new byte[4096];
        byte[] b = new byte[4096];
        b[2048] = 1;
        Assert.Equal(WinFormsAppHost.HashBytes(a), WinFormsAppHost.HashBytes(a));
        Assert.NotEqual(WinFormsAppHost.HashBytes(a), WinFormsAppHost.HashBytes(b));
    }

    [Fact]
    public void RecordBitmap_SkipsDef_WhenBytesUnchanged()
    {
        var recorder = new RecordingRenderer();
        object key = new();
        byte[] rgba = new byte[] { 1, 2, 3, 4 };

        WinFormsAppHost.RecordBitmap(key, recorder, rgba, 1, 1, 0, 0, 1, 1);
        WinFormsAppHost.RecordBitmap(key, recorder, rgba, 1, 1, 0, 0, 1, 1);

        Assert.Equal(3, recorder.Commands.Count);
        Assert.IsType<DefBitmapCommand>(recorder.Commands[0]);
        Assert.IsType<BlitBitmapCommand>(recorder.Commands[1]);
        Assert.IsType<BlitBitmapCommand>(recorder.Commands[2]);
    }

    [Fact]
    public void RecordBitmap_Redefines_WhenBytesChange()
    {
        var recorder = new RecordingRenderer();
        object key = new();

        WinFormsAppHost.RecordBitmap(key, recorder, new byte[] { 1, 2, 3, 4 }, 1, 1, 0, 0, 1, 1);
        WinFormsAppHost.RecordBitmap(key, recorder, new byte[] { 5, 6, 7, 8 }, 1, 1, 0, 0, 1, 1);

        Assert.Equal(4, recorder.Commands.Count);
        Assert.IsType<DefBitmapCommand>(recorder.Commands[0]);
        Assert.IsType<DefBitmapCommand>(recorder.Commands[2]);
    }

    [Fact]
    public void RecordBitmap_PlainBackend_FallsBackToDrawImage()
    {
        var svg = new SvgRenderer();
        svg.BeginFrame(100, 100);

        WinFormsAppHost.RecordBitmap(new object(), svg, new byte[16], 2, 2, 0, 0, 2, 2);

        Assert.Contains("data-image=\"2x2\"", svg.GetSvg());
    }

    [Fact]
    public void HashBytes_Detects_Changes_At_Any_Offset()
    {
        // The word-at-a-time hash must stay single-byte sensitive at
        // every alignment, including the sub-word tail.
        for (int at = 0; at < 9; at++)
        {
            byte[] a = new byte[9];
            byte[] b = new byte[9];
            b[at] = 1;
            Assert.NotEqual(WinFormsAppHost.HashBytes(a), WinFormsAppHost.HashBytes(b));
        }
        byte[] c = new byte[] { 1, 2, 3 };
        Assert.Equal(WinFormsAppHost.HashBytes(c), WinFormsAppHost.HashBytes(c));
    }
}
