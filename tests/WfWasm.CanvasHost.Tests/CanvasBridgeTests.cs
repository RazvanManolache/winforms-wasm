using System.Text.Json;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

public class CanvasBridgeTests
{
    private static JsonElement SerializeScene()
    {
        var recorder = new RecordingRenderer();
        recorder.BeginFrame(800, 450);
        recorder.FillRectangle(0, 0, 800, 40, 0x0078D7FFu);
        recorder.FillRoundedRectangle(10, 20, 100, 80, 15, 0xFFD2DCFFu);
        recorder.DrawText("Hello", 100, 100, "16px sans-serif", 0x1E1E1EFFu);
        recorder.EndFrame();

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void SerializeCommands_ProducesOneOpPerPaintCommand()
    {
        JsonElement ops = SerializeScene();

        Assert.Equal(JsonValueKind.Array, ops.ValueKind);
        Assert.Equal(3, ops.GetArrayLength());
        Assert.Equal(1, ops[0][0].GetInt32());
        Assert.Equal(2, ops[1][0].GetInt32());
        Assert.Equal(3, ops[2][0].GetInt32());
    }

    [Fact]
    public void SerializeCommands_FillRectOpShape()
    {
        JsonElement op = SerializeScene()[0];

        Assert.Equal(800, op[3].GetDouble(), 0);
        Assert.Equal(40, op[4].GetDouble(), 0);
        Assert.Equal(CanvasBridge.Css(0x0078D7FFu), op[5].GetString());
    }

    [Fact]
    public void SerializeCommands_TextOpShape()
    {
        JsonElement op = SerializeScene()[2];

        Assert.Equal("Hello", op[1].GetString());
        Assert.Equal(100, op[2].GetDouble(), 0);
        Assert.Equal("16px sans-serif", op[4].GetString());
    }

    [Fact]
    public void SerializeCommands_GradientOpShape()
    {
        var recorder = new RecordingRenderer();
        recorder.BeginFrame(100, 100);
        recorder.FillGradientVertical(0, 0, 100, 20, 0x000080FFu, 0x1084D0FFu);
        recorder.EndFrame();

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement ops = doc.RootElement;
        Assert.Equal(1, ops.GetArrayLength());
        Assert.Equal(4, ops[0][0].GetInt32());
        Assert.Equal("#000080ff", ops[0][5].GetString());
        Assert.Equal("#1084d0ff", ops[0][6].GetString());
    }

    [Fact]
    public void SerializeCommands_TextCarriesAlignAndMnemonic()
    {
        var recorder = new RecordingRenderer();
        recorder.BeginFrame(100, 100);
        recorder.DrawTextAligned("Hi", 50, 10, "13px sans-serif", 0xFFFFFFFFu,
            TextHAlign.Center, TextVAlign.Middle, mnemonicIndex: 1);
        recorder.EndFrame();

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement op = doc.RootElement[0];
        Assert.Equal(3, op[0].GetInt32());
        Assert.Equal(1, op[6].GetInt32());
        Assert.Equal(2, op[7].GetInt32());
        Assert.Equal(1, op[8].GetInt32());
    }

    [Fact]
    public void SerializeCommands_LineAndEllipseOpShapes()
    {
        var recorder = new RecordingRenderer();
        recorder.BeginFrame(100, 100);
        recorder.DrawLine(1, 2, 30, 40, 3, 0xFF0000FFu);
        recorder.DrawEllipse(10, 10, 40, 20, 2, 0x000000FFu, null);
        recorder.EndFrame();

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement ops = doc.RootElement;
        Assert.Equal(2, ops.GetArrayLength());
        Assert.Equal(5, ops[0][0].GetInt32());
        Assert.Equal("#ff0000ff", ops[0][6].GetString());
        Assert.Equal(6, ops[1][0].GetInt32());
        Assert.Equal(JsonValueKind.Null, ops[1][7].ValueKind);
    }

    [Fact]
    public void SerializeCommands_ImageOpShape()
    {
        // 2x2 opaque red.
        byte[] rgba = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        var recorder = new RecordingRenderer();
        recorder.BeginFrame(100, 100);
        recorder.DrawImage(rgba, 2, 2, 10, 20, 4, 4);
        recorder.EndFrame();

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement ops = doc.RootElement;
        Assert.Equal(1, ops.GetArrayLength());
        JsonElement op = ops[0];
        Assert.Equal(7, op[0].GetInt32());
        Assert.Equal(10, op[1].GetDouble(), 0);
        Assert.Equal(20, op[2].GetDouble(), 0);
        Assert.Equal(4, op[3].GetDouble(), 0);
        Assert.Equal(4, op[4].GetDouble(), 0);
        Assert.Equal(2, op[5].GetInt32());
        Assert.Equal(2, op[6].GetInt32());
        Assert.Equal(rgba, Convert.FromBase64String(op[7].GetString()!));
    }

    [Fact]
    public void Css_FormatsRgbaAsEightHexDigits()
    {
        Assert.Equal("#0078d7ff", CanvasBridge.Css(0x0078D7FFu));
        Assert.Equal("#00000000", CanvasBridge.Css(0u));
    }

    [Fact]
    public void SerializeCommands_NullInputThrows()
    {
        Assert.Throws<ArgumentNullException>(() => CanvasBridge.SerializeCommands(null!));
    }

    [Fact]
    public void SerializeCommands_DefBitmapCarriesNoPixels()
    {
        var recorder = new RecordingRenderer();
        recorder.DefBitmap("L1", new byte[] { 1, 2, 3, 4 }, 1, 1);

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement op = doc.RootElement[0];
        Assert.Equal(8, op[0].GetInt32());
        Assert.Equal("L1", op[1].GetString());
        Assert.Equal(1, op[2].GetInt32());
        Assert.Equal(1, op[3].GetInt32());
        Assert.Equal(4, op.GetArrayLength());
    }

    [Fact]
    public void SerializeCommands_BlitOpShape()
    {
        var recorder = new RecordingRenderer();
        recorder.BlitBitmap("L1", 10, 20, 30, 40);

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement op = doc.RootElement[0];
        Assert.Equal(9, op[0].GetInt32());
        Assert.Equal("L1", op[1].GetString());
        Assert.Equal(10, op[2].GetDouble(), 0);
        Assert.Equal(20, op[3].GetDouble(), 0);
        Assert.Equal(30, op[4].GetDouble(), 0);
        Assert.Equal(40, op[5].GetDouble(), 0);
    }

    [Fact]
    public void SerializeCommands_RetainOpShape()
    {
        var recorder = new RecordingRenderer();
        recorder.RetainLayers(new[] { "L1", "L2" });

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement op = doc.RootElement[0];
        Assert.Equal(10, op[0].GetInt32());
        Assert.Equal(2, op[1].GetArrayLength());
        Assert.Equal("L1", op[1][0].GetString());
        Assert.Equal("L2", op[1][1].GetString());
    }

    [Fact]
    public void SplitUploads_ExtractsDefsInOrderAndStripsThem()
    {
        var recorder = new RecordingRenderer();
        recorder.FillRectangle(0, 0, 4, 4, 0xFFFFFFFFu);
        recorder.DefBitmap("L1", new byte[] { 1, 2, 3, 4 }, 1, 1);
        recorder.BlitBitmap("L1", 0, 0, 1, 1);
        recorder.DefBitmap("L2", new byte[] { 5, 6, 7, 8 }, 1, 1);

        var (json, uploads) = CanvasBridge.SplitUploads(recorder.Commands);
        Assert.Equal(2, uploads.Count);
        Assert.Equal("L1", uploads[0].LayerId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, uploads[0].Rgba);
        Assert.Equal("L2", uploads[1].LayerId);
        Assert.DoesNotContain(json, c => c is DefBitmapCommand);
        Assert.Equal(2, json.Count);
    }

    [Fact]
    public void SerializeCommands_PatchOpShapeCarriesNoPixels()
    {
        var recorder = new RecordingRenderer();
        recorder.PatchBitmap("L1", 3, 4, 2, 2, new byte[16]);

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(recorder.Commands));
        JsonElement op = doc.RootElement[0];
        Assert.Equal(11, op[0].GetInt32());
        Assert.Equal("L1", op[1].GetString());
        Assert.Equal(3, op[2].GetInt32());
        Assert.Equal(4, op[3].GetInt32());
        Assert.Equal(2, op[4].GetInt32());
        Assert.Equal(2, op[5].GetInt32());
        Assert.Equal(6, op.GetArrayLength());
    }

    [Fact]
    public void SplitUploads_ExtractsPatchesWithOffsetsAndStripsThem()
    {
        var recorder = new RecordingRenderer();
        recorder.DefBitmap("L1", new byte[] { 1, 2, 3, 4 }, 1, 1);
        recorder.PatchBitmap("L1", 5, 6, 1, 1, new byte[] { 9, 9, 9, 9 });
        recorder.BlitBitmap("L1", 0, 0, 1, 1);

        var (json, uploads) = CanvasBridge.SplitUploads(recorder.Commands);
        Assert.Equal(2, uploads.Count);
        Assert.False(uploads[0].IsPatch);
        Assert.True(uploads[1].IsPatch);
        Assert.Equal("L1", uploads[1].LayerId);
        Assert.Equal(5, uploads[1].X);
        Assert.Equal(6, uploads[1].Y);
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, uploads[1].Rgba);
        Assert.DoesNotContain(json, c => c is PatchBitmapCommand);
        Assert.Single(json);
    }
}
