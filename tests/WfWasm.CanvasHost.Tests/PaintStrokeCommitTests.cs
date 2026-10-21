using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

namespace WfWasm.CanvasHost.Tests;

// A brush drag must commit visible ink to the active layer surface.
// Regression cover: input delivery (DocPress/moves/release) can succeed
// with zero errors while the layer stays blank (broken commit or broken
// surface binding), which is exactly what "can't draw" looks like.
[Collection("Paint")]
public sealed class PaintStrokeCommitTests
{
    private readonly WinFormsAppHost _host;

    public PaintStrokeCommitTests(PaintHostFixture fx)
    {
        _host = fx.Host;
    }

    private static T InvokePrivate<T>(object target, string name, object?[]? args = null)
    {
        var m = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(m);
        return (T)m.Invoke(target, args)!;
    }

    [Fact]
    public async Task BrushDrag_Commits_Ink_To_Active_Layer()
    {
        var host = _host;
        // Fixture pre-waits for tool readiness; re-check briefly (paint
        // settles tool init) in case collection ordering ever shifts.
        host.Paint(new RecordingRenderer());
        string tool = "";
        for (int i = 0; i < 20; i++)
        {
            tool = InvokePrivate<string>(host, "GetActiveDocToolName");
            if (tool.StartsWith("PaintBrushTool", StringComparison.Ordinal))
                break;
            InvokePrivate<object?>(host, "CycleNextTool");
            host.Paint(new RecordingRenderer());
            await Task.Delay(25);
        }

        Assert.StartsWith("PaintBrushTool", tool);

        host.Paint(new RecordingRenderer());
        dynamic bounds = host.GetType()
            .GetField("_documentPageBounds", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host)!;
        Assert.True(bounds.Width > 100 && bounds.Height > 100);

        int x0 = bounds.X + bounds.Width / 3;
        int y0 = bounds.Y + bounds.Height / 3;
        host.OnMouseDown(x0, y0);
        for (int i = 1; i <= 12; i++)
            host.OnMouseMove(x0 + (i * bounds.Width) / 36, y0 + (i * bounds.Height) / 72);
        host.OnMouseUp(x0 + bounds.Width / 3, y0 + bounds.Height / 6);

        string desc = InvokePrivate<string>(host, "DescribeActiveDocSurface");
        int custom = int.Parse(desc.Split("custom=")[1]);
        Assert.True(custom > 0, $"no ink committed to layer (surface={desc})");
    }
}
