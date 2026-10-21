using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using Xunit;

// Proposed strict-mode contract. No production implementation is added here.
// A software-only target must fail closed before invoking application painting
// when GPU rendering was required. Explicit CPU compatibility mode still works.
public sealed class GpuRenderingPolicyTests
{
    private const string RequireGpu = "WfWasm.RequireGpuRendering";

    private sealed class PaintCounter : System.Windows.Forms.Control
    {
        public int Calls;
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            Calls++;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);
            e.Graphics.FillRectangle(brush, 2, 2, 16, 16);
        }
    }

    [Fact]
    public void Required_Gpu_Rejects_Software_Target_Before_Running_Application_Paint()
    {
        WithControl((host, control) =>
        {
            AppContext.SetSwitch(RequireGpu, true);
            // This target has no device surfaces or WebGL presentation path.
            using var target = new SoftwareTarget();
            var error = Assert.Throws<NotSupportedException>(() => host.Paint(target));
            Assert.Contains("GPU", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, target.Operations);
            Assert.Equal(0, control.Calls);
        });
    }

    [Fact]
    public void Explicit_Cpu_Compatibility_Mode_Still_Runs_Application_Paint()
    {
        WithControl((host, control) =>
        {
            AppContext.SetSwitch(RequireGpu, false);
            using var target = new SoftwareTarget();
            host.Paint(target);
            Assert.True(target.Operations > 0);
            Assert.True(control.Calls > 0);
        });
    }

    private static void WithControl(Action<WinFormsAppHost, PaintCounter> test)
    {
        AppContext.TryGetSwitch(RequireGpu, out bool previous);
        try
        {
            WinFormsExecution.Run(() =>
            {
                using var host = new WinFormsAppHost();
                host.SelectScene(AppScene.Smoke);
                var form = host.GetOrCreateForm(AppScene.Smoke);
                Assert.NotNull(form);
                using var counter = new PaintCounter { Width = 32, Height = 32 };
                form.Controls.Add(counter);
                try { test(host, counter); }
                finally { form.Controls.Remove(counter); }
            });
        }
        finally
        {
            AppContext.SetSwitch(RequireGpu, previous);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    // Intentionally has no GPU capability. Real drawing work must not start
    // before strict-mode capability validation has succeeded.
    private sealed class SoftwareTarget : ICanvasRenderer, IDisposable
    {
        public int Operations;
        public void BeginFrame(int w, int h) => Operations++;
        public void EndFrame() => Operations++;
        public void FillRectangle(float x, float y, float w, float h, uint rgba) => Operations++;
        public void FillRoundedRectangle(float x, float y, float w, float h, float r, uint rgba) => Operations++;
        public void FillGradientVertical(float x, float y, float w, float h, uint top, uint bottom) => Operations++;
        public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba) => Operations++;
        public void DrawEllipse(float x, float y, float w, float h, float lw, uint outline, uint? fill = null) => Operations++;
        public void DrawText(string text, float x, float y, string font, uint rgba) => Operations++;
        public void DrawTextAligned(string text, float x, float y, string font, uint rgba,
            TextHAlign h, TextVAlign v, int mnemonicIndex = -1) => Operations++;
        public void DrawImage(byte[] rgba, int w, int h, float x, float y, float dw, float dh) => Operations++;
        public void Dispose() { }
    }
}
