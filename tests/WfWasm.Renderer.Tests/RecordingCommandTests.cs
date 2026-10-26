using WfWasm.Renderer;
using WfWasm.Shim;

namespace WfWasm.Renderer.Tests;

// Command-fidelity net for the recording backend: every ICanvasRenderer
// call must land as the exact record with defaults preserved, so replay
// and test-oracles see what the app painted.
public sealed class RecordingCommandTests
{
    [Fact]
    public void Frame_Open_Close_Markers_In_Order()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(30, 40);
        r.EndFrame();
        Assert.Equal([new FrameCommand(30, 40), EndFrameCommand.Instance], r.Commands);
    }

    [Fact]
    public void EndFrame_Is_Singleton()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(1, 1);
        r.EndFrame();
        r.BeginFrame(2, 2);
        r.EndFrame();
        Assert.Same(r.Commands[1], r.Commands[3]);
    }

    [Theory]
    [InlineData(1f, 2f, 3f, 4f, 0xFF0000FFu)]
    [InlineData(0f, 0f, 0f, 0f, 0x00000000u)]
    public void FillRectangle_Records_Exactly(float x, float y, float w, float h, uint rgba)
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.FillRectangle(x, y, w, h, rgba);
        Assert.Equal(new FillRectCommand(x, y, w, h, rgba), r.Commands[1]);
    }

    [Fact]
    public void FillRoundedRectangle_Records_Radius()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.FillRoundedRectangle(1, 2, 3, 4, 5, 0x11223344u);
        Assert.Equal(new FillRoundedRectCommand(1, 2, 3, 4, 5, 0x11223344u), r.Commands[1]);
    }

    [Fact]
    public void FillGradient_Records_Both_Stops()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.FillGradientVertical(0, 0, 8, 8, 0xFF0000FFu, 0x0000FFFFu);
        Assert.Equal(new FillGradientCommand(0, 0, 8, 8, 0xFF0000FFu, 0x0000FFFFu), r.Commands[1]);
    }

    [Fact]
    public void DrawLine_Records_Width_And_Color()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.DrawLine(1, 1, 6, 6, 2.5f, 0xFFFFFFFFu);
        Assert.Equal(new DrawLineCommand(1, 1, 6, 6, 2.5f, 0xFFFFFFFFu), r.Commands[1]);
    }

    [Fact]
    public void DrawEllipse_Default_Fill_Is_Null()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.DrawEllipse(1, 1, 4, 4, 1, 0xFF0000FFu);
        var cmd = Assert.IsType<DrawEllipseCommand>(r.Commands[1]);
        Assert.Null(cmd.FillRgba);
        r.DrawEllipse(1, 1, 4, 4, 1, 0xFF0000FFu, 0x00FF00FFu);
        Assert.Equal(0x00FF00FFu, Assert.IsType<DrawEllipseCommand>(r.Commands[2]).FillRgba);
    }

    [Fact]
    public void DrawText_Short_And_Aligned_Forms_Agree()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.DrawText("hi", 1, 2, "12px sans-serif", 0xFFFFFFFFu);
        r.DrawTextAligned("hi", 1, 2, "12px sans-serif", 0xFFFFFFFFu,
            WfWasm.Shim.TextHAlign.Left, WfWasm.Shim.TextVAlign.Baseline);
        Assert.Equal(r.Commands[1], r.Commands[2]);
    }

    [Fact]
    public void DrawTextAligned_Records_Align_And_Mnemonic()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        r.DrawTextAligned("Fil&e", 1, 2, "12px sans-serif", 0xFFFFFFFFu,
            WfWasm.Shim.TextHAlign.Center, WfWasm.Shim.TextVAlign.Middle, 3);
        var cmd = Assert.IsType<DrawTextCommand>(r.Commands[1]);
        Assert.Equal(WfWasm.Shim.TextHAlign.Center, cmd.HAlign);
        Assert.Equal(WfWasm.Shim.TextVAlign.Middle, cmd.VAlign);
        Assert.Equal(3, cmd.MnemonicIndex);
    }

    [Fact]
    public void Bitmap_Layer_Lifecycle_Records()
    {
        var r = new RecordingRenderer();
        Assert.True(r.SupportsBitmapLayers);
        r.BeginFrame(8, 8);
        var px = new byte[16];
        r.DefBitmap("L1", px, 2, 2);
        r.PatchBitmap("L1", 0, 0, 2, 2, px);
        r.BlitBitmap("L1", 0, 0, 2, 2);
        r.RetainLayers(["L1"]);
        Assert.IsType<DefBitmapCommand>(r.Commands[1]);
        Assert.IsType<PatchBitmapCommand>(r.Commands[2]);
        Assert.Equal(new BlitBitmapCommand("L1", 0, 0, 2, 2), r.Commands[3]);
        // RetainLayersCommand holds a list (reference equality), so compare contents.
        Assert.Equal(["L1"], Assert.IsType<RetainLayersCommand>(r.Commands[4]).LiveIds);
    }

    [Fact]
    public void DrawImage_Records_Buffer_Reference()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(8, 8);
        var px = new byte[16];
        r.DrawImage(px, 2, 2, 0, 0, 2, 2);
        var cmd = Assert.IsType<DrawImageCommand>(r.Commands[1]);
        Assert.Same(px, cmd.Rgba);
        Assert.Equal(new DrawImageCommand(px, 2, 2, 0, 0, 2, 2), cmd);
    }
}
