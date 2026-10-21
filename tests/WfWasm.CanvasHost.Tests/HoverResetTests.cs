using System.Net;
using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

namespace WfWasm.CanvasHost.Tests;

public sealed class HoverResetTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Bodies { get; } = [];
        public string[] SnapshotBodies()
        {
            lock (_gate)
            {
                return Bodies.ToArray();
            }
        }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate)
            {
                Bodies.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static T? GetPrivate<T>(object target, string name) where T : class
    {
        FieldInfo? field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field.GetValue(target);
    }

    private static FormsButton FindFirstButton(FormsControl root)
    {
        foreach (FormsControl c in root.Controls)
        {
            if (c is FormsButton btn)
                return btn;
            if (c.Controls.Count > 0)
            {
                try { return FindFirstButton(c); }
                catch (InvalidOperationException) { }
            }
        }

        throw new InvalidOperationException("No button found.");
    }

    private static (int X, int Y) CenterOn(FormsForm form, FormsControl control)
    {
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = control; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }

        return (absX + (control.Width / 2), absY + (control.Height / 2));
    }

    [Fact]
    public void SelectScene_Clears_Hover_And_Drag_State()
    {
        // FIX3/G6: SelectScene disposes other scenes' forms; hover/drag
        // entries must not survive, or the next move raises enter/leave
        // on disposed controls (Telerik RadControl.OnMouseLeave NRE).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm smoke = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(smoke);
        FormsButton button = FindFirstButton(smoke);
        var (x, y) = CenterOn(smoke, button);
        host.OnMouseMove(x, y);
        FormsControl? hovered = GetPrivate<FormsControl>(host, "_hoverControl");
        Assert.NotNull(hovered);
        bool leaveRaised = false;
        hovered.MouseLeave += (_, _) => leaveRaised = true;

        // Same-scene reselect runs the same reset block (only Smoke is
        // headless-local; Paint hangs in tests, small apps need the
        // server). Cross-scene disposal is covered by browser smokes.
        host.SelectScene(AppScene.Smoke);
        Assert.Null(GetPrivate<FormsControl>(host, "_hoverControl"));
        Assert.Null(GetPrivate<object>(host, "_hoverItem"));
        Assert.Null(GetPrivate<object>(host, "_dragForm"));
        Assert.Null(GetPrivate<FormsControl>(host, "_mouseTarget"));

        // End to end: the next move raises no leave on the previously
        // hovered control (its entry was cleared, not left stale).
        host.OnMouseMove(smoke.Location.X + 8, smoke.Location.Y + 38);
        Assert.False(leaveRaised);
    }

    [Fact]
    public void RaiseSimpleEvent_Skips_Disposed_Control()
    {
        // FIX3/G6: desktop never delivers events to disposed controls.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm smoke = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(smoke);
        Assert.NotNull(GetPrivate<object>(host, "_activeForm"));
        FormsButton button = FindFirstButton(smoke);
        bool leaveRaised = false;
        button.MouseLeave += (_, _) => leaveRaised = true;

        MethodInfo? raise = typeof(WinFormsAppHost).GetMethod("RaiseSimpleEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(raise);
        FieldInfo? leaveMethod = typeof(WinFormsAppHost).GetField("s_onMouseLeaveMethod", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(leaveMethod);
        var method = (MethodInfo?)leaveMethod.GetValue(null);
        Assert.NotNull(method);

        // Control case: live control raises.
        raise.Invoke(host, new object?[] { method, button });
        Assert.True(leaveRaised);

        // Disposed control skipped.
        button.Dispose();
        leaveRaised = false;
        raise.Invoke(host, new object?[] { method, button });
        Assert.False(leaveRaised);

        // Non-vacuity: the fork fires MouseLeave on disposed controls
        // when invoked directly, so the skip above is load-bearing.
        method.Invoke(button, new object?[] { EventArgs.Empty });
        Assert.True(leaveRaised);
    }

    [Fact]
    public async Task RaiseSimpleEvent_Reports_Hover_Errors_With_Site()
    {
        // FIX3/G6: hover-raise failures report (rate-limited) with the
        // throw site, so the next Telerik-internal NRE carries a stack.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.Smoke);
            FormsForm smoke = host.GetOrCreateForm(AppScene.Smoke);
            Assert.NotNull(smoke);
            FormsButton button = FindFirstButton(smoke);
            button.MouseLeave += (_, _) => throw new InvalidOperationException("FIX3 hover probe 7f3a");

            MethodInfo? raise = typeof(WinFormsAppHost).GetMethod("RaiseSimpleEvent", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(raise);
            FieldInfo? leaveMethod = typeof(WinFormsAppHost).GetField("s_onMouseLeaveMethod", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(leaveMethod);
            raise.Invoke(host, new object?[] { (MethodInfo?)leaveMethod.GetValue(null), button });

            for (int i = 0; i < 100 && !handler.SnapshotBodies().Any(b => b.Contains("FIX3 hover probe 7f3a") && b.Contains("\"source\":\"hover\"")); i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("FIX3 hover probe 7f3a") && b.Contains("\"source\":\"hover\""));
            Assert.Contains("hover", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }
}
