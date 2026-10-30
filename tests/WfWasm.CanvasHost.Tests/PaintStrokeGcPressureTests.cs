using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

namespace WfWasm.CanvasHost.Tests;

// GC-pressure variant of the stroke-commit contract: the scratch/context
// handles used by a stroke commit must survive finalizer runs between
// strokes. The full-suite fatal (ExecutionEngineException in
// CpuPaint.FromHandle via DrawBitmap during OnMouseUp commit) and the
// intermittent managed "no ink committed" failure point at a freed handle
// being reused under GC pressure. Forcing collection + finalizer drain
// between strokes should surface either manifestation deterministically.
[Collection("Paint")]
public sealed class PaintStrokeGcPressureTests
{
    private readonly WinFormsAppHost _host;

    public PaintStrokeGcPressureTests(PaintHostFixture fx)
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
    public void Repeated_Strokes_Survive_Gc_And_Finalizers()
    {
        var host = _host;
        // Synchronous sleep, never await: PDN tool activation is
        // thread-affine (VerifyAccess, desktop single-UI-thread parity),
        // and awaiting hops the continuation to another pool thread, after
        // which every SetToolFromType throws (swallowed by CycleNextTool)
        // and the tool never advances past RectangleSelect.
        host.Paint(new RecordingRenderer());
        string tool = "";
        for (int i = 0; i < 20; i++)
        {
            tool = InvokePrivate<string>(host, "GetActiveDocToolName");
            if (tool.StartsWith("PaintBrushTool", StringComparison.Ordinal))
                break;
            InvokePrivate<object?>(host, "CycleNextTool");
            host.Paint(new RecordingRenderer());
            Thread.Sleep(25);
        }

        Assert.StartsWith("PaintBrushTool", tool);

        for (int stroke = 0; stroke < 10; stroke++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

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
            Assert.True(custom > 0, $"stroke {stroke}: no ink committed (surface={desc})");
        }
    }
}
