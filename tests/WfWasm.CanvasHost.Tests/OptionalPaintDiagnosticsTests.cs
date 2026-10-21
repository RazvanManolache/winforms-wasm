using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using Xunit;

// Proposed opt-in contract for the implementation agent. AppContext.SetSwitch
// exists already, so these compile and expose today's unwanted logging.
// Existing TestCollections.cs serializes tests that change process globals.
public sealed class OptionalPaintDiagnosticsTests
{
    private const string Switch = "WfWasm.PaintDiagnostics";
    private static readonly string[] Tags = ["[DOCK-", "[Paint]", "[CHROME]", "[GRID-"];

    public sealed class DocumentTabStrip : System.Windows.Forms.Control
    {
        public int InspectionReads;
        // The production diagnostic walker recognizes this type/property.
        // Rendering this ordinary Control does not need this property.
        public object? TabStripElement { get { InspectionReads++; return null; } }
    }

    [Fact]
    public void Disabled_Diagnostics_Are_Quiet_While_Forced_Frames_Still_Render()
    {
        WithHost(host =>
        {
            AppContext.SetSwitch(Switch, false);
            string output = CaptureFrames(host, 3);
            foreach (string tag in Tags) Assert.DoesNotContain(tag, output);
        });
    }

    [Fact]
    public void Explicit_OptIn_Allows_Dock_Diagnostics_On_A_Painted_Frame()
    {
        WithHost(host =>
        {
            AppContext.SetSwitch(Switch, true);
            Assert.Contains("[DOCK-TABS]", CaptureFrames(host, 1));
        });
    }

    [Fact]
    public void Disabling_Diagnostics_Again_Stops_Inspection_Logs()
    {
        WithHost(host =>
        {
            AppContext.SetSwitch(Switch, true);
            Assert.Contains("[DOCK-TABS]", CaptureFrames(host, 1));
            AppContext.SetSwitch(Switch, false);
            string output = CaptureFrames(host, 2);
            foreach (string tag in Tags) Assert.DoesNotContain(tag, output);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptIn_Gates_The_Inspection_Work_Not_Just_Its_Console_Output(bool enabled)
    {
        WithHost(host =>
        {
            var form = host.GetOrCreateForm(AppScene.Smoke);
            Assert.NotNull(form);
            using var strip = new DocumentTabStrip { Width = 24, Height = 24 };
            form.Controls.Add(strip);
            try
            {
                AppContext.SetSwitch(Switch, enabled);
                strip.InspectionReads = 0;
                CaptureFrames(host, 1);
                if (enabled) Assert.True(strip.InspectionReads > 0);
                else Assert.Equal(0, strip.InspectionReads);
            }
            finally { form.Controls.Remove(strip); }
        });
    }

    private static string CaptureFrames(WinFormsAppHost host, int count)
    {
        TextWriter previous = Console.Out;
        using var capture = new StringWriter();
        try
        {
            Console.SetOut(capture);
            for (int i = 0; i < count; i++)
            {
                var form = host.GetOrCreateForm(AppScene.Smoke);
                Assert.NotNull(form);
                System.Windows.Forms.WasmRepaintGate.MarkDirty(form);
                var recorder = new RecordingRenderer();
                host.Paint(recorder);
                // A skipped idle frame must not make the quiet assertion pass.
                Assert.NotEmpty(recorder.Commands);
            }
        }
        finally { Console.SetOut(previous); }
        return capture.ToString();
    }

    private static void WithHost(Action<WinFormsAppHost> test)
    {
        AppContext.TryGetSwitch(Switch, out bool previous);
        try
        {
            WinFormsExecution.Run(() =>
            {
                using var host = new WinFormsAppHost();
                host.SelectScene(AppScene.Smoke);
                test(host);
            });
        }
        finally
        {
            AppContext.SetSwitch(Switch, previous);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }
}
