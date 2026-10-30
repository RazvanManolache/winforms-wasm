using System.Reflection;
using System.Runtime.Loader;

// Consume-path scratch ownership (P0 follow-up to CpuOwnershipTests).
//
// The redirect scratch is freed either by DrawImage consume (blit+free on
// first playback) or by the list finalizer (never-drawn leftovers). Those
// run on different threads - the finalizer has its own thread even in
// browser WASM - so the take must be atomic: both sides claim via
// Interlocked.Exchange(ref _scratch, 0) and the loser observes zero.
//
// The pre-atomic shape (check-then-free-then-zero) let both sides free one
// scratch: NullReferenceException/InvalidCastException in
// CpuPaint.FreeScratchTarget <- CpuCommandList.Finalize, then runtime
// exit 255 mid brush-stroke (flaky: only trips when the finalizer thread
// lands in the consume window).
//
// These tests pin the consume side deterministically on one thread:
// redirect, DrawImage the list (which must claim the scratch exactly
// once: _scratch reads back zero), drop all references, collect. Survival
// IS the assertion - any double-free kills the finalizer thread and
// aborts the host. The 50-cycle pressure test hammers finalizer timing
// the way live strokes do.
public sealed class CpuConsumeTests
{
    // The scenario returns weak refs; the drain runs here, in the caller
    // frame, where the scenario's reflection-invoke temps (arg arrays
    // rooting ctx/list under Debug lifetime) are already dead.
    private static void WithRewrittenCore(Func<Assembly, Assembly, WeakReference[]> scenario)
    {
        string paintDir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(paintDir), "pristine PDN inputs missing: " + paintDir);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        Assert.True(File.Exists(cpuDll), "dependency not built: " + cpuDll);
        string forkDll = @"E:\tmp\rewritten-small\WfWasm.WinForms.dll";
        string drawingDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.Drawing", "bin", "Debug", "net9.0", "WfWasm.Drawing.dll"));
        string corePath = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        Assert.True(File.Exists(corePath), "pristine PDN core missing: " + corePath);
        string tempOut = Path.Combine(Path.GetTempPath(), $"CpuCons_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var alc = new AssemblyLoadContext($"CpuCons_{Guid.NewGuid()}", true);
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
                WeakReference[] refs = scenario(core, cpu);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                foreach (var w in refs)
                    Assert.False(w.IsAlive, "unreachable object survived caller-frame drain (leak/root)");
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    // One redirect + N DrawImage(list) calls in a nested frame that has
    // returned before collection (Debug extends local lifetime to the end
    // of the enclosing frame; collecting in-frame would leave the list
    // alive and fire its finalizer after Unload).
    private static WeakReference[] RedirectConsumeCollect(Assembly core, Assembly cpu, int draws)
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

        long live = (long)createTarget.Invoke(null, new object?[] { 64, 64 })!;
        Assert.True(live != 0, "live target not allocated");
        var targetF = ctxT.GetField("_target",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var scratchF = listT.GetField("_scratch",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(targetF);
        Assert.NotNull(scratchF);
        targetF.SetValue(ctx, live);

        var setTarget = ctxT.GetMethod("SetTarget");
        Assert.NotNull(setTarget);
        setTarget.Invoke(ctx, new object?[] { list });
        long scratch = (long)scratchF.GetValue(list)!;
        Assert.True(scratch != 0, "redirect did not allocate a scratch");

        var drawImage = ctxT.GetMethods()
            .FirstOrDefault(m => m.Name == "DrawImage" && m.GetParameters().Length == 5);
        Assert.NotNull(drawImage);
        for (int i = 0; i < draws; i++)
            drawImage.Invoke(ctx, new object?[] { list, null, null, null, null });

        // The consume path claimed the scratch exactly once: a no-op
        // overload (or a lost race) would leave it nonzero here.
        Assert.Equal(0L, (long)scratchF.GetValue(list)!);

        // Drop every reference; the caller drains (consume already freed,
        // finalizer must observe zero) and asserts survival there.
        var wctx = new WeakReference(ctx);
        var wlist = new WeakReference(list);
        ctx = null;
        list = null;
        return new[] { wctx, wlist };
    }

    [Fact]
    public void DrawImage_Consume_Then_Finalize_Survives_Gc()
    {
        WithRewrittenCore((core, cpu) => RedirectConsumeCollect(core, cpu, 1));
    }

    [Fact]
    public void DrawImage_Double_Consume_Stays_Claimed_And_Survives_Gc()
    {
        // Second playback of the same list must be a no-op, never a
        // second free (the vendor palette draws raw-after-wrapped).
        WithRewrittenCore((core, cpu) => RedirectConsumeCollect(core, cpu, 2));
    }

    private static WeakReference[] RedirectNestedCollect(Assembly core, Assembly cpu)
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

        object? ctx1 = Activator.CreateInstance(ctxT, new object?[] { 0L });
        object? ctx2 = Activator.CreateInstance(ctxT, new object?[] { 0L });
        Assert.NotNull(ctx1);
        Assert.NotNull(ctx2);
        object? list = Activator.CreateInstance(listT);
        Assert.NotNull(list);

        long live1 = (long)createTarget.Invoke(null, new object?[] { 64, 64 })!;
        long live2 = (long)createTarget.Invoke(null, new object?[] { 64, 64 })!;
        Assert.True(live1 != 0 && live2 != 0 && live1 != live2, "targets not allocated");
        var targetF = ctxT.GetField("_target",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var redirF = ctxT.GetField("_redirList",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var scratchF = listT.GetField("_scratch",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var savedF = listT.GetField("_saved",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(targetF);
        Assert.NotNull(redirF);
        Assert.NotNull(scratchF);
        Assert.NotNull(savedF);
        targetF.SetValue(ctx1, live1);
        targetF.SetValue(ctx2, live2);

        var setTarget = ctxT.GetMethod("SetTarget");
        Assert.NotNull(setTarget);
        setTarget.Invoke(ctx1, new object?[] { list });
        long scratch = (long)scratchF.GetValue(list)!;
        Assert.True(scratch != 0, "redirect did not allocate a scratch");
        Assert.Equal(scratch, (long)targetF.GetValue(ctx1)!);
        Assert.Equal(live1, (long)savedF.GetValue(list)!);

        // Nested take on a second dc must be refused: ctx2 keeps its own
        // target with no redirect, and the list still names the first
        // dc's target (a second take would overwrite _saved and let a
        // later teardown free whoever recycled that slot).
        setTarget.Invoke(ctx2, new object?[] { list });
        Assert.Equal(live2, (long)targetF.GetValue(ctx2)!);
        Assert.Null(redirF.GetValue(ctx2));
        Assert.Equal(scratch, (long)scratchF.GetValue(list)!);
        Assert.Equal(live1, (long)savedF.GetValue(list)!);
        Assert.Equal(scratch, (long)targetF.GetValue(ctx1)!);

        // Unwind the first redirect, dispose both, collect: survival is
        // the assertion (no stale handle may reach the finalizer).
        var setNull = ctxT.GetMethod("SetTarget");
        Assert.NotNull(setNull);
        setNull.Invoke(ctx1, new object?[] { null });
        Assert.Equal(live1, (long)targetF.GetValue(ctx1)!);
        ((IDisposable)ctx1).Dispose();
        ((IDisposable)ctx2).Dispose();
        var w1 = new WeakReference(ctx1);
        var w2 = new WeakReference(ctx2);
        var wl = new WeakReference(list);
        ctx1 = null;
        ctx2 = null;
        list = null;
        return new[] { w1, w2, wl };
    }

    [Fact]
    public void Redirect_Nested_Same_List_Second_Take_Refused()
    {
        WithRewrittenCore((core, cpu) => RedirectNestedCollect(core, cpu));
    }

    [Fact]
    public void Repeated_Consume_Cycles_Survive_Gc_Pressure()
    {
        WithRewrittenCore((core, cpu) =>
        {
            var all = new System.Collections.Generic.List<WeakReference>();
            for (int k = 0; k < 50; k++)
                all.AddRange(RedirectConsumeCollect(core, cpu, 1));
            return all.ToArray();
        });
    }
}
