using Mono.Cecil;

// Ink for the CpuDrawingContext synthesis contract (see
// AssemblyRewriter.EnsureCpuDrawingContext): sealed class, base
// PaintDotNet.ComponentModel.RefTrackedObject (parameterless .ctor
// seeds refCount=1), implementing IDrawingContext + IDeviceContext +
// IResourceCacheHolder + ICompatibleDeviceContext. These tests pin the
// metadata contract and the live refcount/dispose behavior; the older
// Rewrite_CpuDrawing_Synthesizes_Headless_Context System.Object
// baseline predates the rev85 device-union change.
public sealed class CpuDrawingContextContractTests
{
    private static string PaintDir()
    {
        string dir = TestPaths.Repo(@"targets\paint.net");
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

    private static string ForkDll()
    {
        string dll = TestPaths.Repo(@".artifacts\runtime\WfWasm.WinForms.dll");
        Assert.True(File.Exists(dll), "migrated fork not built (run rewrite-small.ps1): " + dll);
        return dll;
    }

    [Fact]
    public void CpuDrawingContext_Base_Is_RefTrackedObject_With_Device_Interfaces()
    {
        string corePath = Path.Combine(PaintDir(), "PaintDotNet.Windows.Core.dll");
        Assert.True(File.Exists(corePath), "pristine PDN core missing: " + corePath);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"CtxContract_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            Assert.True(ctx.IsSealed);
            Assert.Equal("PaintDotNet.ComponentModel.RefTrackedObject", ctx.BaseType.FullName);
            foreach (string iface in new[]
            {
                "PaintDotNet.Direct2D1.IDrawingContext",
                "PaintDotNet.Direct2D1.IDeviceContext",
                "PaintDotNet.Direct2D1.IResourceCacheHolder",
                "PaintDotNet.Direct2D1.ICompatibleDeviceContext",
            })
                Assert.Contains(ctx.Interfaces, i => i.InterfaceType.FullName == iface);
            var ctor = Assert.Single(ctx.Methods, m => m.Name == ".ctor" && m.Parameters.Count == 1);
            Assert.Equal("System.Int64", ctor.Parameters[0].ParameterType.FullName);
            Assert.Contains(ctor.Body.Instructions, i =>
                (i.OpCode == Mono.Cecil.Cil.OpCodes.Call || i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == ".ctor"
                && mr.DeclaringType.FullName == "PaintDotNet.ComponentModel.RefTrackedObject");
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void CpuDrawingContext_RefCount_And_Dispose()
    {
        string paintDir = PaintDir();
        string forkDll = ForkDll();
        string drawingDll = BinDll("WfWasm.Drawing", "net9.0", "WfWasm.Drawing");
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        string handmap = TestPaths.Repo(@"src\WfWasm.Rewrite.Tool\handmap.paint.net.txt");
        Assert.True(File.Exists(handmap), "PDN handmap missing: " + handmap);
        string[] closure = new[] { "PaintDotNet.Primitives.dll", "PaintDotNet.ComponentModel.dll", "PaintDotNet.Windows.Core.dll", "PaintDotNet.Windows.dll", "PaintDotNet.Windows.Framework.dll" };
        foreach (string f in closure)
            Assert.True(File.Exists(Path.Combine(paintDir, f)), "pristine PDN input missing: " + f);
        string work = Path.Combine(Path.GetTempPath(), "CtxLife_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = forkDll,
                ["System.Windows.Forms.Primitives"] = forkDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            foreach (string f in closure)
            {
                string input = Path.Combine(paintDir, f);
                var typeMap = WfWasm.Rewrite.GapAnalyzer.AutoMap(input, assemblyMap);
                foreach (string line in File.ReadAllLines(handmap))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                        continue;
                    string[] sides = trimmed.Split('=', 2);
                    if (sides.Length != 2)
                        continue;
                    string[] target = sides[1].Split(':', 2);
                    if (target.Length != 2)
                        continue;
                    typeMap[sides[0]] = (target[0], target[1]);
                }

                WfWasm.Rewrite.AssemblyRewriter.Rewrite(input, Path.Combine(work, f), assemblyMap, typeMap, cpuDll);
            }

            var alc = new System.Runtime.Loader.AssemblyLoadContext($"CtxLife_{Guid.NewGuid()}", true);
            try
            {
                string[] dirs = new[] { work, paintDir, Path.GetDirectoryName(forkDll)!, Path.GetDirectoryName(drawingDll)!, Path.GetDirectoryName(cpuDll)! };
                alc.Resolving += (ctx, name) =>
                {
                    foreach (string d in dirs)
                    {
                        string probe = Path.Combine(d, (name.Name ?? "") + ".dll");
                        if (File.Exists(probe))
                            return ctx.LoadFromAssemblyPath(probe);
                    }

                    return null;
                };
                var core = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.Core.dll"));
                var ctxT = core.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
                Assert.NotNull(ctxT);
                object? ctx = Activator.CreateInstance(ctxT, new object?[] { 0L });
                Assert.NotNull(ctx);
                try
                {
                    // Real consumer surface: disposable + refcounted + disposed-flag.
                    Assert.IsAssignableFrom<IDisposable>(ctx);
                    string[] ifaces = ctx.GetType().GetInterfaces().Select(t => t.FullName ?? "").ToArray();
                    Assert.Contains("PaintDotNet.ComponentModel.IObjectRef", ifaces);
                    Assert.Contains("PaintDotNet.IIsDisposed", ifaces);
                    Assert.Contains("PaintDotNet.ComponentModel.IRefCountedObject", ifaces);
                    // Lifetime reality after the dead-ref trim: RefCount,
                    // AddRef and ReleaseRef are all trimmed (nothing in
                    // the rewritten closure reads them), so the contract
                    // pins behavior, not the counter: casts work, fresh
                    // IsDisposed=false, Dispose() flips it via the real
                    // base path (primary ref released; last ref frees
                    // the CPU target in Dispose(bool)).
                    var isDisposedProp = ctx.GetType().GetProperty("IsDisposed");
                    Assert.NotNull(isDisposedProp);
                    Assert.Equal(false, isDisposedProp.GetValue(ctx));
                    ((IDisposable)ctx).Dispose();
                    object? gone = isDisposedProp.GetValue(ctx);
                    Assert.Equal(true, gone);
                }
                finally
                {
                    (ctx as IDisposable)?.Dispose();
                }
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { System.GC.Collect(); System.GC.WaitForPendingFinalizers(); Directory.Delete(work, true); } catch { }
        }
    }
}
