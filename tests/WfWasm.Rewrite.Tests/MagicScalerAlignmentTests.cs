using Mono.Cecil;

// Pins the verbatim-boundary VTable alignment (rev267): PDN imaging
// adapters are rewritten with drawing types remapped to WfWasm.Drawing,
// so the MagicScaler interfaces they implement must carry the same tokens.
// A verbatim (stub-only) MagicScaler keeps System.Drawing.Primitives
// references: then CopyPixels(Rectangle,...) slots mismatch and every
// IPixelSource implementer fails VTable setup headless, which blanks all
// thumbnails. Rewriting MagicScaler with the same drawing assembly map
// aligns both sides; this test proves the alignment at transform level.
public sealed class MagicScalerAlignmentTests
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

    private static ModuleDefinition Transform(string fileName, string tempOut,
        IReadOnlyDictionary<string, string> assemblyMap)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut, assemblyMap,
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static string RectangleScope(MethodDefinition m)
    {
        var rect = m.Parameters.First(p => p.ParameterType.Name == "Rectangle");
        return rect.ParameterType.Scope.Name;
    }

    [Fact]
    public void MagicScaler_And_Pdn_Adapters_Share_Rectangle_Tokens()
    {
        string drawingDll = BinDll("WfWasm.Drawing", "net9.0", "WfWasm.Drawing");
        var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Drawing"] = drawingDll,
            ["System.Drawing.Common"] = drawingDll,
            ["System.Drawing.Primitives"] = drawingDll,
        };
        string msOut = Path.Combine(Path.GetTempPath(), $"MsAlign_{Guid.NewGuid():N}.dll");
        string coreOut = Path.Combine(Path.GetTempPath(), $"CoreAlign_{Guid.NewGuid():N}.dll");
        try
        {
            var ms = Transform("PhotoSauce.MagicScaler.dll", msOut, assemblyMap);
            var iface = ms.GetType("PhotoSauce.MagicScaler.IPixelSource");
            Assert.NotNull(iface);
            var copyPixels = iface.Methods.First(m => m.Name == "CopyPixels");
            Assert.Equal("WfWasm.Drawing", RectangleScope(copyPixels));

            var core = Transform("PaintDotNet.Windows.Core.dll", coreOut, assemblyMap);
            var clipper = core.GetType("PaintDotNet.Imaging.PixelSourceClipper");
            Assert.NotNull(clipper);
            var impl = clipper.Methods.First(m => m.Name == "CopyPixels");
            Assert.Equal("WfWasm.Drawing", RectangleScope(impl));
        }
        finally
        {
            try { if (File.Exists(msOut)) File.Delete(msOut); } catch { }
            try { if (File.Exists(coreOut)) File.Delete(coreOut); } catch { }
        }

    }

    [Fact]
    public void Simd_Probe_Answers_False()
    {
        // HWIntrinsics.getFastGather executes CPUID unconditionally; the
        // WASM interpreter has no CPUID, so the static ctor throws PNSE and
        // every scale transform dies. The rewritten bundle answers false
        // (managed kernels); the cctor shape (flag assignment) is intact.
        string msOut = Path.Combine(Path.GetTempPath(), $"MsSimd_{Guid.NewGuid():N}.dll");
        try
        {
            var drawingDll = BinDll("WfWasm.Drawing", "net9.0", "WfWasm.Drawing");
            var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var ms = Transform("PhotoSauce.MagicScaler.dll", msOut, assemblyMap);
            var hw = ms.GetType("PhotoSauce.MagicScaler.HWIntrinsics");
            Assert.NotNull(hw);
            var probe = hw.Methods.FirstOrDefault(m =>
                m.Name == "getFastGather" && m.IsStatic && m.HasBody && m.Parameters.Count == 0);
            Assert.True(probe is not null);
            var ins = probe.Body.Instructions;
            Assert.Equal(2, ins.Count);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldc_I4_0, ins[0].OpCode);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, ins[1].OpCode);
            Assert.DoesNotContain(ins, i => i.Operand is MethodReference mr && mr.Name == "CpuId");
            var cctor = hw.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.HasBody);
            Assert.True(cctor is not null);
            Assert.Contains(cctor.Body.Instructions, i => i.Operand is MethodReference mr
                && mr.Name == "getFastGather");
            Assert.Contains(cctor.Body.Instructions, i => i.Operand is FieldReference fr
                && fr.Name == "HasFastGather");
        }
        finally
        {
            try { if (File.Exists(msOut)) File.Delete(msOut); } catch { }
        }
    }
}
