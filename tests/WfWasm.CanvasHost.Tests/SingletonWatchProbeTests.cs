using System.Collections.Concurrent;
using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using Xunit;

// TRANSIENT diagnostic (delete after use): after each stroke, snapshot the
// VisualInvalidated ThreadLocal instantiation's static singleton liveness.
// If a stroke nulls s_idManager (or its fields), that stroke runs the writer.
// If null from stroke 0, the singleton was never published (runtime mystery).
namespace WfWasm.CanvasHost.Tests;

[Collection("Paint")]
public sealed class SingletonWatchProbeTests
{
    private static readonly System.Collections.Generic.Dictionary<string, object> s_seen = new();

    private readonly WinFormsAppHost _host;

    public SingletonWatchProbeTests(PaintHostFixture fx)
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

    private static string SingletonState()
    {
        try
        {
            Type? evt = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType("PaintDotNet.UI.VisualInvalidatedEventArgs"); } catch { return null; } })
                .FirstOrDefault(t => t != null);
            if (evt == null)
                return "no-event-type";
            // Closed nested type by name: GetNestedType on a constructed generic
            // can come back with open parameters; the assembly-qualified form
            // resolves closed.
            Type? ws = Type.GetType(
                "System.Collections.Concurrent.ConcurrentBag`1+WorkStealingQueue[[" +
                evt.AssemblyQualifiedName + "]], System.Collections.Concurrent");
            if (ws == null)
                return "no-wsq-type";
            Type tl = typeof(ThreadLocal<>).MakeGenericType(ws);
            var sm = tl.GetField("s_idManager", BindingFlags.NonPublic | BindingFlags.Static);
            var sl = tl.GetField("s_idManagerLock", BindingFlags.NonPublic | BindingFlags.Static);
            object? mgr = sm?.GetValue(null);
            object? lck = sl?.GetValue(null);
            if (mgr == null)
                return $"mgr=NULL lock={(lck == null ? "NULL" : "live")}";
            static object? Inner(object? o, string f)
            {
                if (o == null)
                    return null;
                return o.GetType().GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
            }
            object? map = Inner(mgr, "_usedIdToTracksAllValuesMap");
            object? free = Inner(mgr, "_freeIds");
            object? flock = Inner(mgr, "_freeIdsLock");
            // Inner arrays: the NRE site dereferences these, not just the shells.
            object? buckets = Inner(map, "_buckets");
            object? entries = Inner(map, "_entries");
            object? items = Inner(free, "_items");
            // Identity: none of these references may EVER change after the
            // static constructor (no shrink/replace paths). A change — or a
            // null — at some mark pins the corrupting call.
            string Id(string n, object? o)
            {
                if (o == null)
                    return n + "=NULL!";
                if (!s_seen.TryGetValue(n, out var first))
                {
                    s_seen[n] = o;
                    return n + "=new";
                }
                return ReferenceEquals(first, o) ? n + "=same" : n + "=CHANGED!";
            }
            return Id("mgr", mgr) + " " + Id("map", map) + " " + Id("free", free) + " " +
                Id("flock", flock) + " " + Id("bk", buckets) + " " + Id("en", entries) + " " + Id("it", items);
        }
        catch (Exception ex)
        {
            return "ERR:" + ex.GetType().Name + ":" + (ex.Message ?? "").Substring(0, Math.Min(160, (ex.Message ?? "").Length));
        }
    }

    [Fact]
    public void ProbeC_Strokes_With_Singleton_Watch()
    {
        File.AppendAllText("E:/tmp/probe-singleton.txt", $"boot: {SingletonState()}{System.Environment.NewLine}");
        var host = _host;
        host.Paint(new RecordingRenderer());
        for (int i = 0; i < 20; i++)
        {
            string tool = InvokePrivate<string>(host, "GetActiveDocToolName");
            if (tool.StartsWith("PaintBrushTool", StringComparison.Ordinal))
                break;
            InvokePrivate<object?>(host, "CycleNextTool");
            host.Paint(new RecordingRenderer());
            Thread.Sleep(25);
        }
        File.AppendAllText("E:/tmp/probe-singleton.txt", $"tooled: {SingletonState()}{System.Environment.NewLine}");
        for (int stroke = 0; stroke < 10; stroke++)
        {
            Action<string> mark = where => File.AppendAllText("E:/tmp/probe-singleton.txt",
                $"s{stroke} {where}: {SingletonState()}{System.Environment.NewLine}");
            mark("pre-paint");
            host.Paint(new RecordingRenderer());
            dynamic bounds = host.GetType()
                .GetField("_documentPageBounds", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(host)!;
            int x0 = bounds.X + bounds.Width / 3;
            int y0 = bounds.Y + bounds.Height / 3;
            mark("pre-down");
            host.OnMouseDown(x0, y0);
            for (int i = 1; i <= 12; i++)
            {
                host.OnMouseMove(x0 + (i * bounds.Width) / 36, y0 + (i * bounds.Height) / 72);
                if (i % 4 == 0)
                    mark($"move{i}");
            }
            mark("pre-up");
            host.OnMouseUp(x0 + bounds.Width / 3, y0 + bounds.Height / 6);
            mark("post-up");
        }
        File.AppendAllText("E:/tmp/probe-singleton.txt", "DONE\n");
    }
}
