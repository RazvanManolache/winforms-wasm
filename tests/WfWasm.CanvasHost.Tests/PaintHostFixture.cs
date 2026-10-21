using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using Xunit;

namespace WfWasm.CanvasHost.Tests;

// Paint.NET is a genuine single-instance app: a second MainForm in one
// process starts degraded (blocked doc/tool init), while disposing the
// first tears down process statics (color profiles, device contexts)
// later scenes reuse. The browser runs one PDN app per session, so the
// suite shares one too: this fixture boots a single Paint host and waits
// for form+workspace+tool readiness before any Paint test runs.
public sealed class PaintHostFixture : IDisposable
{
    public WinFormsAppHost Host { get; } = new();

    public PaintHostFixture()
    {
        Host.SelectScene(AppScene.Paint);
        Host.Paint(new RecordingRenderer());
        // xunit constructs fixtures synchronously: block (bounded) until
        // the phased PDN startup settles (form, workspace, active tool).
        for (int i = 0; i < 200; i++)
        {
            var form = Host.GetOrCreateForm(AppScene.Paint);
            if (form is not null && HasAppWorkspace(form))
            {
                string tool = InvokePrivate<string>(Host, "GetActiveDocToolName");
                if (!string.IsNullOrEmpty(tool) && tool != "null")
                    break;
            }
            Host.Paint(new RecordingRenderer());
            Thread.Sleep(25);
        }
    }

    public void Dispose()
    {
        // Hide, don't dispose: tearing down the shared PDN app poisons
        // process statics; the OS reclaims at process exit. Hide prunes
        // routing (invisible entries drop on snapshot).
        try
        {
            var forms = Host.GetType()
                .GetField("_forms", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(Host);
            if (forms is System.Collections.Generic.Dictionary<AppScene, System.Windows.Forms.Form> dict)
                foreach (var form in dict.Values)
                    try { form.Visible = false; }
                    catch { }
        }
        catch { }
    }

    private static bool HasAppWorkspace(System.Windows.Forms.Control c)
    {
        if (c.GetType().FullName == "PaintDotNet.Controls.AppWorkspace") return true;
        foreach (System.Windows.Forms.Control ch in c.Controls) if (HasAppWorkspace(ch)) return true;
        return false;
    }

    private static T InvokePrivate<T>(object target, string name)
    {
        var m = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(m);
        return (T)m.Invoke(target, null)!;
    }
}

[CollectionDefinition("Paint")]
public sealed class PaintCollection : ICollectionFixture<PaintHostFixture>;
