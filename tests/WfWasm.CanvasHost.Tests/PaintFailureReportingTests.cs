using System.Reflection;
using Microsoft.JSInterop;
using WfWasm.CanvasHost;
using WfWasm.CanvasHost.Pages;
using WfWasm.Shim;

// Component seam, not a source-text test: execute the real Home paint/pump.
// A caller-visible exception or preview.failed are both valid failure channels.
// Silence is never successful rendering, even with diagnostics disabled.
public sealed class PaintFailureReportingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictGpu_Repaint_Failure_Is_Observable_With_Or_Without_Timeline(bool timeline)
    {
        using var fixture = new Fixture(timeline);
        Exception? error = await Record.ExceptionAsync(() => fixture.Invoke("RepaintAsync"));
        Assert.True(error is not null || fixture.JS.Calls.Contains("preview.failed"),
            "Home swallowed the required-GPU paint failure without notifying its caller or shell");
        Assert.DoesNotContain("preview.ready", fixture.JS.Calls);
        Assert.DoesNotContain("wfwasm.paint", fixture.JS.Calls);
    }

    [Fact]
    public async Task Repaint_Pump_Does_Not_Hide_A_StrictGpu_Frame_Failure()
    {
        using var fixture = new Fixture(false);
        SetField(fixture.Component, "_repaintDirty", true);
        SetField(fixture.Component, "_repaintRunning", true);
        Exception? error = await Record.ExceptionAsync(() => fixture.Invoke("PumpRepaintsAsync"));
        Assert.True(error is not null || fixture.JS.Calls.Contains("preview.failed"),
            "The pump consumed a rendering failure and made it indistinguishable from an idle frame");
        Assert.DoesNotContain("preview.ready", fixture.JS.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _gpu, _timeline;
        public Home Component { get; } = new();
        public JsRecorder JS { get; } = new();
        public Fixture(bool timeline)
        {
            AppContext.TryGetSwitch("WfWasm.RequireGpuRendering", out _gpu);
            _timeline = PerfTimeline.Enabled;
            PerfTimeline.Enabled = timeline;
            AppContext.SetSwitch("WfWasm.RequireGpuRendering", true);
            typeof(Home).GetProperty("JS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(Component, JS);
            typeof(Home).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(Component, new SceneState());
            SetField(Component, "_hostScene", AppScene.Smoke);
        }
        public async Task Invoke(string method) => await ((Task)typeof(Home).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Component, null)!);
        public void Dispose()
        {
            SetField(Component, "_disposed", true);
            ((WinFormsAppHost)typeof(Home).GetField("_winFormsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Component)!).Dispose();
            AppContext.SetSwitch("WfWasm.RequireGpuRendering", _gpu);
            PerfTimeline.Enabled = _timeline;
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }
    private static void SetField(object value, string field, object data) => typeof(Home)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
    public sealed class JsRecorder : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) { Calls.Add(identifier); return ValueTask.FromResult(default(T)!); }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
