using Mono.Cecil;
using System.Reflection;
using System.Runtime.Loader;

// Scratch ownership across drawing, disposal and finalization (P0:
// brush strokes died in CpuCommandList.Finalize -> FreeScratchTarget ->
// FromHandle<Target> with InvalidCastException, then runtime exit 255).
//
// Ownership policy under test: a redirect scratch belongs to the command
// list (consumed on DrawImage via blit+free+zero, else freed exactly once
// by the list finalizer), never to the drawing context. In particular a
// DC disposed while redirected must DETACH (restore its own pre-redirect
// target, clear the redirect) and must NOT free the list scratch; the
// stale handle otherwise recycles and kills the finalizer thread.
public sealed class CpuOwnershipTests
{
    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static string BinDll(string project, string tfm, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", tfm, name + ".dll"));
        Assert.True(File.Exists(dll), "dependency not built: " + dll);
        return dll;
    }

    // The whole scenario runs in a nested frame that has returned before
    // collection: Debug extends local lifetime to the end of the enclosing
    // frame (including reflection-invoke arg arrays rooting ctx/list), so
    // collecting in-frame leaves objects alive and their finalizers fire
    // after Unload (host crash instead of a test). The scenario returns
    // weak refs; the caller drains + asserts in its own frame.
    private static (WeakReference, WeakReference) RedirectDisposeCollect(
        System.Reflection.Assembly core, System.Reflection.Assembly cpu)
    {
        var ctxT = core.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
        var listT = core.GetType("PaintDotNet.Direct2D1.CpuCommandList");
        Assert.NotNull(ctxT);
        Assert.NotNull(listT);

        var paint = cpu.GetType("WfWasm.PdnCpu.CpuPaint");
        Assert.NotNull(paint);
        var createTarget = paint.GetMethod("CreateScratchTarget",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createTarget);

        object? ctx = Activator.CreateInstance(ctxT, new object?[] { 0L });
        Assert.NotNull(ctx);
        object? list = Activator.CreateInstance(listT);
        Assert.NotNull(list);

        // Live target: scratch-sized surface (real pinned buffer behind it).
        long live = (long)createTarget.Invoke(null, new object?[] { 64, 64 })!;
        Assert.True(live != 0, "live target not allocated");
        var targetF = ctxT.GetField("_target",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var redirF = ctxT.GetField("_redirList",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var scratchF = listT.GetField("_scratch",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(targetF);
        Assert.NotNull(redirF);
        Assert.NotNull(scratchF);
        targetF.SetValue(ctx, live);

        var setTarget = ctxT.GetMethod("SetTarget");
        Assert.NotNull(setTarget);
        setTarget.Invoke(ctx, new object?[] { list });
        long scratch = (long)scratchF.GetValue(list)!;
        Assert.True(scratch != 0, "redirect did not allocate a scratch");
        Assert.Equal(scratch, (long)targetF.GetValue(ctx)!);

        // Dispose WHILE redirected: must detach, never steal the scratch.
        ((IDisposable)ctx).Dispose();
        Assert.Null(redirF.GetValue(ctx));
        Assert.Equal(live, (long)targetF.GetValue(ctx)!);
        Assert.Equal(scratch, (long)scratchF.GetValue(list)!);

        // Drop every reference; the caller's frame drains (this frame's
        // reflection-invoke temps die on return).
        var wctx = new WeakReference(ctx);
        var wlist = new WeakReference(list);
        ctx = null;
        list = null;
        return (wctx, wlist);
    }

    // Caller-frame drain: the scenario frame's temps are dead here, so a
    // surviving weak ref proves a real root (leak), not Debug lifetime.
    private static void CallerDrain((WeakReference, WeakReference) refs)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(refs.Item1.IsAlive, "disposed redirect dc survived collection (leak/root)");
        Assert.False(refs.Item2.IsAlive, "list survived collection (leak/root)");
    }

    [Fact]
    public void Dispose_Detaches_Redirect_Without_Freeing_List_Scratch()
    {
        string paintDir = PaintDir();
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        string forkDll = @"E:\tmp\rewritten-small\WfWasm.WinForms.dll";
        string drawingDll = BinDll("WfWasm.Drawing", "net9.0", "WfWasm.Drawing");
        string corePath = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        Assert.True(File.Exists(corePath), "pristine PDN core missing: " + corePath);
        string tempOut = Path.Combine(Path.GetTempPath(), $"CpuOwn_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var alc = new AssemblyLoadContext($"CpuOwn_{Guid.NewGuid()}", true);
            try
            {
                string cpuDir = Path.GetDirectoryName(cpuDll)!;
                string forkDir = Path.GetDirectoryName(forkDll)!;
                string drawingDir = Path.GetDirectoryName(drawingDll)!;
                alc.Resolving += (ctx, name) =>
                {
                    foreach (string d in new[] { paintDir, cpuDir, forkDir, drawingDir })
                    {
                        string probe = Path.Combine(d, (name.Name ?? "") + ".dll");
                        if (File.Exists(probe)) return ctx.LoadFromAssemblyPath(probe);
                    }
                    return null;
                };
                var core = alc.LoadFromAssemblyPath(tempOut);
                var cpu = alc.LoadFromAssemblyPath(cpuDll);
                (WeakReference, WeakReference) refs = RedirectDisposeCollect(core, cpu);
                CallerDrain(refs);
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Repeated_Redirect_Dispose_Cycles_Survive_Gc_Pressure()
    {
        string paintDir = PaintDir();
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        string forkDll = @"E:\tmp\rewritten-small\WfWasm.WinForms.dll";
        string drawingDll = BinDll("WfWasm.Drawing", "net9.0", "WfWasm.Drawing");
        string corePath = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        Assert.True(File.Exists(corePath), "pristine PDN core missing: " + corePath);
        string tempOut = Path.Combine(Path.GetTempPath(), $"CpuGp_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var alc = new AssemblyLoadContext($"CpuGp_{Guid.NewGuid()}", true);
            try
            {
                string cpuDir = Path.GetDirectoryName(cpuDll)!;
                string forkDir = Path.GetDirectoryName(forkDll)!;
                string drawingDir = Path.GetDirectoryName(drawingDll)!;
                alc.Resolving += (ctx, name) =>
                {
                    foreach (string d in new[] { paintDir, cpuDir, forkDir, drawingDir })
                    {
                        string probe = Path.Combine(d, (name.Name ?? "") + ".dll");
                        if (File.Exists(probe)) return ctx.LoadFromAssemblyPath(probe);
                    }
                    return null;
                };
                // 50 abandoned redirects (never drawn) + dispose pressure,
                // then a full collection: previously this shape left stale
                // handles behind and killed the finalizer thread.
                var core = alc.LoadFromAssemblyPath(tempOut);
                var cpu = alc.LoadFromAssemblyPath(cpuDll);
                var allRefs = new System.Collections.Generic.List<(WeakReference, WeakReference)>();
                for (int k = 0; k < 50; k++)
                    allRefs.Add(RedirectDisposeCollect(core, cpu));
                foreach (var r in allRefs)
                    CallerDrain(r);
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
